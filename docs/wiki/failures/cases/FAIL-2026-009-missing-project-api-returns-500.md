# FAIL-2026-009 — 없는 projectId 조회가 4xx 대신 500을 반환

- 상태: 확인됨
- 최초 발생일: 2026-07-11
- 최근 발생일: 2026-07-11
- 관련 DI: CODEX-AUTO-15min §4.5 E2E 실사용 검사
- 구성요소: project-api-read
- failureClass: error_handling, design_learning
- 심각도: medium

## 발생 상황

코덱스 15분 루틴의 idle E2E 검사 중, 정상 프로젝트 조회와 잘못된 ID 조회를 비교했다. 정상 프로젝트의 `/state` 조회는 200을 반환했지만, 존재하지 않는 projectId에 대한 `/state`, `/context` 조회는 500 Internal Server Error를 반환했다.

E2E 지시서의 엣지·에러 처리 기준은 없는 projectId, 잘못된 taskId, malformed 입력이 4xx로 정상 거부되는 것이다. 따라서 없는 projectId 조회에서 500이 노출되는 것은 실사용 결함이다.

## 관찰된 증상과 영향

재현 명령:

```powershell
dotnet run --project server -c Release --no-build
Invoke-WebRequest -Uri 'http://localhost:5173/api/projects/dev-pack/state' -UseBasicParsing
Invoke-WebRequest -Uri 'http://localhost:5173/api/projects/__missing__/state' -UseBasicParsing
Invoke-WebRequest -Uri 'http://localhost:5173/api/projects/__missing__/context' -UseBasicParsing
```

실측:

| 요청 | 실제 상태 |
| --- | --- |
| `GET /api/projects/dev-pack/state` | 200 |
| `GET /api/projects/__missing__/state` | 500 |
| `GET /api/projects/__missing__/context` | 500 |
| `GET /api/outbox/__missing__` | 404 |

영향:

- 사용자가 잘못된 프로젝트 ID를 열 때 클라이언트가 일반적인 404/400 처리 대신 서버 오류로 인식한다.
- E2E 기준상 “사용자 실수는 4xx로 정상 거부”라는 API 계약을 어긴다.
- 서버 내부 예외/오류 경로가 사용자 입력 검증 실패와 구분되지 않는다.

## 발생 이유

확정된 사실:

- 없는 outbox task는 404를 반환한다.
- 없는 projectId의 `state`, `context`는 500을 반환한다.

추정:

- 프로젝트 존재 검증 또는 파일 경로 검증 실패가 HTTP 4xx 도메인 오류로 매핑되지 않고 일반 예외 처리 경로로 흘러 500으로 변환되는 것으로 보인다.

## 제안 수정 방향

- `ReadFile`, `ProjectContext`, 관련 project read 경로에서 없는 projectId를 404 또는 400으로 명시 변환한다.
- 파일 없음, 프로젝트 없음, 경로 검증 실패를 내부 오류와 분리한다.
- `GET /api/projects/__missing__/state`, `/context`, `/measurement`, `/cycle-summary`가 모두 4xx를 반환하는 E2E/CLI 하네스를 추가한다.

## 판정 기준

없는 projectId에 대한 조회성 API가 500을 반환하지 않고 4xx JSON 오류로 응답해야 한다. 정상 projectId 조회는 기존처럼 200을 유지해야 한다.

## 검증 결과

2026-07-11 E2E:

| 단계 | 명령/요청 | 결과 |
| --- | --- | --- |
| 서버 실행 | `dotnet run --project server -c Release --no-build` | 5173 listen |
| 정상 조회 | `GET /api/projects/dev-pack/state` | 200 |
| 엣지 조회 | `GET /api/projects/__missing__/state` | 500 |
| 엣지 조회 | `GET /api/projects/__missing__/context` | 500 |
| 비교 조회 | `GET /api/outbox/__missing__` | 404 |

2026-07-11 22:30 KST 재현:

| 요청 | 실제 상태 | 응답 요약 |
| --- | ---: | --- |
| `GET /data/projects.json` | 200 | 정상 |
| `GET /api/projects/dev-pack/state` | 200 | 정상 |
| `GET /api/projects/__missing__/state` | 500 | `reasonCode=system.read_failed`, `Project is not registered: __missing__` |
| `GET /api/projects/__missing__/context` | 500 | missing project 오류 |
| `GET /api/projects/__missing__/measurement` | 500 | missing project 오류 |
| `GET /api/projects/__missing__/cycle-summary` | 500 | missing project 오류 |
| `GET /api/outbox/__missing__` | 404 | `reasonCode=dispatch.not_found` |

재현 환경: 기존 실행 중 서버 `LocalFirstWorkflowDashboard.Server` PID `30040` (`C:\Users\1\wf-server-run\LocalFirstWorkflowDashboard.Server.exe`). 코덱스가 띄운 임시 서버 시도는 포트 점유로 시작 실패했고, 기존 리스너를 대상으로 GET 요청만 수행했다.

## 재발 방지

- FEAT-02 `e2e-usage` 하네스에 없는 projectId 조회 시나리오를 포함한다.
- 조회성 API는 “없음/잘못된 입력 = 4xx, 서버 결함 = 5xx” 계약을 공통화한다.
