using Demo.Core.Chat;
using Demo.Core.Users;

namespace Demo.Client;

// Local UI snapshots; these never cross the network.
public sealed record ChatLine(string Text, UserId? UserId = null, MessageId? MessageId = null, bool Edited = false);

public sealed record ChatTranscript(ChatLine[] Lines, MessageId? EditingMessage = null)
{
    public string Text => string.Join('\n', Lines.Select(line => line.Text));
}
