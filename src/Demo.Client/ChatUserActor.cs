using System.Threading.Channels;
using Akka.Actor;
using Demo.Core;
using Demo.Core.Chat;
using Demo.Core.Users;
using static Demo.Core.Users.UserCommands;

namespace Demo.Client;

public sealed class ChatUserActor : ReceiveActor, IWithTimers
{
    // This channel and these notifications stay inside the client process.
    public sealed record Connect(ChannelWriter<object> Events);
    public sealed record Disconnect;
    public sealed record Connecting;
    public sealed record ConnectionFailed;
    public sealed record Disconnected;
    private sealed record ConnectionTimedOut;
    private sealed record ExitTimedOut;
    private enum TimerKey { Connection, Exit }

    private sealed class Connection(ChannelWriter<object> events)
    {
        public ChannelWriter<object> Events { get; } = events;
        public bool Joined { get; set; }
    }

    private readonly ActorSelection _server;
    private readonly UserId _id;
    private readonly UserName _name;
    private Connection? _connection;

    public ITimerScheduler Timers { get; set; } = null!;

    public ChatUserActor(string serverPath, UserId id, UserName name)
    {
        _server = Context.ActorSelection(serverPath);
        _id = id;
        _name = name;

        Receive<Connect>(message =>
        {
            if (_connection is not null) return;
            _connection = new Connection(message.Events);
            Publish(new Connecting());
            _server.Tell(new JoinChat(_id, _name, Self));
            Timers.StartSingleTimer(TimerKey.Connection, new ConnectionTimedOut(), TimeSpan.FromSeconds(10));
        });
        Receive<ConnectionTimedOut>(_ => Publish(new ConnectionFailed()));
        Receive<ExitTimedOut>(_ => FinishDisconnect());
        Receive<Disconnect>(_ => DisconnectFromChat());
        Receive<IChatCommand>(ForwardCommand);
        Receive<ChangeUserName>(ForwardCommand);
        Receive<IChatEvent>(Publish);
        Receive<IUserEvent>(Publish);
        Receive<ChatResponse>(response =>
        {
            Publish(response);
            if (response.IsError || _connection is null) return;
            switch (response.OriginalCommand)
            {
                case JoinChat:
                    Timers.Cancel(TimerKey.Connection);
                    _connection.Joined = true;
                    break;
                case LeaveChat:
                    FinishDisconnect();
                    break;
            }
        });
    }

    private void Publish(object message) => _connection?.Events.TryWrite(message);

    private void ForwardCommand(object command)
    {
        if (_connection is { Joined: true } && !Timers.IsTimerActive(TimerKey.Exit))
            _server.Tell(command);
    }

    private void DisconnectFromChat()
    {
        if (_connection is null || Timers.IsTimerActive(TimerKey.Exit)) return;
        // Also undo a pending join: both commands reach the server from this actor in order.
        _server.Tell(new LeaveChat(_id));
        if (!_connection.Joined) FinishDisconnect();
        else Timers.StartSingleTimer(TimerKey.Exit, new ExitTimedOut(), TimeSpan.FromSeconds(2));
    }

    private void FinishDisconnect()
    {
        Timers.CancelAll();
        Publish(new Disconnected());
        _connection?.Events.TryComplete();
        _connection = null;
    }

    protected override void PostStop()
    {
        if (_connection is not null) _server.Tell(new LeaveChat(_id), Self);
        FinishDisconnect();
        base.PostStop();
    }
}
