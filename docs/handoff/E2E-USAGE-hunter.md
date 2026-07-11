# E2E 실사용 결함 헌터 — 유휴 시간 자동 탐색

> 목적: 지시서 발사 사이 유휴 시간에 "진짜 사용자처럼" 시스템을 써보고 실사용 결함(잘못된 응답·상태 불일치·미처리 에러)을 찾는다. 밸런스 튜닝이 idle에 자동 탐색했듯, 이건 idle에 E2E 결함을 탐색한다.
> 실행자: 코덱스(QA 역할) 또는 별도 E2E 예약. 저장소: `C:\Users\1\Documents\Local-First Workflow Dashboard`.
> 함께 읽기: `docs/handoff/VERIFY-PROTOCOL-universal.md`, `docs/handoff/CODEX-ROLE-bug-hunter.md`.

## 안전 경계 (필수)

- **상태를 바꾸는 요청 금지**: proposal 승인/거절, outbox 반입, contributions 승급, dispatch는 하지 않는다(사람 게이트 + 상태 오염 방지).
- 허용: GET 조회 전부, `measure`(재측정 — 코드 변경 아님), 엣지 입력으로 4xx 유도(안전).
- **코드 무수정. git commit/push 금지.** 발견은 `docs/qa/`·`docs/wiki/failures/`에만 기록.
- server가 리팩토링 중(작업트리 server/ 미커밋 or sonnet 실행 중)이면 이번 회차 스킵 — 안정 상태에서만.
- 서버는 실행 중인 것을 쓴다(localhost:5173). 별도 빌드가 필요하면 `-c Release`.

## E2E 시나리오 (실사용 재현)

각 단계에서 응답 코드·본문·상태 일관성을 검증하고, 기대와 다르면 기록한다.

1. **프로젝트 발견→열람**: `GET /data/projects.json` → 각 프로젝트 `GET /api/projects/{id}/context`·`/state`·`/measurement`·`/cycle-summary`. 200인지, schemaVersion 일치, 필드 누락 없는지.
2. **측정 정합성**: `POST /api/projects/{id}/actions/measure`(코드 변경 아님) → 응답의 violationCount·지표가 기존 `measurement.json`과 모순 없는지. 두 번 호출 시 결정론.
3. **인박스 일관성**: `GET /api/inbox` → 각 항목의 `assignableTo`·`kind`가 규칙대로인지(결재=human 등), 존재하지 않는 프로젝트/task 참조 없는지.
4. **outbox 조회**: `GET /api/outbox/{taskId}` → stale task(예: 옛 #12)가 어떤 상태로 조회되는지, diff 필드 정상인지.
5. **엣지·에러 처리** (실사용자의 실수 재현):
   - 없는 projectId, 잘못된 taskId, malformed JSON POST, 빈 본문, 아주 긴 입력 → **4xx로 정상 거부**되는지(500·크래시·스택 노출이면 결함).
   - 잘못된 순서(예: 검토 전 단계 액션) → 계약대로 거부되는지.
6. **상태 교차 일관성**: `workflow-state` ↔ `measurement` ↔ `run-log`의 loopIteration·stage·proposalId가 서로 모순 없는지.

## 판정·자산화

- 각 시나리오 결과를 `docs/qa/e2e-usage-<날짜>.md`에 표로(시나리오·요청·기대·실제·판정 정상/이상). 
- **재현되는 진짜 결함**(500·상태 불일치·계약 위반)은 `docs/wiki/failures/`에 FAIL 등록(v9 형식). 단순 관찰·오탐은 e2e 리포트에만.
- sonnet 수정이 필요한 결함은 "sonnet 수정 필요: FAIL-XXX"로 CODEX-QUEUE 또는 리포트에 남긴다(오케스트레이터가 부록 지시서로 넘김).

## 완료 기준 (5개)
1. `docs/qa/e2e-usage-<날짜>.md`에 6개 시나리오 결과가 표로 있다.
2. 엣지 입력이 500/크래시를 유발하면 결함으로 기록된다(4xx면 정상).
3. 상태 교차 불일치가 있으면 기록된다.
4. 재현된 결함은 FAIL 위키에, 관찰은 리포트에 분리 기록.
5. `git status`에 `docs/qa/`·`docs/wiki/` 외 변경 없음(코드·상태 무변경).
