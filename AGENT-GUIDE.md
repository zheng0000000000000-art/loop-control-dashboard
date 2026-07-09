# AGENT-GUIDE — 새 에이전트가 맥락 없이 읽고 즉시 쓰는 문서

이 문서만 읽고도 이 시스템에서 일을 받아 처리하고 결과를 남길 수 있어야 한다. 배경 설명은 최소로 하고, 실행 가능한 것만 적는다.

## ① 시스템 3문장

목표(blueprint)를 선언하면 로컬 AI들이 측정→제안→상호 검토를 자율로 반복해 기준을 통과한 답안만 사람 결재에 올리는 로컬 우선 워크 시스템이다. 서버는 `dotnet run --project server`로 뜨는 C# ASP.NET Minimal API 하나이며, 기본 주소는 `http://localhost:5173`(포트가 막혀 있으면 `server/appsettings.json`의 `BindUrls`와 `.claude/launch.json`의 `port`를 함께 바꿔 확인한다). 코드 변경·결재·반입은 구조적으로 사람만 할 수 있고, 에이전트는 인박스를 읽고 dispatch로 격리 실행한 결과(outbox)를 사람에게 넘기는 역할이다.

## ② 사용 API 목록

베이스 URL: `http://localhost:5173` (로컬 기본). 쓰기 요청(POST)은 서버에 `RemoteActionToken`이 설정된 경우에만 헤더 `X-Action-Token: <토큰>`이 필요하다 — 로컬 기본 설정(`appsettings.json`의 `RemoteActionToken` 빈 문자열)에서는 토큰 없이도 된다. GET은 항상 토큰이 필요 없다. **예외**: `dispatch`·`outbox` 계열 액션(`/actions/dispatch`, `/actions/self-refactor-dispatch`, `/outbox/{id}/approve-import`, `/outbox/{id}/reject-import`)은 서버에 토큰이 반드시 설정돼 있어야 동작한다(설정 안 돼 있으면 로컬에서도 401) — 코드 실행 경로는 토큰 없이 열어두지 않는다는 안전장치다.

| 메서드 | 엔드포인트 | 용도 | 요청 예시 | 응답 예시 |
| --- | --- | --- | --- | --- |
| GET | `/data/projects.json` | 등록된 프로젝트 ID 목록(정적 파일, `/api` 아님) | — | `{"projects":[{"id":"ruined-lab","name":"...","path":"./data/ruined-lab/"},{"id":"dev-pack","name":"...","path":"./data/dev-pack/"}]}` |
| GET | `/api/inbox` | 모든 프로젝트의 사람/에이전트 대기 항목 전체 | — | `{"schemaVersion":2,"items":[{"projectId":"dev-pack","kind":"regression","title":"악화 확인 필요","summary":"skillDomainViolations","assignableTo":"human"}]}` |
| GET | `/api/projects/{id}/context` | 온보딩 한 방 조회: blueprint+최근 회차+미결+관련 스킬 경로 | — | `{"schemaVersion":2,"projectId":"ruined-lab","blueprint":{...},"recentCycle":{"loopIteration":7,"segments":{...}},"pending":[],"relevantSkillPaths":["skills/common/directive-writing.md","skills/domains/game/balance-tuning.md"]}` |
| GET | `/api/projects/{id}/state` \| `/runlog` \| `/proposal` \| `/reviews` \| `/definition` \| `/blueprint` \| `/measurement` \| `/cycle-summary` | 프로젝트 파일 개별 조회 | — | 해당 JSON 파일 그대로 |
| POST | `/api/projects/{id}/actions/measure` | 측정 재실행(코드 변경 아님, 읽기에 가까움) | `{}` | `{"state":{...},"runLog":{...},"proposal":{...},"reviewReport":{...},"measurement":{...},"cycleSummary":{...}}` |
| POST | `/api/projects/{id}/actions/dispatch` (토큰 필수) | 저장소 **사본**에서 지시를 실행하고 diff를 outbox에 만든다 | `{"executor":"claude-code","instruction":"server/Foo.cs에 X 기능 추가. 완료 기준: dotnet build 오류 0, measure dev-pack 무위반."}` | `{"taskId":"task-...","status":"import_pending"\|"needs_questions"\|"failed","changedFiles":[...],"measureExitCode":0,...}` |
| GET | `/api/outbox/{taskId}` | dispatch 결과 상세(diff 포함) 조회 | — | `{"taskId":"...","status":"import_pending","diff":"--- a/...","changedFiles":[...]}` |
| POST | `/api/projects/{id}/outbox/{taskId}/approve-import` (토큰 필수, **사람 전용**) | outbox diff를 본 저장소에 반입 | — | — |
| POST | `/api/contributions` | 기여 제출(아래 ⑤) | `{"kind":"incident","content":"...","evidence":"..."}` | `{"event":"contribution.submitted","params":{"id":"contribution-...","kind":"incident","status":"recorded","incidentPath":"docs/incidents/..."}}` |

