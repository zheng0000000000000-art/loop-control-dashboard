# Review FEAT-01 conditional delegation

검수 대상: `docs/verification/feat01-conditional-delegation.md`, 현재 HEAD `8e20ed2` 기준 FEAT-01 주장
검수자: codex
일시: 2026-07-11 18:15 KST

## 대상 파악

- `WORKSTATE.json`: `diId=FEAT-01`, `status=verifying`.
- 주장 변경 파일: `server/Tier2Approver.cs`, `server/Tier2ApproverTestCli.cs`.
- 최신 `8e20ed2` 자체는 `scope-check` 승격/allowlist 문서 커밋이며 서버 파일 변경은 없다.
- `docs/verification/feat01-conditional-delegation.md`는 FEAT-01 자체검증 결과로 8개 tier2test 시나리오 PASS, measure violations=3을 주장한다.

## 독립 재실행

| 항목 | 결과 |
| --- | --- |
| `dotnet build server -c Release` | FAIL. 실행 중 서버 PID 14252가 `bin/Release/...Server.exe`를 잠가 apphost 복사 실패. warnings 10, errors 2 |
| `dotnet run --project server -c Release --no-build -- verify-behavior` | PASS. `behaviorEqual:true` |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | FAIL 기준. exit 1, `violationCount:4` |
| `dotnet run --project server -c Release --no-build -- tier2test` | exit 1, `usage: tier2test <scenario>` |
| `tier2test disabled` | PASS. `decision:"skipped"` |
| `tier2test eligible-approved` | FAIL. `DirectoryNotFoundException` writing `docs/audit/tier2-import-approvals-state.json` |
| `tier2test core-file-touched` | FAIL. `DirectoryNotFoundException` writing `outbox/task-0000/meta.json` |
| `tier2test baseline-file-touched` | PASS. `decision:"blocked_ineligible"` |
| `tier2test violations-increased` | PASS. `decision:"blocked_ineligible"` |
| `tier2test daily-cap` | FAIL. `DirectoryNotFoundException` writing `docs/audit/tier2-import-approvals-state.json` |
| `tier2test anomaly-halt` | PASS. `firstDecision:"anomaly_halted"`, `secondDecision:"blocked_halted"` |
| `tier2test reviewer-unavailable` | PASS. `decision:"reviewed_not_approved"` |
| `doc-integrity` | PASS. checked 12, `brokenCount:0` |
| `hs-scan` | exit 1, HS-GATE 의무 지속 |

## 주장 vs 실측 불일치

- 자체검증 문서 주장: `measure dev-pack` violations=3. 실측: violations=4.
- 자체검증 문서 주장: tier2test 8개 시나리오 PASS. 실측: `eligible-approved`, `core-file-touched`, `daily-cap`이 예외 종료.
- 자체검증 문서 주장: build 0/0. 실측: universal protocol의 필수 `dotnet build server -c Release`는 PID 14252 잠금으로 실패.

## 판정

판정: FAIL

이유: 필수 build 실패, measure 위반 증가/문서 주장 불일치, tier2test 시나리오 3개 예외 재현. `FAIL-2026-014`로 자산화했다.
