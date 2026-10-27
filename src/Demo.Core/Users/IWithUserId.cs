namespace Demo.Core.Users;

public interface IWithUserId
{
    public UserId UserId { get; }
}

/// <summary>
/// Identifies a unique user
/// </summary>
/// <param name="Value">The user id</param>
[AkkaSerializable]
public readonly record struct UserId([property: AkkaField(0)]string Value);

/// <summary>
/// The display name for a user
/// </summary>
/// <param name="Value"></param>
[AkkaSerializable]
public readonly record struct UserName([property: AkkaField(0)]string Value);