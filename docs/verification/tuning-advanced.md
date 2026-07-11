# 탐색 고도화 검증

일시: 2026-07-09

## 참조한 스킬

- `skills/common/directive-writing.md`
- `skills/common/verification.md`

## 변경 요약

- `GameSimulator`가 방별 도달률 `roomNReachRate`를 측정 사실로 기록한다.
- `BalanceTuner`가 이웃 탐색에서 개선을 찾지 못하면 deterministic random restart 후보를 같은 `maxCandidates` 예산 안에서 평가한다.
- `ruined-lab` definition에 `randomRestarts: 3`, `selfReview: true`, `suggestedBlueprintMetrics`를 추가했다.
- `OllamaExecutor`가 제출 전 자가 비평 1패스를 수행하고, 실패 시 기존 rule-engine 폴백 경로로 강등한다.
- `skills/domains/game/balance-tuning.md`에 문턱형 지표에는 연속형 동반 지표를 붙인다는 예방 규칙을 추가했다.

## 실행 검증

### 빌드

명령:

```powershell
dotnet build server
```

결과: O. 경고 0개, 오류 0개.

### 시뮬레이터 재현성

명령:

```powershell
dotnet run --project server -- simtest ruined-lab
```

결과: O.

요약:

```json
{"reproducible":true,"completionRate":0,"roomReachRates":[100,100,100,100],"roomDeathRates":[0,0,0,100],"rewardPerRunMean":8.82}
```

### 재시작 탐색

명령:

```powershell
dotnet run --project server -- simtune ruined-lab
```

결과: O. `random-restart`가 실제로 3개 후보를 평가했다.

요약:

```json
{"candidatesUsed":40,"reachedBand":false,"baselineDistance":45,"finalDistance":39.6,"baselineProgressedRooms":3,"finalProgressedRooms":3.996,"restartAttempts":3,"changedLevers":["rooms[3].enemies.hp","rooms[1].enemies.count","rooms[2].enemies.count"],"completionRatePrediction":"0 -> 99.6","residualViolations":["completionRate=99.6 (밴드 45~60 밖)"]}
```

판정: O. 재시작 후보가 고원을 벗어나 completionRate 예측을 0에서 99.6으로 올렸지만, 목표 밴드 45~60을 초과해 최선 후보·밴드 미달 proposal로 남았다.

### 측정 루프

명령:

```powershell
dotnet run --project server -- measure ruined-lab
```

결과: O/X 혼합. 명령은 정상 실행됐고 위반 1건을 반환해 종료 코드 1로 끝났다. 이는 현재 completionRate가 밴드 밖이므로 정상이다.

요약:

```json
{"projectId":"ruined-lab","violationCount":1,"proposalLifecycle":"submitted","createdBy":{"provider":"rule-engine","model":null},"currentStage":"patchApproval","overallStatus":"warning"}
```

확인:

- `measurement.json`에 `room1ReachRate`, `room2ReachRate`, `room3ReachRate`, `room4ReachRate`가 기록됐다.
- `blueprint.json`은 변경하지 않았다.
- 방별 도달률 기준 후보는 `definition.suggestedBlueprintMetrics`에만 있으며, blueprint 반영은 사람 결재가 필요한 proposal 경로로만 가능하다.
- 자가 비평이 실행됐고, 생성 문안이 예측/측정 언어를 혼동한 것으로 판정되어 rule-engine 폴백 proposal이 생성됐다.
- 사람 결재는 수행하지 않았다.

### 관례 게이트

명령:

```powershell
dotnet run --project server -- measure dev-pack
```

결과: X. 기존 구조 진단 위반 1건이 남아 있다.

```json
{"gate":"dev-pack","violations":1,"attempt":1}
```

남은 위반:

```json
{"metricId":"maxFunctionLength","value":246,"evidence":["server/Program.cs:740-985"]}
```

판정: 이번 작업 범위는 탐색 고도화이며 Program.cs 분리 리팩터링은 하지 않았다. 위반 proposal `proposal-1783608549264`는 사람 결재 대기 상태로 남겼다.

## 완료 기준 판정

- 보조 지표 측정: O. 방별 도달률이 measurement에 기록된다.
- blueprint 직접 변경 금지: O. `blueprint.json`은 수정하지 않았다.
- 재시작 모드: O. `random-restart 후보 3개`가 실측 로그에 남았다.
- selfReview 1패스: O. `proposal.generated` 로그에 `selfReviewed: true`, `selfReviewPassed: false`가 기록됐다.
- 스킬 승급: O. `balance-tuning.md`에 연속형 동반 지표 예방 규칙을 추가했다.
- 코어 청결: O. `Engine.cs`, `Guardrails.cs`는 미수정이다. `Storage.cs`의 기존 `GameDataFile` 상수는 이번 변경이 아니다.
- 사람 결재 대행 금지: O. approve/reject를 호출하지 않았다.
