# Tier-2 AI 반입 승인 — 검증

## 배경 / 이양 범위

사람이 채팅으로 승인한 "제한된 이양안"을 구현한다. 대상은 **outbox 반입(import) 한 건**뿐이다 —
proposal 자체의 최초 승인/거절은 여전히 사람 전용이며, 코드에서도 손대지 않았다
(`/actions/approve`, `/actions/reject`, `/outbox/{id}/reject-import` 미변경).

자동 반입 대상 조건(AND, `Tier2Approver.CheckEligibility`):
- 게이트 위반 비증가: dispatch 시작 시점(원본 워크스페이스)과 dispatch 완료 시점(반영 후 사본)의
  `measure dev-pack` violationCount를 비교해 `after > before`가 아니어야 한다.
- 코어 3파일(`server/Engine.cs`/`Storage.cs`/`Guardrails.cs`) 무수정.
- 기준 파일(파일명이 `workflow-definition.json` 또는 `blueprint.json`) 무수정.
- 위 셋 중 하나라도 파싱 실패(측정 불가)면 안전 쪽으로 부적격 처리한다.

적격 건만 상위 티어 AI(로컬 Ollama `qwen3:14b`, 실패 시 `llama3.1:8b` 폴백)에게 diff를 보여주고
"secrets·범위 이탈·명백히 깨진 로직"만 추가로 검토시킨다 — 승인 시에만 실제 반입을 수행한다.
리뷰어 연결 실패·거절은 모두 그대로 `import_pending`(사람 대기)으로 남긴다.

## 이양 고정점 준수

- `Tier2Approver.Enabled`는 `server/appsettings.json`에 **기본값 false**로 커밋한다. 이번 세션은
  장치(게이트·감사·캡·halt)를 짓는 것까지가 이양 결정 실행 범위라고 판단했다 — 08b648c가
  "enabled:true 자가 커밋"으로 revert된 전례([[STATUS.md]] 참고)를 반복하지 않기 위해서다.
  **켜는 결정은 사람이 appsettings.json에서 `Tier2Approver.Enabled: true`로 직접 바꿔야 한다.**
- Program.cs/Storage.cs/Guardrails.cs에는 손대지 않았다(설정은 `Tier2ApproverOptions.Load`가
  `server/appsettings.json`을 직접 읽어 Program.cs의 배선을 늘리지 않는 방식으로 구현).

## 구현 파일

- `server/Tier2Approver.cs` (신규): 적격성 판정, Ollama 리뷰 호출, 캡/halt 상태 관리, 감사 로그 기록.
- `server/OutboxManager.cs`: dispatch 시작 시 원본 워크스페이스 기준선 측정(`Enabled`일 때만, `--no-build`로
  실행 중인 서버 자신의 빌드 산출물과 충돌하지 않게 함) 추가, `ApproveImport`의 파일 적용 로직을
  `ApplyChangedFiles`로 분리해 사람 경로(`ApproveImport`)와 tier-2 내부 경로(`ApplyAutoImport`)가
  공유, dispatch 완료 직전 `Tier2Approver.MaybeAutoApprove` 호출.
- `server/Tier2ApproverTestCli.cs` (신규, 검증용): `dotnet run --project server -- tier2test <시나리오>`로
  임시 작업공간에서 8개 분기를 결정론적으로 재현. `server/Program.cs`에 4줄만 추가해 라우팅
  (기존 `simtest`/`snapshot-behavior` 등과 동일한 패턴).
- `server/appsettings.json`: `Tier2Approver` 설정 섹션 추가(`Enabled: false`, `DailyCap: 5`).
- `docs/audit/tier2-import-approvals.md`: 사람이 읽는 감사 로그(매 평가마다 한 줄 append) — 신규.
- `docs/audit/tier2-import-approvals-state.json`: 일일 캡 카운트 + halted 상태(gitignore 대상 아님,
  다만 처음엔 파일이 없다가 첫 이벤트에서 생성됨). halt 해제는 사람이 이 파일의 `halted`를
  `false`로 고쳐야 한다(자동 해제 없음 — 의도적).

