# 게임 팩 측정 공급자 — 전투 시뮬레이터 뼈대 실행 검증

검증일: 2026-07-09

배경: ruined-lab(게임 밸런싱 쇼케이스)의 측정을 mock에서 실제 파이프라인으로 연결했다. **분업 원칙 준수**: 이 작업에서 에이전트는 계약(시그니처)·배관(provider 연결)·검증 하네스(`simtest`)만 구현했다. `SimCombat()`(방 하나의 실제 턴제 전투 규칙)은 **의도적으로 미구현 스텁**으로 남겼다 — 항상 `{Survived: true, RemainingHp: player.Hp, RewardGained: 0}`을 반환한다. 실제 전투 로직은 사람이 채운다.

## A. 계약 배관

- `server/GameSimulator.cs` 신규 — 도메인 모듈(코어 3파일 무접촉). `RunSimulation(gameData, seed, runs)` → `SimResult(CompletionRate, RoomDeathRates[], AvgHpPerRoom[], RewardPerRunMean, RewardPerRunStdDev)`.
- `dashboard/data/ruined-lab/game-data.json` 신규 — 방 4개 세로 슬라이스. **기존 mock proposal(`patch-proposal.json`)의 path와 값을 그대로 맞췄다**: `rooms[2].rewards.commonDropRate = 0.28`, `rooms[3].hazards.damageMultiplier = 1.0`, `bosses[0].healthMultiplier.lowUpgradePath = 1.35` — 옛 mock의 `before` 값과 정확히 일치한다.
- `workflow-definition.json`에 `measurementProvider: { id: "ruined-lab-sim", seed: 42, dryRunSamples: 500 }` 추가.
- `blueprint.json`을 mock 지표(`completionRate`, `earlyVariance`)에서 실제 지표로 교체: `completionRate band[45,60] high`(유지), `room1DeathRate band[0,20]`, `room3DeathRate band[0,35]`, `avgRewardPerRun band[8,20]`.
- `Program.cs`의 `RunMeasureCore`가 `providerId`로 `DevPackMeasures`/`GameSimulator`를 분기하도록 배관 연결. `ResolveMeasurementStages`의 게이트 조건 선택을 `FirstOrDefault`→`LastOrDefault`로 보정했다 — ruined-lab의 `patchApproval` 게이트가 `schemaValidation`·`balanceValidation` 두 조건을 갖고 있어, 첫 번째(형식 검사)가 아니라 마지막(내용 검사)을 "괴리 판정" 대응 스테이지로 잡아야 정확했다. dev-pack은 게이트 조건이 1개뿐이라 이 변경으로 동작이 바뀌지 않는다(실측으로 재확인, 아래 참조).

## B. 시뮬레이션 본체 — 미착수 (지시대로)

`SimCombat(player, room, random)`은 스텁이다. 이 절은 사람이 직접 채울 자리이며, 이번 작업에서는 건드리지 않았다.

## C. 검증 하네스

### C-1. simtest 재현성 게이트

```
dotnet run --project server -- simtest ruined-lab
```
```json
{"projectId":"ruined-lab","seed":42,"runs":500,"reproducible":true,"completionRate":100,"roomDeathRates":[0,0,0,0],"avgHpPerRoom":[100,100,100,100],"rewardPerRunMean":0,"rewardPerRunStdDev":0}
```
종료 코드 `0`. 같은 시드(42)로 `RunSimulation`을 두 번 호출해 전체 필드가 완전히 일치하는지 비교했다 — 스텁 상태에서 이미 통과하며, 이는 `Random(seed)`를 시뮬레이션마다 새로 생성하는 설계 덕분에 사람이 나중에 `SimCombat`에 실제 로직(그 `random` 인스턴스만 사용하는 한)을 채워도 이 게이트가 회귀 검증 역할을 계속한다.

스텁 결과 해석: 완주율 100%, 방별 사망률 0%, 평균 HP 그대로, 보상 0 — "아직 아무 일도 일어나지 않는다"는 스텁의 정직한 신호다.

### C-2. 스텁 상태에서 measure 배관 전체 완주

```
dotnet run --project server -- measure ruined-lab
```
```json
{"projectId":"ruined-lab","violationCount":2,"proposalId":"proposal-1783592066380","proposalLifecycle":"submitted","createdBy":{"provider":"ollama","model":"qwen3:8b"},"currentStage":"patchApproval","overallStatus":"warning"}
```

