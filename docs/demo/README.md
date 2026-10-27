# Record the chat demo

`chat.tape` records two **real Native AOT clients** side by side in tmux. It shows message exchange, a nickname change that relabels older messages, an in-place edit with target highlighting, and Ctrl+C to leave. The server runs in the background; the launcher cleans up its own server and tmux session when the recording ends.

From the repository root on Linux or macOS:

```sh
# Publish for the recording machine first (use osx-arm64 on Apple Silicon).
pwsh -File scripts/publish-native.ps1 -RuntimeIdentifier linux-x64
vhs docs/demo/chat.tape
```

Install [VHS](https://github.com/charmbracelet/vhs), its `ttyd` and `ffmpeg` dependencies, and `tmux` on your PATH. The tape uses the DejaVu Sans Mono font. The checked-in recording was generated on Linux with VHS 0.12.1, ttyd 1.7.7, tmux 3.5a, and the native Linux x64 applications.

Leave port 8081 free before recording. `scripts/demo-chat.sh` starts its own server, writes its log to `artifacts/demo-server.log`, and uses a private tmux socket so it does not touch existing tmux sessions. To try the two-pane demo interactively, run that launcher directly. Switch panes with Ctrl+B then Left/Right.

The output is `docs/demo/chat.gif`. No application behavior or terminal output is simulated by the tape.
