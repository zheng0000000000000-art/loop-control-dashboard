# SimCombat 구현 투입 + 첫 실측 밸런스 사이클 실행 검증

검증일: 2026-07-09

배경: `GameSimulator.cs`의 `SimCombat` 스텁을 지시된 구현(턴제 근사 전투)으로 정확히 교체했다. 로직은 임의로 바꾸지 않았다 — 제공된 코드를 그대로 옮겼다. `providerVersion`을 `"1"→"2"`로 올렸다(전투 규칙이 측정의 정의이므로).

## A. 코드 교체

`server/GameSimulator.cs`의 `SimCombat`을 지시된 구현으로 교체. 방 하나당 적을 한 명씩 상대하고(공격력의 80~120% 변동 피해, 플레이어↔적 교대), 전멸 시 드롭률 판정으로 회복+보상 기록, 사망 시 즉시 종료. 난수는 전달받은 `Random random` 인스턴스만 사용(시드 보장 유지).

## B. 실행 검증

### B-1. simtest 재현성 — 시드 42, 500회 (첫 실측 밸런스 데이터)

```
dotnet run --project server -- simtest ruined-lab
```

| 지표 | 값 |
| --- | --- |
| `reproducible` | `true` |
| `completionRate` | **0%** |
| `roomDeathRates` (방 1~4) | `[0, 0, 0.8, 100]` (%) |
| `avgHpPerRoom` (방 1~4 진입 시 평균 HP) | `[94.906, 73.906, 23.802, 0]` |
| `rewardPerRunMean` | `8.076` |
| `rewardPerRunStdDev` | `6.4586549683351295` |

종료 코드 `0`. 같은 시드로 `RunSimulation`을 두 번 호출한 결과가 완전히 일치했다 — 재현성 게이트가 실제 전투 로직이 들어간 뒤에도 통과함을 확인했다(스텁 때는 자명하게 통과했지만, 이번엔 `Random` 소비가 많은 실제 로직에서도 결정적임을 실측으로 재확인한 것).

**읽는 법**: 500회 중 4번째 방(보스 직전)에서 100% 사망 — 아무도 마지막 방을 클리어하지 못한다. 3번째 방 진입 시 평균 HP가 이미 23.8/100까지 떨어져 있어, 4번째 방(적 hp 45·공격 10·3마리)을 버틸 체력이 남지 않는다. `completionRate = 0`은 목표 밴드 `[45, 60]`을 크게 벗어난다.

### B-2. 시드 변동 폭 — 시드 7, 500회

```
dotnet run --project server -- simtest ruined-lab 7
```

| 지표 | 시드 42 | 시드 7 |
| --- | --- | --- |
| `completionRate` | 0% | 0% |
| `roomDeathRates` | `[0, 0, 0.8, 100]` | `[0, 0, 0, 100]` |
| `avgHpPerRoom` | `[94.906, 73.906, 23.802, 0]` | `[94.984, 74.128, 24.008, 0]` |
| `rewardPerRunMean` | 8.076 | 7.96 |
| `rewardPerRunStdDev` | 6.4587 | 6.7444 |

두 시드 간 편차가 1% 미만 수준으로 작다 — 500회 표본에서 이미 수렴돼 있다. **4번째 방 100% 사망은 특정 시드의 우연이 아니라 game-data 자체의 구조적 특성**이라는 뜻이다(이 결론 자체가 첫 실측의 가치이며, 게임 데이터를 고치지 않고 있는 그대로 기록했다).

### B-3. measure 배관 완주

```
dotnet run --project server -- measure ruined-lab
```
```json
{"projectId":"ruined-lab","violationCount":1,"proposalId":"proposal-1783592715979","proposalLifecycle":"submitted","createdBy":{"provider":"ollama","model":"qwen3:8b"},"currentStage":"patchApproval","overallStatus":"warning"}
```

`measurement.json`(실측):

| metricId | 값 | 밴드 | 판정 |
| --- | --- | --- | --- |
| `completionRate` | 0 | [45, 60] | **위반** |
| `room1DeathRate` | 0 | [0, 20] | 통과 |
| `room3DeathRate` | 0.8 | [0, 35] | 통과 |
| `avgRewardPerRun` | 8.076 | [8, 20] | 통과 (밴드 하한에 거의 붙어 있음) |

`providerVersion: "2"`, `seed: 42` 기록 확인.

**위반이 1건 나와 배관이 끝까지 돌았다** — 지시대로 위반이 있는 경우다.

