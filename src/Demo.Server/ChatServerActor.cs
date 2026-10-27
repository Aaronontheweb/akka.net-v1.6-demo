using Akka.Actor;
using Demo.Core;
using Demo.Core.Chat;
using Demo.Core.Users;
using static Demo.Core.Chat.ChatCommands;
using static Demo.Core.Chat.ChatEvents;
using static Demo.Core.Users.UserCommands;
using static Demo.Core.Users.UserEvents;

namespace Demo.Server;

public sealed class ChatServerActor : ReceiveActor
{
    private readonly Dictionary<UserId, (UserName Name, IActorRef Actor)> _users = new();
    private readonly Dictionary<MessageId, UserId> _messages = new();

    public ChatServerActor()
    {
        Receive<JoinChat>(command =>
        {
            if (string.IsNullOrWhiteSpace(command.UserId.Value) ||
                string.IsNullOrWhiteSpace(command.UserName.Value) ||
                !command.UserActor.Equals(Sender) || _users.ContainsKey(command.UserId))
            {
                Reject(command, "Invalid or already joined user.");
                return;
            }

            // Bring the newcomer up to date using the existing JoinedChat event.
            foreach (var (id, user) in _users)
                Sender.Tell(new JoinedChat(id, user.Name));

            _users.Add(command.UserId, (command.UserName, command.UserActor));
            Context.Watch(command.UserActor);
            Broadcast(new JoinedChat(command.UserId, command.UserName));
            Accept(command);
        });

        Receive<ChangeUserName>(command =>
        {
            if (!IsMember(command)) return;
            if (string.IsNullOrWhiteSpace(command.NewUserName.Value))
            {
                Reject(command, "A name cannot be blank.");
                return;
            }

            var user = _users[command.UserId];
            _users[command.UserId] = (command.NewUserName, user.Actor);
            Broadcast(new ChangedUserName(command.UserId, command.NewUserName, user.Name));
            Accept(command);
        });

        Receive<SendMessage>(command =>
        {
            if (!IsMember(command)) return;
            if (command.MessageId == MessageId.Empty || !_messages.TryAdd(command.MessageId, command.UserId))
            {
                Reject(command, "A message needs a unique, non-empty id.");
                return;
            }

            Broadcast(new MessageSent(command.MessageId, command.UserId, command.Message));
            Accept(command);
        });

        Receive<EditMessage>(command =>
        {
            if (!IsMember(command)) return;
            if (!_messages.TryGetValue(command.MessageId, out var author) || author != command.UserId)
            {
                Reject(command, "You can only edit your own messages.");
                return;
            }

            Broadcast(new MessageEdited(command.MessageId, command.UserId, command.NewMessage));
            Accept(command);
        });

        Receive<LeaveChat>(command =>
        {
            if (!IsMember(command)) return;
            RemoveUser(command.UserId);
            Accept(command);
        });

        Receive<Terminated>(message =>
        {
            foreach (var (id, user) in _users.ToArray())
                if (user.Actor.Equals(message.ActorRef)) RemoveUser(id);
        });
    }

    private bool IsMember(IWithUserId command)
    {
        if (_users.TryGetValue(command.UserId, out var user) && user.Actor.Equals(Sender)) return true;
        Reject(command, "Join the chat before sending commands.");
        return false;
    }

    private void RemoveUser(UserId id)
    {
        Context.Unwatch(_users[id].Actor);
        _users.Remove(id);
        foreach (var message in _messages.Where(pair => pair.Value == id).Select(pair => pair.Key).ToArray())
            _messages.Remove(message);
        Broadcast(new LeftChat(id));
    }

    private void Broadcast(IWithUserId message)
    {
        foreach (var user in _users.Values) user.Actor.Tell(message);
    }

    private void Accept(IWithUserId command) =>
        Sender.Tell(new ChatResponse(ChatResponseCode.Success, OriginalCommand: command));

    private void Reject(IWithUserId command, string reason) =>
        Sender.Tell(new ChatResponse(ChatResponseCode.Error, reason, command));
}
