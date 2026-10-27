#!/usr/bin/env bash
# Start two native chat clients side by side for docs/demo/chat.tape.
set -euo pipefail
cd "$(dirname "$0")/.."

for tool in tmux; do
    command -v "$tool" >/dev/null || { echo "Install $tool first." >&2; exit 1; }
done
for binary in artifacts/server/Demo.Server artifacts/client/Demo.Client; do
    test -x "$binary" || {
        echo "Publish the native apps first with scripts/publish-native.ps1." >&2
        exit 1
    }
done

socket="akka-chat-demo-$$"
log="artifacts/demo-server.log"
server_pid=''
cleanup() {
    tmux -L "$socket" kill-server 2>/dev/null || true
    if test -n "$server_pid"; then
        kill -INT "$server_pid" 2>/dev/null || true
        wait "$server_pid" 2>/dev/null || true
    fi
}
trap cleanup EXIT INT TERM

./artifacts/server/Demo.Server >"$log" 2>&1 &
server_pid=$!
for ((attempt = 0; attempt < 100; attempt++)); do
    if grep -q 'Application started' "$log"; then break; fi
    if ! kill -0 "$server_pid" 2>/dev/null; then cat "$log" >&2; exit 1; fi
    sleep 0.1
done
if ! grep -q 'Application started' "$log"; then cat "$log" >&2; exit 1; fi

tmux -L "$socket" -f /dev/null new-session -d -s chat -x 190 -y 34 -n 'Native AOT chat' \
    './artifacts/client/Demo.Client Alice'
tmux -L "$socket" set-option -g status off
tmux -L "$socket" set-option -g pane-border-status top
tmux -L "$socket" set-option -g pane-border-format ' #{pane_title} '
tmux -L "$socket" set-option -g pane-border-style 'fg=colour240'
tmux -L "$socket" set-option -g pane-active-border-style 'fg=colour44'
tmux -L "$socket" select-pane -t chat:0.0 -T Alice
tmux -L "$socket" split-window -h -t chat:0 './artifacts/client/Demo.Client Bob'
tmux -L "$socket" select-pane -t chat:0.1 -T Bob
tmux -L "$socket" select-pane -t chat:0.0
tmux -L "$socket" attach-session -t chat
