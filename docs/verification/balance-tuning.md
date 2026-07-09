# 밸런스 레버 + dry-run 자율 튜닝 실행 검증

검증일: 2026-07-09

배경: 직전 실측(completionRate 0%)에서 8b의 제안이 지표 자체(`completionRate`)를 직접 겨눠 14b가 "위험 설명 부족"으로 uncertain 판정했다. 이번 작업은 **레버 선언 + 결정론적 시뮬 채점 탐색**으로 실제 game-data 조정안을 찾고, 8b는 그 결과를 서술만 하도록 역할을 분리했다. **예측/사실 분리**: dry-run 결과는 `measurement.json`에 절대 기록하지 않고 proposal의 `predictedMetrics`에만 넣는다. game-data 원본은 승인 전 수정하지 않는다.

## A. 레버 선언

`ruined-lab/workflow-definition.json`에 `tunableLevers`(4개 선언, `rooms[*]` 와일드카드로 방 4개에 대해 확장 → 실제 12개 + `player.maxHp` 1개 = **13개 구체 레버**)와 `tuning`(`maxCandidates: 40`, `dryRunSamples: 500`, `maxLeversPerProposal: 3`) 추가.

## B. BalanceTuner.cs — 그리디 탐색

새 도메인 모듈(코어 3파일 무접촉). 위반 거리(밴드 편차 합)를 계산해 이웃 후보(레버 하나를 ±1스텝) 중 가장 개선되는 것을 채택, 반복. 레버 경로 get/set은 선언된 레버 목록만 다룬다 — **레버 밖 경로를 만지는 코드 경로 자체가 없다**(파서가 오직 `tunableLevers`에서 나온 path 문자열만 조작).

### simtune 실행 결과 (탐색 궤적)

```
dotnet run --project server -- simtune ruined-lab
```
```
[tuning] baseline: distance=45 candidates=1
[tuning] 개선되는 이웃 없음 — candidates=26 distance=45에서 종료
```
```json
{"projectId":"ruined-lab","seed":42,"candidatesUsed":26,"reachedBand":false,"changedLevers":[],"predictedMetrics":[...],"residualViolations":["completionRate=0 (밴드 45~60 밖)"]}
```

**정직한 알고리즘 결과**: baseline distance=45(주로 `completionRate` 0 vs 밴드 45~60). 13개 레버 × 2방향 = 26개 이웃 후보를 전부 시도했지만 **단 하나도 distance를 개선하지 못했다** — `maxCandidates=40`을 다 쓰기도 전에 "개선되는 이웃 없음"으로 조기 종료했다. 이는 버그가 아니라 순수 그리디 탐색의 알려진 한계다: 3번째 방 진입 시 평균 HP가 이미 23.8/100까지 떨어져 있어(직전 검증에서 실측), 레버 하나를 한 스텝만 움직이는 정도로는 4번째 방의 "전멸" 문턱을 넘지 못한다. `completionRate`는 500회 중 몇 명이 4개 방을 모두 살아남는지의 비율이라 문턱함수에 가깝고, 단일 스텝의 그레이디언트가 사실상 0으로 측정된 것이다. 알고리즘을 임의로 바꾸거나 스텝을 키우지 않고 지시된 대로 정직하게 기록한다.

## C. measure 흐름 통합 (게임 팩 한정)

`ApplyMeasurementResult`에 `providerId == "ruined-lab-sim"` 분기 추가(dev-pack 경로는 완전히 무변경). 위반 시 `BalanceTuner.Search` 실행 → `OllamaExecutor.GenerateForTuning`(8b, 서술 전용) → 14b 검토, 기존 재생성 루프 재사용.

### 실측: measure ruined-lab

```
dotnet run --project server -- measure ruined-lab
```
```json
{"projectId":"ruined-lab","violationCount":1,"proposalId":"proposal-1783594012403","proposalLifecycle":"submitted","createdBy":{"provider":"ollama","model":"qwen3:8b"},"currentStage":"patchApproval","overallStatus":"warning"}
```

