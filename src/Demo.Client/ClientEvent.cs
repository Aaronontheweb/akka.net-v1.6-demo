using Demo.Core;
using Demo.Core.Chat;
using Demo.Core.Users;

namespace Demo.Client;

// Local notifications only; wrapped server messages keep their existing wire format.
public abstract record ClientEvent
{
    private ClientEvent() { }

    public sealed record Chat(IChatEvent Event) : ClientEvent;
    public sealed record User(IUserEvent Event) : ClientEvent;
    public sealed record Response(ChatResponse Value) : ClientEvent;
    public sealed record Connecting : ClientEvent;
    public sealed record ConnectionFailed : ClientEvent;
    public sealed record Disconnected : ClientEvent;
}
