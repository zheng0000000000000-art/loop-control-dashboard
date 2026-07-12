# launch-check transport receipt QA

- actor: codex
- target: `server/Harness/LaunchCheckCli.cs`
- scope: `server/Harness/`, `docs/qa/`, `docs/handoff/sessions/`
- direct-path reason: existing harness replacement in codex-owned `server/Harness/`

## Contract

`launch-check <taskId> <transportEvidencePath>` reads the launch wrapper evidence file, normally `outputs/launch/<TaskId>.transport.json`.

Required evidence fields:

```json
{
  "schemaVersion": 1,
  "taskId": "LEDGER-05",
  "cliVersion": "...",
  "sourceSha256": "<64 hex chars>",
  "payloadSha256": "<64 hex chars>",
  "replaySha256": "<64 hex chars>",
  "sourceByteLength": 0,
  "payloadByteLength": 0,
  "replayByteLength": 0,
  "replayEventCount": 1,
  "pid": 0,
  "startedAt": "...",
  "exitedAt": "...",
  "verdict": "TRANSPORT_VALID"
}
```

The harness passes only when `payloadSha256 == replaySha256`, `replayEventCount == 1`, all required fields are valid, and no command-line prompt fallback marker is present. It records hashes and byte lengths only; prompt bodies are not copied into harness output.

`docs/handoff/LAUNCH-BUDGET.json` was absent in this workspace, so budget checking was `skipped` and did not affect exit codes.

## Harness Results

| Case | Command | Exit | Result |
| --- | --- | ---: | --- |
| current missing evidence before replacement | `dotnet run --project server -c Release -- launch-check LEDGER-05 outputs/launch/LEDGER-05.transport.json` | 1 | old ACK harness failed because the file did not exist |
| current missing evidence after replacement | same command | 1 | `evidence-missing`, `verdict=TRANSPORT_INVALID` |
| encoding damage fixture | temp evidence with UTF-8 payload hash and PowerShell-default encoded replay hash | 1 | mismatch detected |
| UTF-8 direct fixture | temp evidence with matching UTF-8 payload/replay hashes | 0 | `TRANSPORT_VALID` |
| multilingual fixture | Korean, emoji, quotes, backslash, CRLF/LF | 0 | `TRANSPORT_VALID` |
| no replay event | `replayEventCount=0` | 1 | fail-closed |
| two replay events | `replayEventCount=2` | 1 | fail-closed |
| tampered JSON | malformed evidence JSON | 1 | fail-closed |
| one-character changed | replay hash from payload plus one character | 1 | mismatch detected |
| long prompt | 12,000 repeated lines through evidence file | 0 | independent of command-line length |
| command-line fallback marker | `inputTransport=command-line-argument` | 1 | fallback detected |
| build | `dotnet build server -c Release` | 0 | warnings 0, errors 0 |
| forbidden phrase scan | searched `server/Harness/LaunchCheckCli.cs` for removed ACK variables, quota helpers, and model-scope verdict terms | 1 | no matches |
| dev-pack gate | `dotnet run --project server -c Release -- measure dev-pack` | 0 | `{"gate":"dev-pack","violations":0,"attempt":1}` |

Fixture directory used for the injection run:

```text
C:\Users\1\AppData\Local\Temp\lfwd-launch-check-c9b8ca5d9d2c4bf7bc4b8360f3da8d43
```

## Referenced Skills

- `skills/common/verification.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/directive-writing.md`
- `skills/common/executor-launch.md`
- `skills/common/hs-gate.md`
- `skills/domains/dev/file-navigation.md`
- `skills/domains/dev/path-escape-qa.md`
