# AI 1급 사용자 — 에이전트의 파이프라인 상주 실행 검증

검증일: 2026-07-10

목적: 에이전트가 이 시스템의 "일 받기·작업·보고·기여" 경로를 파이프라인(인박스→dispatch→outbox→반입)으로 쓰도록 만든다. 이 지시서 자체는 전환 이전이므로 직접 커밋 경로가 허용됐다 — 아래 모든 코드/문서 변경은 그 예외를 쓴 것이고, 이 사실을 여기 명시한다.

참조한 스킬: `skills/common/directive-writing.md`, `skills/common/verification.md`(항상), `skills/domains/dev/file-navigation.md`(트리거 `server/**`, `dashboard/data/**`, `AGENTS.md`, `CLAUDE.md`, `skills/**` 일치), `skills/domains/docs/README.md`(트리거 `docs/**` 일치, 내용은 빈 스텁). `design`·`game` 도메인은 변경 파일 경로(`dashboard/index.html`·`style.css`·`app.js`, `ruined-lab` 데이터)와 무관해 열지 않았다.

## A. AGENT-GUIDE.md

루트에 신규 작성(①시스템 3문장 ②API 표 ③수명주기 ④금지선 ⑤기여 방법). `CLAUDE.md`·`AGENTS.md` 최상단에 "먼저 AGENT-GUIDE.md를 읽는다" 한 줄 추가.

## B. API 정비

### ① `GET /api/inbox`의 `assignableTo`

`AddProjectInboxItems`(approval/checkpoint/guardrail/regression)와 `OutboxManager.AddInboxItems`(import_pending/dispatch_questions), 신규 `ContributionStore.AddPendingInboxItems`(contribution) 세 생산자 모두에 `assignableTo` 필드를 붙였다: 결재·반입·악화 확인·기여 승급은 `"human"`, dispatch 지시 보완은 `"agent_or_human"`. 실측(아래 D에서 만든 실제 데이터 포함):

```json
{"kind":"regression","assignableTo":"human"}
{"kind":"import_pending","taskId":"task-20260709133814794","assignableTo":"human"}
{"kind":"import_pending","taskId":"task-20260709193731051","assignableTo":"human"}
{"kind":"contribution","contributionId":"contribution-20260709193544899","assignableTo":"human"}
```

### ② 기존 dispatch

무변경 확인: `outboxManager.DispatchAsync`/`ApproveImport`/`RejectImport` 로직은 손대지 않았다(`git diff server/OutboxManager.cs`는 `AddInboxItems`의 `assignableTo` 한 줄 추가뿐).

### ③ `POST /api/contributions`

신규 `server/ContributionStore.cs`. `kind`가 `incident`면 `docs/incidents/`에 즉시 사실 기록 파일을 쓰고 `status:"recorded"`, 나머지 3종(`skill_draft`/`checklist_suggestion`/`blueprint_suggestion`)은 전역 `dashboard/data/contributions.json`에 `status:"pending"`으로 쌓여 `GET /api/inbox`에 `kind:"contribution"`으로 뜬다(승급은 사람). `contribution.submitted` 이벤트 형태로 기록하고 `ko.json`/`en.json`에 템플릿 추가(`"기여 접수: {kind}, 상태 {status} — {content}"`). Storage.cs에는 도메인 지식 없는 범용 헬퍼 3개(`GlobalFilePath`/`ReadGlobalFile`/`WriteGlobalFile`)만 추가했다.

### ④ `GET /api/projects/{id}/context`

`bundle.Blueprint` + 기존 `BuildCycleSummary`(중복 구현 없이 재사용) + 기존 `AddProjectInboxItems`(그대로 재사용) + 신규 `SkillRouter.RelevantPaths`(skills/ 파일의 '트리거:' 메타데이터 줄을 파싱해 `dashboard/data/{id}/` 후보 경로와 겹치는 스킬만 반환 — 도메인 하드코딩 없이 스킬 파일 자체가 선언한 트리거로 매칭). 실측(`ruined-lab`):

```
relevantSkillPaths: ["skills/common/directive-writing.md","skills/common/verification.md","skills/domains/dev/file-navigation.md","skills/domains/game/balance-tuning.md"]
```

`dev-pack`으로 호출하면 `game` 도메인 스킬은 빠지고 `dev`만 남는 것도 확인했다(design/game 트리거가 프로젝트 데이터 경로와 안 겹침).

## C. 관례 개정

`CLAUDE.md`/`AGENTS.md`의 `## 관례`에 추가: "코드 변경의 기본 경로는 dispatch/outbox다 — 사본에서 작업하고 diff를 제출하며, 반입은 사람이 한다. 직접 수정 + 커밋은 예외이며 다음 경우에만 쓴다: ①관례·가이드 문서 자체 ②지시서에 '직접 경로'가 명시된 경우. 예외를 썼으면 작업 보고에 사유를 남긴다." 이번 작업의 서버 코드(`Program.cs`, `Storage.cs`, `OutboxManager.cs`, 신규 `ContributionStore.cs`/`SkillRouter.cs`) 직접 수정은 이 새 관례의 예외 ②를 쓴 것이다 — 지시서 원문이 "이 지시서 자체는 직접 경로 허용(전환 이전이므로)"이라고 명시했다.

