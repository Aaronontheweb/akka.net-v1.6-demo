#!/usr/bin/env python3
"""Exercise two real Termina clients in pseudo-terminals against the native server (Linux/macOS)."""
import fcntl
import os
from pathlib import Path
import pty
import re
import select
import signal
import struct
import subprocess
import sys
import termios
import time


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
        self.master, slave = pty.openpty()
        fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 30, 120, 0, 0))
        self.process = subprocess.Popen(
            [str(binary), name], stdin=slave, stdout=slave, stderr=slave,
            env={**os.environ, "TERM": "xterm-256color"},
        )
        os.close(slave)

    def wait_for(self, text, timeout=20):
        deadline = time.monotonic() + timeout
        while text not in screen_text(self.output):
            if time.monotonic() >= deadline or self.process.poll() is not None:
                raise AssertionError(f"{self.name} did not display {text!r}:\n{screen_text(self.output)}")
            if select.select([self.master], [], [], 0.1)[0]:
                try:
                    self.output.extend(os.read(self.master, 65536))
                except OSError as error:
                    raise AssertionError(f"{self.name} exited:\n{self.output.decode(errors='replace')}") from error

    def send(self, text):
        os.write(self.master, (text + "\r").encode())

    def close(self):
        if self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait()
        os.close(self.master)


def main():
    root = Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts").resolve()
    clients = []
    with open(root / "server-smoke.log", "w+") as log:
        server = subprocess.Popen([str(root / "server/Demo.Server")], stdout=log, stderr=log)
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

            alice = Client(root / "client/Demo.Client", "Alice")
            clients.append(alice)
            alice.wait_for("Connected.")
            bob = Client(root / "client/Demo.Client", "Bob")
            clients.append(bob)
            bob.wait_for("Connected.")
            alice.wait_for("Bob joined")
            bob.wait_for("Alice joined")

            alice.send("Hello from Alice")
            alice.wait_for("Alice: Hello from Alice")
            bob.wait_for("Alice: Hello from Alice")
            bob.send("/nick Robert")
            alice.wait_for("Bob is now Robert")
            bob.wait_for("Bob is now Robert")
            bob.send("Hello from Robert")
            alice.wait_for("Robert: Hello from Robert")
            bob.wait_for("Robert: Hello from Robert")
            alice.send("/edit Hello edited")
            alice.wait_for("Alice (edited): Hello edited")
            bob.wait_for("Alice (edited): Hello edited")
            bob.send("/exit")
            alice.wait_for("Robert left")
            assert bob.process.wait(timeout=15) == 0

            charlie = Client(root / "client/Demo.Client", "Charlie")
            clients.append(charlie)
            charlie.wait_for("Connected.")
            alice.wait_for("Charlie joined")
            charlie.process.kill()
            charlie.process.wait(timeout=5)
            alice.wait_for("Charlie left", timeout=40)

            alice.send("/exit")
            assert alice.process.wait(timeout=15) == 0
            print("PASS: native TUI clients chatted, renamed, edited, left, and detected a crashed client over Artery.")
        finally:
            for client in clients:
                client.close()
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