`executor`는 `claude-code`·`codex`·`ollama` 중 하나만 허용된다. `instruction`에는 대상 파일/범위와 완료 기준(검증 방법)이 들어 있어야 한다 — 없으면 `dispatch`가 즉시 `needs_questions` 상태로 되돌아온다(아래 항목 참고).

## ③ 작업 수명주기

1. **인박스 확인**: `GET /api/inbox`로 대기 항목을 본다. 각 항목의 `assignableTo`가 `human`이면 손대지 않는다(결재·반입·기준 변경류). `assignableTo`가 `agent_or_human`이거나 항목이 없으면(예: `dispatch_questions`) 에이전트가 정보를 보완해 다시 시도할 수 있다.
2. **맥락 파악**: `GET /api/projects/{id}/context`로 blueprint·최근 회차·관련 스킬 경로를 한 번에 확인한다. `relevantSkillPaths`에 나온 파일만 읽는다(스킬 라우팅 원칙).
3. **dispatch 수행**: `POST /api/projects/{id}/actions/dispatch`로 지시를 보낸다. 서버가 워크스페이스를 임시 사본으로 복제해 그 안에서만 실행하고, 원본 저장소는 건드리지 않는다.
4. **outbox 산출**: 실행이 끝나면 `status: "import_pending"`과 함께 diff·측정 결과가 `outbox/{taskId}/`에 남는다. `GET /api/outbox/{taskId}`로 diff를 확인할 수 있다.
5. **반입은 사람**: outbox 상태가 `import_pending`이면 그대로 둔다. `approve-import`/`reject-import` 호출은 에이전트가 하지 않는다 — 사람이 diff를 보고 결정한다.

## ④ 금지선

- **결재·반입 대행 금지**: `/actions/approve`, `/actions/reject`, `/outbox/{id}/approve-import`, `/outbox/{id}/reject-import`는 에이전트가 호출하지 않는다. 검증 목적이라도 예외 없음.
- **기준 완화 금지**: `blueprint.json`·`workflow-definition.json`(레버 범위, 게이트 조건, 체크리스트 완화 포함)을 고쳐서 측정을 통과시키지 않는다. 기준 변경이 필요하면 사람에게 질문으로 되묻는다.
- **git 직접 조작 금지**: 코드 변경의 기본 경로는 dispatch/outbox다(아래 관례 참고). `git commit`·`git push`를 직접 실행하는 것은 예외 상황(관례·가이드 문서 자체, 또는 지시서에 "직접 경로"가 명시된 경우)에만 허용되고, 예외를 썼으면 보고에 사유를 남긴다.
- **코어 3파일 청결**: `Engine.cs`·`Storage.cs`·`Guardrails.cs`에 게임 용어·metricId·ollama/ntfy 같은 특정 provider 지식을 넣지 않는다.

## ⑤ 기여 방법

`POST /api/contributions`로 4종 중 하나를 제출한다:

- `kind: "incident"` — 실제로 벌어진 장애·마찰의 **사실 기록**. 즉시 `docs/incidents/`에 파일로 남고 승급 절차가 없다(이미 일어난 일이므로).
- `kind: "skill_draft"` | `"checklist_suggestion"` | `"blueprint_suggestion"` — **개선 제안**. `contributions.json`에 `status: "pending"`으로 쌓이고 `GET /api/inbox`에 `kind: "contribution"`, `assignableTo: "human"`으로 뜬다. 사람이 승급(채택) 여부를 결정한다 — 에이전트가 스스로 스킬·blueprint에 반영하지 않는다.

요청 본문: `{"kind": "...", "content": "무엇을·왜", "evidence": "근거(로그·명령·파일 경로)"}`. `kind`가 4종에 없거나 `content`가 비어 있으면 400을 받는다.
