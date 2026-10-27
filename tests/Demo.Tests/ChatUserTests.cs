using Akka.Actor;
using Akka.Hosting;
using Demo.Client;
using Demo.Core;
using Demo.Core.Chat;
using Demo.Core.Users;
using Xunit;
using static Demo.Core.Chat.ChatCommands;
using static Demo.Core.Chat.ChatEvents;
using static Demo.Core.Users.UserCommands;
using static Demo.Core.Users.UserEvents;

namespace Demo.Tests;

public sealed class ChatUserTests : Akka.Hosting.TestKit.TestKit
{
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider) =>
        builder.WithChatSerializer();

    [Fact]
    public void Local_input_produces_only_existing_wire_messages_and_formats_received_events()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var server = CreateTestProbe();
        var display = CreateTestProbe();
        var id = new UserId("alice");
        var user = Sys.ActorOf(Props.Create(() => new ChatUserActor(server.Ref.Path.ToString(), id, new UserName("Alice"))));
        user.Tell(new ChatUserActor.Connect(transcript => display.Ref.Tell(transcript.Text), () => display.Ref.Tell("exited")));
        var join = server.ExpectMsg<JoinChat>(cancellationToken: cancellation);
        Assert.Equal(user, join.UserActor);
        Assert.Equal(user, server.LastSender);
        display.ExpectMsg("Connecting…", cancellationToken: cancellation);

        user.Tell(new JoinedChat(id, join.UserName), server.Ref);
        user.Tell(new ChatResponse(ChatResponseCode.Success, OriginalCommand: join), server.Ref);
        display.ExpectMsg<string>(text => text.Contains("* Alice joined"), cancellationToken: cancellation);
        display.ExpectMsg<string>(text => text.Contains("Connected."), cancellationToken: cancellation);

        user.Tell(new ChatUserActor.Submit("hello"));
        var send = server.ExpectMsg<SendMessage>(cancellationToken: cancellation);
        Assert.Equal(id, send.UserId);
        Assert.Equal("hello", send.Message.Content);
        user.Tell(new MessageSent(send.MessageId, id, send.Message), server.Ref);
        display.ExpectMsg<string>(text => text.Contains("Alice: hello"), cancellationToken: cancellation);

        user.Tell(new ChatUserActor.Submit("/edit revised"));
        var edit = server.ExpectMsg<EditMessage>(cancellationToken: cancellation);
        Assert.Equal(send.MessageId, edit.MessageId);
        Assert.Equal("revised", edit.NewMessage.Content);
        user.Tell(new ChatUserActor.Submit("/nick Alicia"));
        Assert.Equal(new ChangeUserName(id, new UserName("Alicia")), server.ExpectMsg<ChangeUserName>(cancellationToken: cancellation));

        user.Tell(new ChatUserActor.Submit("/exit"));
        var leave = server.ExpectMsg<LeaveChat>(cancellationToken: cancellation);
        user.Tell(new ChatResponse(ChatResponseCode.Success, OriginalCommand: leave), server.Ref);
        display.ExpectMsg("exited", cancellationToken: cancellation);
    }

    [Fact]
    public void Exit_finishes_even_if_the_server_stops_replying()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var server = CreateTestProbe();
        var display = CreateTestProbe();
        var user = Sys.ActorOf(Props.Create(() => new ChatUserActor(server.Ref.Path.ToString(), new UserId("a"), new UserName("Alice"))));
        user.Tell(new ChatUserActor.Connect(_ => { }, () => display.Ref.Tell("exited")));
        var join = server.ExpectMsg<JoinChat>(cancellationToken: cancellation);
        user.Tell(new ChatResponse(ChatResponseCode.Success, OriginalCommand: join), server.Ref);
        user.Tell(new ChatUserActor.Submit("/exit"));
        server.ExpectMsg<LeaveChat>(cancellationToken: cancellation);
        display.ExpectMsg("exited", TimeSpan.FromSeconds(5), cancellationToken: cancellation);
    }

    [Fact]
    public void User_can_exit_before_connecting()
    {
        var server = CreateTestProbe();
        var display = CreateTestProbe();
        var user = Sys.ActorOf(Props.Create(() => new ChatUserActor(server.Ref.Path.ToString(), new UserId("a"), new UserName("Alice"))));
        user.Tell(new ChatUserActor.Connect(transcript => display.Ref.Tell(transcript.Text), () => display.Ref.Tell("exited")));
        user.Tell(new ChatUserActor.Submit("/exit"));
        display.ExpectMsg("Connecting…", cancellationToken: TestContext.Current.CancellationToken);
        display.ExpectMsg("exited", cancellationToken: TestContext.Current.CancellationToken);
    }
}
