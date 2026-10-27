# Akka.NET 1.6 Native AOT chat

A small chat server with multiple [Termina](https://github.com/Aaronontheweb/termina) TUI clients. Both applications compile to native executables; running them requires no .NET installation.

## Run native clients

Publishing requires the .NET 10 SDK from `global.json`, PowerShell 7, and the [Native AOT build prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/#prerequisites).

```powershell
# Linux x64 (use win-x64 on Windows or osx-arm64 on Apple Silicon)
./scripts/publish-native.ps1 -RuntimeIdentifier linux-x64
```

Start the server, then launch each client in its own terminal:

```sh
./artifacts/server/Demo.Server
```

```sh
./artifacts/client/Demo.Client Alice
```

```sh
./artifacts/client/Demo.Client Bob
```

On Windows, use `artifacts/server/Demo.Server.exe` and `artifacts/client/Demo.Client.exe`.

Type a message and press Enter. `/nick name` changes your display name, `/edit text` edits your last message, and `/exit` leaves. Up/Down recalls input; PageUp/PageDown scrolls the transcript. Each client gets a unique user ID and an ephemeral port, so multiple copies run together without configuration.

For development, use `dotnet run --project src/Demo.Server` and `dotnet run --project src/Demo.Client -- Alice`.

## Code

- `Demo.Core`: the existing commands, events, value types, source-generated serializer, and shared Artery configuration.
- `Demo.Server`: one actor tracks users and message ownership, validates commands, and broadcasts events.
- `Demo.Client`: Termina sends local `Connect` and `Submit` messages to its local `ChatUserActor`. That actor handles the network protocol and posts display updates back through Termina's UI dispatcher. The page owns layout and input; the ViewModel keeps the last 200 transcript lines.

The wire protocol is unchanged: `JoinChat`, `ChangeUserName`, `LeaveChat`, `SendMessage`, `EditMessage`, their existing events, and `ChatResponse`. Joining sends the newcomer existing users' `JoinedChat` events. Successful and rejected commands return `ChatResponse` with `OriginalCommand`. The server watches client actors to remove disconnected users. There is no persistence, message history replay, or automatic reconnect.

## Test

```sh
dotnet test -c Release

# After native publishing; Linux/macOS, Python 3 standard library only
python3 scripts/smoke-chat.py
```

The Akka.Hosting.TestKit suite covers every protocol message and every polymorphic response member; records and readonly record structs; GUIDs; Unicode and long strings; empty/default IDs; enums, including unknown values; nullable fields; actor references; unknown fields and missing optional fields; malformed payloads; and Hosting serializer registration with reflection-based loading disabled. A second actor system verifies every message and response union member over an actual Artery TCP connection. Actor tests cover chat behavior, ownership validation, and local TUI input translation.

The native smoke test starts the server and real TUI clients in pseudo-terminals. It verifies joining, both directions of chat, renaming, editing, leaving, clean client exits, and DeathWatch cleanup after a third client crashes. Server errors fail the test.

## Native AOT details

This demo uses **experimental Artery TCP remoting**, bound to loopback on port 8081. Classic DotNetty remoting is not supported under Native AOT. Both apps set `PublishAot`; Akka's NuGet build targets disable `Akka.DynamicTypeLoading` for native publishing. Application serialization uses the generated MessagePack serializer registered by `WithChatSerializer`.

Akka.NET 1.6.0-beta3 still loads the DeathWatch failure detector by name. `ChatRemoting` preserves only `PhiAccrualFailureDetector`'s public constructors using `DynamicDependency`, so remote watches work after trimming. The remaining known dependency warning locations are recorded in `scripts/aot-warnings.txt`: classic transport/adapters, remote deployment, protobuf activation, exception deserialization, and the failure detector loader. They remain visible during publishing; `publish-native.ps1` rejects additional warning locations. The demo does not use remote deployment or custom transports/serializers.

CI builds and tests on Linux and Windows, publishes both native apps, runs the native multi-client smoke test on Linux, and uploads native binaries as workflow artifacts. Windows native publishing is checked in CI; macOS is not covered by this workflow.
