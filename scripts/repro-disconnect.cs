#:project ../src/Demo.Server/Demo.Server.csproj

using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Demo.Core;
using Demo.Core.Users;
using Demo.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Diagnostic, not a normal CI test: returns 1 when the disconnect warning loop is observed.
// Both systems use ephemeral ports, so a running demo on port 8081 is unaffected.
AppContext.SetSwitch("Akka.DynamicTypeLoading", false);
var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var leaveAcknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var repeatedReconnectWarnings = 0;

var serverBuilder = Host.CreateApplicationBuilder();
serverBuilder.Services.AddAkka("ReproServer", akka => akka.WithChatRemoting(0)
    .WithActors((system, registry) => registry.Register<ChatServerActor>(
        system.ActorOf(Props.Create(() => new ChatServerActor()), "chat"))));
using var server = serverBuilder.Build();
await server.StartAsync();
var serverSystem = server.Services.GetRequiredService<ActorSystem>();
var observer = serverSystem.ActorOf(Props.Create(() => new WarningObserver(() => Interlocked.Increment(ref repeatedReconnectWarnings))));
serverSystem.EventStream.Subscribe(observer, typeof(Warning));
var path = $"{((ExtendedActorSystem)serverSystem).Provider.DefaultAddress}/user/chat";
Console.WriteLine($"[REPRO] Server endpoint {path}");

var clientBuilder = Host.CreateApplicationBuilder();
clientBuilder.Services.AddAkka("ReproClient", akka => akka.WithChatRemoting(0)
    .WithActors((system, registry) => registry.Register<DiagnosticClient>(system.ActorOf(
        Props.Create(() => new DiagnosticClient(path, () => connected.TrySetResult(),
            () => leaveAcknowledged.TrySetResult())), "client"))));
using var client = clientBuilder.Build();
try
{
    await client.StartAsync();
    var user = client.Services.GetRequiredService<ActorRegistry>().Get<DiagnosticClient>();
    user.Tell("join");
    await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Console.WriteLine("[REPRO] Joined; requesting normal /exit");
    user.Tell("/exit");
    await leaveAcknowledged.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Console.WriteLine("[REPRO] Server acknowledged LeaveChat; stopping client host normally");
    await client.StopAsync();
    Console.WriteLine("[REPRO] Client host stopped; leaving server idle for 25 seconds");
    await Task.Delay(TimeSpan.FromSeconds(25));
    var warnings = Volatile.Read(ref repeatedReconnectWarnings);
    Console.WriteLine($"[REPRO] Observed {warnings} repeated control reconnect warnings after graceful disconnect");
    return warnings > 0 ? 1 : 0;
}
finally
{
    await client.StopAsync();
    await server.StopAsync();
}

sealed class WarningObserver : ReceiveActor
{
    public WarningObserver(Action onReconnectWarning)
    {
        Receive<Warning>(warning =>
        {
            var message = warning.Message.ToString() ?? "";
            if (message.Contains("Still unable to reconnect Artery Control outbound connection"))
                onReconnectWarning();
        });
    }
}

// The callback fires only on the actual server response, never on a local exit timeout.
sealed class DiagnosticClient : ReceiveActor
{
    public DiagnosticClient(string path, Action joined, Action left)
    {
        var server = Context.ActorSelection(path);
        var id = new UserId("test");
        Receive<string>(command =>
        {
            if (command == "join") server.Tell(new UserCommands.JoinChat(id, new UserName("Alice"), Self));
            else if (command == "/exit") server.Tell(new UserCommands.LeaveChat(id));
        });
        Receive<ChatResponse>(response =>
        {
            if (response.ResponseCode != ChatResponseCode.Success) return;
            if (response.OriginalCommand is UserCommands.JoinChat) joined();
            else if (response.OriginalCommand is UserCommands.LeaveChat) left();
        });
        Receive<IWithUserId>(_ => { });
    }
}
