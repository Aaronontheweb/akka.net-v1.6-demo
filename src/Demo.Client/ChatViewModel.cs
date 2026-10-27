using Akka.Actor;
using Akka.Hosting;
using R3;
using Termina.Reactive;

namespace Demo.Client;

public sealed class ChatViewModel(IRequiredActor<ChatUserActor> requiredUser) : ReactiveViewModel
{
    private IActorRef _user = ActorRefs.Nobody;
    public ReactiveProperty<ChatTranscript> Transcript { get; } = new(new ChatTranscript([]));
    public ReactiveProperty<string?> EditingText { get; } = new(null);

    public override void OnActivated() => _ = ConnectAsync();

    private async Task ConnectAsync()
    {
        try
        {
            var user = await requiredUser.GetAsync();
            Post(() =>
            {
                _user = user;
                user.Tell(new ChatUserActor.Connect(Display, () => Post(Shutdown),
                    text => Post(() => EditingText.Value = text)));
            });
        }
        catch (Exception error)
        {
            Post(() => Transcript.Value = new ChatTranscript([new ChatLine($"Could not start chat: {error.Message}")]));
        }
    }

    private void Display(ChatTranscript transcript) => Post(() => Transcript.Value = transcript);

    public void Submit(string text) => _user.Tell(new ChatUserActor.Submit(text));

    public void BeginEdit() => _user.Tell(new ChatUserActor.BeginEdit());
    public void CancelEdit() => _user.Tell(new ChatUserActor.CancelEdit());

    public override void RequestShutdown()
    {
        if (_user.Equals(ActorRefs.Nobody)) Shutdown();
        else _user.Tell(new ChatUserActor.Disconnect());
    }

    public override void Dispose()
    {
        Transcript.Dispose();
        EditingText.Dispose();
        base.Dispose();
    }
}
