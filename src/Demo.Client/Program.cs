using Akka.Actor;
using Akka.Hosting;
using Demo.Client;
using Demo.Core;
using Demo.Core.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Termina.Hosting;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.WriteLine("Usage: Demo.Client <name>");
    return 1;
}

var name = new UserName(args[0].Trim());
var id = new UserId(Guid.NewGuid().ToString("N"));
var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders(); // Console logging would overwrite the TUI.
builder.Services.AddAkka("ChatClient", akka => akka
    .WithChatRemoting(0) // Each client gets its own available port.
    .AddHocon("akka.stdout-loglevel = OFF", HoconAddMode.Prepend)
    .ConfigureLoggers(loggers =>
    {
        loggers.ClearLoggers();
        loggers.AddLoggerFactory();
    })
    .WithActors((system, registry) => registry.Register<ChatUserActor>(
        system.ActorOf(Props.Create(() => new ChatUserActor(ChatRemoting.ServerPath, id, name)), "user"))));
builder.Services.AddTermina("/chat", termina => termina.RegisterRoute<ChatPage, ChatViewModel>(
    "/chat", _ => new ChatPage(),
    services => new ChatViewModel(services.GetRequiredService<IRequiredActor<ChatUserActor>>(), id))
    .ConfigureRuntime(options => options.CtrlCHandlingMode = CtrlCHandlingMode.Disabled));

var previousControlC = Console.TreatControlCAsInput;
try
{
    Console.TreatControlCAsInput = true;
    await builder.Build().RunAsync();
}
finally
{
    Console.TreatControlCAsInput = previousControlC;
}
return 0;
