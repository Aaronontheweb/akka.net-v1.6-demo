using Demo.Core.Users;

namespace Demo.Core.Chat;

/// <summary>
/// All chat messages have an associated userId too, for speaker attribution
/// </summary>
public interface IWithMessageId : IWithUserId
{
    public MessageId MessageId { get; }
}

[AkkaSerializable]
public readonly record struct MessageId([property: AkkaField(0)] Guid Value)
{
    public static readonly MessageId Empty = new(Guid.Empty);

    public static MessageId Next() => new(Guid.CreateVersion7());
}

/// <summary>
/// A non-empty, 
/// </summary>
[AkkaSerializable]
public sealed record MessageContent
{
    [AkkaField(0)]
    public string Content { get; }

    public MessageContent(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrEmpty(content);

        Content = content;
    }
}