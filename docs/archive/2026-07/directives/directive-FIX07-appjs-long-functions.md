# FIX-07 — dashboard/app.js 장문 함수 분할 (measure 두더지 잡기 종료, 2/2)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: fix. 근거: FIX-06이 server/ 4건을 없애면 `maxFunctionLength`의 마지막 서식지는 `dashboard/app.js`다. **이걸 끝내면 measure 위반 0 — dev-pack 루프가 새 proposal을 만들지 않게 되고, 결재 대기 무한 재생성이 멈춘다.**

## 절대 금지
**blueprint.json·workflow-definition.json·`server/DevPackMeasures.cs`를 고쳐서 통과시키지 마라.** 기준 변경은 사람 결재 사항이다(CLAUDE.md 금지 1호).

## 대상 (측정 규칙 복제 전수 스캔 결과)

| # | 위치 | 길이 |
| --- | --- | --- |
| 1 | `dashboard/app.js:751` | 99줄 |
| 2 | `dashboard/app.js:1071` | 86줄 |
| 3 | `dashboard/app.js:535` | 81줄 |

> 줄 번호는 FIX-06 이전 기준이다. **먼저 `measure dev-pack`을 돌려 현재 evidence로 위치를 재확인**하고 시작하라. server/ 파일이 evidence에 남아 있으면 FIX-06이 아직 안 끝난 것이니 **작업하지 말고 보고하고 중단하라.**

## ★★ 최대 함정 — 두 지표가 서로를 잡아먹는다

`appJsLines`의 상한은 **2692**이고, `dashboard/app.js`는 **정확히 2692줄**이다. **한 줄만 늘어도 새 위반이 생긴다.**
함수를 쪼개면 헬퍼 선언·닫는 괄호로 줄이 **늘어난다.** 따라서:

- **분할하면서 전체 줄 수를 2692 이하로 유지하라.** 중복 렌더 코드 통합·불필요한 중간 변수 제거 등으로 상쇄한다.
- FIX-04에서 실행자가 `renderApprovalPanel` 159→78줄로 쪼개면서 **2692를 지켜냈다.** 가능하다.
- **불가능하면 고치지 말고 중단하고 보고하라.** 위반 하나를 없애며 다른 위반을 만드는 것은 **실패**다.
- `appJsLines`를 맞추려고 **기능을 삭제하지 마라.** 동작 보존이 우선이다.

## 작업
1. `measure dev-pack`으로 현재 evidence 확인 → 대상 함수 위치 확정.
2. 각 함수를 80줄 이하로 분할. 추출한 헬퍼는 **JS 파일이므로 `functionsWithoutComment` 측정 대상**이다 — **각 헬퍼 위에 한국어 기능 주석 1줄** 필수(지금 0이다, 올리지 마라).
3. 매 분할 후 `measure`로 `maxFunctionLength`와 `appJsLines`를 **동시에** 확인.
4. 대시보드 화면이 기존과 동일하게 렌더되는지 확인(승인 패널·큐·요약 카드).

## 검수 기준 (검증 가능)
1. `measure dev-pack` → **`violationCount: 0`**. 이것이 이 작업의 존재 이유다.
2. `appJsLines` ≤ **2692**.
3. `functionsWithoutComment` = **0**.
4. `verify-behavior` → `behaviorEqual: true` (exit 0).
5. `build-verify` → `verdict: PASS`, exit 0. **exit code로만 판정.**
6. blueprint·workflow-definition·DevPackMeasures.cs **무수정**.

## v9 산출물
WORKSTATE 갱신(diId FIX-07), `docs/verification/fix07-appjs-long-functions.md`(**①주체(actor) ②하네스 결과: 명령·exit code·수치 ③참조 스킬** 필수 + **measure violationCount 0 증거 JSON 그대로 첨부**), `docs/directives/FIX07-appjs-long-functions.md` 보관.

## 허용 파일 (allowlist)

- dashboard/app.js
- docs/verification/fix07-appjs-long-functions.md
- docs/directives/FIX07-appjs-long-functions.md
- docs/handoff/WORKSTATE.json

> 이 목록 밖을 수정하면 산출물 전체가 반려된다. **server/ 무접촉**(다른 실행자·코덱스 영역).

## 경계 / 보고
git commit/push 금지. 결재·반입·발사 금지. `-c Release`. stdout에 수행요약·자가점검표·measure JSON.
**한도·중단이 임박하면 종료 전 마지막 세 줄: `QUOTA_SIGNAL` / `CHANGED: <수정한 파일>` / `NEXT: <다음 할 일 한 줄>`.** 부분 작업물은 되돌리지 말고 verification 문서에 `상태: 미완(한도)`로 적어라(`docs/handoff/QUOTA-POLICY.md`).
