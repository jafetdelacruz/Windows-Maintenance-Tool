# Native H04 verification

Local opt-in tool; never invoked by the test suite. Requires Windows x64 and
the operator's confirmation that open work is saved. Runs the standard Windows
composition, including real CPU load, then cancels through CancellationToken
10 seconds after entry to the CPU stage. There are no fake probes or sources.
Run it alone, after a complete CLI run, with no existing interrupted session.

```powershell
dotnet run --project tools/review/WinMaint.NativeStabilityVerification -c Release -- --confirm-work-saved NEW_EVIDENCE_DIRECTORY
```

Acceptance requires observed process CPU activity, CPU Cancelled, RAM/read
Skipped, return within 5 seconds of cancellation, less than 0.25 CPU seconds
during a 2-second observation after return, and a durable interrupted marker.
These CPU observations are supporting process measurements, not task-level
tracing. The application contract awaits workload and observer completion.
The tool saves a product JSON/TXT pair and verification.json, archives its own
interrupted marker, then clears that marker through the native store. It refuses
to overwrite an evidence directory or an existing interrupted session.

This verifies native cooperative cancellation, not the Esc/Ctrl+C keyboard
transport, console presentation, UAC, or a portable final candidate. Reliable
thermal telemetry is unavailable in the current Windows source. No resource
exhaustion, thermal fault, or power failure is induced on the host.
