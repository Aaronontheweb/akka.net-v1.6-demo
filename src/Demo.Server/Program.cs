using Akka.Actor;
using Akka.Hosting;
using Demo.Core;
using Demo.Server;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddAkka("ChatServer", akka => akka
    .WithChatRemoting(8081)
    .WithActors((system, registry) => registry.Register<ChatServerActor>(
        system.ActorOf(Props.Create(() => new ChatServerActor()), "chat"))));

await builder.Build().RunAsync();
