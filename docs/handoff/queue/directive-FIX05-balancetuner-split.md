# FIX-05 — 마지막 measure 위반 제거 (server/BalanceTuner.cs 함수 분할)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: fix. 근거: FIX-04로 measure 위반이 4 → **1**이 됐다. 남은 1건을 없애면 **dev-pack 루프가 새 proposal을 만들지 않는다** — 결재 대기 무한 재생성이 멈춘다.

## 절대 금지
**blueprint.json·workflow-definition.json·`server/DevPackMeasures.cs`를 고쳐서 통과시키지 마라.** 기준 변경은 사람 결재 사항이다(CLAUDE.md 금지사항 1호). 목표치를 손대는 순간 전체 반려다.

## 대상 (measurement.json evidence — 추측 아님)

| metric | 현재 | 기준 | 실체 |
| --- | --- | --- | --- |
| `maxFunctionLength` | **115** | ≤80 | `server/BalanceTuner.cs:43-157` |

> `maxFunctionLength`는 **저장소에서 가장 긴 함수 하나만** 보고한다. 이 함수를 80줄 이하로 줄이면 **그다음으로 긴 함수가 드러날 수 있다.** 작업 후 반드시 `measure dev-pack`을 다시 돌려 확인하고, 새로 드러난 위반이 있으면 **고치지 말고 그 함수의 위치를 보고하라**(다음 지시서 대상).

## 작업
1. `server/BalanceTuner.cs`의 43~157행 함수를 **80줄 이하**가 되도록 의미 단위로 분할한다. 추출한 헬퍼에는 **한국어 기능 주석 1줄**을 붙인다(CLAUDE.md 관례 — 안 붙이면 `functionsWithoutComment`가 다시 올라간다).
2. **동작 보존**: `verify-behavior` → `behaviorEqual: true`.
3. 코어 3파일(Engine.cs·Storage.cs·Guardrails.cs) 무접촉. 도메인 지식을 코어로 옮기지 마라.

## ★ 동시 작업 주의 (중요)
`server/`에는 **다른 주체가 동시에 쓰고 있다**:
- 코덱스 → `server/Harness/**` (배타)
- 이전 작업(ACTOR-01) → `server/Program.cs`·`OutboxManager.cs`·`GitDataCommitter.cs`

**너는 `server/BalanceTuner.cs` 하나만 만진다.** 다른 파일이 미커밋 상태로 보여도 **건드리지도, 되돌리지도 마라.**

## 검수 기준 (검증 가능)
1. `dotnet run --project server -c Release -- measure dev-pack` → `maxFunctionLength` ≤ 80. **새 위반이 드러났으면 그 사실과 위치를 보고**(그건 실패가 아니다).
2. `verify-behavior` → `behaviorEqual: true` (exit 0).
3. `build-verify` 하네스 → `verdict: PASS`, exit 0. (서버 실행 중 exe 락은 이 하네스가 우회한다. 문자열 정규식으로 빌드 성패를 판정하지 마라 — FAIL/KNOWN-ISSUES I-11)
4. `functionsWithoutComment` = 0 유지(추출한 헬퍼에 주석 필수).
5. blueprint.json·workflow-definition.json·DevPackMeasures.cs 무수정(git status로 증명).

## v9 산출물
WORKSTATE 갱신(diId FIX-05), `docs/verification/fix05-balancetuner-split.md`(위 5기준 실측 + **①주체(actor) ②사용한 하네스와 결과: 명령·exit code·수치 ③참조한 스킬**), `docs/directives/FIX05-balancetuner-split.md` 보관.

## 허용 파일 (allowlist)

- server/BalanceTuner.cs
- docs/verification/fix05-balancetuner-split.md
- docs/directives/FIX05-balancetuner-split.md
- docs/handoff/WORKSTATE.json

> 이 목록 밖을 수정하면 산출물 전체가 반려된다.

## 경계 / 보고
git commit/push 금지. 결재·반입·발사 금지. `-c Release`. stdout에 수행요약·검수기준 자가점검표·measure 결과 JSON 출력.
**한도·중단이 임박하면 종료 전에 마지막 세 줄을 출력하라: `QUOTA_SIGNAL` / `CHANGED: <수정한 파일>` / `NEXT: <다음 할 일 한 줄>`.** 부분 작업물은 되돌리지 말고 verification 문서에 `상태: 미완(한도)`로 적어라(docs/handoff/QUOTA-POLICY.md).
