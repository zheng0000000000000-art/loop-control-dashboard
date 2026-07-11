# HARNESS-02 — hs-scan: 승격 심사 트리거를 기계가 탐지 (dotnet run -- hs-scan)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: harness. 근거: KNOWLEDGE-PROMOTION.md의 승격 파이프라인이 **문서로만 존재하고 한 번도 실행되지 않았다** — HS-CANDIDATES.md가 2026-07-11까지 부재했던 것이 증거. 트리거가 LLM 재량("해당하면 판정하라")이었기 때문이다.

## 목표
승격 심사 트리거를 **프로그램이 탐지**하는 읽기 전용 CLI `hs-scan`. 점수화(판단)는 하지 않는다 — 그건 `skills/common/hs-gate.md`(LLM)의 몫. **"LLM은 판단, 프로그램은 탐지·기억".**

## 전제 조건
server/ clean. 순차.

## 작업
1. 참조 스캐폴드 `docs/handoff/queue/HsScanCli.reference.cs` → `server/HsScanCli.cs`, CliRouter에 `hs-scan` 분기 등록.
2. 입력(읽기 전용): `docs/wiki/failures/index.md`(표 파싱: ID·failureClass·구성요소), `docs/handoff/HS-CANDIDATES.md`(`lastGate:`, `judgedClasses:` 메타).
3. 신호 탐지(전부 기계 판정, 해석 없음):
   - **S1 반복 실패계열**: 같은 failureClass가 **2회 이상**이고 `judgedClasses`에 없으면 후보. ← 핵심 신호
     **메타 태그 제외 필수**: `design_learning`·`known_failure`는 실패 메커니즘이 아니라 분류 꼬리표다(전자는 10건 중 8건에 붙어 있다).
     거르지 않으면 매 회차 노이즈 후보가 올라온다. 제외 목록은 코드 상수로 두고 주석으로 이유를 남긴다.
   - **S2 실패 누적**: 마지막 심사 이후 FAIL 3건 이상 증가.
   - **S3 정기**: 마지막 심사 후 24시간 경과(또는 심사 이력 없음).
   - **S4 반복 구성요소**: 같은 구성요소 3회 이상.
4. JSON 출력: `{harness, triggered, lastGate, daysSinceLastGate, failureCaseCount, signals[], candidates[{signal, failureClass|component, occurrences, cases[], suggestedType, why}], action}`.
5. exit: **0=트리거 없음, 1=트리거 있음(HS-GATE 수행 의무), 2=오류.**
6. `judgedClasses`에 있는 계열은 다시 제기하지 않는다(중복 제기 방지).
7. **안전**: 읽기 전용. 파일 쓰기·git 변경 없음. 부작용 0.

## 검수 기준 (검증 가능 6개)
1. `dotnet run --project server -c Release -- hs-scan`이 위 스키마 JSON 출력.
2. **회귀 검증(핵심)**: 현재 위키에는 `unnormalized_gate`가 FAIL-005·FAIL-010 2건이다. `HS-CANDIDATES.md`의 `judgedClasses`에서 이를 **제거하고** 실행하면 → S1 후보로 잡히고 `triggered:true`, exit 1. 되돌리면 잡히지 않는다.
   **즉 이 하네스는 "gate-clean 승격을 스스로 발견했어야 했다"를 증명해야 한다.**
3. `path_escape`(FAIL-006·007 2건)도 S1로 잡힌다(아직 미심사라면).
3-b. **노이즈 차단 검증**: `design_learning`(8회)·`known_failure`(2회)는 2회 이상이지만 **후보로 잡히지 않는다**(메타 태그 제외).
4. 트리거가 하나도 없으면 exit 0, `triggered:false`.
5. 실행이 어떤 파일도 쓰지 않는다(전후 `git status` 동일). `dotnet build -c Release` 0/0, `verify-behavior` true.
6. 코어 3파일 무접촉.

## v9 산출물
WORKSTATE(diId HARNESS-02), `docs/verification/harness02-hs-scan.md`(6기준 + 회귀 검증 2번 실측), `docs/directives/HARNESS02-hs-scan.md`.

## 소비자 (후속)
- **코덱스 15분 루틴 4.7**: 매 회차 `hs-scan` 실행 → **exit 1이면 반드시** `skills/common/hs-gate.md` 절차로 심사·기록. 재량 아님.
- 검수자: `즉시제작` 후보를 SONNET-QUEUE에 등재(등재·발사는 사람 게이트).

## 경계 / 보고
server/ + 위 문서만. dashboard/·docs/qa/·docs/wiki/ 무접촉. git commit/push 금지. `-c Release`. stdout에 수행요약·자가점검표·hs-scan 출력 JSON. rate limit 시 마지막 줄 QUOTA_SIGNAL.
