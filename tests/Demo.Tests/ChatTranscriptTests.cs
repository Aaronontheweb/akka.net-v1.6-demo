using Akka.Actor;
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

public sealed class ChatTranscriptTests
{
    private static readonly UserId Alice = new("alice");
    private static readonly UserId Bob = new("bob");

    private static ChatClientState ConnectedState()
    {
        var state = new ChatClientState(Alice);
        var join = new JoinChat(Alice, new UserName("Alice"), ActorRefs.Nobody);
        state.Apply(new ChatUserActor.Connecting());
        state.Apply(new JoinedChat(Alice, join.UserName));
        state.Apply(new ChatResponse(ChatResponseCode.Success, OriginalCommand: join));
        return state;
    }

    [Fact]
    public void Edit_replaces_only_the_matching_message_and_preserves_its_position()
    {
        var state = ConnectedState();
        var first = MessageId.Next();
        var second = MessageId.Next();
        state.Apply(new MessageSent(first, Alice, new MessageContent("same text")));
        state.Apply(new MessageSent(second, Alice, new MessageContent("same text")));
        var before = state.Snapshot;
        state.Apply(new MessageEdited(first, Alice, new MessageContent("revised")));
        var after = state.Snapshot;
        Assert.Equal(before.Lines.Length, after.Lines.Length);
        Assert.Equal("Alice: revised *", after.Lines[^2].Text);
        Assert.True(after.Lines[^2].Edited);
        Assert.Equal("Alice: same text", after.Lines[^1].Text);
        Assert.Equal("Alice: same text", before.Lines[^2].Text);
    }

    [Fact]
    public void Rename_relabels_all_prior_messages_by_user_id_and_keeps_names_after_leave()
    {
        var state = ConnectedState();
        // Identical display names must not make one user's rename affect another.
        state.Apply(new JoinedChat(Bob, new UserName("Alice")));
        var first = MessageId.Next();
        state.Apply(new MessageSent(first, Alice, new MessageContent("first")));
        state.Apply(new MessageSent(MessageId.Next(), Alice, new MessageContent("second")));
        state.Apply(new MessageSent(MessageId.Next(), Bob, new MessageContent("other user")));
        state.Apply(new MessageEdited(first, Alice, new MessageContent("edited")));
        state.Apply(new ChangedUserName(Alice, new UserName("Alicia"), new UserName("Alice")));
        Assert.Contains("Alicia: edited *", state.Snapshot.Text);
        Assert.Contains("Alicia: second", state.Snapshot.Text);
        Assert.Contains("Alice: other user", state.Snapshot.Text);
        Assert.DoesNotContain("Alice: first", state.Snapshot.Text);
        state.Apply(new LeftChat(Alice));
        Assert.Contains("Alicia: edited *", state.Snapshot.Text);
    }

    [Fact]
    public void Edit_mode_identifies_the_last_own_message_and_cancel_preserves_content()
    {
        var state = ConnectedState();
        var id = MessageId.Next();
        state.Apply(new MessageSent(id, Alice, new MessageContent("original")));
        state.Apply(new MessageSent(MessageId.Next(), Bob, new MessageContent("not mine")));
        state.BeginEdit();
        Assert.Equal("original", state.EditingText);
        Assert.Equal(id, state.Snapshot.EditingMessage);
        state.CancelEdit();
        Assert.Null(state.EditingText);
        Assert.Null(state.Snapshot.EditingMessage);
        Assert.Contains("Alice: original", state.Snapshot.Text);
        Assert.DoesNotContain(state.Snapshot.Lines, line => line.Edited);
    }

    [Fact]
    public void Saving_waits_for_server_acceptance_and_exit_still_works_during_edit()
    {
        var state = ConnectedState();
        var id = MessageId.Next();
        state.Apply(new MessageSent(id, Alice, new MessageContent("original")));
        state.BeginEdit();
        var edit = Assert.IsType<EditMessage>(state.Submit("revised"));
        Assert.Equal(id, edit.MessageId);
        Assert.Equal("revised", edit.NewMessage.Content);
        Assert.Contains("Alice: original", state.Snapshot.Text);
        state.Apply(new ChatResponse(ChatResponseCode.Error, "try again", edit));
        Assert.Equal(id, state.Snapshot.EditingMessage);
        Assert.Equal("original", state.EditingText);
        Assert.Contains("try again", state.Snapshot.Text);
        state.Apply(new MessageEdited(id, Alice, edit.NewMessage));
        state.Apply(new ChatResponse(ChatResponseCode.Success, OriginalCommand: edit));
        Assert.Null(state.EditingText);
        Assert.Null(state.Snapshot.EditingMessage);
        state.BeginEdit();
        Assert.Equal("revised", state.EditingText);
        Assert.IsType<ChatUserActor.Disconnect>(state.Submit("/exit"));
    }

    [Fact]
    public void Editing_an_evicted_message_does_not_add_it_back_to_history()
    {
        var state = ConnectedState();
        var first = MessageId.Next();
        state.Apply(new MessageSent(first, Alice, new MessageContent("evicted")));
        for (var i = 0; i < 200; i++)
            state.Apply(new MessageSent(MessageId.Next(), Alice, new MessageContent($"message {i}")));
        state.Apply(new MessageEdited(first, Alice, new MessageContent("should stay evicted")));
        state.Apply(new ChangedUserName(Alice, new UserName("Alicia"), new UserName("Alice")));
        Assert.Equal(200, state.Snapshot.Lines.Length);
        Assert.DoesNotContain(state.Snapshot.Lines, line => line.MessageId == first);
        Assert.DoesNotContain("should stay evicted", state.Snapshot.Text);
    }

    [Fact]
    public void Local_input_creates_existing_protocol_commands_with_the_current_user_id()
    {
        var state = ConnectedState();
        var send = Assert.IsType<SendMessage>(state.Submit(" hello "));
        Assert.Equal(Alice, send.UserId);
        Assert.NotEqual(MessageId.Empty, send.MessageId);
        Assert.Equal("hello", send.Message.Content);
        state.Apply(new MessageSent(send.MessageId, Alice, send.Message));
        var edit = Assert.IsType<EditMessage>(state.Submit("/edit revised"));
        Assert.Equal(send.MessageId, edit.MessageId);
        Assert.Equal(Alice, edit.UserId);
        Assert.Equal("revised", edit.NewMessage.Content);
        Assert.Equal(new ChangeUserName(Alice, new UserName("Alicia")), state.Submit("/nick Alicia"));
    }

    [Fact]
    public void Commands_wait_for_connection_but_exit_is_always_available()
    {
        var state = new ChatClientState(Alice);
        state.Apply(new ChatUserActor.Connecting());
        Assert.Null(state.Submit("hello"));
        Assert.IsType<ChatUserActor.Disconnect>(state.Submit("/exit"));
        state.Apply(new ChatUserActor.ConnectionFailed());
        Assert.Null(state.Submit("hello"));
        Assert.IsType<ChatUserActor.Disconnect>(state.Submit("/exit"));
        state = ConnectedState();
        state.Apply(new ChatUserActor.Disconnected());
        Assert.Null(state.Submit("hello"));
        Assert.IsType<ChatUserActor.Disconnect>(state.Submit("/exit"));
    }
}
