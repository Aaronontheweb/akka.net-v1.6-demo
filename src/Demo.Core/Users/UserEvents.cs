namespace Demo.Core.Users;

public interface IUserEvent : IWithUserId;

public static class UserEvents
{
    [AkkaSerializable(Manifest = "jc-e")]
    public sealed record JoinedChat([property: AkkaField(0)] UserId UserId, [property: AkkaField(1)] UserName UserName) : IUserCommand;
    
    [AkkaSerializable(Manifest = "cn-e")]
    public sealed record ChangedUserName([property: AkkaField(0)] UserId UserId,  [property: AkkaField(1)] UserName NewUserName, [property: AkkaField(2)] UserName OldUserName) : IUserCommand;
    
    [AkkaSerializable(Manifest = "lc-e")]
    public sealed record LeftChat([property: AkkaField(0)] UserId UserId) : IUserCommand;
}