## 실측 — CLI 시나리오 (결정론적, `dotnet run --project server -- tier2test <시나리오>`)

| 시나리오 | 기대 결과 | 실제 결과 |
| --- | --- | --- |
| `eligible-approved` | 게이트 클린 + AI 승인 → imported | `{"status":"imported","decision":"approved","eligible":true}` |
| `disabled` | 기능 꺼짐 → 기존과 동일하게 대기 | `{"status":"import_pending","decision":"skipped","reason":"tier2_disabled"}` |
| `core-file-touched` | Engine.cs 변경 → 부적격 | `{"status":"import_pending","decision":"blocked_ineligible","eligible":false}` |
| `baseline-file-touched` | workflow-definition.json 변경 → 부적격 | `{"status":"import_pending","decision":"blocked_ineligible","eligible":false}` |
| `violations-increased` | 위반 3→5 → 부적격 | `{"status":"import_pending","decision":"blocked_ineligible","eligible":false}` |
| `reviewer-unavailable` | 리뷰어 연결 실패(실제 미연결 endpoint) → 사람 대기 | `{"status":"import_pending","decision":"reviewed_not_approved","reason":"reviewer_unavailable"}` |
| `daily-cap`(cap=5, 6건 연속) | 6번째 건 차단 | `{"status":"import_pending","decision":"blocked_daily_cap"}` |
| `anomaly-halt` | 반입 후 재측정에서 위반 증가(3→7) → halt, 다음 건도 차단 | `{"firstDecision":"anomaly_halted","secondDecision":"blocked_halted"}` |

8개 시나리오 전부 기대대로 동작했다.

## 실측 — 실제 서버 + 실제 로컬 Ollama(qwen3:14b) dispatch 2회

검증용 사본 appsettings(포트 5299, `Tier2Approver.Enabled:true`, `DailyCap:2`)로 실제
`dotnet run --project server`를 띄우고 `POST /api/projects/dev-pack/actions/dispatch`를
실제 토큰으로 호출했다(작업 트리는 실제 저장소 그대로 — 서버가 자체 워크스페이스를 사본으로
복제해 실행하는 기존 dispatch 격리 구조를 그대로 썼다).

- 1차 호출에서 `gateViolationsBefore`가 `2147483647`(파싱 실패 안전값)로 나오는 버그를 발견했다 —
  원인: 반입 전 기준선 측정이 **실행 중인 서버 자신의 `server/bin` 산출물이 잠긴 상태**에서
  재빌드를 시도해 `MSB3026/3027`(apphost.exe 복사 충돌)로 실패했다. `RunMeasureAsync`에
  `noBuild` 옵션을 추가하고 원본 워크스페이스 측정에는 `--no-build`를 쓰도록 고쳤다(사본
  측정은 그대로 풀빌드 — 사본엔 bin이 없으므로 문제 없음). 아울러 `CheckEligibility`가
  `int.MaxValue`(측정 실패 안전값)를 "위반 안 늘어남"으로 잘못 통과시키는 논리 구멍도
  발견해 명시적으로 부적격 처리하도록 고쳤다.
- 수정 후 재호출: `gateViolationsBefore:5, gateViolationsAfter:5`(정확히 일치, 파싱 정상),
  `tier2.eligible:true`, 실제 Ollama(`qwen3:14b`) 리뷰 호출 결과 `verdict:"reject"` →
  `decision:"reviewed_not_approved"` → `status`는 그대로 `import_pending`으로 사람 대기 유지.
  실제 로컬 LLM 호출이 전 구간 정상 동작함을 확인했다(daemon 미연결 시의 안전 폴백은
  위 CLI `reviewer-unavailable` 시나리오로 이미 검증).
- 이 두 건의 실측 기록은 `docs/audit/tier2-import-approvals.md`에 실시간으로 남았다(제출 전
  테스트 항목이라 최종 커밋에서는 헤더만 남기고 비웠다 — 실측값은 위 표에 그대로 인용).

