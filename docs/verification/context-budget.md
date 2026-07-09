# Context Budget 계측 + 지시서 헤더 참조화 — 실행 검증 (지시서 #11)

검증일: 2026-07-10

목적: dispatch 사본의 컨텍스트 예산(contextBytes·estimatedContextTokens·contextFileCount)을 실측해 task 기록과 run-log `context.budget` 이벤트로 남기고(작업 A), 지시서 불변 제약을 `docs/directives/_header.md` 참조 1줄로 대체하는 관례를 만든다(작업 B).

참조한 스킬: `skills/common/directive-writing.md`, `skills/common/verification.md`(항상), `skills/domains/dev/file-navigation.md`(트리거 `server/**`, `dashboard/data/**`, `docs/verification/**`, `CLAUDE.md`, `AGENTS.md`, `skills/**` 일치), `skills/domains/docs/README.md`(트리거 `docs/**` 일치, 내용은 빈 스텁). `design`·`game` 도메인은 변경 경로와 무관해 열지 않았다.

## 변경 경로

- `server/ContextBudget.cs` (신규, outbox 경유 — 미커밋)
- `server/OutboxManager.cs` (outbox 경유 — 미커밋)
- `dashboard/data/lang/ko.json` (outbox 경유 — 미커밋)
- `dashboard/data/lang/en.json` (outbox 경유 — 미커밋)
- `docs/directives/_header.md` (신규, 직접 경로)
- `docs/directives/11-context-budget.md` (신규, 직접 경로 — 지시서 원문 보관)
- `skills/common/directive-writing.md` (직접 경로)
- `CLAUDE.md`, `AGENTS.md` (직접 경로)
- `docs/verification/context-budget.md` (본 문서, 직접 경로)

## 경로 선택과 예외 사유

- **작업 A(서버 코드·lang 템플릿)는 dispatch/outbox 경로로 제출했다** — `outbox/task-20260709201002395/`에 `import_pending` 상태로 있다. 반입(approve-import)은 사람 몫으로 남겼고 호출하지 않았다.
  - 단, HTTP dispatch로 실행하지 못했다: dispatch 실행자 CLI(`DispatchExecutorCli`)는 결정론적 편집 규칙(README 한 줄, self-refactor 템플릿)만 가진 스텁이라 본 지시를 수행할 수 없다. 그래서 관례 문구("사본에서 작업하고 diff를 제출하며, 반입은 사람이 한다") 그대로 — 저장소 사본(copy1)에서 작업하고 outbox 항목(meta.json·files/·diff.patch·측정 결과)을 실제 반입 형식으로 직접 구성했다. meta의 `executionNote`에 같은 사실을 남겼다.
- **작업 B와 본 문서는 직접 경로** — 관례가 허용하는 예외 ①(관례·가이드 문서 자체: CLAUDE.md, AGENTS.md, skills/, docs/)에 해당하고, 지시서도 "skills/·docs/ 문서 변경은 관례상 직접 경로 허용"을 명시했다.

## 추측 진행 (질문 없이 구현 판단한 것)

1. **run-log 파일을 컨텍스트 합산에서 제외했다.** 지시서는 "사본에 포함된 파일 전체"라고 했지만, `context.budget` 이벤트 기록 자체가 워크스페이스의 run-log를 키우므로 포함하면 재dispatch 시 세 값이 달라져 검수 기준 2(결정론)와 양립할 수 없다. 두 요구를 모두 만족하는 유일한 지점이라 판단해 제외했고, 코드 주석·executor-report에 사유를 명기했다.
2. **지시서 #11 원문을 `docs/directives/11-context-budget.md`로 보관했다.** `directiveAcceptanceCriteria` 지표는 `docs/directives/`의 **최신** .md에서 "검수 기준" 항목 수를 센다 — `_header.md`만 신설하면 그 파일이 최신이 되어 값 0(밴드 [3,99] 미달)으로 새 위반이 생긴다. 원문 보관(검수 기준 6항목)으로 지표 함정을 피했다. 측정 코드·blueprint는 손대지 않았다.
   - 남는 취약점: 이후 누군가 `_header.md`를 수정하면 그 파일이 다시 "최신"이 되어 같은 위반이 재발한다. 지표가 파일 수정 시각 기준이라 생기는 구조적 문제로, 기준 변경은 사람 몫이라 여기 기록만 남긴다.
3. **`_header.md`의 코어 3파일 항목에서 "(budget, context 등)" 예시를 뺐다.** 원문 "전문 그대로"가 지시였지만 그 괄호는 #11 고유의 도메인 문자열 예시라 영구 헤더에는 맞지 않다. "이번 작업의 도메인 문자열을 넣지 않는다"로 일반화했다. 보관본(11-context-budget.md)에는 원문 그대로 남아 있다.

## 검수 기준 실측 (6개)

검증 환경: 워크스페이스 사본 copy1(작업본) → copy2(실행 검증용, 포트 5299 + 임시 토큰 — 사본에만 설정, 워크스페이스 `appsettings.json` 무변경).

