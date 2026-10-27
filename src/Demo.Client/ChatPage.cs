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
            if (ViewModel.EditingText.Value is null) _input.Clear();
        }).DisposeWith(Subscriptions);
        ViewModel.EditingText.Subscribe(text =>
        {
            _input.Text = text ?? "";
            _input.MoveCursorToEnd();
        }).DisposeWith(Subscriptions);
        KeyBindings.Register(ConsoleKey.E, ConsoleModifiers.Control, ViewModel.BeginEdit);
        KeyBindings.Register(ConsoleKey.Escape, ViewModel.CancelEdit);
        KeyBindings.Register(ConsoleKey.C, ConsoleModifiers.Control, ViewModel.RequestShutdown);
        KeyBindings.Register(ConsoleKey.PageUp, _scroll.PageUp);
        KeyBindings.Register(ConsoleKey.PageDown, _scroll.PageDown);
    }

    public override ILayoutNode BuildLayout() => Layouts.Vertical()
        .WithChild(new TextNode("Akka.NET 1.6 · Native AOT chat").WithForeground(Color.Cyan).Height(1))
        .WithChild(_scroll.WithContent(ViewModel.Transcript
            .Select<ChatTranscript, ILayoutNode>(RenderTranscript).AsLayout()).Fill())
        .WithChild(ViewModel.EditingText.Select<string?, ILayoutNode>(text => new TextNode(text is null
            ? "Ctrl+E: edit last message | *: edited message"
            : $"Editing your last message: {text} | Enter: save | Esc: cancel").WithForeground(Color.Yellow)).AsLayout().Height(2))
        .WithChild(new TextNode("Enter: send/save | Esc: cancel edit | ↑/↓: history | PgUp/PgDn: scroll | Ctrl+C: exit").Height(1))
        .WithChild(_input.Height(1));

    private static ILayoutNode RenderTranscript(ChatTranscript transcript)
    {
        var layout = Layouts.Vertical();
        foreach (var line in transcript.Lines)
        {
            var editing = line.MessageId is not null && line.MessageId == transcript.EditingMessage;
            var node = new TextNode(editing ? $"> {line.Text}" : line.Text);
            if (editing) node.WithForeground(Color.Yellow).Bold();
            layout.WithChild(node);
        }
        return layout;
    }
}