run-log 순서(실측):
```
measure.completed        { violationCount: 1 }
stage.warning             { stage: balanceValidation }
proposal.generated       { provider: ollama, model: qwen3:8b, durationMs: 4042, fallback: false }
proposal.created         { proposalId: proposal-1783592715979 }
review.tier1_completed   { proposalId: proposal-1783592715979, verdict: uncertain, model: qwen3:14b, durationMs: 7217, checkCount: 3, uncertainCount: 1 }
```

### B-4. 제안 내용과 검토 소견 (인용, 승인하지 않음)

`qwen3:8b`가 생성한 제안(`revisionOf`가 직전 스텁 시절의 미결 제안 `proposal-1783592066380`에 체이닝됨 — 그것도 아직 결재 대기였으므로 정상 동작):

> **제목**: "완료율 개선"
> **note**: "시뮬레이션 전체 실행을 통해 전체 과정을 완료할 수 있는 비율을 측정하여, 시스템의 완성도와 사용자 경험을 개선하는 데 기여합니다."
> `changes[0]`: `completionRate` `before: 0` → `after: [45, 60]`

`qwen3:14b`의 검토(`verdict: uncertain`):

| checkId | answer | 비고 |
| --- | --- | --- |
| `note-direction-match` | `true` | note의 방향(0→45~60 증가)이 실제 변경 방향과 일치 |
| `risk-note-present` | `false`(`uncertain: true`) | "변경 항목에 위험 또는 영향에 대한 명확한 설명이 포함되어 있지 않음" |
| `no-unrelated-change` | `true` | 무관한 변경 없음 |

`risk-note-present`가 불확실 판정을 받아 `uncertain`으로 사람 결재로 넘어갔다 — 8b의 note가 "무엇을 바꿀지"는 말했지만 "왜/무슨 위험이 있는지"는 말하지 않았다는 정당한 지적이다. **이 proposal은 승인하지 않고 결재 대기로 남겨뒀다.**

### B-5. 밸런스 미조정 확인 (지시 준수)

```
git diff --stat dashboard/data/ruined-lab/game-data.json dashboard/data/ruined-lab/blueprint.json
```
결과: 변경 없음. 이번 사이클에서 game-data·blueprint·전투 규칙(SimCombat 본체, 교체 이후) 중 어느 것도 밸런스를 좋게 만들려고 건드리지 않았다. `completionRate = 0`이라는 나쁜 첫 실측을 그대로 기록하는 것이 이 사이클의 목적이었다 — 고치는 것은 결재 이후의 몫이다.

## 불변 확인

| 항목 | 명령 | 결과 | 판정 |
| --- | --- | --- | --- |
| Core 격리 | `rg -n "GameSimulator\|SimCombat\|ruined-lab-sim" server/Engine.cs server/Storage.cs server/Guardrails.cs` | 결과 없음 | O |
| 빌드 | `dotnet build server/LocalFirstWorkflowDashboard.Server.csproj` | 경고 0, 오류 0 | O |
| providerVersion | `measurement.json`의 `providerVersion` | `"2"` | O |
| game-data·blueprint 무수정 | `git diff --stat` | 변경 없음 | O |
| 미결 proposal 미승인 | 세션 전체에서 approve/reject 미호출 | 확인 | O |

## CLAUDE.md 게이트 준수 (최종 커밋 전)

`dotnet run --project server --no-build -- measure dev-pack` → `{"violationCount":1,...}`, 종료 코드 `1`. 이 1건은 직전 "디자인 게이트" 검증에서 이미 기록한 기존 결재 대기 건(`smallTouchTargets`)이며, 이번 작업이 새로 만든 위반이 아니다. 이번 사이클에서 다룬 dev-pack 관련 변경은 `server/GameSimulator.cs`·`server/Program.cs`(simtest 시드 인자 추가)뿐이고, blueprint·definition·측정 코드는 게이트 통과 목적으로 건드리지 않았다.

## 결론

- `SimCombat`이 실제 턴제 로직으로 교체된 뒤에도 재현성 게이트가 통과한다.
- 첫 실측: 완주율 0%, 4번째 방 100% 사망 — 시드 42·7 양쪽에서 동일하게 나타나는 구조적 결과다.
- measure 배관이 위반 발생 시나리오로 끝까지 완주했다: 실측→blueprint 대조→8b 제안(위험 설명 부족)→14b 검토(uncertain)→사람 결재 대기.
- 밸런스는 조정하지 않았다 — 결재함에 두 건(`dev-pack` 터치 타겟, `ruined-lab` 완료율 개선)이 사람을 기다리고 있다.
