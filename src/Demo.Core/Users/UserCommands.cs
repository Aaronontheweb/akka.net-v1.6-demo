using Akka.Actor;

namespace Demo.Core.Users;

/// <summary>
/// Command-type message - requires validation.
/// </summary>
public interface IUserCommand : IWithUserId;

/// <summary>
/// Commands pertaining to the user and not chat messages themselves
/// </summary>
public static class UserCommands
{
    [AkkaSerializable(Manifest = "jc")]
    public sealed record JoinChat([property: AkkaField(0)] UserId UserId, [property: AkkaField(1)] UserName UserName, [property: AkkaField(2)] IActorRef UserActor) : IUserCommand;
    
    [AkkaSerializable(Manifest = "cn")]
    public sealed record ChangeUserName([property: AkkaField(0)] UserId UserId,  [property: AkkaField(1)] UserName NewUserName) : IUserCommand;
    
    [AkkaSerializable(Manifest = "lc")]
    public sealed record LeaveChat([property: AkkaField(0)] UserId UserId) : IUserCommand;
}
