# Artery reconnect warnings after a graceful client disconnect

Confirmed with Akka.NET `1.6.0-beta3` on Linux x64. This is a transport association lifecycle issue exposed by a graceful disconnect; the chat user really does leave successfully. It reproduces in a managed process too, so Native AOT and Termina are not prerequisites.

## Reproduce

From the repository root with the .NET 10 SDK:

```sh
dotnet run scripts/repro-disconnect.cs
```

The standalone diagnostic uses the real `ChatServerActor` and a minimal diagnostic client actor using only the existing chat wire messages. It starts both actor systems on ephemeral loopback ports, joins, sends the normal `/exit` command, waits for the successful `LeaveChat` response, and stops the client host normally. It then leaves the server alive without application traffic for 25 seconds.

The diagnostic returns **1 when a repeated control reconnect warning (`Still unable to reconnect ...`) is observed**, and 0 when none is observed in that window. A single initial disconnect warning does not fail the diagnostic. It is intentionally separate from the passing CI suite. It does not bind port 8081 or stop a running demo.

## Observed behavior

A local run on October 27, 2026 showed this sequence:

```text
[REPRO] Server acknowledged LeaveChat; stopping client host normally
20:31:15.393 [ReproClient] Artery TCP remoting shut down
[REPRO] Client host stopped; leaving server idle for 25 seconds
20:31:25.343 [ReproServer] Artery Control outbound connection to
  akka://ReproClient@127.0.0.1:36799 failed ... Broken pipe
20:31:26.439 Restarting graph due to failure ... Connect(127.0.0.1:36799) failed
20:31:27.568 Restarting graph due to failure ... Connect(127.0.0.1:36799) failed
20:31:35.019 Still unable to reconnect Artery Control outbound connection ... attempt 10
[REPRO] Observed 1 repeated control reconnect warnings after graceful disconnect
```

The server keeps reconnecting to the departed client's ephemeral port. The first failed control connection is reported as a warning, followed by periodic summaries such as `Still unable to reconnect ... attempt 10`. Akka.Streams' inner connection restart also emits `Restarting graph due to failure` warnings. The loop ends when the surviving server shuts down; there is no finite retry limit for this association.

## Cause in core Akka.NET

The inspected core checkout was commit `939278954a6912723aa6868f79a386b3b10d94ef`:

- [`ArteryRemoting.MaterializeOutboundStream`](https://github.com/akkadotnet/akka.net/blob/939278954a6912723aa6868f79a386b3b10d94ef/src/core/Akka.Remote/Artery/ArteryRemoting.cs#L2096) explicitly schedules reconnects even after a graceful peer-side socket close.
- [`ScheduleOutboundRestart`](https://github.com/akkadotnet/akka.net/blob/939278954a6912723aa6868f79a386b3b10d94ef/src/core/Akka.Remote/Artery/ArteryRemoting.cs#L2494) restarts a control stream whenever `ShouldRestartControl()` is true.
- [`Association.ShouldRestartControl`](https://github.com/akkadotnet/akka.net/blob/939278954a6912723aa6868f79a386b3b10d94ef/src/core/Akka.Remote/Artery/AssociationRegistry.cs#L755) only checks whether **this side** has permanently closed the association's control channel. Quarantine deliberately does not stop control reconnects.
- [`ArteryControlMessages`](https://github.com/akkadotnet/akka.net/blob/939278954a6912723aa6868f79a386b3b10d94ef/src/core/Akka.Remote/Artery/ArteryControlMessages.cs) has heartbeats, heartbeat replies, and quarantine notifications, but no peer-shutdown/goodbye notification. A closed TCP connection alone cannot safely distinguish a departing actor system from a transient network break.

The current behavior follows the initial unlimited-reconnect design. What is missing is a way to retire or quiesce an association that no longer has any work after its peer leaves. Hiding the warnings would leave the connection attempts and association state alive.

## Suggested upstream work

Add a demand-aware idle/quiescent association lifecycle, or an explicit UID-scoped peer-shutdown notification with compatibility handling. A quiesced association must be able to reconnect on subsequent demand; simply setting its permanent shutdown flags on any socket close would break transient recovery and same-address actor-system restarts.

Regression coverage should establish all of these:

1. A graceful peer departure with no queued or unacknowledged work eventually stops outbound connection attempts and repeated warnings while the surviving system remains running.
2. Transient connection failure with pending reliable system messages still reconnects and preserves delivery state.
3. New application traffic can reconnect after an idle association, including when a different peer UID appears at the same address.
4. A stale shutdown/close event cannot retire a replacement incarnation.
5. Ordinary, control, and large-message streams all obey the same retirement decision without dropping buffered work silently.

No upstream transport fix or warning-suppression workaround is included in this demo change.
