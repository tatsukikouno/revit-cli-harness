#nullable enable

using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RevitHarness.Addin.Ipc
{
    /// <summary>
    /// Named Pipe IPC server for receiving commands from CLI.
    /// </summary>
    public sealed class QueuedRequest
    {
        public QueuedRequest(IpcRequest request, Action<IpcResponse> callback, CancellationToken abandoned)
        {
            Request = request;
            Callback = callback;
            Abandoned = abandoned;
        }

        public IpcRequest Request { get; }
        public Action<IpcResponse> Callback { get; }
        public CancellationToken Abandoned { get; }
    }

    public class IpcServer : IDisposable
    {
        public const string PipeName = "RevitHarness2024";
        private const int MaxMessageSize = 10 * 1024 * 1024; // 10MB max

        private CancellationTokenSource? _cts;
        private Task? _serverTask;
        private readonly ConcurrentQueue<QueuedRequest> _requestQueue;
        private readonly Action<string> _log;
        private readonly ConcurrentDictionary<Task, byte> _clients = new ConcurrentDictionary<Task, byte>();
        private readonly string _pipeName;
        private bool _disposed;

        public event EventHandler<IpcRequest>? RequestReceived;

        /// <summary>
        /// Answers requests on the pipe thread without the Revit main thread (a modal dialog blocks that thread).
        /// Returns null to fall through to the normal queue.
        /// </summary>
        public Func<IpcRequest, IpcResponse?>? DirectHandler { get; set; }

        // The pipe name is a parameter so tests can run a server next to a real Revit.
        public IpcServer(Action<string> log, string pipeName = PipeName)
        {
            _log = log;
            _pipeName = pipeName;
            _requestQueue = new ConcurrentQueue<QueuedRequest>();
        }

        /// <summary>
        /// Starts the IPC server.
        /// </summary>
        public void Start()
        {
            if (_serverTask != null)
                return;

            _cts = new CancellationTokenSource();
            _serverTask = Task.Run(() => RunServerAsync(_cts.Token));
            _log("IPC server started");
        }

        /// <summary>
        /// Stops the IPC server.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            _serverTask?.Wait(TimeSpan.FromSeconds(5));
            try { Task.WaitAll(_clients.Keys.ToArray(), TimeSpan.FromSeconds(5)); }
            catch (AggregateException) { }
            _serverTask = null;
            _cts?.Dispose();
            _cts = null;
            _log("IPC server stopped");
        }

        /// <summary>
        /// Tries to dequeue a pending request. A request whose client already gave up
        /// (its deadline passed or its pipe closed) is dropped here and never returned.
        /// </summary>
        public bool TryDequeueRequest(out QueuedRequest item)
        {
            while (_requestQueue.TryDequeue(out var candidate))
            {
                if (candidate.Abandoned.IsCancellationRequested)
                {
                    _log($"Dropped stale request: op={candidate.Request.Op}, id={candidate.Request.Id}");
                    continue;
                }
                item = candidate;
                return true;
            }
            item = null!;
            return false;
        }

        /// <summary>
        /// Gets the count of pending requests.
        /// </summary>
        public int PendingRequestCount => _requestQueue.Count;

        // Each client gets its own task, so a request that waits on a blocked Revit command
        // does not stop dialog-log or reload-allowlist from connecting.
        private async Task RunServerAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream? pipeServer = null;
                try
                {
                    pipeServer = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    _log("Waiting for client connection...");
                    await pipeServer.WaitForConnectionAsync(ct);
                    _log("Client connected");

                    var connected = pipeServer;
                    pipeServer = null;
                    var client = HandleClientAndDisposeAsync(connected, ct);
                    _clients.TryAdd(client, 0);
                    _ = client.ContinueWith(t => _clients.TryRemove(t, out _), TaskScheduler.Default);
                }
                catch (OperationCanceledException)
                {
                    _log("Server operation cancelled");
                }
                catch (Exception ex)
                {
                    _log($"Server error: {ex.Message}");
                }
                finally
                {
                    pipeServer?.Dispose();
                }
            }
        }

        private async Task HandleClientAndDisposeAsync(NamedPipeServerStream pipe, CancellationToken ct)
        {
            try { await HandleClientAsync(pipe, ct); }
            finally { pipe.Dispose(); }
        }

        private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
        {
            try
            {
                var lengthBuffer = new byte[4];
                Task<int>? carriedRead = null;
                while (pipe.IsConnected && !ct.IsCancellationRequested)
                {
                    var request = await ReadMessageAsync(pipe, lengthBuffer, carriedRead, ct);
                    carriedRead = null;
                    if (request == null)
                    {
                        _log("Client disconnected or invalid message");
                        break;
                    }

                    _log($"Received request: op={request.Op}, id={request.Id}");
                    RequestReceived?.Invoke(this, request);

                    IpcResponse? direct;
                    try { direct = DirectHandler?.Invoke(request); }
                    catch (Exception ex)
                    {
                        direct = IpcResponse.Failure(request.Id, ErrorCodes.InternalError, ex.Message, 0);
                    }
                    if (direct != null)
                    {
                        await WriteMessageAsync(pipe, direct, ct);
                        continue;
                    }

                    var tcs = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var timeoutMs = request.GetPayloadValue<int?>("timeoutMs") ?? 300000; // 5 minutes default
                    using var abandoned = new CancellationTokenSource();
                    _requestQueue.Enqueue(new QueuedRequest(request, response => tcs.TrySetResult(response), abandoned.Token));

                    // A pipe read cannot be cancelled on .NET Framework, so the read that watches for a closed pipe
                    // is the first read of the next message and is carried over to the next loop iteration.
                    var disconnect = pipe.ReadAsync(lengthBuffer, 0, 4, ct);
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var timeout = Task.Delay(timeoutMs, timeoutCts.Token);
                    var first = await Task.WhenAny(tcs.Task, disconnect, timeout);
                    timeoutCts.Cancel();

                    if (first != tcs.Task)
                    {
                        abandoned.Cancel();
                        if (first == timeout)
                        {
                            carriedRead = disconnect;
                            await WriteMessageAsync(pipe, IpcResponse.Failure(request.Id, ErrorCodes.Timeout, "Request timed out", timeoutMs), ct);
                            continue;
                        }
                        _log($"Client gave up on request: op={request.Op}, id={request.Id}");
                        break;
                    }

                    carriedRead = disconnect;
                    var response = tcs.Task.Result;
                    _log($"Sending response: ok={response.Ok}, id={response.Id}");
                    await WriteMessageAsync(pipe, response, ct);
                }
            }
            catch (IOException ex)
            {
                _log($"IO error in client handler: {ex.Message}");
            }
            catch (Exception ex)
            {
                _log($"Error in client handler: {ex.Message}");
            }
        }

        private async Task<IpcRequest?> ReadMessageAsync(Stream stream, byte[] lengthBuffer, Task<int>? firstRead, CancellationToken ct)
        {
            // Read length prefix (int32 little endian); firstRead is a read into lengthBuffer that is already in flight
            var bytesRead = firstRead != null ? await firstRead : await stream.ReadAsync(lengthBuffer, 0, 4, ct);
            if (bytesRead == 0)
                return null;
            while (bytesRead < 4)
            {
                var more = await stream.ReadAsync(lengthBuffer, bytesRead, 4 - bytesRead, ct);
                if (more == 0)
                    throw new InvalidDataException("Failed to read message length");
                bytesRead += more;
            }

            var length = BitConverter.ToInt32(lengthBuffer, 0);
            if (length <= 0 || length > MaxMessageSize)
                throw new InvalidDataException($"Invalid message length: {length}");

            // Read message body
            var messageBuffer = new byte[length];
            var totalRead = 0;
            while (totalRead < length)
            {
                bytesRead = await stream.ReadAsync(messageBuffer, totalRead, length - totalRead, ct);
                if (bytesRead == 0)
                    throw new InvalidDataException("Unexpected end of stream");
                totalRead += bytesRead;
            }

            var json = Encoding.UTF8.GetString(messageBuffer);
            return JsonConvert.DeserializeObject<IpcRequest>(json);
        }

        private async Task WriteMessageAsync(Stream stream, IpcResponse response, CancellationToken ct)
        {
            var json = JsonConvert.SerializeObject(response);
            var messageBytes = Encoding.UTF8.GetBytes(json);
            var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

            await stream.WriteAsync(lengthBytes, 0, 4, ct);
            await stream.WriteAsync(messageBytes, 0, messageBytes.Length, ct);
            await stream.FlushAsync(ct);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Stop();
        }
    }
}
