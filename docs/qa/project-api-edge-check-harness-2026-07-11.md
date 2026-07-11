# project-api-edge-check harness — 2026-07-11

## 개요

- actor: codex
- 작업: `FAIL-2026-009` HTTP edge 재현을 회귀 하네스로 고정
- 산출물: `server/Harness/ProjectApiEdgeCheckCli.cs`, `server/Harness/HarnessRegistry.cs`
- 등록 방식: `HarnessRegistry` 표에 `project-api-edge-check` 한 줄 추가. `server/Cli/CliRouter.cs`는 수정하지 않음.
- 참조한 스킬/문서: `docs/handoff/CODEX-AUTO-15min-routine.md`, `skills/common/root-cause-diagnosis.md`, `skills/common/hs-gate.md`, `docs/wiki/failures/cases/FAIL-2026-009-missing-project-api-returns-500.md`

## 데이터 존재 관문

이 하네스가 보는 데이터는 실제 HTTP 응답의 status code와 body다.

| 데이터 | 실체 여부 | 근거 |
| --- | --- | --- |
| 정상 API 상태 | 실체 | `GET /data/projects.json` = 200, `GET /api/projects/dev-pack/state` = 200 |
| 결함 API 상태 | 실체 | missing project read API 4개가 HTTP 500 반환 |
| 비교 기준 | 실체 | `GET /api/outbox/__missing__` = 404 |

커밋 메시지, 로그 문구, 자기보고 같은 프록시를 사용하지 않는다.

## 사용한 하네스와 결과

| 명령 | exit code | 핵심 수치 |
| --- | ---: | --- |
| `dotnet run --project server -c Release -- hs-scan` | 1 | `failureCaseCount=14`, candidate=`executor-orchestration(6)` |
| `dotnet build server -c Release` | 0 | 경고 0, 오류 0 |
| `dotnet run --project server -c Release --no-build -- project-api-edge-check http://127.0.0.1:5173` | 1 | `checkCount=7`, `failureCount=4`, `verdict=FAIL` |
| `dotnet run --project server -c Release --no-build -- project-api-edge-check not-a-url` | 1 | 사용법 오류, non-zero exit |
| `dotnet run --project server -c Release --no-build -- build-verify` | 0 | `verdict=PASS` |
| `dotnet run --project server -c Release --no-build -- gate-clean server/Harness` | 1 | `contentDirtyCount=2` — 이번 하네스 변경 2건 |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | 1 | `violationCount=1`, 잔여 `dashboard/app.js` |

`project-api-edge-check`가 exit 1인 것은 현재 `FAIL-2026-009`가 재현되기 때문이다. 이 하네스의 목표는 수정 전에는 실패하고, 수정 후에는 exit 0으로 바뀌는 회귀 검사다.

## 내장 검사

| 케이스 | 기대 |
| --- | --- |
| `GET /data/projects.json` | 200 |
| `GET /api/projects/dev-pack/state` | 200 |
| `GET /api/projects/__missing__/state` | 4xx |
| `GET /api/projects/__missing__/context` | 4xx |
| `GET /api/projects/__missing__/measurement` | 4xx |
| `GET /api/projects/__missing__/cycle-summary` | 4xx |
| `GET /api/outbox/__missing__` | 404 |

## 판정

- 재현된 진짜 버그: 1건 — `FAIL-2026-009`
- 의심: 0건
- 오탐: 0건
- 하네스 제작: 완료

다음 sonnet 수정 후 `dotnet run --project server -c Release --no-build -- project-api-edge-check http://127.0.0.1:5173`이 exit 0이 되어야 한다.