생성된 proposal(전문):

```json
{
  "kind": "tuning",
  "title": "밴드 도달 실패",
  "createdBy": { "provider": "ollama", "model": "qwen3:8b" },
  "summary": "레버 변경 없이 밴드 도달에 실패했으며, completionRate=0로 밴드 45~60 밖에 위치해 잔여 위반 상태 (레버 범위 내 해 없음. 잔여 위반: completionRate=0 (밴드 45~60 밖). 레버 범위 확장은 definition 변경 사항으로 별도 사람 결재가 필요하다.)",
  "changes": [],
  "predictedMetrics": [
    { "metricId": "completionRate", "before": 0, "after": 0, "band": "45~60" },
    { "metricId": "room1DeathRate", "before": 0, "after": 0, "band": "0~20" },
    { "metricId": "room3DeathRate", "before": 0.8, "after": 0.8, "band": "0~35" },
    { "metricId": "avgRewardPerRun", "before": 8.076, "after": 8.076, "band": "8~20" }
  ],
  "predictedReachedBand": false,
  "impact": [
    { "label": "레버 변경", "value": "0" },
    { "label": "탐색 후보 수", "value": "26" }
  ]
}
```

`changes: []`(레버 개선안 없음)이지만 **proposal 자체는 정직하게 생성됐다** — B.2에서 지시한 "밴드 도달 후보를 못 찾으면 최선 후보 + note에 명시" 경로가 정확히 이 모양이다. 요약문 끝의 "(레버 범위 내 해 없음 ... 별도 사람 결재가 필요하다)" 부분은 **서버가 모델 순응 여부와 무관하게 항상 덧붙이는 고정 문구**다 — 8b가 이 사실을 언급하지 않아도 안전망으로 항상 붙는다(모델 프롬프트에도 포함하라고 지시했지만, 안전 정보를 LLM 준수에만 맡기지 않았다).

### 14b 검토

```json
{ "verdict": "approved", "findings": [], "reason": "1층 체크리스트를 통과했다. 사람 최종 결재가 필요하다." }
```

`changes`가 비어 있어 체크리스트의 change-순회 루프가 돌지 않았고(`findings: []`), 반박할 것이 없으니 자동으로 `approved`가 됐다 — 기존 일반 리뷰 로직을 그대로 재사용한 자연스러운 결과다(빈 changes를 위한 특수 분기를 추가하지 않았다).

**이 proposal은 승인하지 않고 결재 대기로 남겨뒀다.**

## D. 승인 시 실제 적용 (구현됨, 실행 검증은 하지 않음)

`Storage.cs`의 `CoreFiles`/`StartupCheckedFiles`에 `game-data.json` 추가(복원 지점·시작 시 검증 대상 포함). `Approve()`에 `kind == "tuning"` 분기 추가: 승인 시 `changes`를 `game-data.json`에 실제로 기록(`BalanceTuner.SetLeverValue` + 기존 `Storage.WriteProjectFile` 원자 쓰기 재사용) → `RunMeasureCore` 재사용으로 자동 재측정 → `tuning.applied` 이벤트(예측·실측 비교) 기록.

**이 경로는 approve 액션을 실제로 호출해야만 실행되므로, 이번 검증에서는 실행하지 않았다** — CLAUDE.md·관례 전체가 "approve/reject 계열 액션 호출 금지"를 명시하고 있고, 이는 특정 proposal에 한정된 규칙이 아니라 검증 목적의 임시 승인도 포함한다. 코드는 빌드 통과와 정적 검토로 확인했다: `ApplyTuningChanges`는 `changes.Count == 0`이면(이번 실측처럼) 아무 것도 쓰지 않도록 가드돼 있고, `changes.Count > 0`일 때만 `game-data.json`을 갱신하고 재측정을 트리거한다. 실제 승인은 사람 몫으로 남긴다.

