using Demo.Core;
using Demo.Core.Chat;
using Demo.Core.Users;
using static Demo.Core.Chat.ChatCommands;
using static Demo.Core.Chat.ChatEvents;
using static Demo.Core.Users.UserCommands;
using static Demo.Core.Users.UserEvents;

namespace Demo.Client;

// Owned by the ViewModel and updated only on Termina's UI thread.
public sealed class ChatClientState(UserId id)
{
    private readonly Dictionary<UserId, UserName> _names = new();
    private readonly List<ChatLine> _lines = new();
    private bool _joined;
    private MessageId _lastMessage;
    private MessageId? _editingMessage;

    public string? EditingText { get; private set; }

    public ChatTranscript Snapshot => new(_lines.Select(line =>
        line.UserId is { } author
            ? line with { Text = $"{NameOf(author)}: {line.Text}{(line.Edited ? " *" : "")}" }
            : line).ToArray(), _editingMessage);

    public void Apply(ClientEvent message)
    {
        switch (message)
        {
            case ClientEvent.Connecting:
                AddNotice("Connecting…");
                break;
            case ClientEvent.ConnectionFailed:
                AddNotice("Could not join the server. /exit to close and try again.");
                break;
            case ClientEvent.Disconnected:
                _joined = false;
                break;
            case ClientEvent.User { Event: JoinedChat joined }:
                _names[joined.UserId] = joined.UserName;
                AddNotice($"* {joined.UserName.Value} joined");
                break;
            case ClientEvent.User { Event: ChangedUserName renamed }:
                _names[renamed.UserId] = renamed.NewUserName;
                AddNotice($"* {renamed.OldUserName.Value} is now {renamed.NewUserName.Value}");
                break;
            case ClientEvent.User { Event: LeftChat left }:
                // Retain the name for older messages still in the transcript.
                AddNotice($"* {NameOf(left.UserId)} left");
                break;
            case ClientEvent.Chat { Event: MessageSent sent }:
                if (sent.UserId == id) _lastMessage = sent.MessageId;
                AddLine(new ChatLine(sent.Message.Content, sent.UserId, sent.MessageId));
                break;
            case ClientEvent.Chat { Event: MessageEdited edited }:
                var index = _lines.FindIndex(line => line.MessageId == edited.MessageId && line.UserId == edited.UserId);
                if (index >= 0) _lines[index] = _lines[index] with { Text = edited.NewMessage.Content, Edited = true };
                break;
            case ClientEvent.Response { Value: { IsError: true } error }:
                AddNotice($"Error: {error.ErrorMessage}");
                break;
            case ClientEvent.Response { Value.OriginalCommand: JoinChat }:
                _joined = true;
                AddNotice("Connected. Type a message, /nick name, /edit text, or /exit.");
                break;
            case ClientEvent.Response { Value.OriginalCommand: EditMessage edit } when edit.MessageId == _editingMessage:
                CancelEdit();
                break;
        }
    }

    public IWithUserId? Submit(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;
        if (!_joined)
        {
            AddNotice("Waiting for the server…");
            return null;
        }
        if (_editingMessage is { } editing)
            return new EditMessage(editing, id, new MessageContent(text));
        return text.StartsWith('/')
            ? ParseCommand(text)
            : new SendMessage(MessageId.Next(), id, new MessageContent(text));
    }

    private IWithUserId? ParseCommand(string text)
    {
        switch (text.Split(' ', 2, StringSplitOptions.TrimEntries))
        {
            case ["/nick", { Length: > 0 } name]:
                return new ChangeUserName(id, new UserName(name));
            case ["/edit", { Length: > 0 } content]:
                return EditLastMessage(content);
            default:
                AddNotice("Commands: /nick name, /edit text (your last message), /exit");
                return null;
        }
    }

    private EditMessage? EditLastMessage(string text)
    {
        if (_lastMessage != MessageId.Empty)
            return new EditMessage(_lastMessage, id, new MessageContent(text));

        AddNotice("Send a message before editing it.");
        return null;
    }

    public void BeginEdit()
    {
        if (!_joined) return;
        var line = _lines.LastOrDefault(line => line.UserId == id && line.MessageId is not null);
        if (line is null)
        {
            AddNotice("Send a message before editing it.");
            return;
        }
        _editingMessage = line.MessageId;
        EditingText = line.Text;
    }

    public void CancelEdit()
    {
        _editingMessage = null;
        EditingText = null;
    }

    private void AddNotice(string text) => AddLine(new ChatLine(text));

    private void AddLine(ChatLine line)
    {
        _lines.Add(line);
        if (_lines.Count > 200) _lines.RemoveAt(0);
    }

    private string NameOf(UserId author) => _names.TryGetValue(author, out var name) ? name.Value : author.Value;
}
