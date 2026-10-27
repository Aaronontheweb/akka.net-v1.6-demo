using Akka.Actor;
using Akka.Hosting;
using R3;
using Termina.Reactive;

namespace Demo.Client;

public sealed class ChatViewModel(IRequiredActor<ChatUserActor> requiredUser) : ReactiveViewModel
{
    private IActorRef _user = ActorRefs.Nobody;
    private readonly Queue<string> _lines = new();
    public ReactiveProperty<string> Transcript { get; } = new("");

    public override void OnActivated() => _ = ConnectAsync();

    private async Task ConnectAsync()
    {
        try
        {
            var user = await requiredUser.GetAsync();
            Post(() =>
            {
                _user = user;
                user.Tell(new ChatUserActor.Connect(Display, () => Post(Shutdown)));
            });
        }
        catch (Exception error)
        {
            Post(() => Transcript.Value = $"Could not start chat: {error.Message}");
        }
    }

    private void Display(string line) => Post(() =>
    {
        _lines.Enqueue(line);
        while (_lines.Count > 200) _lines.Dequeue();
        Transcript.Value = string.Join('\n', _lines);
    });

    public void Submit(string text) => _user.Tell(new ChatUserActor.Submit(text));

    public override void Dispose()
    {
        Transcript.Dispose();
        base.Dispose();
    }
}
