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
    public sealed record Connect(Action<ChatTranscript> Display, Action Exit, Action<string?>? EditChanged = null);
    public sealed record Submit(string Text);
    public sealed record BeginEdit;
    public sealed record CancelEdit;
    public sealed record Disconnect;
    private sealed record ConnectionTimedOut;
    private sealed record ExitTimedOut;

    private readonly Dictionary<UserId, UserName> _names = new();
    private readonly List<ChatLine> _lines = new();
    private readonly ActorSelection _server;
    private readonly UserId _id;
    private readonly UserName _name;
    private Action<ChatTranscript> _display = _ => { };
    private Action<string?> _editChanged = _ => { };
    private MessageId? _editingMessage;
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
            _editChanged = message.EditChanged ?? (_ => { });
            AddNotice("Connecting…");
            _server.Tell(new JoinChat(_id, _name, Self));
            _connectionTimeout = Context.System.Scheduler.ScheduleTellOnceCancelable(
                TimeSpan.FromSeconds(10), Self, new ConnectionTimedOut(), Self);
        });
        Receive<ConnectionTimedOut>(_ => AddNotice("Could not join the server. /exit to close and try again."));
        Receive<ExitTimedOut>(_ => _exit());
        Receive<Submit>(message => SubmitText(message.Text));
        Receive<BeginEdit>(_ => StartEditing());
        Receive<CancelEdit>(_ => FinishEditing());
        Receive<Disconnect>(_ => DisconnectFromChat());

        Receive<JoinedChat>(message =>
        {
            _names[message.UserId] = message.UserName;
            AddNotice($"* {message.UserName.Value} joined");
        });
        Receive<ChangedUserName>(message =>
        {
            _names[message.UserId] = message.NewUserName;
            AddNotice($"* {message.OldUserName.Value} is now {message.NewUserName.Value}");
        });
        Receive<LeftChat>(message =>
        {
            AddNotice($"* {NameOf(message.UserId)} left");
            // Keep the last known name for messages left in the transcript.
        });
        Receive<MessageSent>(message =>
        {
            if (message.UserId == _id) _lastMessage = message.MessageId;
            AddLine(new ChatLine(message.Message.Content, message.UserId, message.MessageId));
        });
        Receive<MessageEdited>(message =>
        {
            var index = _lines.FindIndex(line => line.MessageId == message.MessageId && line.UserId == message.UserId);
            if (index < 0) return; // The message may have scrolled out of retained history.
            _lines[index] = _lines[index] with { Text = message.NewMessage.Content, Edited = true };
            RefreshTranscript();
        });
        Receive<ChatResponse>(response =>
        {
            if (response.IsError)
            {
                AddNotice($"Error: {response.ErrorMessage}");
                return;
            }

            switch (response.OriginalCommand)
            {
                case JoinChat:
                    _connectionTimeout?.Cancel();
                    _joined = true;
                    AddNotice("Connected. Type a message, /nick name, /edit text, or /exit.");
                    break;
                case EditMessage edit when edit.MessageId == _editingMessage:
                    FinishEditing();
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
        if (text == "/exit")
        {
            DisconnectFromChat();
            return;
        }
        if (!_joined)
        {
            AddNotice("Waiting for the server…");
            return;
        }

        if (_editingMessage is { } editing)
            _server.Tell(new EditMessage(editing, _id, new MessageContent(text)));
        else if (text.StartsWith("/nick ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(text[6..]))
            _server.Tell(new ChangeUserName(_id, new UserName(text[6..].Trim())));
        else if (text.StartsWith("/edit ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(text[6..]))
        {
            if (_lastMessage == MessageId.Empty) AddNotice("Send a message before editing it.");
            else _server.Tell(new EditMessage(_lastMessage, _id, new MessageContent(text[6..].Trim())));
        }
        else if (text.StartsWith('/')) AddNotice("Commands: /nick name, /edit text (your last message), /exit");
        else _server.Tell(new SendMessage(MessageId.Next(), _id, new MessageContent(text)));
    }

    private void StartEditing()
    {
        if (!_joined) return;
        var line = _lines.LastOrDefault(line => line.UserId == _id && line.MessageId is not null);
        if (line is null)
        {
            AddNotice("Send a message before editing it.");
            return;
        }
        _editingMessage = line.MessageId;
        _editChanged(line.Text);
        RefreshTranscript();
    }

    private void FinishEditing()
    {
        _editingMessage = null;
        _editChanged(null);
        RefreshTranscript();
    }

    private void DisconnectFromChat()
    {
        if (!_joined) { _exit(); return; }
        _server.Tell(new LeaveChat(_id));
        _exitTimeout ??= Context.System.Scheduler.ScheduleTellOnceCancelable(
            TimeSpan.FromSeconds(2), Self, new ExitTimedOut(), Self);
    }

    private void AddNotice(string text) => AddLine(new ChatLine(text));

    private void AddLine(ChatLine line)
    {
        _lines.Add(line);
        if (_lines.Count > 200) _lines.RemoveAt(0);
        RefreshTranscript();
    }

    private void RefreshTranscript() => _display(new ChatTranscript(_lines.Select(line =>
        line.UserId is { } id
            ? line with { Text = $"{NameOf(id)}: {line.Text}{(line.Edited ? " *" : "")}" }
            : line).ToArray(), _editingMessage));

    private string NameOf(UserId id) => _names.TryGetValue(id, out var name) ? name.Value : id.Value;

    protected override void PostStop()
    {
        _connectionTimeout?.Cancel();
        _exitTimeout?.Cancel();
        if (_joined) _server.Tell(new LeaveChat(_id), Self);
        base.PostStop();
    }
}
