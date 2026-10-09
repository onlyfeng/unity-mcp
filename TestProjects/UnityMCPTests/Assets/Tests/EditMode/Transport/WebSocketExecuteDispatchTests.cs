using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Services.Transport.Transports;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Transport
{
    /// <summary>
    /// The receive loop must hand an "execute" message off and keep reading, so pings and polls
    /// such as get_test_job are still read while a long async command (e.g. refresh_unity waiting
    /// for a compile) is in flight. TransportCommandDispatcher already runs several async commands
    /// at once; awaiting each execute inside the receive loop serialized them at the socket.
    /// </summary>
    public class WebSocketExecuteDispatchTests
    {
        private static readonly MethodInfo HandleMessageAsync = typeof(WebSocketTransportClient)
            .GetMethod("HandleMessageAsync", BindingFlags.Instance | BindingFlags.NonPublic);

        [Test]
        public void ExecuteMessage_DoesNotBlockReceiveLoopUntilCommandCompletes()
        {
            Assert.IsNotNull(HandleMessageAsync, "WebSocketTransportClient.HandleMessageAsync not found");

            var client = new WebSocketTransportClient();
            string message = new JObject
            {
                ["type"] = "execute",
                ["id"] = "execute-dispatch-test",
                ["name"] = "mcp_test_command_that_does_not_exist",
                ["params"] = new JObject(),
                ["timeout"] = 30,
            }.ToString();

            using var cts = new CancellationTokenSource();
            try
            {
                // Like the real receive loop, run on a pool thread: the dispatcher then posts the
                // command to the main thread, which this test holds, so the command cannot finish
                // while we wait. Only a receive path that does not await the command can return.
                Task handled = Task.Run(() => (Task)HandleMessageAsync.Invoke(client, new object[] { message, cts.Token }));

                Assert.IsTrue(handled.Wait(TimeSpan.FromSeconds(5)),
                    "Handling an execute message waited for the command to finish.");
                Assert.IsFalse(handled.IsFaulted, handled.Exception?.ToString());
            }
            finally
            {
                // Cancels the still-pending command so nothing is left queued for later tests.
                cts.Cancel();
            }
        }
    }
}
