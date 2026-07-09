# AI 결재자 tier-2 검증

목적: 산정 위험도 low + 1층 통과 제안을 상위 AI 결재자가 자동 결재하고, medium 이상·메타·pacing 보존 구간은 사람 경로로 남긴다.

## 변경 범위

- `server/ApproverTier.cs`, `server/ApproverWorkflow.cs`: 결재 정책 게이트, claude-code 호출/JSON 검증, approve/reject/defer 처리.
- `server/ApproverTestCli.cs`: 임시 데이터 사본에서 결재 분기를 실측하는 CLI.
- `server/AutoApprovalInbox.cs`, `dashboard/app.js`, `dashboard/style.css`, `dashboard/index.html`, `dashboard/data/lang/*.json`: 자동 결재 감사 섹션, AI 결재 배지, 무인 진행 카운터.
- `dashboard/data/*/workflow-definition.json`: `reviewPolicy.approverTier` 추가.
- `docs/DECISIONS.md`: 결재 사다리 결정 기록.

직접 경로 사유: 이번 지시는 완료 시 verification 작성, 커밋·push까지 요구했고, outbox 반입은 사람 전용이라 dispatch 기본 경로만으로 완료 기준을 충족할 수 없었다.

## 실측

| 항목 | 명령 | 결과 |
| --- | --- | --- |
| 빌드 | `dotnet build server` | exit 0, 경고 0 |
| low 무인 승인 | `dotnet run --no-build --project server -- approvertest approve` | `handled:true`, `afterLoop:4`, `proposalLifecycle:"decided"`, `reviewerRole:"approver"`, `subscriptionCalls:2` |
| medium 사람 강등 | `dotnet run --no-build --project server -- approvertest medium` | `handled:false`, `latestReasonCode:"approver.risk_too_high"` |
| 타임아웃 mock defer | `dotnet run --no-build --project server -- approvertest timeout` | `handled:false`, `latestReasonCode:"approver.call_failed"`, `subscriptionCalls:1` |
| pacing 보존 mock | `dotnet run --no-build --project server -- approvertest pacing` | `handled:false`, `latestReasonCode:"approver.pacing_degraded"` |
| 사람 롤백 감사 잔존 | `dotnet run --no-build --project server -- approvertest rollback-audit` | `autoReportRetainedAfterHumanRollback:true` |

## 완료 기준

- 무인 회차 실측: O — approve 시나리오에서 루프가 3→4로 진행되고 proposal이 `decided`가 됐다.
- 메타 변경·반입에 approver 개입 경로 부재: O — `not_meta_change` 요구 조건과 `blueprint`/`workflow-definition`/`skills`/`outbox` 경로 차단을 구현했다.
- 구독 회계: O — approver 호출은 `subscriptionCalls:1`, `role:"runtime"`으로 기록한다.
- 강등 동작: O — medium, timeout, pacing 시나리오가 사람 경로로 남았다.
- 사람 경로 무변경: O — 기존 `/actions/approve`와 `/actions/reject` 라우트는 그대로 두고, 승인 적용 본문만 공용 함수로 재사용했다.
- ntfy 자동 결재 무발송: O — 자동 승인 경로에서 `NotifyReviewPending`을 호출하지 않는다. 악화 감지는 기존 `measurement.regressed` 경로를 그대로 탄다.

## 게이트

{"gate":"dev-pack","violations":2,"attempt":1}

남은 위반:

- `skillDomainViolations=2`: `docs/verification/tuning-advanced.md -> skills/domains/dev/file-navigation.md`, `docs/verification/tuning-advanced.md -> skills/domains/game/balance-tuning.md`
- `maxFunctionLength=246`: `server/Program.cs:714-959`

`programCsLines`는 이번 작업 중 3149까지 악화됐다가 `ApproverWorkflow`/`AutoApprovalInbox`/`CycleSummaryBuilder` 분리 후 2654로 기준(0~2661)에 복귀했다. 남은 2건은 기존 추적 위반이며, 기준·측정 코드는 수정하지 않았다.

## 참조한 스킬

- `skills/common/directive-writing.md`
- `skills/common/verification.md`
- dev/file-navigation
- design/design
