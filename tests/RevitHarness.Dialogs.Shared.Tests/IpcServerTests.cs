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

        private static async Task Send(Stream stream, string op, int? timeoutMs = null)
        {
            var request = new IpcRequest { Id = op, Op = op };
            if (timeoutMs != null) request.Payload = new System.Collections.Generic.Dictionary<string, object> { ["timeoutMs"] = timeoutMs.Value };
            var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request));
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
        public async Task StartingServerAnswersHarnessNotReadyExceptForDirectRequests()
        {
            var pipe = "RevitHarnessTest" + Guid.NewGuid().ToString("N");
            using var server = new IpcServer(_ => { }, pipe)
            {
                DirectHandler = r => r.Op == "direct" ? IpcResponse.Success(r.Id, new { }, 0) : null,
                Ready = false
            };
            server.Start();
            using var client = await Connect(pipe);

            await Send(client, "ping");
            var notReady = await Receive(client).WaitAsync(TimeSpan.FromSeconds(5));
            await Send(client, "direct");
            var direct = await Receive(client).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(notReady.Ok);
            Assert.Equal("HARNESS_NOT_READY", notReady.Error!.Code);
            Assert.True(direct.Ok);
            Assert.Equal(0, server.PendingRequestCount);
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

        [Fact]
        public async Task RequestWhoseClientDisconnectedIsDroppedNotRunLate()
        {
            var pipe = "RevitHarnessTest" + Guid.NewGuid().ToString("N");
            using var server = new IpcServer(_ => { }, pipe);
            server.Start();

            var gone = await Connect(pipe);
            await Send(gone, "abandoned");
            await Task.Delay(200);
            gone.Dispose();
            await Task.Delay(300);

            using var alive = await Connect(pipe);
            await Send(alive, "live");
            await Task.Delay(200);

            Assert.True(server.TryDequeueRequest(out var item));
            Assert.Equal("live", item.Request.Op);
            Assert.False(server.TryDequeueRequest(out _));
        }

        [Fact]
        public async Task RequestPastItsDeadlineIsDroppedAndTheClientGetsATimeoutResponse()
        {
            var pipe = "RevitHarnessTest" + Guid.NewGuid().ToString("N");
            using var server = new IpcServer(_ => { }, pipe);
            server.Start();

            using var client = await Connect(pipe);
            await Send(client, "slow", timeoutMs: 200);
            var response = await Receive(client).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(response.Ok);

            Assert.False(server.TryDequeueRequest(out _));
        }

        [Fact]
        public async Task RequestStillWaitingIsDequeuedAndItsResponseReachesTheClient()
        {
            var pipe = "RevitHarnessTest" + Guid.NewGuid().ToString("N");
            using var server = new IpcServer(_ => { }, pipe);
            server.Start();

            using var client = await Connect(pipe);
            await Send(client, "work");
            await Task.Delay(200);

            Assert.True(server.TryDequeueRequest(out var item));
            item.Callback(IpcResponse.Success(item.Request.Id, new { }, 0));
            var response = await Receive(client).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(response.Ok);
        }

        [Fact]
        public async Task SecondRequestOnTheSameConnectionIsAnsweredAfterTheFirst()
        {
            var pipe = "RevitHarnessTest" + Guid.NewGuid().ToString("N");
            using var server = new IpcServer(_ => { }, pipe);
            server.Start();

            using var client = await Connect(pipe);
            await Send(client, "first");
            await Task.Delay(200);
            Assert.True(server.TryDequeueRequest(out var one));
            one.Callback(IpcResponse.Success(one.Request.Id, new { }, 0));
            Assert.True((await Receive(client).WaitAsync(TimeSpan.FromSeconds(5))).Ok);

            await Send(client, "second");
            await Task.Delay(200);
            Assert.True(server.TryDequeueRequest(out var two));
            Assert.Equal("second", two.Request.Op);
            two.Callback(IpcResponse.Success(two.Request.Id, new { }, 0));
            Assert.Equal("second", (await Receive(client).WaitAsync(TimeSpan.FromSeconds(5))).Id);
        }

        [Fact]
        public async Task NegativeTimeoutDoesNotLeaveALiveRequestBehind()
        {
            var pipe = "RevitHarnessTest" + Guid.NewGuid().ToString("N");
            using var server = new IpcServer(_ => { }, pipe);
            server.Start();

            using var client = await Connect(pipe);
            await Send(client, "negative", timeoutMs: -5);
            var response = await Receive(client).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(response.Ok);
            Assert.False(server.TryDequeueRequest(out _));
        }

        [Fact]
        public async Task RequestPastItsDeadlineIsNotDequeuedEvenBeforeTheServerNoticed()
        {
            var pipe = "RevitHarnessTest" + Guid.NewGuid().ToString("N");
            using var server = new IpcServer(_ => { }, pipe);
            server.Start();

            using var client = await Connect(pipe);
            await Send(client, "short", timeoutMs: 100);
            await Task.Delay(1);
            Assert.False(await WaitUntilDequeueable(server, 100, 0));
        }

        private static async Task<bool> WaitUntilDequeueable(IpcServer server, int deadlineMs, int graceMs)
        {
            await Task.Delay(deadlineMs + graceMs + 50);
            return server.TryDequeueRequest(out _);
        }
    }
}
