using Demo.Core.Users;

namespace Demo.Core;

public enum ChatResponseCode
{
    Success = 0,
    NoOp = 1,
    Error = 2,
}

[AkkaSerializable(Manifest = "cr")]
public sealed record ChatResponse(
    [property: AkkaField(0)] ChatResponseCode ResponseCode,
    [property: AkkaField(1)] string? ErrorMessage = null,
    [property: AkkaField(2)] IWithUserId? OriginalCommand = null)
{
    public static ChatResponse Success => new(ChatResponseCode.Success);
    
    public static ChatResponse Error => new(ChatResponseCode.Error);
    
    public bool IsError => ResponseCode == ChatResponseCode.Error;
    
    public bool IsSuccess => !IsError;
}