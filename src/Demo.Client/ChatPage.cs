using R3;
using Termina.Extensions;
using Termina.Layout;
using Termina.Reactive;
using Termina.Terminal;

namespace Demo.Client;

public sealed class ChatPage : ReactivePage<ChatViewModel>
{
    private readonly TextInputNode _input = new TextInputNode()
        .WithPlaceholder("Message or /nick, /edit, /exit")
        .WithHistory(50);
    private readonly ScrollableContainerNode _scroll = new();

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        Focus.SetFocus(_input);
        _input.Submitted.Subscribe(text =>
        {
            ViewModel.Submit(text);
            _input.Clear();
        }).DisposeWith(Subscriptions);
        KeyBindings.Register(ConsoleKey.PageUp, _scroll.PageUp);
        KeyBindings.Register(ConsoleKey.PageDown, _scroll.PageDown);
    }

    public override ILayoutNode BuildLayout() => Layouts.Vertical()
        .WithChild(new TextNode("Akka.NET 1.6 · Native AOT chat").WithForeground(Color.Cyan).Height(1))
        .WithChild(_scroll.WithContent(ViewModel.Transcript
            .Select<string, ILayoutNode>(text => new TextNode(text)).AsLayout()).Fill())
        .WithChild(new TextNode("Enter: send | ↑/↓: input history | PgUp/PgDn: scroll | /exit: leave").Height(1))
        .WithChild(_input.Height(1));
}
