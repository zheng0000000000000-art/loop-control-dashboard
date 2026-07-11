# P0-06 scope-check claim extension QA

- actor: codex
- target: P0-06 `scope-check` extension
- files touched: `server/Harness/ScopeCheckCli.cs`, `docs/qa/scope-check-claims-p0-06-2026-07-12.md`, `docs/handoff/sessions/SESSION-2026-07-12-codex-053.md`
- original claims file: `docs/handoff/FILE-CLAIMS.json` read only

## Exit code policy

- `exit 1`: a changed file overlaps another actor's `status: active` claim, or the existing directive allowlist check finds out-of-scope files.
- `exit 0`: no conflict and no out-of-scope file, even if warning-only claims exist.
- `staleClaims` are warnings because PID death needs human cleanup of the ledger.
- `unknownAllowlistClaims` are warnings because `allowlistSource: null` means "allowlist unknown", not an empty allowlist.

## Fault injection

All injection files were written under `$env:TEMP`; `docs/handoff/FILE-CLAIMS.json` was not modified.

| Check | Command | Exit | Evidence |
| --- | --- | ---: | --- |
| Build | `dotnet build server -c Release` | 0 | warnings 0, errors 0 |
| Conflict injection | `dotnet run --project server -c Release --no-build -- scope-check <temp allow-all directive> --claims <temp conflict claims> --actor codex` | 1 | `claimConflictCount: 1`, file `server/Harness/ScopeCheckCli.cs`, actor `sonnet`, taskId `INJECT-CONFLICT`, live pid |
| Stale injection | `dotnet run --project server -c Release --no-build -- scope-check <temp allow-all directive> --claims <temp stale claims> --actor codex` | 0 | `staleClaimCount: 1`, `unknownAllowlistClaimCount: 1`, pid `999999` |
| Normal current claims | `dotnet run --project server -c Release --no-build -- scope-check <temp allow-all directive> --actor codex` | 0 | default `docs/handoff/FILE-CLAIMS.json`, all claims released, conflict/stale counts 0, released smoke claim reported as `unknownAllowlistClaimCount: 1` |
| Final gate | `dotnet run --project server -c Release -- measure dev-pack` | 0 | `{"gate":"dev-pack","violations":0,"attempt":1}` |

## Notes

- The harness only reads the claim ledger. It does not release, expire, delete, revert, or kill anything.
- `allowlistSource: null` is reported even on released claims because it means the allowlist was unknown, not safely empty.
- `expiresAt` is emitted as context in warning rows, but stale detection is based on PID liveness.
- Existing directive allowlist behavior remains in the same verdict path through `outOfScopeCount`.
