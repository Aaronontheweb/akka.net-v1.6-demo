# Run from the repository root after publishing linux-x64 or osx-arm64.
Require tmux
Output docs/demo/chat.gif
Set Shell bash
Set FontFamily "DejaVu Sans Mono"
Set FontSize 16
Set Width 1800
Set Height 760
Set Padding 12
Set Theme "Catppuccin Mocha"
Set Framerate 12
Set TypingSpeed 45ms

Hide
Type "./scripts/demo-chat.sh"
Enter
Wait+Screen /Connected\./
Wait+Screen /Bob joined/
Sleep 1s
Show

Type "Hello Bob! Two native clients, one chat."
Enter
Sleep 2s
Ctrl+B
Right
Type "Hi Alice! No .NET runtime needed."
Enter
Sleep 2s
Type "/nick Robert"
Enter
Sleep 3s

Ctrl+B
Left
Ctrl+E
Sleep 2s
Type " And editing works too."
Enter
Sleep 3s

Ctrl+B
Right
Type "Your edit updated in my chat too."
Enter
Sleep 2s
Ctrl+C
Sleep 2s

Hide
Ctrl+B
Left
Ctrl+C
Sleep 1s
