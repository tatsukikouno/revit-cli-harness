using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RevitHarness.Addin.Ipc;
using Xunit;

namespace RevitHarness.Dialogs.Shared.Tests
{
    public sealed class IpcServerTests
    {
        private static async Task<NamedPipeClientStream> Connect(string pipe)
        {
            var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000);
            return client;
        }

        private static async Task Send(Stream stream, string op)
        {
            var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new IpcRequest { Id = op, Op = op }));
            await stream.WriteAsync(BitConverter.GetBytes(body.Length), 0, 4);
            await stream.WriteAsync(body, 0, body.Length);
            await stream.FlushAsync();
        }

        private static async Task<IpcResponse> Receive(Stream stream)
        {
            var length = new byte[4];
            Assert.Equal(4, await stream.ReadAsync(length, 0, 4));
            var body = new byte[BitConverter.ToInt32(length, 0)];
            var read = 0;
            while (read < body.Length) read += await stream.ReadAsync(body, read, body.Length - read);
            return JsonConvert.DeserializeObject<IpcResponse>(Encoding.UTF8.GetString(body))!;
        }

        [Fact]
        public async Task DirectRequestIsAnsweredWhileAnotherClientsCommandIsStillRunning()
        {
            var pipe = "RevitHarnessTest" + Guid.NewGuid().ToString("N");
            using var server = new IpcServer(_ => { }, pipe)
            {
                DirectHandler = r => r.Op == "direct" ? IpcResponse.Success(r.Id, new { }, 0) : null
            };
            server.Start();

            using var blocked = await Connect(pipe);
            await Send(blocked, "never-answered");
            await Task.Delay(200);

            using var second = await Connect(pipe);
            await Send(second, "direct");
            var response = await Receive(second).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(response.Ok);
            Assert.Equal(1, server.PendingRequestCount);
        }

        [Fact]
        public async Task ExceptionInDirectHandlerBecomesAnErrorResponseNotADroppedPipe()
        {
            var pipe = "RevitHarnessTest" + Guid.NewGuid().ToString("N");
            using var server = new IpcServer(_ => { }, pipe)
            {
                DirectHandler = r => throw new UnauthorizedAccessException("denied")
            };
            server.Start();
            using var client = await Connect(pipe);
            await Send(client, "dialog-log");
            var response = await Receive(client).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(response.Ok);
        }
    }
}
