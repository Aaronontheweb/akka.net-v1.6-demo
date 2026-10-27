using Akka.Actor;
using Demo.Core;
using Demo.Core.Chat;
using Demo.Core.Users;
using static Demo.Core.Chat.ChatCommands;
using static Demo.Core.Chat.ChatEvents;
using static Demo.Core.Users.UserCommands;
using static Demo.Core.Users.UserEvents;

namespace Demo.Client;

public sealed class ChatUserActor : ReceiveActor
{
    // Local UI messages. None of these cross the network.
    public sealed record Connect(Action<string> Display, Action Exit);
    public sealed record Submit(string Text);
    private sealed record ConnectionTimedOut;
    private sealed record ExitTimedOut;

    private readonly Dictionary<UserId, UserName> _names = new();
    private readonly ActorSelection _server;
    private readonly UserId _id;
    private readonly UserName _name;
    private Action<string> _display = _ => { };
    private Action _exit = () => { };
    private bool _joined;
    private MessageId _lastMessage;
    private ICancelable? _connectionTimeout;
    private ICancelable? _exitTimeout;

    public ChatUserActor(string serverPath, UserId id, UserName name)
    {
        _server = Context.ActorSelection(serverPath);
        _id = id;
        _name = name;

        Receive<Connect>(message =>
        {
            _display = message.Display;
            _exit = message.Exit;
            _display("Connecting…");
            _server.Tell(new JoinChat(_id, _name, Self));
            _connectionTimeout = Context.System.Scheduler.ScheduleTellOnceCancelable(
                TimeSpan.FromSeconds(10), Self, new ConnectionTimedOut(), Self);
        });
        Receive<ConnectionTimedOut>(_ => _display("Could not join the server. /exit to close and try again."));
        Receive<ExitTimedOut>(_ => _exit());
        Receive<Submit>(message => SubmitText(message.Text));

        Receive<JoinedChat>(message =>
        {
            _names[message.UserId] = message.UserName;
            _display($"* {message.UserName.Value} joined");
        });
        Receive<ChangedUserName>(message =>
        {
            _names[message.UserId] = message.NewUserName;
            _display($"* {message.OldUserName.Value} is now {message.NewUserName.Value}");
        });
        Receive<LeftChat>(message =>
        {
            _display($"* {NameOf(message.UserId)} left");
            _names.Remove(message.UserId);
        });
        Receive<MessageSent>(message =>
        {
            if (message.UserId == _id) _lastMessage = message.MessageId;
            _display($"{NameOf(message.UserId)}: {message.Message.Content}");
        });
        Receive<MessageEdited>(message =>
            _display($"{NameOf(message.UserId)} (edited): {message.NewMessage.Content}"));
        Receive<ChatResponse>(response =>
        {
            if (response.IsError)
            {
                _display($"Error: {response.ErrorMessage}");
                return;
            }

            switch (response.OriginalCommand)
            {
                case JoinChat:
                    _connectionTimeout?.Cancel();
                    _joined = true;
                    _display("Connected. Type a message, /nick name, /edit text, or /exit.");
                    break;
                case LeaveChat:
                    _exitTimeout?.Cancel();
                    _joined = false;
                    _exit();
                    break;
            }
        });
    }

    private void SubmitText(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        if (text == "/exit" && !_joined)
        {
            _exit();
            return;
        }
        if (!_joined)
        {
            _display("Waiting for the server…");
            return;
        }

        if (text == "/exit")
        {
            _server.Tell(new LeaveChat(_id));
            _exitTimeout ??= Context.System.Scheduler.ScheduleTellOnceCancelable(
                TimeSpan.FromSeconds(2), Self, new ExitTimedOut(), Self);
        }
        else if (text.StartsWith("/nick ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(text[6..]))
            _server.Tell(new ChangeUserName(_id, new UserName(text[6..].Trim())));
        else if (text.StartsWith("/edit ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(text[6..]))
        {
            if (_lastMessage == MessageId.Empty) _display("Send a message before editing it.");
            else _server.Tell(new EditMessage(_lastMessage, _id, new MessageContent(text[6..].Trim())));
        }
        else if (text.StartsWith('/')) _display("Commands: /nick name, /edit text (your last message), /exit");
        else _server.Tell(new SendMessage(MessageId.Next(), _id, new MessageContent(text)));
    }

    private string NameOf(UserId id) => _names.TryGetValue(id, out var name) ? name.Value : id.Value;

    protected override void PostStop()
    {
        _connectionTimeout?.Cancel();
        _exitTimeout?.Cancel();
        if (_joined) _server.Tell(new LeaveChat(_id), Self);
        base.PostStop();
    }
}
