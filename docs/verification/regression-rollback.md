# 악화 감지 + 롤백 proposal 실행 검증

검증일: 2026-07-09

배경: measure 시 직전 측정과 신규 측정을 metric별로 비교해 "충족→위반" 전환(악화)만 감지한다. 악화 감지 시 rule-engine이 역산으로 롤백 proposal을 만들어 1층 검토를 건너뛰고 곧장 사람 결재로 보낸다. 자동 롤백은 어디에도 없다 — 언제나 proposal이고 결재는 사람이다.

## 시행착오: 첫 주입 대상이 틀렸다

`functionsWithoutComment` 위반을 만들려고 `server/Program.cs`의 `ProjectDisplayName` 함수 위 주석을 지웠으나, 재측정해도 위반이 잡히지 않았다. 원인 확인: 측정 정규식(`DevPackMeasures.CountFunctionsWithoutComment`)은 `^\s*(public|private|protected|internal)\s+...`로 접근제어자로 시작하는 줄만 함수로 인식하는데, `Program.cs`는 top-level statements 파일이라 로컬 함수에 접근제어자가 전혀 없다(`static string ProjectDisplayName(...)`처럼). 즉 이 체크는 애초에 `Program.cs`의 로컬 함수를 스캔 대상으로 보지 않는다 — 버그가 아니라 설계상 대상 범위 밖이었다. 주석을 원복하고, 접근제어자가 있는 `server/Notifier.cs`의 `public static void NotifyRestored(...)` 위 주석을 지우는 것으로 바꿔 실측을 진행했다.

## A. 기준선 확보

`dotnet run --project server --no-build -- measure dev-pack` → `{"violationCount":0,...}`, 종료 코드 `0`. `suspendedTracks: []`, `functionsWithoutComment: 0`.

## B. 악화 주입 → 감지 → 롤백 proposal 사람 직행

`server/Notifier.cs`의 `NotifyRestored` 위 주석 한 줄을 지운 뒤 `POST actions/measure`를 호출했다(ntfy 활성 + 임시 topic).

### run-log 이벤트 순서 (실측)

```
measure.completed        { violationCount: 1 }
stage.warning             { reasonCode: checklist.blueprint_deviation }
measurement.regressed    { metricId: functionsWithoutComment, before: 0, after: 1, reasonCode: checklist.metric_regressed }
proposal.created         { proposalId: proposal-1783590563696 }
review.routed            { proposalId: proposal-1783590563696, reasonCode: regression_direct_to_human, text: "악화 롤백은 체크리스트가 검토할 내용이 없어 1층을 건너뛰고 사람 결재로 직행한다." }
```

`measure.completed`(18:49:23.6885)부터 `review.routed`(18:49:23.6976)까지 **9ms** — `proposal.generated`나 `review.tier1_completed` 이벤트가 전혀 없다. 실행자(Ollama)도 검토자(Ollama)도 호출되지 않았다는 뜻이다(AI 호출이었다면 초 단위가 걸렸을 것 — 지난 검증들에서 실측한 생성 4~5초, 검토 7~8초와 대조됨).

### 생성된 롤백 proposal

```json
{
  "id": "proposal-1783590563696",
  "title": "악화 롤백 제안: functionsWithoutComment",
  "lifecycle": "submitted",
  "kind": "rollback",
  "createdBy": { "provider": "rule-engine", "model": null },
  "revisionOf": null,
  "changes": [
    {
      "path": "functionsWithoutComment",
      "before": 1,
      "after": 0,
      "note": "직전 승인 원인 미상 이후 functionsWithoutComment이 0→1로 악화됨. 되돌리거나 원인 조사 필요"
    }
  ]
}
```

`createdBy.provider = rule-engine`(Ollama 불요, 지시대로), `changes[0]`의 `before=1`(현재 악화값)·`after=0`(직전 정상값, 지시된 방향)이 정확히 일치한다. `note`는 고정 서술형 템플릿대로 생성됐다.

### suspendedTracks 등록

```json
{
  "metricId": "functionsWithoutComment",
  "detectedAt": "2026-07-09T18:49:23.6965226+09:00",
  "relatedProposalId": null,
  "suspectConfidence": "temporal"
}
```

### 용의자 추정 (D-3)

이 검증 세션에서는 승인 이력이 전혀 없으므로 `relatedProposalId: null`("원인 미상")로 정확히 기록됐다 — 이것도 정상 경로임을 확인했다(용의자가 없을 때 억지로 채우지 않는다).

