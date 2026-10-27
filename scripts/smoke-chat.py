#!/usr/bin/env python3
"""Exercise real native Termina clients in POSIX pseudo-terminals or Windows ConPTY."""
import os
from pathlib import Path
import queue
import re
import signal
import subprocess
import sys
import threading
import time

WINDOWS = os.name == "nt"
if WINDOWS:
    from winpty import PtyProcess
else:
    import fcntl
    import pty
    import struct
    import termios


def screen_text(output):
    # Termina writes changed cells at absolute cursor positions. Reconstruct the
    # screen so assertions check what a person sees, rather than raw ANSI bytes.
    screen = [[" "] * 120 for _ in range(30)]
    row = column = 0
    for token in re.findall(r"\x1b\[[0-?]*[ -/]*[@-~]|\x1b.|[^\x1b]+", output.decode(errors="replace")):
        if token.startswith("\x1b["):
            values = token[2:-1]
            if token[-1] in "Hf":
                position = (values or "1;1").split(";")
                row = int(position[0] or 1) - 1
                column = int(position[1] or 1) - 1 if len(position) > 1 else 0
            elif token[-1] == "J" and values in ("2", "3"):
                screen = [[" "] * 120 for _ in range(30)]
        elif not token.startswith("\x1b"):
            for char in token:
                if char == "\r":
                    column = 0
                elif char == "\n":
                    row += 1
                else:
                    if 0 <= row < 30 and 0 <= column < 120:
                        screen[row][column] = char
                    column += 1
    return "\n".join("".join(line) for line in screen)


class Client:
    def __init__(self, binary, name):
        self.name = name
        self.output = bytearray()
        self.received = queue.Queue()
        if WINDOWS:
            self.process = PtyProcess.spawn([str(binary), name], dimensions=(30, 120))
        else:
            self.master, slave = pty.openpty()
            fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 30, 120, 0, 0))
            self.process = subprocess.Popen(
                [str(binary), name], stdin=slave, stdout=slave, stderr=slave,
                env={**os.environ, "TERM": "xterm-256color"},
            )
            os.close(slave)
        threading.Thread(target=self.read_output, daemon=True).start()

    def read_output(self):
        try:
            while True:
                data = self.process.read(65536).encode() if WINDOWS else os.read(self.master, 65536)
                if not data:
                    break
                self.received.put(data)
        except (EOFError, OSError):
            pass
        finally:
            self.received.put(None)

    def is_running(self):
        return self.process.isalive() if WINDOWS else self.process.poll() is None

    def wait_for(self, text, timeout=20):
        deadline = time.monotonic() + timeout
        while text not in screen_text(self.output):
            if time.monotonic() >= deadline:
                raise AssertionError(f"{self.name} did not display {text!r}:\n{screen_text(self.output)}")
            try:
                data = self.received.get(timeout=0.1)
            except queue.Empty:
                continue
            if data is None:
                raise AssertionError(f"{self.name} exited before displaying {text!r}:\n{screen_text(self.output)}")
            self.output.extend(data)

    def press(self, keys):
        if WINDOWS:
            self.process.write(keys)
        else:
            os.write(self.master, keys.encode())

    def send(self, text):
        self.press(text + "\r")

    def assert_no_line(self, text):
        assert text not in [line.strip() for line in screen_text(self.output).splitlines()], text

    def wait_exit(self, timeout=15):
        deadline = time.monotonic() + timeout
        while self.is_running():
            if time.monotonic() >= deadline:
                raise AssertionError(f"{self.name} did not exit within {timeout} seconds")
            time.sleep(0.1)
        return self.process.exitstatus if WINDOWS else self.process.returncode

    def crash(self):
        if WINDOWS:
            self.process.kill(signal.SIGTERM)
        else:
            self.process.kill()
        self.wait_exit(timeout=5)

    def close(self):
        if self.is_running():
            self.crash()
        if WINDOWS:
            self.process.close(force=True)
        else:
            os.close(self.master)


