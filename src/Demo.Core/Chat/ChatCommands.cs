using Demo.Core.Users;

namespace Demo.Core.Chat;

public interface IChatCommand : IWithMessageId;


public static class ChatCommands
{
    [AkkaSerializable(Manifest = "sm")]
    public sealed record SendMessage([property: AkkaField(0)] MessageId MessageId, [property: AkkaField(1)]UserId UserId, 
        [property: AkkaField(2)]MessageContent Message) : IChatCommand;
    
    [AkkaSerializable(Manifest = "em")]
    public sealed record EditMessage([property: AkkaField(0)] MessageId MessageId, [property: AkkaField(1)]UserId UserId, 
        [property: AkkaField(2)]MessageContent NewMessage) : IChatCommand;
}