#nullable enable

using Newtonsoft.Json;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RevitHarness.Cli
{
    /// <summary>
    /// Named Pipe IPC client for communicating with Revit harness.
    /// </summary>
    public class IpcClient : IDisposable
    {
        public const string PipeName = "RevitHarness2024";
        private const int MaxMessageSize = 10 * 1024 * 1024; // 10MB max
        private const int DefaultTimeoutMs = 30000; // 30 seconds

        private NamedPipeClientStream? _pipe;
        private bool _disposed;

        /// <summary>
        /// Connects to the Revit harness.
        /// </summary>
        /// <param name="timeoutMs">Connection timeout in milliseconds.</param>
        public async Task ConnectAsync(int timeoutMs = DefaultTimeoutMs)
        {
            _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await _pipe.ConnectAsync(timeoutMs, cts.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is TimeoutException)
            {
                throw new HarnessFailureException(FailureKind.ConnectionFailed,
                    $"No RevitHarness pipe within {timeoutMs}ms. Revit is not running, or the harness add-in is not installed or failed to load.", ex);
            }
        }

        /// <summary>
        /// Sends a request and waits for response.
        /// </summary>
        public async Task<IpcResponse> SendRequestAsync(IpcRequest request, int timeoutMs = DefaultTimeoutMs)
        {
            if (_pipe == null || !_pipe.IsConnected)
                throw new InvalidOperationException("Not connected to Revit harness");

            using var cts = new CancellationTokenSource(timeoutMs);
            var notAnswered = new HarnessFailureException(FailureKind.Timeout,
                $"Revit did not answer '{request.Op}' in time. A request Revit has not started yet is dropped when the client gives up; one that already started still finishes, so check its effect before running it again.");

            try
            {
                await WriteMessageAsync(_pipe, request, cts.Token);
                var response = await ReadMessageAsync(_pipe, cts.Token);
                if (response.Error?.Code == "TIMEOUT") throw notAnswered;
                if (response.Error?.Code == "HARNESS_NOT_READY")
                    throw new HarnessFailureException(FailureKind.HarnessNotReady, response.Error.Message);
                return response;
            }
            catch (OperationCanceledException)
            {
                throw notAnswered;
            }
        }

        private async Task WriteMessageAsync(Stream stream, IpcRequest request, CancellationToken ct)
        {
            var json = JsonConvert.SerializeObject(request);
            var messageBytes = Encoding.UTF8.GetBytes(json);
            var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

            await stream.WriteAsync(lengthBytes, 0, 4, ct);
            await stream.WriteAsync(messageBytes, 0, messageBytes.Length, ct);
            await stream.FlushAsync(ct);
        }

        private async Task<IpcResponse> ReadMessageAsync(Stream stream, CancellationToken ct)
        {
            // Read length prefix (int32 little endian)
            var lengthBuffer = new byte[4];
            var bytesRead = await ReadExactAsync(stream, lengthBuffer, 0, 4, ct);
            if (bytesRead != 4)
                throw new InvalidDataException("Failed to read message length");

            var length = BitConverter.ToInt32(lengthBuffer, 0);
            if (length <= 0 || length > MaxMessageSize)
                throw new InvalidDataException($"Invalid message length: {length}");

            // Read message body
            var messageBuffer = new byte[length];
            bytesRead = await ReadExactAsync(stream, messageBuffer, 0, length, ct);
            if (bytesRead != length)
                throw new InvalidDataException("Failed to read complete message");

            var json = Encoding.UTF8.GetString(messageBuffer);
            return JsonConvert.DeserializeObject<IpcResponse>(json)
                ?? throw new InvalidDataException("Failed to parse response");
        }

        private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            var totalRead = 0;
            while (totalRead < count)
            {
                var bytesRead = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, ct);
                if (bytesRead == 0)
                    break;
                totalRead += bytesRead;
            }
            return totalRead;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _pipe?.Dispose();
        }
    }
}