### ntfy 알림 (긴급)

`https://ntfy.sh/{임시 topic}/json?poll=1`로 실측:

```json
{"title":"악화 감지","message":"개발 팩 — 자기 검수 루프: functionsWithoutComment 악화 — 롤백 제안이 결재 대기 중","priority":4}
```

`priority: 4`(high) — 통과성 이벤트나 일반 결재 대기(`priority: 3`)보다 급함이 구분된다.

### 헤더 배지

`GET state` 응답의 `suspendedTracks.length === 1`일 때 프런트가 헤더에 `status-regressed` 배지(주황, `--orange`/`--orange-bg` CSS 변수)를 표시하는 것을 `preview_inspect`로 확인했다(`className: "status-badge status-regressed"`, `color: rgb(255, 171, 94)`, `background-color: rgb(64, 39, 15)`). 클릭 시 `elements.approvalPanel.scrollIntoView(...)`가 등록되어 있다.

## C. 원복 → suspend 자동 해제

`NotifyRestored` 주석을 복원한 뒤 재측정했다.

| 항목 | 결과 |
| --- | --- |
| `violationCount` | `0` |
| `suspendedTracks` | `[]` |
| `track.resumed` 로그 | `{ metricId: "functionsWithoutComment" }` 발생 |
| 롤백 proposal `lifecycle` | `superseded`(기존 "위반 0 → 이전 submitted 제안 superseded" 로직 그대로 적용됨) |
| 사람 조치 | **없음** — 승인·거절 어느 것도 호출하지 않았다. superseded는 재측정만으로 자연히 일어났다 |

## 불변 확인

| 항목 | 명령 | 결과 | 판정 |
| --- | --- | --- | --- |
| Core 격리 | `rg -n "ollama\|reviewChecklist\|note-nonempty\|after-matches-goal\|no-scope-creep\|OllamaExecutor\|Notifier\|ntfy\|MetricRegression" server/Engine.cs server/Storage.cs server/Guardrails.cs` | `Engine.cs`에 `suspendedTracks` 2건만 매칭 — 이번 작업 이전부터 있던 범용 패스스루(`ApplyStatePatch`가 `state["suspendedTracks"]`를 의미 모른 채 그대로 복사)이며 신규 도메인 코드 아님 | O |
| 빌드 | `dotnet build server/LocalFirstWorkflowDashboard.Server.csproj` | 경고 0, 오류 0 | O |
| 프런트 문법 | `node --check dashboard/app.js` | 오류 없음 | O |
| lang JSON 유효성 | `node -e "JSON.parse(...)"` | 유효 | O |
| 자동 롤백 부재 | 코드 전체에서 `Approve`/`Reject` 호출 없이 데이터(파일)가 되돌려지는 경로를 검색 | 없음 — `CreateRollbackProposal`은 오직 **proposal**을 만들 뿐 어떤 소스 파일도 직접 쓰지 않는다. 실제 되돌리기는 사람이 승인 후 직접 수정하는 기존 `apply` 단계(사람 몫)를 그대로 거친다 | O |

## CLAUDE.md 게이트 준수 (최종 커밋 전)

`dotnet run --project server --no-build -- measure dev-pack` → `{"violationCount":0,...}`, 종료 코드 `0`. blueprint·definition·측정 코드는 수정하지 않았다. approve/reject는 이번 검증 전체에서 한 번도 호출하지 않았다.

## 결론

- 충족→위반 전환만 악화로 감지된다(기존부터 위반이던 항목의 심화는 대상 아님) — `DetectRegressions`가 직전 체크의 `Passed==true`였던 것만 필터링해 실증됨.
- 롤백 proposal은 1층 검토를 완전히 건너뛰고(실행자·검토자 호출 0회, 9ms 내 완료) 사람 결재로 직행하며, `review.routed` 이벤트에 라우팅 근거(`regression_direct_to_human`)가 기록된다.
- suspend 등록·해제가 실측대로 동작하고, 용의자는 "추정"으로만 기록되며(`suspectConfidence: temporal`) 없으면 정직하게 `null`이다.
- 자동 롤백 경로는 어디에도 없다 — 롤백은 언제나 proposal이고, 실제 파일 되돌리기는 사람이 승인한 뒤 기존 `apply` 단계에서 수행한다.