def main():
    root = Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts").resolve()
    executable_suffix = ".exe" if WINDOWS else ""
    client_binary = root / f"client/Demo.Client{executable_suffix}"
    clients = []
    log_name = "raw-server-smoke.log" if os.environ.get("TERMINA_RAW_INPUT") == "1" else "server-smoke.log"
    with open(root / log_name, "w+") as log:
        server = subprocess.Popen([str(root / f"server/Demo.Server{executable_suffix}")], stdout=log, stderr=log)
        try:
            deadline = time.monotonic() + 20
            while True:
                log.seek(0)
                output = log.read()
                if "Application started" in output:
                    break
                if server.poll() is not None or time.monotonic() >= deadline:
                    raise AssertionError(f"Server failed to start:\n{output}")
                time.sleep(0.1)

            alice = Client(client_binary, "Alice")
            clients.append(alice)
            alice.wait_for("Connected.")
            bob = Client(client_binary, "Bob")
            clients.append(bob)
            bob.wait_for("Connected.")
            alice.wait_for("Bob joined")
            bob.wait_for("Alice joined")

            alice.send("Hello from Alice")
            alice.wait_for("Alice: Hello from Alice")
            bob.wait_for("Alice: Hello from Alice")
            bob.send("Earlier from Bob")
            alice.wait_for("Bob: Earlier from Bob")
            bob.wait_for("Bob: Earlier from Bob")
            bob.send("/nick Robert")
            alice.wait_for("Bob is now Robert")
            bob.wait_for("Bob is now Robert")
            alice.wait_for("Robert: Earlier from Bob")
            bob.wait_for("Robert: Earlier from Bob")
            alice.assert_no_line("Bob: Earlier from Bob")
            bob.assert_no_line("Bob: Earlier from Bob")
            bob.send("Hello from Robert")
            alice.wait_for("Robert: Hello from Robert")
            bob.wait_for("Robert: Hello from Robert")
            alice.press("\x05")  # Ctrl+E: edit with prefilled input and a highlighted row.
            alice.wait_for("Editing your last message: Hello from Alice")
            alice.wait_for("> Alice: Hello from Alice")
            alice.press("\x1b")  # Escape cancels without changing the message.
            alice.wait_for("Ctrl+E: edit last message")
            alice.press("\x05")
            alice.wait_for("Editing your last message: Hello from Alice")
            alice.send(" revised")  # Appends to the prefilled original text.
            alice.wait_for("Alice: Hello from Alice revised *")
            bob.wait_for("Alice: Hello from Alice revised *")
            alice.wait_for("Ctrl+E: edit last message")
            alice.assert_no_line("Alice: Hello from Alice")
            bob.assert_no_line("Alice: Hello from Alice")
            alice.send("/nick Alicia")
            alice.wait_for("Alicia: Hello from Alice revised *")
            bob.wait_for("Alicia: Hello from Alice revised *")

            charlie = Client(client_binary, "Charlie")
            clients.append(charlie)
            charlie.wait_for("Connected.")
            alice.wait_for("Charlie joined")
            charlie.press("\x03")  # Ctrl+C exits from normal chat.
            assert charlie.wait_exit() == 0
            alice.wait_for("Charlie left")

            dave = Client(client_binary, "Dave")
            clients.append(dave)
            dave.wait_for("Connected.")
            alice.wait_for("Dave joined")
            dave.crash()
            alice.wait_for("Dave left", timeout=40)

            alice.press("\x05")
            alice.wait_for("Editing your last message: Hello from Alice revised")
            alice.press("\x03")  # Ctrl+C also exits while editing.
            assert alice.wait_exit() == 0
            bob.wait_for("Alicia left")
            bob.send("/exit")
            assert bob.wait_exit() == 0
            print("PASS: native TUI chat, in-place edits, rename history, edit guidance, Ctrl+C, and crash cleanup.")
        finally:
            for client in clients:
                client.close()
            if WINDOWS:
                server.terminate()
            else:
                server.send_signal(signal.SIGINT)
            try:
                server.wait(timeout=15)
            except subprocess.TimeoutExpired:
                server.kill()
                server.wait()
            log.seek(0)
            output = log.read()
            if "[ERROR]" in output or "[FATAL]" in output:
                raise AssertionError(f"Server logged errors:\n{output}")


if __name__ == "__main__":
    main()
