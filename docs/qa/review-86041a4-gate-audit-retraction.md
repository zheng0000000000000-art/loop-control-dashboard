# Review 86041a4 / gate-audit retraction flow

검수 대상: `86041a4` 중심, 직전 서버 변경 `c8fe1dd`/`8a0fccb` 포함
검수자: codex
일시: 2026-07-11 16:30 KST

## 대상 파악

- `c8fe1dd`: `server/GateAuditCli.cs` 삭제, `server/Cli/CliRouter.cs`에서 `gate-audit` 라우팅 제거.
- `8a0fccb`: `server/DocIntegrityCli.cs` 감시목록 확장.
- `86041a4`: FAIL-2026-012 문서화, `gate-audit`/HS-04 철회, ACTOR-01 지시서 추가, hs-gate 스킬에 데이터 존재 확인 관문 추가.
- `docs/handoff/WORKSTATE.json`은 아직 FEAT-02 verifying 상태로 남아 있어 최신 harness 철회 흐름과 불일치한다.

## 독립 재실행

| 항목 | 결과 |
| --- | --- |
| `dotnet build server -c Release` | exit 0, warnings 0, errors 0 |
| `dotnet run --project server -c Release --no-build -- verify-behavior` | exit 0, `behaviorEqual:true` |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | exit 1, `violationCount:3` |
| `dotnet run --project server -c Release --no-build -- gate-clean server` | exit 0, `gate:"PASS"`, `contentDirtyCount:0` |
| `dotnet run --project server -c Release --no-build -- doc-integrity` | exit 0, checked 12, `brokenCount:0`, `verdict:"INTACT"` |
| `dotnet run --project server -c Release --no-build -- hs-scan` | exit 1, `triggered:true`, 후보 3건 |
| `dotnet run --project server -c Release --no-build -- claim-check FEAT-02` | exit 0, `verdict:"MATCH"` |
| `dotnet run --project server -c Release --no-build -- gate-audit` | timeout 124s |

`gate-audit` 호출 타임아웃은 라우팅 문자열이 남아서가 아니라, 미등록 CLI 인자가 서버 기동 경로로 떨어지는 기존 동작으로 보인다. `rg` 기준 `server/`와 `docs/handoff`에서 활성 `gate-audit` 라우팅/CLI 구현은 남아 있지 않았다.

## 불변식

- 기준 파일 무수정: `git diff --stat c8fe1dd^..86041a4 -- "**/blueprint.json" "**/workflow-definition.json" server/appsettings.json` 빈 결과.
- 영역 격리: 서버 변경은 `CliRouter` 라우팅 제거와 `DocIntegrityCli` 감시목록 확장에 한정. 최신 `86041a4`는 문서/스킬/outputs만 변경.
- 비밀 미포함: `server/appsettings.json` diff 없음.
- 검증 문서: `docs/verification/`에 `gate-audit` 철회/DocIntegrity 감시목록 확장 전용 verification 문서는 확인되지 않음.

## 주장 vs 실측

- 일치: `gate-audit` 하네스 철회 문서와 `server/GateAuditCli.cs` 삭제, `CliRouter` 라우팅 제거가 일치한다.
- 일치: `doc-integrity` 감시목록 확장 주장은 실측 checked 12로 확인된다.
- 일치: FAIL-2026-012 위키와 failure index 반영 확인.
- 주의: 철회된 `gate-audit` 명령을 직접 실행하면 즉시 unknown-command 오류가 아니라 서버 기동 경로로 빠져 타임아웃된다. 문서상 "하네스 철회"와는 충돌하지 않지만, stale CLI 호출 사용자 경험은 별도 개선 후보다.
- 주의: `WORKSTATE.json`은 FEAT-02 verifying 상태라 최신 sonnet/조율자 상태와 맞지 않는다.

## 판정

판정: 조건부 PASS

이유: build 0/0, behavior true, measure 기준선 3 유지, 핵심 철회/문서 주장은 실측과 일치한다. 다만 해당 철회 흐름 전용 verification 산출물이 없고, `WORKSTATE.json`이 최신 상태를 반영하지 않는다.
