using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit;
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

public sealed class ChatTranscriptTests : Akka.Hosting.TestKit.TestKit
{
    private static readonly UserId Alice = new("alice");
    private static readonly UserId Bob = new("bob");
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider) =>
        builder.WithChatSerializer();

    private (IActorRef User, TestProbe Server, TestProbe Display, TestProbe Editor) Connect()
    {
        var server = CreateTestProbe();
        var display = CreateTestProbe();
        var editor = CreateTestProbe();
        var user = Sys.ActorOf(Props.Create(() => new ChatUserActor(server.Ref.Path.ToString(), Alice, new UserName("Alice"))));
        user.Tell(new ChatUserActor.Connect(snapshot => display.Ref.Tell(snapshot),
            () => editor.Ref.Tell("exited"), text => editor.Ref.Tell(text ?? "cancelled")));
        var join = server.ExpectMsg<JoinChat>(cancellationToken: Cancellation);
        display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        user.Tell(new JoinedChat(Alice, join.UserName), server.Ref);
        user.Tell(new ChatResponse(ChatResponseCode.Success, OriginalCommand: join), server.Ref);
        display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        return (user, server, display, editor);
    }

    [Fact]
    public void Edit_replaces_only_the_matching_message_and_preserves_its_position()
    {
        var (user, server, display, _) = Connect();
        var first = MessageId.Next();
        var second = MessageId.Next();
        user.Tell(new MessageSent(first, Alice, new MessageContent("same text")), server.Ref);
        user.Tell(new MessageSent(second, Alice, new MessageContent("same text")), server.Ref);
        display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        var before = display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        user.Tell(new MessageEdited(first, Alice, new MessageContent("revised")), server.Ref);
        var after = display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        Assert.Equal(before.Lines.Length, after.Lines.Length);
        Assert.Equal("Alice: revised *", after.Lines[^2].Text);
        Assert.True(after.Lines[^2].Edited);
        Assert.Equal("Alice: same text", after.Lines[^1].Text);
    }

    [Fact]
    public void Rename_relabels_all_prior_messages_by_user_id_and_keeps_names_after_leave()
    {
        var (user, server, display, _) = Connect();
        // Same display names must not make one user's rename affect the other.
        user.Tell(new JoinedChat(Bob, new UserName("Alice")), server.Ref);
        var first = MessageId.Next();
        user.Tell(new MessageSent(first, Alice, new MessageContent("first")), server.Ref);
        user.Tell(new MessageSent(MessageId.Next(), Alice, new MessageContent("second")), server.Ref);
        user.Tell(new MessageSent(MessageId.Next(), Bob, new MessageContent("other user")), server.Ref);
        user.Tell(new MessageEdited(first, Alice, new MessageContent("edited")), server.Ref);
        for (var i = 0; i < 5; i++) display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);

        user.Tell(new ChangedUserName(Alice, new UserName("Alicia"), new UserName("Alice")), server.Ref);
        var renamed = display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        Assert.Contains("Alicia: edited *", renamed.Text);
        Assert.Contains("Alicia: second", renamed.Text);
        Assert.Contains("Alice: other user", renamed.Text);
        Assert.DoesNotContain("Alice: first", renamed.Text);
        user.Tell(new LeftChat(Alice), server.Ref);
        Assert.Contains("Alicia: edited *", display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation).Text);
    }

    [Fact]
    public void Edit_mode_identifies_the_last_own_message_and_cancel_preserves_content()
    {
        var (user, server, display, editor) = Connect();
        var id = MessageId.Next();
        user.Tell(new MessageSent(id, Alice, new MessageContent("original")), server.Ref);
        user.Tell(new MessageSent(MessageId.Next(), Bob, new MessageContent("not mine")), server.Ref);
        display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        user.Tell(new ChatUserActor.BeginEdit());
        editor.ExpectMsg("original", cancellationToken: Cancellation);
        Assert.Equal(id, display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation).EditingMessage);
        user.Tell(new ChatUserActor.CancelEdit());
        editor.ExpectMsg("cancelled", cancellationToken: Cancellation);
        var cancelled = display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        Assert.Null(cancelled.EditingMessage);
        Assert.Contains("Alice: original", cancelled.Text);
        Assert.DoesNotContain(cancelled.Lines, line => line.Edited);
    }

    [Fact]
    public void Saving_waits_for_server_acceptance_and_exit_still_works_during_edit()
    {
        var (user, server, display, editor) = Connect();
        var id = MessageId.Next();
        user.Tell(new MessageSent(id, Alice, new MessageContent("original")), server.Ref);
        display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        user.Tell(new ChatUserActor.BeginEdit());
        editor.ExpectMsg("original", cancellationToken: Cancellation);
        display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        user.Tell(new ChatUserActor.Submit("revised"));
        var edit = server.ExpectMsg<EditMessage>(cancellationToken: Cancellation);
        Assert.Equal(id, edit.MessageId);
        Assert.Equal("revised", edit.NewMessage.Content);
        user.Tell(new ChatResponse(ChatResponseCode.Error, "try again", edit), server.Ref);
        Assert.Equal(id, display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation).EditingMessage);
        user.Tell(new MessageEdited(id, Alice, edit.NewMessage), server.Ref);
        display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        user.Tell(new ChatResponse(ChatResponseCode.Success, OriginalCommand: edit), server.Ref);
        editor.ExpectMsg("cancelled", cancellationToken: Cancellation);
        Assert.Null(display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation).EditingMessage);

        user.Tell(new ChatUserActor.BeginEdit());
        editor.ExpectMsg("revised", cancellationToken: Cancellation);
        user.Tell(new ChatUserActor.Disconnect());
        var leave = server.ExpectMsg<LeaveChat>(cancellationToken: Cancellation);
        user.Tell(new ChatResponse(ChatResponseCode.Success, OriginalCommand: leave), server.Ref);
        editor.ExpectMsg("exited", cancellationToken: Cancellation);
    }

    [Fact]
    public void Editing_an_evicted_message_does_not_add_it_back_to_history()
    {
        var (user, server, display, _) = Connect();
        var first = MessageId.Next();
        user.Tell(new MessageSent(first, Alice, new MessageContent("evicted")), server.Ref);
        display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        for (var i = 0; i < 200; i++)
        {
            user.Tell(new MessageSent(MessageId.Next(), Alice, new MessageContent($"message {i}")), server.Ref);
            display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        }
        user.Tell(new MessageEdited(first, Alice, new MessageContent("should stay evicted")), server.Ref);
        user.Tell(new ChangedUserName(Alice, new UserName("Alicia"), new UserName("Alice")), server.Ref);
        var snapshot = display.ExpectMsg<ChatTranscript>(cancellationToken: Cancellation);
        Assert.Equal(200, snapshot.Lines.Length);
        Assert.DoesNotContain(snapshot.Lines, line => line.MessageId == first);
        Assert.DoesNotContain("should stay evicted", snapshot.Text);
    }
}
