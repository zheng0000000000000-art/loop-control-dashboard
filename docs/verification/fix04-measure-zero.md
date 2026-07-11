# FIX-04 검증 기록

일시: 2026-07-11

## ① 주체 (actor)

- claude-sonnet-4-6 (FIX-04 직접 수행)

## ② 사용한 하네스와 결과

| 하네스 / 명령 | exit code | 핵심 수치 |
|---|---|---|
| `dotnet build server -c Release -o /tmp/fix04-build` | 0 | 경고 0, 오류 0 |
| `dotnet run --project server -c Release -- measure dev-pack` | 1 | violationCount 1 (하단 상세) |
| `dotnet run --project server -c Release -- verify-behavior` | 0 | behaviorEqual: true |

## ③ 참조한 스킬

- (스킬 라우팅: dashboard/style.css, dashboard/app.js, docs/verification/ → 트리거 일치 도메인 스킬 없음)
- skills/common/ 해당 없음 (작업 내용이 CSS·JS 코드 수정 + 문서 정정)

## 수행 내용

### 1. smallTouchTargets (고침)

`dashboard/style.css` line 1133: `.inbox-import-actions .button { min-height: 32px → 44px }`

### 2. maxFunctionLength (부분 고침 — 하단 블로커 참조)

`dashboard/app.js:852-1010` `renderApprovalPanel()` 159줄 → 4개 헬퍼 추출로 78줄(≤80) 달성.

추출한 헬퍼:
- `renderNoPendingProposalPanel(context)` (28줄)
- `renderApprovalMeta(proposal, risk)` (17줄)
- `renderLabeledList(labelKey, items, renderItem, emptyKey)` (9줄, changes+impact 통합)
- `renderApprovalActions(context, autoApproveTarget)` (27줄)

`appJsLines`: 2692 → 2692 (변화 없음, 통합 상쇄로 유지).

### 3. skillDomainViolations (고침)

`docs/verification/tuning-advanced.md` `## 참조한 스킬`에서 트리거 불일치 2건 제거:
- `skills/domains/dev/file-navigation.md` 제거
- `skills/domains/game/balance-tuning.md` 제거

## measure 결과

```json
{"gate":"dev-pack","violations":1,"attempt":1}
```

남은 위반:
```json
{"metricId":"maxFunctionLength","value":115,"evidence":["server/BalanceTuner.cs:43-157"]}
```

## 블로커 — maxFunctionLength 미완료

지시서는 `dashboard/app.js:852-1010` (renderApprovalPanel, 159줄)을 고치면 `maxFunctionLength` 기준을 충족할 것으로 특정했다.
실제로 renderApprovalPanel을 78줄로 줄였지만, 측정 도구는 전체 코드에서 가장 긴 함수를 탐색한다.
renderApprovalPanel이 해소되자 그 뒤에 가려져 있던 `server/BalanceTuner.cs:43-157` (115줄)이 최장 함수로 노출됐다.

- 해당 파일은 allowlist 밖(`server/`)이며 지시서가 절대 금지한 영역이다.
- 직접 수정하면 산출물 전체 반려 조건에 해당한다.
- 따라서 `maxFunctionLength` 위반 1건이 남아 있다.

## 완료 기준 자가점검

| # | 기준 | 판정 |
|---|---|---|
| 1 | skillDomainViolations·smallTouchTargets·maxFunctionLength 전부 기준 충족 | PARTIAL — 앞 2개 충족, maxFunctionLength 1건(BalanceTuner.cs) 잔존 |
| 2 | appJsLines ≤ 2692 | PASS (2692) |
| 3 | verify-behavior → behaviorEqual: true | PASS |
| 4 | dotnet build -c Release → exit 0 | PASS |
| 5 | blueprint.json·workflow-definition.json·DevPackMeasures.cs 무수정 | PASS (git status 확인) |
| 6 | 대시보드 승인 패널 기존과 동일 렌더 | PASS (behaviorEqual: true, 헬퍼 추출로 동작 보존) |

## functionsWithoutComment 예외 기록

지시서에서 언급된 `server/Harness/ScopeCheckCli.cs:156` 코덱스 소유 1건은 측정 결과 `value: 0`으로 이미 해소됐다.
