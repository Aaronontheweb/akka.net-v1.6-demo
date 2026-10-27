using System.Threading.Channels;
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

public sealed class ChatUserTests : Akka.Hosting.TestKit.TestKit
{
    private static readonly UserId Alice = new("alice");
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider) =>
        builder.WithChatSerializer();

    private (IActorRef User, TestProbe Server, ChannelReader<object> Events) Connect()
    {
        var server = CreateTestProbe();
        var events = Channel.CreateUnbounded<object>();
        var user = Sys.ActorOf(Props.Create(() => new ChatUserActor(server.Ref.Path.ToString(), Alice, new UserName("Alice"))));
        user.Tell(new ChatUserActor.Connect(events.Writer));
        return (user, server, events.Reader);
    }

    private static async Task<T> ReadAsync<T>(ChannelReader<object> events)
    {
        var message = await events.ReadAsync(Cancellation).AsTask().WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
        return Assert.IsType<T>(message);
    }

    private static async Task ExpectCompletedAsync(ChannelReader<object> events) =>
        await events.Completion.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);

    [Fact]
    public async Task Connecting_precedes_server_events_and_commands_wait_for_join_acceptance()
    {
        var (user, server, events) = Connect();
        await ReadAsync<ChatUserActor.Connecting>(events);
        var join = await server.ExpectMsgAsync<JoinChat>(cancellationToken: Cancellation);
        Assert.Equal(user, join.UserActor);
        Assert.Equal(user, server.LastSender);

        user.Tell(new SendMessage(MessageId.Next(), Alice, new MessageContent("too early")));
        var joined = new JoinedChat(Alice, join.UserName);
        var accepted = new ChatResponse(ChatResponseCode.Success, OriginalCommand: join);
        user.Tell(joined, server.Ref);
        user.Tell(accepted, server.Ref);
        Assert.Equal(joined, await ReadAsync<JoinedChat>(events));
        Assert.Equal(accepted, await ReadAsync<ChatResponse>(events));

        var send = new SendMessage(MessageId.Next(), Alice, new MessageContent("hello"));
        user.Tell(send);
        Assert.Equal(send, await server.ExpectMsgAsync<SendMessage>(cancellationToken: Cancellation));
        Assert.Equal(user, server.LastSender);
        var edit = new EditMessage(send.MessageId, Alice, new MessageContent("revised"));
        var rename = new ChangeUserName(Alice, new UserName("Alicia"));
        user.Tell(edit);
        user.Tell(rename);
        Assert.Equal(edit, await server.ExpectMsgAsync<EditMessage>(cancellationToken: Cancellation));
        Assert.Equal(rename, await server.ExpectMsgAsync<ChangeUserName>(cancellationToken: Cancellation));
    }

    [Fact]
    public async Task Server_events_are_delivered_unchanged_and_in_order_through_the_channel()
    {
        var (user, server, events) = Connect();
        await ReadAsync<ChatUserActor.Connecting>(events);
        var join = await server.ExpectMsgAsync<JoinChat>(cancellationToken: Cancellation);
        var messageId = MessageId.Next();
        object[] messages =
        [
            new JoinedChat(Alice, join.UserName),
            new ChatResponse(ChatResponseCode.Success, OriginalCommand: join),
            new MessageSent(messageId, Alice, new MessageContent("original")),
            new MessageEdited(messageId, Alice, new MessageContent("revised")),
            new ChangedUserName(Alice, new UserName("Alicia"), join.UserName),
            new LeftChat(Alice),
            new ChatResponse(ChatResponseCode.Error, "rejected")
        ];
        foreach (var message in messages) user.Tell(message, server.Ref);
        foreach (var expected in messages)
        {
            var actual = await events.ReadAsync(Cancellation).AsTask().WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task Disconnect_delivers_leave_ack_then_disconnected_and_completes_the_channel()
    {
        var (user, server, events) = Connect();
        await ReadAsync<ChatUserActor.Connecting>(events);
        var join = await server.ExpectMsgAsync<JoinChat>(cancellationToken: Cancellation);
        user.Tell(new ChatResponse(ChatResponseCode.Success, OriginalCommand: join), server.Ref);
        await ReadAsync<ChatResponse>(events);
        user.Tell(new ChatUserActor.Disconnect());
        var leave = await server.ExpectMsgAsync<LeaveChat>(cancellationToken: Cancellation);
        Assert.Equal(user, server.LastSender);
        var accepted = new ChatResponse(ChatResponseCode.Success, OriginalCommand: leave);
        user.Tell(accepted, server.Ref);
        Assert.Equal(accepted, await ReadAsync<ChatResponse>(events));
        await ReadAsync<ChatUserActor.Disconnected>(events);
        await ExpectCompletedAsync(events);
    }

    [Fact]
    public async Task Exit_finishes_even_if_the_server_stops_replying()
    {
        var (user, server, events) = Connect();
        await ReadAsync<ChatUserActor.Connecting>(events);
        var join = await server.ExpectMsgAsync<JoinChat>(cancellationToken: Cancellation);
        user.Tell(new ChatResponse(ChatResponseCode.Success, OriginalCommand: join), server.Ref);
        await ReadAsync<ChatResponse>(events);
        user.Tell(new ChatUserActor.Disconnect());
        await server.ExpectMsgAsync<LeaveChat>(cancellationToken: Cancellation);
        await ReadAsync<ChatUserActor.Disconnected>(events);
        await ExpectCompletedAsync(events);
    }

    [Fact]
    public async Task Exit_before_join_acceptance_undoes_the_pending_join_and_ignores_a_late_ack()
    {
        var (user, server, events) = Connect();
        await ReadAsync<ChatUserActor.Connecting>(events);
        var join = await server.ExpectMsgAsync<JoinChat>(cancellationToken: Cancellation);
        user.Tell(new ChatUserActor.Disconnect());
        var leave = await server.ExpectMsgAsync<LeaveChat>(cancellationToken: Cancellation);
        Assert.Equal(Alice, leave.UserId);
        Assert.Equal(user, server.LastSender);
        await ReadAsync<ChatUserActor.Disconnected>(events);
        user.Tell(new ChatResponse(ChatResponseCode.Success, OriginalCommand: join), server.Ref);
        await ExpectCompletedAsync(events);
    }

    [Fact]
    public async Task Stopping_the_user_actor_sends_leave_and_completes_the_event_channel()
    {
        var (user, server, events) = Connect();
        await ReadAsync<ChatUserActor.Connecting>(events);
        var join = await server.ExpectMsgAsync<JoinChat>(cancellationToken: Cancellation);
        user.Tell(new ChatResponse(ChatResponseCode.Success, OriginalCommand: join), server.Ref);
        await ReadAsync<ChatResponse>(events);
        Sys.Stop(user);
        await server.ExpectMsgAsync<LeaveChat>(cancellationToken: Cancellation);
        await ReadAsync<ChatUserActor.Disconnected>(events);
        await ExpectCompletedAsync(events);
    }
}
