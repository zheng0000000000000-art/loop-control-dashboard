# FEAT-02 — E2E 실사용 하네스 내재화 (dotnet run -- e2e-usage)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: harness. 근거: 지금 E2E 실사용 검사는 스케줄 태스크가 상위 모델로 시나리오를 매번 구성해 토큰을 소모한다. 이를 서버 CLI 하네스로 코드화하면 로컬 결정론·토큰 0·회귀 하네스 자산이 된다. 시나리오 원본: `docs/handoff/E2E-USAGE-hunter.md`.

## 전제 조건
server/ clean(선행 DI 커밋 완료). 순차.

## 목표
`E2E-USAGE-hunter.md`의 6개 실사용 시나리오를 서버 CLI `e2e-usage`로 구현한다. HTTP 서버를 띄우지 않고 **인프로세스**로 기존 로직(Storage·measure·inbox 빌더·outbox 등)을 직접 호출해 검증한다.

## 작업
1. `server/E2EUsageCli.cs` 신설 + CliRouter에 `e2e-usage [projectId]` 분기 등록(R-01의 CliRouter 패턴 재사용).
2. 시나리오 6개를 인프로세스로 실행(상태 변경 없음 — 읽기·measure만):
   - ① 프로젝트 열람 정합성: 각 프로젝트의 state·measurement·context·cycle-summary 로드 성공 + schemaVersion 일치 + 필수 필드 존재.
   - ② measure 결정론: 같은 프로젝트 measure 2회 결과 동일.
   - ③ 인박스 일관성: 인박스 항목의 assignableTo·kind가 규칙대로(결재=human 등), 존재하지 않는 프로젝트/task 참조 없음.
   - ④ outbox 조회: import_pending task 조회 시 meta·diff 필드 정상, stale 상태 판정 일관.
   - ⑤ 엣지: 없는 projectId·잘못된 taskId·malformed 입력이 **크래시가 아니라 잡힌 예외/거부**로 처리되는지.
   - ⑥ 상태 교차 일관성: workflow-state ↔ measurement ↔ run-log의 loopIteration·stage·proposalId 모순 없음.
3. 결과를 JSON으로 출력: `{"scenarios":[{"id":"S1","name":"...","result":"pass|fail","detail":"..."}],"failCount":N}`. failCount>0이면 exit code 2.
4. **안전**: 어떤 시나리오도 상태를 바꾸지 않는다(proposal·outbox·contributions 무변경).

## 검수 기준 (검증 가능 6개)
1. `dotnet run --project server -c Release -- e2e-usage`가 6개 시나리오 결과 JSON을 출력한다.
2. 정상 상태에서 통과하는 시나리오는 pass, 알려진 이슈(예: outbox stale #12)는 fail로 정확히 잡고 detail에 근거.
3. 엣지 입력이 프로세스 크래시가 아니라 잡힌 결과로 리포트된다.
4. 실행이 상태를 바꾸지 않는다(전후 measurement·workflow-state 불변 확인).
5. `dotnet build server -c Release` 0/0, `verify-behavior` behaviorEqual:true.
6. 코어 3파일 도메인 무지 유지(E2E 로직은 신규 파일).

## v9 산출물
WORKSTATE 갱신(diId FEAT-02), `docs/verification/feat02-e2e-harness.md`, `docs/directives/FEAT02-e2e-harness.md` 보관.

## 후속 (이번 범위 아님)
E2E 스케줄 태스크(`e2e-usage-hunter`)를 이 CLI 호출로 전환 — 상위 모델은 시나리오를 짜지 않고 `dotnet run -- e2e-usage` 결과(JSON)만 파싱해 FAIL 등록. 토큰 절감 + 결정론.

## 경계 / 보고
server/ + 위 문서만. dashboard/·docs/qa/·docs/wiki/ 무접촉. git commit/push 금지. `-c Release`. stdout에 수행요약·검수기준 자가점검표·시나리오 결과 출력. rate limit 시 마지막 줄 QUOTA_SIGNAL.
