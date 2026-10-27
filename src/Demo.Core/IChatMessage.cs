namespace Demo.Core;
using Akka.Serialization.V2;

public interface IChatMessage
{
    public UserId UserId { get; }
}

[AkkaSerializable]
public readonly record struct MessageId([property: AkkaField(0)] Guid Value)
{
    public static readonly MessageId Empty = new(Guid.Empty);

    public static MessageId Next() => new(Guid.CreateVersion7());
}


/// <summary>
/// Identifies a unique user
/// </summary>
/// <param name="Value">The user id</param>
[AkkaSerializable]
public readonly record struct UserId([property: AkkaField(0)]string Value);