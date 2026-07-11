# P0-04 Projection CLI — 검증 문서

## ① 주체 (actor)

- **실행자**: claude-sonnet-4-6 (Claude Code, 조율자 지시 수신)
- **작업 ID**: P0-04
- **날짜**: 2026-07-11

## ② 사용한 하네스와 결과

| 하네스 명령 | exit code | 핵심 수치 / 비고 |
| --- | --- | --- |
| `dotnet build -c Release` | 0 | 경고 0, 오류 0 |
| `dotnet run -c Release -- projection` (1회차) | 0 | stamped=3, missingFiles=0, FIX-07 changedFiles sha256 채움 |
| `dotnet run -c Release -- handoff-integrity` (1회차) | 0 | verdict=PASS, failureCount=0, warningCount=0 |
| `dotnet run -c Release -- projection` (2회차 idempotent) | 0 | stamped=3, missingFiles=0 — 결과 동일 확인 |
| `dotnet run -c Release -- handoff-integrity` (2회차) | 0 | verdict=PASS, failureCount=0, warningCount=0 |
| `dotnet run -c Release -- measure dev-pack` | 1 | violationCount=1 — 기준선과 동일, 비악화 확인 |
| `dotnet run -c Release -- build-verify` | 0 | verdict=PASS, locked=false, 경고 0 오류 0 |
| `dotnet run -c Release -- verify-behavior` | 0 | behaviorEqual=true |

### 실제 실행 결과 (주요 JSON)

**build (Release):**
```
경고 0개, 오류 0개
```

**projection 1회차 (FIX-07 sha256 스탬핑):**
```json
{"ok":true,"stamped":3,"missingFiles":0,"runtimeIndex":"docs/context/RUNTIME-INDEX.md","handoff":"docs/handoff/HANDOFF.md"}
```

**handoff-integrity (exit 0 — 이 작업의 존재 이유):**
```json
{"harness":"handoff-integrity","diId":"FIX-07","status":"done","changedFileCount":3,"failureCount":0,"warningCount":0,"verdict":"PASS"}
```

**measure dev-pack (비악화):**
```json
{"projectId":"dev-pack","violationCount":1}
```
> 기준선 violationCount=1 (코덱스 몫, 이 작업 전부터 존재). 내가 새 위반 추가하지 않음.  
> 중간에 GenerateHandoff 99줄 위반(maxFunctionLength) 발생 → AppendHandoffHeader 등 4개 헬퍼로 분할 → 재측정 violationCount=1로 복귀.

**build-verify:**
```json
{"harness":"build-verify","exitCode":0,"verdict":"PASS","locked":false}
```

**verify-behavior:**
```json
{"behaviorEqual":true,"snapshot":"docs/behavior-snapshot.json"}
```

## ③ 참조한 스킬

- `docs/directives/_header.md` (불변 제약 + 검수 기준)
- `docs/plan/INTENT-DIGEST.md` (핵심 철학)
- `AGENT-GUIDE.md` (API 수명주기, 기여 방법)

## 지표는 만족했으나 목적은 미달인 부분 (자진 신고 — ADR-005)

**자진 신고 1건**: WORKSTATE diId를 P0-04로 갱신하지 못함.

- **지표**: handoff-integrity exit 0 ✓ (FIX-07 sha256 채워진 상태)
- **목적 미달**: `v9 산출물`에 "WORKSTATE 갱신(diId P0-04)"이 요구됨. P0-04로 갱신 시도했으나 `SONNET-QUEUE.md`(allowlist 밖)에 P0-04가 "**진행**(PID 9804)"으로 등재돼 있어 handoff-integrity가 `queue-status-mismatch` failure를 발생시켜 exit 1이 됨.
- **SONNET-QUEUE.md는 allowlist 밖**이므로 수정 불가.
- **대안 선택**: WORKSTATE를 FIX-07(done, sha256 채워짐) 상태로 유지 → 원래 목표(missing-hash → exit 0) 달성.
- **조율자 조치 필요**: SONNET-QUEUE.md에서 P0-04를 "완료" 상태로 표시 후 `dotnet run -- projection` 재실행하면 diId P0-04로 갱신 가능.

## 직접 경로 사유

허용 파일(server/ProjectionCli.cs, server/Cli/CliRouter.cs, docs/context/RUNTIME-INDEX.md, docs/handoff/HANDOFF.md, docs/handoff/WORKSTATE.json, docs/verification/p004-projection.md, docs/directives/P004-projection.md)만 수정함. docs/verification/*, docs/directives/* 는 관례상 직접 경로 허용. server/ 코드는 지시서 §작업 1번에 명시됨.