## D. 자기 사용 검증

서버를 재시작(`preview_start`, 포트 충돌 회피로 5199 임시 사용 — 기존 스킬 문서에 기록된 우회)해 새 코드를 반영한 뒤, **AGENT-GUIDE.md만 보고** 아래를 실제로 호출했다.

### D-1. 프로젝트 발견 + context 조회

```
GET /data/projects.json → {"projects":[{"id":"ruined-lab",...},{"id":"dev-pack",...}]}
GET /api/projects/ruined-lab/context → 200, blueprint 4항목 + recentCycle(loopIteration:7) + pending:[] + relevantSkillPaths 4개
GET /api/projects/dev-pack/context → 200, blueprint 16항목 + pending:[{kind:"regression","assignableTo":"human"}] + relevantSkillPaths 3개(dev만)
```

가이드만으로 막힘 없이 두 호출 모두 한 번에 성공했다 — 여기서는 가이드 결함이 없었다.

### D-2. 기여 제출 — 마찰 1건 + 실발견 2건

첫 시도에서 `curl -d '{...한글...}'`(인라인 셸 문자열)로 보내자 서버가 `400 {"reasonCode":"dispatch.failed","reason":"Cannot transcode invalid UTF-8 JSON text to UTF-16 string."}`를 반환했다. 원인을 추적하니 Windows Git Bash가 인라인 `-d` 인자의 한글을 UTF-8이 아닌 바이트로 넘기는 셸 환경 문제였다(서버 코드 결함도 AGENT-GUIDE 결함도 아니다) — 파일로 저장한 뒤 `curl --data-binary @file`로 보내자 즉시 성공했다. 이 마찰은 가이드에 반영하지 않았다(가이드는 요청 형식만 규정하고 셸 인코딩까지 책임질 범위가 아니라고 판단) — 대신 여기 정직하게 기록한다.

실제로 두 건을 제출했다:

1. **incident** — `GET /api/projects/dev-pack/context` 응답을 읽다가 `blueprint.json`의 `programCsLines`/`appJsLines`/`maxFunctionLength` 세 항목 `note`가 한글 대신 리터럴 `?`로 깨져 있는 것을 발견했다(다른 13개 항목은 정상). `docs/incidents/2026-07-10-agent-contribution-423742.md`로 즉시 기록됐다(`status:"recorded"`, `incidentPath` 반환 확인). `blueprint.json`은 고치지 않았다 — 기준 파일 직접 수정 금지.
2. **skill_draft** — `skills/domains/dev/file-navigation.md`의 트리거 목록에 `AGENT-GUIDE.md`가 빠져 있음을 발견(방금 만든 루트 문서인데 `CLAUDE.md`/`AGENTS.md`만 트리거에 있음). `contributions.json`에 `status:"pending"`으로 쌓였고 `GET /api/inbox`에 `kind:"contribution"`, `assignableTo:"human"`으로 뜨는 것을 확인했다. 스킬 파일은 사람 승인 없이 직접 고치지 않았다.

두 제출 모두 `event:"contribution.submitted"` 형태로 정확히 반환됐고, incident는 즉시 `status:"recorded"`로, 나머지는 `status:"pending"`으로 정확히 분기했다.

### D-3. dispatch → outbox → import_pending

`RequireDispatchToken`은 서버에 토큰이 설정돼 있지 않으면 로컬에서도 무조건 401을 낸다는 걸 코드에서 미리 확인했다 — `appsettings.json`에 임시 토큰(`agent-guide-verification-temp`)을 설정하고 서버를 재시작해 검증했다. 첫 시도로 가이드 예시와 같은 "README 한 줄 추가" 지시를 보냈더니 `status:"failed"`, `changedFiles:[]`가 나왔다 — 원인을 `README.md`에서 확인하니 그 줄(`- Dispatch verification line.`)이 이전 세션의 dispatch 검증에서 이미 추가돼 있어 이번 실행이 멱등하게 아무것도 안 한 것이었다(하네스 버그도 가이드 결함도 아님, 이미 적용된 결정론적 규칙의 자연스러운 재실행 결과). 두 번째로 "no rule matched" 기본 경로를 타는 무해한 지시를 보내자:

```json
{"taskId":"task-20260709193731051","status":"import_pending","executorExitCode":0,"changedFiles":["server/EXECUTOR_REPORT.md"],"measureExitCode":1,"behaviorExitCode":1,"strictGate":false}
```

