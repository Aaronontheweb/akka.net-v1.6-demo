using Demo.Core.Users;

namespace Demo.Core.Chat;

public interface IChatEvent : IWithMessageId;

public static class ChatEvents
{
    [AkkaSerializable(Manifest = "sm-e")]
    public sealed record MessageSent([property: AkkaField(0)] MessageId MessageId, [property: AkkaField(1)]UserId UserId, 
        [property: AkkaField(2)]MessageContent Message) : IChatEvent;
    
    [AkkaSerializable(Manifest = "em-e")]
    public sealed record MessageEdited([property: AkkaField(0)] MessageId MessageId, [property: AkkaField(1)]UserId UserId, 
        [property: AkkaField(2)]MessageContent NewMessage) : IChatEvent;
}