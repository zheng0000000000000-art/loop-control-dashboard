# FIX-04 — measure 위반을 실제로 0으로 (게이트 통과가 아니라 원인 제거)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: fix. 근거: `measure dev-pack` 위반이 남아 있는 한 dev-pack 루프가 **매 측정마다 새 proposal을 생성**해 사람 결재 대기를 무한 재생성한다(run-log 실측: measure.completed 136 → stage.warning 102 → proposal.created 110). **결재로는 이 루프가 끊기지 않는다. 원인(위반)을 없애야 끊긴다.**

## 절대 금지 (이 지시서의 핵심)
**blueprint.json·workflow-definition.json·측정 코드(`server/DevPackMeasures.cs`)를 고쳐서 통과시키지 마라.** 기준 변경은 사람 결재 사항이다(CLAUDE.md 금지사항). 코드·문서의 **실제 내용**을 고쳐라. 목표치를 낮추는 순간 이 작업은 전체 반려다.

## 대상 (measurement.json evidence로 특정됨 — 추측 아님)

| metric | 현재 | 기준 | 실체 |
| --- | --- | --- | --- |
| `smallTouchTargets` | 1 | 0 | `dashboard/style.css` — `.inbox-import-actions .button { min-height: 32px; }` (44px 미만) |
| `maxFunctionLength` | 159 | ≤80 | `dashboard/app.js:852-1010` — `renderApprovalPanel()` |
| `skillDomainViolations` | 2 | 0 | `docs/verification/tuning-advanced.md`의 `## 참조한 스킬`이 그 작업의 변경 파일과 무관한 도메인 스킬 2개(`skills/domains/dev/file-navigation.md`, `skills/domains/game/balance-tuning.md`)를 참조 |

> `functionsWithoutComment`의 남은 1건(`server/Harness/ScopeCheckCli.cs:156`)은 **코덱스 소유 영역이다. 건드리지 마라.** 코덱스가 자기 큐에서 처리한다.
> 나머지 5건은 검수자가 `docs/handoff/queue/OrchestratorObserverCli.reference.cs`(ORCH-01 참조본, 역할 종료)를 삭제해 이미 제거했다.

## 작업

1. **smallTouchTargets** — `.inbox-import-actions .button`의 `min-height`를 **44px 이상**으로 올린다. 레이아웃이 깨지지 않는지 확인(패딩·폰트 크기 조정 허용).
2. **maxFunctionLength** — `renderApprovalPanel()`(159줄)을 **80줄 이하**가 되도록 분할한다. 렌더 단위별 헬퍼로 쪼개되, **동작은 보존**(`verify-behavior` behaviorEqual:true).
3. **skillDomainViolations** — `docs/verification/tuning-advanced.md`의 `## 참조한 스킬` 목록에서 **그 문서의 변경 파일 경로와 스킬의 `트리거:`가 일치하지 않는 항목을 제거**한다. 스킬 라우팅 규칙(CLAUDE.md '스킬 라우팅')이 판정 기준이다. **문서를 지우지 말고 목록만 정정하라.**

## ★ 함정 (반드시 읽어라)
`appJsLines`는 현재 **2692줄로 상한(2692)에 정확히 붙어 있다.** 2번 작업으로 함수를 쪼개면 줄 수가 늘어 **새 위반이 생긴다.** 분할하면서 **app.js 전체 줄 수를 2692 이하로 유지**하라(중복 렌더 코드 통합 등으로 상쇄). 유지가 불가능하면 **고치지 말고 중단하고 보고하라** — 위반 하나를 없애며 다른 위반을 만드는 건 실패다.

## 검수 기준 (검증 가능)
1. `dotnet run --project server -c Release -- measure dev-pack` → **`skillDomainViolations`·`smallTouchTargets`·`maxFunctionLength` 전부 기준 충족**. (`functionsWithoutComment`의 코덱스 1건은 예외로 남아도 된다 — 그 사실을 보고에 명시하라.)
2. `appJsLines` ≤ 2692 (악화 금지).
3. `dotnet run --project server -c Release -- verify-behavior` → `behaviorEqual: true`.
4. `dotnet build server -c Release` → **exit 0**(문자열 정규식으로 판정 금지). 서버가 실행 중이면 `-o <임시경로>`로 락을 우회하고 exit code로만 판정하라.
5. blueprint.json·workflow-definition.json·server/DevPackMeasures.cs **무수정**(git status로 증명).
6. 대시보드 승인 패널이 기존과 동일하게 렌더된다(수동 확인 or 스냅샷).

## v9 산출물
WORKSTATE 갱신(diId FIX-04), `docs/verification/fix04-measure-zero.md`(위 6기준 실측 + **①주체(actor) ②사용한 하네스와 결과: 명령·exit code·수치 ③참조한 스킬**), `docs/directives/FIX04-measure-zero.md` 보관.

## 허용 파일 (allowlist)

- dashboard/style.css
- dashboard/app.js
- docs/verification/tuning-advanced.md
- docs/verification/fix04-measure-zero.md
- docs/directives/FIX04-measure-zero.md
- docs/handoff/WORKSTATE.json

> 이 목록 밖의 파일을 수정하면 산출물 전체가 반려된다. 특히 `server/` 아래는 **다른 실행자(sonnet ACTOR-01, 코덱스 Harness)가 동시에 쓰고 있다 — 절대 건드리지 마라.**

## 경계 / 보고
git commit/push 금지. 결재·반입·발사 금지. `-c Release`. stdout에 수행요약·검수기준 자가점검표·measure 결과 JSON 출력. rate limit 시 마지막 줄 QUOTA_SIGNAL.
