# SESSION-2026-07-11-codex-016

## 확인한 sonnet 작업
- 최근 커밋:
  - `8af4e76` KNOWN-ISSUES + STATUS 갱신
  - `7c3d163` 조율자: docs/qa FIX-02 검수리포트 반영
  - `49ed417` SONNET-QUEUE #2 FIX-02 완료 표시
- `WORKSTATE.json`: 아직 `FIX-02`, `status: verifying`.
- `e2e-usage` CLI는 아직 server에 없음.

## QA 결과
- 신규 sonnet server 커밋은 없음.
- CODEX-AUTO-15min §4.5에 따라 수동 E2E를 수행했다.
- 서버 실행: `dotnet run --project server -c Release --no-build`, `http://localhost:5173`.
- 안전 경계:
  - 승인/거절, outbox 반입, dispatch, contributions 호출 없음.
  - `server/appsettings.json`의 `AutoCommitData:true` 때문에 API `POST /api/projects/{id}/actions/measure`는 호출하지 않음.
- 정상 조회:
  - `GET /data/projects.json`: 200
  - `GET /api/projects/dev-pack/state`: 200
  - 서버 로그상 ruined-lab/dev-pack의 context/state/measurement/cycle-summary 조회 200
  - `GET /api/inbox`: 200
  - `GET /api/outbox/task-20260710070612000`: 200
- 엣지 조회:
  - `GET /api/outbox/__missing__`: 404
  - `GET /api/projects/__missing__/state`: 500
  - `GET /api/projects/__missing__/context`: 500

## 자산화
- `docs/qa/e2e-usage-2026-07-11.md` 작성.
- `FAIL-2026-009` 등록:
  - `docs/wiki/failures/cases/FAIL-2026-009-missing-project-api-returns-500.md`
  - `docs/wiki/failures/by-component/project-api-read.md`
  - `docs/wiki/failures/by-failure-class/error_handling.md`
  - `docs/wiki/failures/index.md`

## 발견/의심/오탐
- 재현: 1
  - 없는 projectId 조회가 4xx 대신 500을 반환.
- 의심: 0
- 오탐: 0

## 다음 픽업 후보
- sonnet에 `FAIL-2026-009` 수정 지시: project read API의 없는 projectId를 404/400으로 매핑.
- FEAT-02 `e2e-usage` 하네스에 없는 projectId 조회 시나리오를 포함한다.
