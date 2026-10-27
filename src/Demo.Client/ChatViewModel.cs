using System.Threading.Channels;
using Akka.Actor;
using Akka.Hosting;
using Demo.Core.Users;
using R3;
using Termina.Reactive;

namespace Demo.Client;

public sealed class ChatViewModel(IRequiredActor<ChatUserActor> requiredUser, UserId id) : ReactiveViewModel
{
    private readonly ChatClientState _state = new(id);
    private readonly Channel<object> _events = Channel.CreateUnbounded<object>();
    private readonly CancellationTokenSource _lifetime = new();
    private IActorRef _user = ActorRefs.Nobody;
    private Task? _reader;
    private bool _disposed;

    public ReactiveProperty<ChatTranscript> Transcript { get; } = new(new ChatTranscript([]));
    public ReactiveProperty<string?> EditingText { get; } = new(null);

    public override void OnActivated() => _reader ??= ReadEventsAsync();

    private async Task ReadEventsAsync()
    {
        var cancellation = _lifetime.Token;
        try
        {
            var user = await requiredUser.GetAsync(cancellation);
            await InvokeAsync(() =>
            {
                if (_disposed) return;
                _user = user;
                user.Tell(new ChatUserActor.Connect(_events.Writer));
            }, cancellation);

            await foreach (var message in _events.Reader.ReadAllAsync(cancellation))
            {
                Post(() =>
                {
                    if (_disposed) return;
                    _state.Apply(message);
                    Refresh();
                });
            }
            Post(() => { if (!_disposed) Shutdown(); });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Disposal cancels both actor startup and the channel reader.
        }
        catch (Exception error)
        {
            Post(() =>
            {
                if (_disposed) return;
                Transcript.Value = new ChatTranscript([new ChatLine($"Chat stopped: {error.Message}")]);
            });
        }
    }

    public void Submit(string text)
    {
        if (text.Trim() == "/exit") RequestShutdown();
        else if (_state.Submit(text) is { } command) _user.Tell(command);
        Refresh();
    }

    public void BeginEdit()
    {
        _state.BeginEdit();
        Refresh();
    }

    public void CancelEdit()
    {
        _state.CancelEdit();
        Refresh();
    }

    public override void RequestShutdown()
    {
        if (_user.Equals(ActorRefs.Nobody)) Shutdown();
        else _user.Tell(new ChatUserActor.Disconnect());
    }

    private void Refresh()
    {
        Transcript.Value = _state.Snapshot;
        EditingText.Value = _state.EditingText;
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _events.Writer.TryComplete();
        if (!_user.Equals(ActorRefs.Nobody)) _user.Tell(new ChatUserActor.Disconnect());
        Transcript.Dispose();
        EditingText.Dispose();
        _lifetime.Dispose();
        base.Dispose();
    }
}