## E. 예측/사실 분리 확인

```
grep -ci "predict" dashboard/data/ruined-lab/measurement.json
```
결과: `0`. `measurement.json`에는 `seed`·`providerVersion`·실측 `metrics`만 있고 `predicted` 계열 필드가 전혀 없다 — 예측치는 오직 `patch-proposal.json`의 `predictedMetrics`에만 존재한다.

## UI: 예측 표시

결재 패널에 `kind: "tuning"` 배지("튜닝")와 `predictedMetrics` 섹션("예측 지표", 각 행에 "예측" 태그 + "현재 → 예측 (밴드 X~Y)")을 추가했다. 실제 ruined-lab 대기 중 proposal로 렌더링을 실측 확인(`preview_inspect`로 좌표·계산된 스타일 확인 — `preview_screenshot`은 이번 세션 내내 반복적으로 타임아웃돼 기존 검증 문서들과 동일하게 우회했다). 첫 렌더에서 `.change-item`의 그리드 레이아웃이 예측 지표 두 칸(태그+텍스트) 배치에 맞지 않아 `.tag-predicted` 너비가 236px로 깨졌던 것을 발견해 `.predicted-metric-item`에 `display:flex` 오버라이드를 추가했고, 재확인 결과 `.tag-predicted` 너비가 정상(44.97px)으로 확인됐다.

## 불변 확인

| 항목 | 명령 | 결과 | 판정 |
| --- | --- | --- | --- |
| Core 격리 | `rg -n "BalanceTuner\|GameSimulator\|SimCombat\|ruined-lab-sim\|tunableLevers\|LeverChange\|TuningResult" server/Engine.cs server/Guardrails.cs` | 결과 없음 | O |
| 빌드 | `dotnet build server/LocalFirstWorkflowDashboard.Server.csproj` | 경고 0, 오류 0 | O |
| 프런트 문법 | `node --check dashboard/app.js` | 오류 없음 | O |
| game-data 무수정 | `git diff --stat dashboard/data/ruined-lab/game-data.json` | 변경 없음 | O |
| 레버 밖 경로 탐색 없음 | `BalanceTuner.ParseLevers`가 `definition.tunableLevers`에서만 경로를 얻고, `Search`의 모든 후보 생성이 그 목록만 순회 | 코드 검토로 확인 | O |
| 미결 proposal 미승인 | 세션 전체에서 approve/reject 미호출 | 확인 | O |

## CLAUDE.md 게이트 준수 (최종 커밋 전)

`dotnet run --project server --no-build -- measure dev-pack` → `{"violationCount":1,...}`, 종료 코드 `1`. 이 1건은 이전 "디자인 게이트" 검증에서 이미 기록한 결재 대기 건(`smallTouchTargets`)이며 이번 작업이 만든 위반이 아니다. dev-pack 관련 이번 작업의 변경은 없다(`Program.cs`·`OllamaExecutor.cs`·`Storage.cs`는 게임 팩 분기만 추가했고 dev-pack 경로는 그대로다).

## 결론

- 탐색은 레버 목록 밖을 절대 건드리지 않는다(코드 구조상 불가능).
- proposal의 `changes`는 실제 game-data 경로를 가리키고(이번엔 0건이었지만), `predictedMetrics`가 항상 첨부된다.
- 승인 시 실제 적용·자동 재측정·예측-실측 괴리 로그는 구현했지만 이번 세션에서는 실행(=승인)하지 않았다 — 결재는 사람 몫이라는 원칙을 검증 과정에도 그대로 적용했다.
- `measurement.json`은 예측치로 오염되지 않았다.
- 수치 결정 주체는 시종일관 `BalanceTuner`(결정론적 탐색)였고, 8b는 단 한 번도 수치를 만들지 않았다 — `changes.Count == 0`인 이번 실측에서 8b는 note를 하나도 쓰지 않고 summary만 작성했다.