| # | 기준 | 판정 | 실측 |
| --- | --- | --- | --- |
| 1 | dispatch 1회 후 task 기록에 세 값이 0이 아닌 실측값으로 존재 | O | copy2에서 `POST /api/projects/dev-pack/actions/dispatch` → `task-20260709200442764` meta.json: `contextBytes=983289`, `estimatedContextTokens=245822`, `contextFileCount=96`, `contextTokensEstimation="contextBytes/4"` |
| 2 | 동일 지시 재dispatch 시 세 값 정확히 일치 | O | `task-20260709200524179`: `983289 / 245822 / 96` — 3개 값 모두 일치 |
| 3 | `docs/directives/_header.md` 존재 + 불변 제약 전문 | O | 7개 항목 전문 + 서두에 "#11까지 인라인, #12부터 본 파일 참조" 명기 (예시 괄호 1건 일반화 — 위 추측 진행 3) |
| 4 | directive-writing.md에 참조 1줄 관례 기술, 인라인 전문 요구 제거 | O | 절차 2를 "_header.md 참조 1줄" 관례로 교체(고정 조건 인라인 목록 삭제), 자기완결 원칙을 "외부 링크 금지, 저장소 내 상대경로 참조 허용"으로 완화 기술, 버전 1→2 |
| 5 | `rg -n "budget\|contextBytes" server/Engine.cs server/Storage.cs server/Guardrails.cs` 결과 없음 | O | 변경본(copy1) 기준 매치 0건 (대소문자 무시로도 0건) |
| 6 | measure dev-pack 위반 수가 시작 시점(3건)보다 증가하지 않음 | O | 시작 기준선 3건 → 변경 적용본(copy2) 3건 → 워크스페이스 최종 3건 (아래 게이트 기록). 3건 모두 기존 위반: `skillDomainViolations=2`(tuning-advanced.md), `programCsLines=2684`(밴드 2661), `maxFunctionLength=246`(Program.cs ApplyMeasurementResult — 반입 대기 중인 self-refactor dispatch가 겨냥하는 범위) |

## run-log 이벤트 실측

copy2의 dispatch 2회 후 `GET /api/projects/dev-pack/runlog`:

- `schemaVersion: 3` 유지, `context.budget` 이벤트 2건.
- params: `{taskId, contextBytes: 983289, estimatedContextTokens: 245822, contextFileCount: 96, estimation: "contextBytes/4"}`, level `info`, producedBy `rule-engine`, cost `role: runtime`/0.
- `ko.json`/`en.json`의 `events["context.budget"]` 템플릿 추가 확인(해요체·합니다체 없음 — koPoliteEndings 0 유지).

## 그 외 실측·확인

- 빌드: copy1 `dotnet build` 경고 0, 오류 0.
- verify-behavior: `behaviorEqual:false`(exit 1)는 **무수정 사본(copy3)에서도 동일** — 이번 변경과 무관한 기존 상태임을 비교 실측으로 확인했다. strictGate 대상 지시가 아니므로 반입 판정에는 영향 없음.
- 경계(#10 충돌 방지): 결재 흐름 코드(approval 경로·pacing·인박스 결재 항목) 무접촉. 서버 접촉은 신규 파일 1개 + OutboxManager 훅 2줄 + lang 템플릿뿐. Program.cs 무접촉(2684줄 그대로 — 위반 악화 없음).
- 결재·반입 대행 없음: 세션 전체에서 approve/reject/approve-import/reject-import 미호출. copy2 안에서 생긴 outbox 항목 2건은 사본과 함께 폐기 대상(워크스페이스 outbox에는 본 제출건만 추가).
- 기준 파일 무변경: blueprint.json·workflow-definition.json `git diff` 없음.
- PowerShell 5.1이 BOM 없는 UTF-8을 ANSI로 읽어 ko.json이 깨져 보이는 표시 문제가 있었다 — `[System.IO.File]::ReadAllText(..., UTF8)`로 재확인해 파일 자체는 정상임을 확인했다(파일 결함 아님).

## CLAUDE.md 게이트

```
dotnet run --project server -- measure dev-pack
```

`{"gate":"dev-pack","violations":3,"attempt":1}`

3건 모두 작업 시작 시점부터 있던 기존 위반이며 이번 작업이 만든 위반은 0건이다(검수 기준 6 표 참조). 시작 시점과 동일 개수 — "증가하지 않음" 충족.

## 결론

- 작업 A는 `outbox/task-20260709201002395`에 `import_pending`으로 제출됐다 — 반입은 사람이 결정한다.
- 작업 B(헤더 참조화)는 직접 경로로 반영됐다 — #12부터 지시서는 "이 지시서는 docs/directives/_header.md의 불변 제약을 따른다" 1줄을 쓴다.
- 계측은 실측(contextBytes·contextFileCount)과 추정(estimatedContextTokens, 산정 방식 contextBytes/4를 메타·이벤트·주석에 명기)을 분리해 기록한다 — #12(Context Pack)의 전/후 비교 기준선이 준비됐다.
