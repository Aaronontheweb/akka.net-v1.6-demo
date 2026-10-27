using Akka.Actor;
using Akka.Hosting;
using Demo.Core;
using Demo.Core.Chat;
using Demo.Core.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using static Demo.Core.Chat.ChatCommands;
using static Demo.Core.Chat.ChatEvents;
using static Demo.Core.Users.UserCommands;
using static Demo.Core.Users.UserEvents;

namespace Demo.Tests;

public sealed class RemoteSerializationTests : Akka.Hosting.TestKit.TestKit
{
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider) =>
        builder.WithChatRemoting(0).WithActors((system, _) =>
            system.ActorOf(Props.Create(() => new EchoActor()), "echo"));

    [Fact]
    public async Task All_messages_and_response_union_members_cross_an_Artery_connection()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddAkka("SerializationClient", akka => akka.WithChatRemoting(0));
        using var client = builder.Build();
        await client.StartAsync(cancellation);
        try
        {
            var clientSystem = client.Services.GetRequiredService<ActorSystem>();
            var probe = CreateTestProbe(clientSystem);
            var address = ((ExtendedActorSystem)Sys).Provider.DefaultAddress;
            var remote = clientSystem.ActorSelection($"{address}/user/echo");
            var id = new UserId("alice");
            var name = new UserName("Alice 👋");
            var messageId = MessageId.Next();
            var content = new MessageContent("こんにちは\nchat over Artery");
            IWithUserId[] messages =
            [
                new JoinChat(id, name, probe.Ref), new ChangeUserName(id, name), new LeaveChat(id),
                new JoinedChat(id, name), new ChangedUserName(id, name, new UserName("old")), new LeftChat(id),
                new SendMessage(messageId, id, content), new EditMessage(messageId, id, content),
                new MessageSent(messageId, id, content), new MessageEdited(messageId, id, content)
            ];

            foreach (var message in messages)
            {
                remote.Tell(message, probe.Ref);
                var restored = await probe.ExpectMsgAsync<IWithUserId>(TimeSpan.FromSeconds(10), cancellationToken: cancellation);
                Assert.Equal(message, restored);
                remote.Tell(new ChatResponse(ChatResponseCode.Error, "test", message), probe.Ref);
                var response = await probe.ExpectMsgAsync<ChatResponse>(TimeSpan.FromSeconds(10), cancellationToken: cancellation);
                Assert.Equal(message, response.OriginalCommand);
                Assert.Equal("test", response.ErrorMessage);
            }

            foreach (var code in Enum.GetValues<ChatResponseCode>())
            {
                var response = new ChatResponse(code);
                remote.Tell(response, probe.Ref);
                Assert.Equal(response, await probe.ExpectMsgAsync<ChatResponse>(cancellationToken: cancellation));
            }
        }
        finally
        {
            await client.StopAsync(CancellationToken.None);
        }
    }

    private sealed class EchoActor : ReceiveActor
    {
        public EchoActor() => ReceiveAny(message => Sender.Tell(message));
    }
}
