using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit;
using Demo.Core;
using Demo.Core.Chat;
using Demo.Core.Users;
using Demo.Server;
using Xunit;
using static Demo.Core.Chat.ChatCommands;
using static Demo.Core.Chat.ChatEvents;
using static Demo.Core.Users.UserCommands;
using static Demo.Core.Users.UserEvents;

namespace Demo.Tests;

public sealed class ChatServerTests : Akka.Hosting.TestKit.TestKit
{
    private static readonly UserId Alice = new("alice");
    private static readonly UserId Bob = new("bob");
    private IActorRef Server => ActorRegistry.Get<ChatServerActor>();

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider) =>
        builder.WithChatSerializer().WithActors((system, registry) =>
            registry.Register<ChatServerActor>(system.ActorOf(Props.Create(() => new ChatServerActor()))));

    private TestProbe Join(UserId id, string name)
    {
        var probe = CreateTestProbe();
        Server.Tell(new JoinChat(id, new UserName(name), probe.Ref), probe.Ref);
        probe.FishForMessage(message => message is ChatResponse { ResponseCode: ChatResponseCode.Success }, cancellationToken: TestContext.Current.CancellationToken);
        return probe;
    }

    [Fact]
    public void Two_users_can_chat_rename_edit_and_leave()
    {
        var alice = Join(Alice, "Alice");
        var bob = Join(Bob, "Bob");
        alice.ExpectMsg(new JoinedChat(Bob, new UserName("Bob")), cancellationToken: TestContext.Current.CancellationToken);

        var send = new SendMessage(MessageId.Next(), Alice, new MessageContent("Hello Bob 👋"));
        Server.Tell(send, alice.Ref);
        var sent = new MessageSent(send.MessageId, Alice, send.Message);
        alice.ExpectMsg(sent, cancellationToken: TestContext.Current.CancellationToken);
        bob.ExpectMsg(sent, cancellationToken: TestContext.Current.CancellationToken);
        alice.ExpectMsg(new ChatResponse(ChatResponseCode.Success, OriginalCommand: send), cancellationToken: TestContext.Current.CancellationToken);

        var rename = new ChangeUserName(Bob, new UserName("Robert"));
        Server.Tell(rename, bob.Ref);
        var renamed = new ChangedUserName(Bob, rename.NewUserName, new UserName("Bob"));
        alice.ExpectMsg(renamed, cancellationToken: TestContext.Current.CancellationToken);
        bob.ExpectMsg(renamed, cancellationToken: TestContext.Current.CancellationToken);
        bob.ExpectMsg<ChatResponse>(cancellationToken: TestContext.Current.CancellationToken);

        var edit = new EditMessage(send.MessageId, Alice, new MessageContent("Hello Robert!"));
        Server.Tell(edit, alice.Ref);
        var edited = new MessageEdited(edit.MessageId, Alice, edit.NewMessage);
        alice.ExpectMsg(edited, cancellationToken: TestContext.Current.CancellationToken);
        bob.ExpectMsg(edited, cancellationToken: TestContext.Current.CancellationToken);
        alice.ExpectMsg<ChatResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Server.Tell(new LeaveChat(Bob), bob.Ref);
        alice.ExpectMsg(new LeftChat(Bob), cancellationToken: TestContext.Current.CancellationToken);
        bob.ExpectMsg<ChatResponse>(response => response.IsSuccess, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public void New_user_receives_existing_names()
    {
        Join(Alice, "Alice");
        var bob = CreateTestProbe();
        Server.Tell(new JoinChat(Bob, new UserName("Bob"), bob.Ref), bob.Ref);
        bob.ExpectMsg(new JoinedChat(Alice, new UserName("Alice")), cancellationToken: TestContext.Current.CancellationToken);
        bob.ExpectMsg(new JoinedChat(Bob, new UserName("Bob")), cancellationToken: TestContext.Current.CancellationToken);
        bob.ExpectMsg<ChatResponse>(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Unjoined_user_and_spoofed_identity_are_rejected()
    {
        var alice = Join(Alice, "Alice");
        var stranger = CreateTestProbe();
        Server.Tell(new ChangeUserName(Alice, new UserName("Imposter")), stranger.Ref);
        stranger.ExpectMsg<ChatResponse>(response => response.IsError, cancellationToken: TestContext.Current.CancellationToken);
        Server.Tell(new JoinChat(Bob, new UserName("Bob"), alice.Ref), stranger.Ref);
        stranger.ExpectMsg<ChatResponse>(response => response.IsError, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Duplicate_message_and_editing_another_users_message_are_rejected()
    {
        var alice = Join(Alice, "Alice");
        var bob = Join(Bob, "Bob");
        alice.ExpectMsg<JoinedChat>(cancellationToken: TestContext.Current.CancellationToken);
        var send = new SendMessage(MessageId.Next(), Alice, new MessageContent("original"));
        Server.Tell(send, alice.Ref);
        alice.ExpectMsg<MessageSent>(cancellationToken: TestContext.Current.CancellationToken);
        alice.ExpectMsg<ChatResponse>(cancellationToken: TestContext.Current.CancellationToken);
        bob.ExpectMsg<MessageSent>(cancellationToken: TestContext.Current.CancellationToken);
        Server.Tell(send, alice.Ref);
        alice.ExpectMsg<ChatResponse>(response => response.IsError, cancellationToken: TestContext.Current.CancellationToken);
        Server.Tell(new EditMessage(send.MessageId, Bob, new MessageContent("stolen")), bob.Ref);
        bob.ExpectMsg<ChatResponse>(response => response.IsError, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Stopped_user_is_removed()
    {
        var alice = Join(Alice, "Alice");
        var bob = Join(Bob, "Bob");
        alice.ExpectMsg<JoinedChat>(cancellationToken: TestContext.Current.CancellationToken);
        Sys.Stop(bob.Ref);
        alice.ExpectMsg(new LeftChat(Bob), cancellationToken: TestContext.Current.CancellationToken);
    }
}
