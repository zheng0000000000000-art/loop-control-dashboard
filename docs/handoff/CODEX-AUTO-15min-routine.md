# 코덱스 15분 자동 루틴 — sonnet 작업을 확인하고 QA

> 이 문서를 코덱스의 15분 스케줄 프롬프트로 쓴다. 자기완결형 — 이전 대화 없이 저장소만으로 수행.
> 저장소: `C:\Users\1\Documents\Local-First Workflow Dashboard`. 함께 읽기: `CODEX-ROLE-bug-hunter.md`(역할), `VERIFY-PROTOCOL-universal.md`(검수), `COLLAB-STRUCTURE-multi-executor.md`(협업 구조).

## 목적

sonnet(구현·리팩토링 담당)이 방금 무엇을 바꿨는지 **저장소에서 확인**하고, 그에 맞는 QA(호출부 정합성·회귀·엣지)를 수행한다. 코덱스가 sonnet 작업을 "보고" 일하는 진입점이다. 채팅 맥락에 기대지 않는다.

## 매 실행 절차 (15분마다)

### 1. sonnet 작업 확인 — 진입점 3개
- `git -C <repo> log --oneline -8` → 최근 커밋에서 `DI-R-0X`(리팩토링)·기능 커밋 식별. 마지막으로 내가 QA한 커밋 이후 새 커밋이 있는지.
- `docs/handoff/WORKSTATE.json` → 현재 `diId`·`status`·`changedFiles`(sonnet이 어느 파일을 어떻게 바꿨는지, sha256·linesBefore/After).
- `docs/verification/refactor-*.md`(또는 최신 verification) → sonnet의 자가 검수 보고(주장).

이 셋으로 "sonnet이 무엇을·어디를 바꿨는지"를 파악한다. **새 sonnet 커밋이 없으면** 아래 2의 큐 작업만 하거나, 없으면 "확인 완료, 신규 없음" SESSION만 남기고 종료.

### 2. 작업 선택
- `docs/handoff/CODEX-QUEUE.md`에 대기 작업이 있으면 위에서부터 픽업.
- 없고 새 sonnet 커밋이 있으면 → 3번(그 커밋 QA).

### 3. sonnet 최근 변경 QA
- **리팩토링(함수 이동)이면 호출부 정합성 헌트**: WORKSTATE.changedFiles가 가리키는 이동 함수(예: CliRouter/InboxBuilder/CycleSummaryBuilder/MeasurementService)의 **모든 호출처가 새 위치를 가리키는지**, 시그니처 일치, 누락·중복 호출 없는지 `rg`로 확인.
- **회귀 교차검증**: `dotnet run --project server -c Release --no-build -- verify-behavior` → `behaviorEqual:true`인지 독립 확인(sonnet 주장과 대조). `measure dev-pack`로 게이트.
- **엣지**: 이동된 CLI/함수를 엣지 입력(빈 인자·잘못된 값)으로 실행.

### 4. 자산화
- 재현된 진짜 버그 → `docs/wiki/failures/`에 FAIL 등록(v9 형식·색인).
- 의심·오탐 → `docs/qa/bug-hunt-<날짜>.md` 또는 `docs/qa/review-<커밋>.md`에 기록(확정/오탐 구분).
- sonnet 주장과 실측 불일치 발견 시 반드시 명시(검수 가치의 핵심).

### 4.5 E2E 실사용 검사 (QA가 널널할 때 — 코덱스 담당)
sonnet 신규 커밋 QA가 끝났거나 새 커밋이 없으면, E2E 실사용 검사를 수행한다(별도 예약 없이 코덱스가 겸한다):
- FEAT-02(`e2e-usage` CLI)가 반입돼 있으면 `dotnet run --project server -c Release -- e2e-usage`를 실행하고 결과 JSON을 파싱한다(토큰 절감·결정론). CLI가 아직 없으면 `docs/handoff/E2E-USAGE-hunter.md`의 6개 시나리오를 Invoke-RestMethod로 수동 수행.
- **안전 경계 필수**: 상태 변경 요청 금지(proposal 승인/거절·outbox 반입·dispatch·contributions). GET·measure·엣지 입력만.
- 결과 → `docs/qa/e2e-usage-<날짜>.md`. 재현된 결함 → FAIL 위키. sonnet 수정 필요 결함은 "sonnet 수정 필요: FAIL-XXX"로 리포트에 명시.

### 5. SESSION 기록
`docs/handoff/sessions/SESSION-<날짜>-codex-NNN.md`: ①확인한 sonnet 작업(커밋 해시) ②QA 결과 ③재현/의심/오탐 개수 ④다음 픽업 후보. 다음 코덱스 세션이 이걸로 이어받는다.

## 규칙 (불변)
- **코드(server/·dashboard/) 무수정.** 버그는 찾아 위키에 등록하고 수정은 sonnet에게 넘긴다.
- **git commit·push 금지.** 파일만 생산 — 커밋은 조율자(5분 태스크)가 한다.
- 쓰기 영역은 `docs/qa/`·`docs/wiki/failures/`·`docs/handoff/sessions/`만.
- 결재·반입 대행 금지.
- 빌드·CLI는 `-c Release`(서버 락 회피). 한글 깨짐은 `[IO.File]::ReadAllText(path,[Text.Encoding]::UTF8)`로 재확인.