run-log 이벤트 순서(실측):
```
measure.completed        { providerId: ruined-lab-sim, violationCount: 2 }
stage.warning             { stage: balanceValidation, reasonCode: checklist.blueprint_deviation }
proposal.generated       { provider: ollama, model: qwen3:8b, durationMs: 4646, fallback: false }
proposal.created         { proposalId: proposal-1783592066380 }
review.tier1_completed   { verdict: uncertain, model: qwen3:14b, durationMs: 9803, checkCount: 6 }
```

`stage.warning`이 정확히 `balanceValidation`(내용 검사)에 붙었다 — `schemaValidation`(형식 검사)이 아니다. `ResolveMeasurementStages`의 `LastOrDefault` 보정이 의도대로 동작함을 실측으로 확인했다. 단계 상태:

```json
{ "schemaValidation": "passed", "balanceValidation": "warning", "patchApproval": "pending_review", "unityExport": "blocked" }
```

`measurement.json`에 `"seed": 42`가 기록됐다(재현성 계약). 위반 2건은 `completionRate = 100`(목표 45~60 밖)과 `avgRewardPerRun = 0`(목표 8~20 밖) — 둘 다 스텁의 "고정값"이 band 밖이라 자연스럽게 발생했다(지시대로 예상된 결과).

`qwen3:8b`가 생성한 제안은 `revisionOf: "patch-2026-07-08-01"`로 기존 데모용 mock proposal에 정확히 체이닝됐다 — 별도 처리 없이 "직전 submitted 제안" 로직이 옛 mock도 올바르게 인식했다.

이 proposal은 검증 세션 내내 **승인·거절하지 않고 결재 대기 상태로 남겨뒀다**(사람 몫). dev-pack의 터치 타겟 제안과 함께 대기 중이다.

### dev-pack 회귀 확인

`ResolveMeasurementStages` 변경이 dev-pack에 영향 없는지 별도로 확인했다: dev-pack의 게이트 조건은 1개뿐이라 `LastOrDefault`가 `FirstOrDefault`와 같은 결과를 낸다. `dotnet run --project server -- measure dev-pack` 실행 결과 `deviationCheck`/`changeReview` 매핑이 이전과 동일하게 동작했다(violationCount는 직전 작업에서 남겨둔 `smallTouchTargets` 결재 대기 건 그대로 1).

## 불변 확인

| 항목 | 명령 | 결과 | 판정 |
| --- | --- | --- | --- |
| Core 격리 | `rg -n "GameSimulator\|SimCombat\|ruined-lab-sim\|room1DeathRate\|room3DeathRate\|avgRewardPerRun" server/Engine.cs server/Storage.cs server/Guardrails.cs` | 결과 없음 | O |
| 빌드 | `dotnet build server/LocalFirstWorkflowDashboard.Server.csproj` | 경고 0, 오류 0 | O |
| 프런트 문법 | `node --check dashboard/app.js` | 오류 없음(이번 작업은 프런트를 건드리지 않았다) | O |
| JSON 유효성 | game-data·definition·blueprint | 모두 유효 | O |
| SimCombat 미구현 | `grep -n "TODO(사람 작성" server/GameSimulator.cs` | 스텁 표시 존재, 항상 고정값 반환 확인 | O |

## CLAUDE.md 게이트 준수 (최종 커밋 전)

`dotnet run --project server --no-build -- measure dev-pack` → `{"violationCount":1,...}`, 종료 코드 `1`. 이 1건은 이번 작업이 만든 것이 아니라 직전 "디자인 게이트" 검증에서 이미 기록한, 사람 결재 대기 중인 기존 건(`smallTouchTargets`)이다 — 새 위반이 없음을 확인했다. blueprint·definition·측정 코드는 (ruined-lab 쪽 정당한 변경 외에는) 게이트를 통과시키려는 목적으로 건드리지 않았고, approve/reject는 호출하지 않았다.

## 결론

- 스텁 상태에서도 measure 배관이 dev-pack과 동일한 흐름(측정→blueprint 대조→8b 제안→14b 검토)을 완주한다 — 실측 확인.
- `simtest`의 시드 재현성 게이트가 통과하며, 사람이 나중에 `SimCombat`을 채워도 이 게이트가 회귀 검증으로 계속 쓰인다.
- `game-data.json` 구조가 기존 mock proposal의 path·값과 완전히 정합한다.
- 코어 3파일은 청결하고, `SimCombat` 본체는 스텁 상태로 인계됐다 — 다음은 사람 차례다.