## 게이트

`dotnet run --project server -- measure dev-pack` (실제 저장소, 최종 상태):

```json
{"gate":"dev-pack","violations":4,"attempt":2}
```

attempt 1(수정 전, `functionsWithoutComment=5` 포함) 대비 함수 주석 누락 5건은 이번 커밋에서
전부 수정했다. 남은 4건:

- `smallTouchTargets=1` (`dashboard/style.css:1133`, `.inbox-import-actions .button`) — **기존 위반**,
  이번 작업 이전부터 있었고(대기 중인 proposal-1783646562038이 이 항목의 수정을 이미 제안 중),
  이번 세션에서 건드리지 않았다.
- `skillDomainViolations=2` (`docs/verification/tuning-advanced.md` 참조 불일치) — **기존 위반**,
  이번 작업과 무관.
- `maxFunctionLength=246` (`server/Program.cs:790-1035`) — **기존 위반**, 함수 자체는 손대지
  않았고 위쪽 줄 삽입으로 라인 번호만 밀렸다(246줄 그대로).
- `programCsLines=2689` (band 0~2661) — **이번 세션에서 5줄 악화**(2684→2689). `tier2test` CLI
  라우팅 4줄 + 공백 1줄. 검증용 CLI를 Program.cs 최상단 기존 패턴(simtest/snapshot-behavior와
  동일한 분기)으로 배선하기 위한 최소 추가다. 기존에 이미 23줄 초과 상태였고(2684>2661,
  DECISIONS.md 2026-07-10 기록 — Program.cs 분리는 반입 대기 중인 self-refactor-dispatch 몫),
  이번 5줄로 28줄 초과가 됐다. 측정 코드나 band 자체는 건드리지 않았다.

## 참조한 스킬

- `skills/common/verification.md`
- `skills/common/directive-writing.md`

## 추측 진행

- 이 작업은 지시서가 아니라 사람이 채팅으로 직접 승인한 결정의 대행 구현이라, 착수 전 "선택지
  딸린 질문"을 던지는 대신 아래 사항은 이번 세션이 판단해 진행하고 여기 남긴다:
  - `Tier2Approver.Enabled` 기본값을 **false**로 유지(이양 고정점 재발 방지 — 위 절 참고). 사람이
    즉시 켜는 것까지 원했을 수도 있어, 그 경우 `server/appsettings.json`의
    `Tier2Approver.Enabled`를 `true`로 바꾸면 된다.
  - "상위 티어 AI"는 이 환경에 실제로 설치된 모델 중 가장 큰 `qwen3:14b`로 정했다(더 큰 모델이
    로컬에 없음 — `reviewerPolicy.tier1`이 이미 이 모델을 검토용으로 쓰고 있어 동일 선택).
  - 일일 캡 기본값 5는 이 프로젝트에 정확히 대응하는 기존 캡이 없어(가장 가까운 것은
    `guardrails.maxSubscriptionCalls` 등 프로젝트별 개념이라 outbox 반입에는 그대로 못 씀)
    새로 정한 합리적 기본값이다 — DECISIONS.md에 근거를 남겼다.

## 추가 반영 — 활성화 + 주석

사람이 채팅으로 `Tier2Approver.Enabled: true` 활성화를 명시적으로 확인해 반영했다
(`server/appsettings.json`). `server/Tier2Approver.cs`의 `RequestReview` 위에 이 시스템의
시뮬레이션 기반 선택(simtune/BalanceTuner류) 기능에 대한 한국어 기능 주석을 추가했다.

게이트 재확인(활성화 + 주석 반영 후):

```json
{"gate":"dev-pack","violations":4,"attempt":3}
```

attempt 2와 동일한 4건(`smallTouchTargets`/`skillDomainViolations`/`programCsLines`/
`maxFunctionLength`) — 이번 변경(설정값 1개 + 주석 2줄)으로 새로 생기거나 악화된 위반은 없다.
`functionsWithoutComment`도 0 유지.