`GET /api/outbox/task-20260709193731051`로 diff 확인, `GET /api/inbox`에 `kind:"import_pending"`, `assignableTo:"human"`으로 뜨는 것 확인. **`approve-import`/`reject-import`는 호출하지 않았다** — 반입은 사람 대기로 남겨뒀다. 검증 후 임시 토큰과 포트(5199→5173)를 원래 값으로 되돌리고 재빌드해 `git diff server/appsettings.json`이 빈 것을 확인했다.

### D-4. 부수 발견 — outbox가 .gitignore에 없었다

`git status`에서 `outbox/task-*/`가 `??`(추적 대상 후보)로 뜨는 것을 보고 `.gitignore`를 확인하니 `outbox/`가 빠져 있었다(CLAUDE.md는 "git status로 outbox 미포함 확인"만 요구하고 자동 차단은 없었다). `.gitignore`에 `outbox/` 한 줄을 추가했다 — 이미 커밋된 과거 outbox 파일은 그대로 두고(추적 해제는 별도 결정 사항), 앞으로의 실수만 막는다.

## 불변 확인

| 항목 | 방법 | 결과 |
| --- | --- | --- |
| 코어 3파일 청결 | `rg -n "ContributionStore|SkillRouter|skill_draft|blueprint_suggestion" server/Engine.cs server/Storage.cs server/Guardrails.cs` | 결과 없음(Storage.cs에는 범용 Global 헬퍼만, kind 문자열은 없음) |
| 결재·반입 대행 없음 | 세션 전체에서 `approve`/`reject`/`approve-import`/`reject-import` 미호출 | 확인 |
| 기준 완화 없음 | `blueprint.json`·`workflow-definition.json` 미수정(D-2에서 발견한 깨짐도 고치지 않음) | `git diff --stat`로 무변경 확인 |
| 임시 설정 원복 | `RemoteActionToken`, `BindUrls`, `.claude/launch.json` port | `git diff server/appsettings.json` 빈 결과 |
| 빌드 | `dotnet build server/LocalFirstWorkflowDashboard.Server.csproj` | 경고 0, 오류 0 |

## CLAUDE.md 게이트

```
dotnet run --project server -- measure dev-pack
```

`{"gate":"dev-pack","violations":3,"attempt":2}`

3건 중 2건은 이번 작업과 무관한 기존 위반이다:
- `skillDomainViolations=2` — evidence가 `docs/verification/tuning-advanced.md`를 가리킨다(이번 작업이 만든 문서 아님).
- `maxFunctionLength=246`(`server/Program.cs:785-1030`, `ApplyMeasurementResult`) — 이미 반입 대기 중인 self-refactor-dispatch(`task-20260709133814794`, Orchestrator.cs/ProposalFlow.cs 분리)가 해소할 몫으로 이전 세션부터 추적 중이던 건이다.

나머지 1건은 이번 작업이 직접 만들었다: `programCsLines=2684`(밴드 `[0,2661]`, 23줄 초과). `Program.cs`는 시작 시점에 2639줄(여유 22줄)이었는데, API 2종·`assignableTo` 3곳·`contributions` 라우팅으로 +45줄이 필요했다. 스킬 트리거 매칭 로직(`RelevantSkillPaths`+`MatchesTrigger`, 38줄)은 다른 Program.cs 로컬 함수에 의존하지 않아 안전하게 `server/SkillRouter.cs`로 뽑아냈지만(최초 +83줄 → +45줄로 축소), `ProjectContext`는 `AddProjectInboxItems`·`BuildCycleSummary`·`ProjectDisplayName` 같은 Program.cs 로컬 함수(다른 파일에서 호출 불가 — 톱레벨 문의 로컬 함수 제약)에 의존해 더 뽑아내려면 그 함수들까지 별도 파일로 승격해야 했다. 그건 이미 대기 중인 self-refactor-dispatch가 겨냥하는 것과 같은 범위의 구조 변경이라, 같은 지표를 이 작업에서 또 손대는 대신 위반으로 정직하게 보고하고 `DECISIONS.md`에 판단 근거를 남겼다. `programCsLines`의 밴드 자체는 건드리지 않았다.

## 결론

- AGENT-GUIDE.md 단독 사용으로 프로젝트 발견→context 조회→기여 제출→인박스 확인까지 막힘없이 완료했다(마찰 1건은 셸 인코딩이었지 가이드 결함이 아니었다).
- `POST /api/contributions`는 실제로 동작했고, 그 과정에서 진짜 결함(blueprint.json 한글 깨짐)과 진짜 개선 제안(스킬 트리거 누락)을 하나씩 발견해 사실대로 기록·제안했다 — 둘 다 직접 고치지 않았다.
- 관례가 개정됐고, 이번 작업의 직접 커밋 자체가 그 관례의 명시된 예외임을 여기 남긴다.
- 결재·반입은 여전히 사람 전용이다 — dispatch로 만든 outbox 산출물(`task-20260709193731051`)은 `import_pending` 상태로 남겨뒀다.
- 위반 3건 중 1건(`programCsLines`)은 이번 작업이 만들었고 완전히 해소하지 못했다 — 위 표에 그대로 남긴다.
