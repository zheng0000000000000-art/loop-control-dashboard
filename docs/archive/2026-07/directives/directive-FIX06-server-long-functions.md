# FIX-06 — server/ 장문 함수 일괄 분할 (measure 두더지 잡기 종료, 1/2)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: fix. 근거: `maxFunctionLength`는 **저장소에서 가장 긴 함수 하나만** 보고한다. 그래서 한 개 고칠 때마다 다음 놈이 드러난다(159 → 115 → 101). **한 번에 전부 없앤다.**

## 절대 금지
**blueprint.json·workflow-definition.json·`server/DevPackMeasures.cs`를 고쳐서 통과시키지 마라.** 기준 변경은 사람 결재 사항이다(CLAUDE.md 금지 1호). 목표치를 손대면 전체 반려.

## 대상 (측정 규칙을 그대로 복제해 전수 스캔한 결과 — 추측 아님)

이번 지시서는 **server/ 4건**이다. dashboard/app.js 3건은 FIX-07이 맡는다. **app.js를 건드리지 마라.**

| # | 파일:줄 | 길이 |
| --- | --- | --- |
| 1 | `server/Tier2Approver.cs:38` | 101줄 |
| 2 | `server/OutboxManager.cs:38` | 99줄 |
| 3 | `server/Program.cs:1521` | 93줄 |
| 4 | `server/Engine.cs:152` | 92줄 |

## 작업
각 함수를 **80줄 이하**로 의미 단위 분할한다. 추출한 헬퍼마다 **한국어 기능 주석 1줄** 필수(없으면 `functionsWithoutComment`가 다시 올라간다 — 지금 0이다).

**순서대로 하나씩 고치고, 하나 끝날 때마다 `measure dev-pack`을 돌려 `maxFunctionLength` 값이 내려가는지 확인하라.** 4건을 다 고치면 남는 최댓값은 dashboard/app.js의 99줄이 된다 — **그건 FIX-07 몫이니 손대지 말고 보고만 하라.**

### ★ Engine.cs 주의
`Engine.cs`는 코어 파일이다. **분할은 허용되지만, 도메인 지식(게임 용어·metricId·ollama 코드)을 코어에 넣는 것은 금지**다(CLAUDE.md). 순수하게 함수만 쪼개라. 동작이 조금이라도 달라지면 `verify-behavior`가 잡는다.

### ★ 동시 작업 주의
`server/Harness/**`는 **코덱스 배타 영역**이다. 미커밋 변경이 보여도 **건드리지도, 되돌리지도 마라.**

## 검수 기준 (검증 가능)
1. `measure dev-pack` → `maxFunctionLength` 값이 **dashboard/app.js 것(99)만 남을 것**. 즉 server/ 파일이 evidence에서 사라져야 한다.
2. `functionsWithoutComment` = **0 유지**.
3. `verify-behavior` → `behaviorEqual: true` (exit 0).
4. `build-verify` 하네스 → `verdict: PASS`, exit 0. **성패는 exit code로만 판정하라**(문자열 정규식 금지 — I-11).
5. blueprint.json·workflow-definition.json·DevPackMeasures.cs **무수정**(git status로 증명).
6. 코어 3파일에 도메인 지식 미유입.

## v9 산출물
WORKSTATE 갱신(diId FIX-06), `docs/verification/fix06-server-long-functions.md`(**①주체(actor) ②사용한 하네스와 결과: 명령·exit code·수치 ③참조한 스킬** 필수), `docs/directives/FIX06-server-long-functions.md` 보관.

## 허용 파일 (allowlist)

- server/Tier2Approver.cs
- server/OutboxManager.cs
- server/Program.cs
- server/Engine.cs
- docs/verification/fix06-server-long-functions.md
- docs/directives/FIX06-server-long-functions.md
- docs/handoff/WORKSTATE.json

> 이 목록 밖을 수정하면 산출물 전체가 반려된다. **dashboard/·server/Harness/ 무접촉.**

## 경계 / 보고
git commit/push 금지. 결재·반입·발사 금지. `-c Release`. stdout에 수행요약·자가점검표·measure JSON.
**한도·중단이 임박하면 종료 전 마지막 세 줄: `QUOTA_SIGNAL` / `CHANGED: <수정한 파일>` / `NEXT: <다음 할 일 한 줄>`.** 부분 작업물은 되돌리지 말고 verification 문서에 `상태: 미완(한도)`로 적어라(`docs/handoff/QUOTA-POLICY.md`).
