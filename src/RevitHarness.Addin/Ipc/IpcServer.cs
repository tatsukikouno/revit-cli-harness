#nullable enable

using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RevitHarness.Addin.Ipc
{
    /// <summary>
    /// Named Pipe IPC server for receiving commands from CLI.
    /// </summary>
    public class IpcServer : IDisposable
    {
        public const string PipeName = "RevitHarness2024";
        private const int MaxMessageSize = 10 * 1024 * 1024; // 10MB max

        private CancellationTokenSource? _cts;
        private Task? _serverTask;
        private readonly ConcurrentQueue<(IpcRequest Request, Action<IpcResponse> Callback)> _requestQueue;
        private readonly Action<string> _log;
        private bool _disposed;

        public event EventHandler<IpcRequest>? RequestReceived;

        public IpcServer(Action<string> log)
        {
            _log = log;
            _requestQueue = new ConcurrentQueue<(IpcRequest, Action<IpcResponse>)>();
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
            _serverTask = null;
            _cts?.Dispose();
            _cts = null;
            _log("IPC server stopped");
        }

        /// <summary>
        /// Tries to dequeue a pending request.
        /// </summary>
        public bool TryDequeueRequest(out (IpcRequest Request, Action<IpcResponse> Callback) item)
        {
            return _requestQueue.TryDequeue(out item);
        }

        /// <summary>
        /// Gets the count of pending requests.
        /// </summary>
        public int PendingRequestCount => _requestQueue.Count;

        private async Task RunServerAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream? pipeServer = null;
                try
                {
                    pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    _log("Waiting for client connection...");
                    await pipeServer.WaitForConnectionAsync(ct);
                    _log("Client connected");

                    await HandleClientAsync(pipeServer, ct);
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

        private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
        {
            try
            {
                while (pipe.IsConnected && !ct.IsCancellationRequested)
                {
                    var request = await ReadMessageAsync(pipe, ct);
                    if (request == null)
                    {
                        _log("Client disconnected or invalid message");
                        break;
                    }

                    _log($"Received request: op={request.Op}, id={request.Id}");
                    RequestReceived?.Invoke(this, request);

                    // Create a completion source for the response
                    var tcs = new TaskCompletionSource<IpcResponse>();
                    Action<IpcResponse> callback = response =>
                    {
                        tcs.TrySetResult(response);
                    };

                    // Enqueue the request
                    _requestQueue.Enqueue((request, callback));

                    // Wait for response (with timeout)
                    var timeoutMs = request.GetPayloadValue<int?>("timeoutMs") ?? 300000; // 5 minutes default
                    using var timeoutCts = new CancellationTokenSource(timeoutMs);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                    try
                    {
                        var response = await tcs.Task.WithCancellation(linkedCts.Token);
                        _log($"Sending response: ok={response.Ok}, id={response.Id}");
                        await WriteMessageAsync(pipe, response, ct);
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                    {
                        var timeoutResponse = IpcResponse.Failure(
                            request.Id,
                            ErrorCodes.Timeout,
                            "Request timed out",
                            timeoutMs);
                        await WriteMessageAsync(pipe, timeoutResponse, ct);
                    }
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

        private async Task<IpcRequest?> ReadMessageAsync(Stream stream, CancellationToken ct)
        {
            // Read length prefix (int32 little endian)
            var lengthBuffer = new byte[4];
            var bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4, ct);
            if (bytesRead == 0)
                return null;
            if (bytesRead != 4)
                throw new InvalidDataException("Failed to read message length");

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

    internal static class TaskExtensions
    {
        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>();
            using (ct.Register(s => ((TaskCompletionSource<bool>)s!).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                {
                    throw new OperationCanceledException(ct);
                }
            }
            return await task;
        }
    }
}
