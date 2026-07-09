# 레버 확장 후속 실행 검증

검증일: 2026-07-10

배경: 사람이 `ruined-lab/workflow-definition.json`의 `tunableLevers`에 `rooms[*].enemies.hp`(10~30, step 5)와 `rooms[*].enemies.count`(1~4, step 1)를 직접 추가했다 — 직전 사이클에서 시스템이 "레버 범위 내 해 없음"을 정직하게 보고한 뒤, 사람이 결재로 울타리를 넓힌 첫 사례다. 본 작업은 그 확장을 전제로 재탐색·재측정만 수행하고, 탐색 알고리즘·기준(blueprint)은 건드리지 않는다.

## ① JSON 유효성 확인

```
node -e "JSON.parse(require('fs').readFileSync('dashboard/data/ruined-lab/workflow-definition.json','utf8'))"
node -e "JSON.parse(require('fs').readFileSync('dashboard/data/ruined-lab/game-data.json','utf8'))"
```

결과: 둘 다 파싱 성공(`valid` 출력). `tunableLevers`는 이제 6개 선언(`rooms[*]` 와일드카드 4개 + `player.maxHp` + 신규 `enemies.hp`/`enemies.count`)이며 방 4개 확장 시 실제 21개 구체 레버가 된다.

## ② DECISIONS.md

한 줄 추가: "레버 확장 첫 사례 — 시스템이 '범위 내 해 없음' 보고 → 사람이 울타리 확장 승인." (`docs/DECISIONS.md`)

## ③ simtune ruined-lab — 레버 개방 후 첫 탐색

```
dotnet run --project server --no-build -- simtune ruined-lab
```

궤적(요약):

```
[tuning] baseline: distance=45 progress=3 score=45.000001 candidates=1
[tuning] step=±1 후보 9개 시도 → 개선 없음
[tuning] step=±2 후보 10개 시도 → 개선 없음
[tuning] step=±3 후보 10개 시도 → 개선 없음
[tuning] two-lever 후보 5개 시도 → 개선 없음
[tuning] random-restart 후보 3개 시도, distance 45→40, progress 3→4
[tuning] 채택: candidates=38 distance=40 levers=rooms[2].enemies.count, rooms[1].enemies.count, rooms[3].enemies.hp
[tuning] step=±1 채택: candidates=39 distance=39.6 levers=rooms[3].enemies.hp
[tuning] 후보 상한 도달 — candidates=40 distance=39.6에서 종료
```

최종 JSON:

```json
{
  "candidatesUsed": 40, "reachedBand": false,
  "baselineDistance": 45, "finalDistance": 39.6,
  "baselineProgressedRooms": 3, "finalProgressedRooms": 3.996,
  "changedLevers": [
    {"path":"rooms[3].enemies.hp","before":45,"after":15},
    {"path":"rooms[1].enemies.count","before":2,"after":1},
    {"path":"rooms[2].enemies.count","before":3,"after":2}
  ],
  "predictedMetrics": [
    {"metricId":"completionRate","before":0,"after":99.6,"band":"45~60"},
    {"metricId":"avgRewardPerRun","before":8.82,"after":9.472,"band":"8~20"}
  ],
  "residualViolations": ["completionRate=99.6 (밴드 45~60 밖)"]
}
```

**정직한 결과**: 레버 개방 전에는 `rooms[*].enemies.hp/count`가 탐색 대상이 아니어서 distance가 45에서 전혀 움직이지 않았다(이전 검증 기록). 개방 후에는 단일-스텝 이웃(±1~±3, 6개 레버 조합)까지는 여전히 개선을 못 찾았고, `random-restart`(무작위 재시작 3회)가 처음으로 개선을 찾아 distance 45→40으로 이동했다 — 즉 이번 개선은 그리디 인접 탐색이 아니라 재시작 휴리스틱이 만든 결과다. 이후 로컬 개선(±1) 한 번이 추가로 39.6까지 이동했지만 `maxCandidates=40`을 소진해 조기 종료됐다.

**완주율(예측)은 0 → 99.6으로 변했다** — 요청받은 확인 항목이 실측으로 나왔다. 다만 밴드(45~60)를 반대쪽으로 넘어섰다: 방3 적 hp를 45→15로 크게 낮추고 방1·2 적 수를 줄인 결과, 몬스터가 거의 위협이 안 되는 쪽으로 과이동했다. "밴드 안"이 아니라 "밴드를 관통해 반대편으로 넘어간" 것이므로 `reachedBand: false`, 잔여 위반은 여전히 1건(`completionRate=99.6`, 상한 초과)이다. 탐색기·스텝 크기를 조정해 "딱 맞게" 만들지 않고 있는 그대로 기록한다 — 그 튜닝은 사람 결재 대상이다.

## ④ measure ruined-lab → proposal 결재 대기

```
dotnet run --project server --no-build -- measure ruined-lab
```

```json
{"projectId":"ruined-lab","violationCount":1,"proposalId":"proposal-1783609070062","proposalLifecycle":"submitted","createdBy":{"provider":"rule-engine","model":null},"currentStage":"patchApproval","overallStatus":"warning"}
```

`changes`가 3건(위 `changedLevers`와 동일)이라 "변경 0건은 제안하지 않는다"(DECISIONS.md 기존 결정) 분기를 타지 않고 실제 proposal이 생성됐다 — 레버 확장 전이었다면 이 proposal 자체가 없었을 것이다.

**8b 실행자가 자가 비평에 걸려 rule-engine으로 강등됐다** (`createdBy.provider: "rule-engine"`). run-log의 `proposal.generated` 이벤트:

```json
{"reasonCode":"system.executor_degraded","failReason":"자가 비평 실패: 예측을 측정 결과라고 부르는 표현이 있으면 passed=false"}
```

Ollama 서비스 자체는 정상(`curl 127.0.0.1:11434/api/tags` 응답 확인, `qwen3:8b` 로드됨) — 8b가 note 초안에서 예측치를 측정 결과처럼 서술해 자가 비평(selfReview)이 그 초안을 반려한 것이다. 세이프가드가 설계대로 작동해 잘못된 표현이 proposal에 실리지 않고 rule-engine 정형 문구로 대체됐다는 뜻이며, 이번 세션에서만이 아니라 직전 두 차례 측정(23:45, 23:47)에서도 같은 사유로 반복 강등됐다 — 8b가 "튜닝 서술" 과제에서 이 문구를 일관되게 틀리는 패턴으로 보이나, 프롬프트나 자가 비평 기준을 이번 작업 범위에서 바꾸지 않았다(범위 밖).

**14b 검토는 uncertain**: rule-engine이 채운 note("자동 서술 실패로 rule-engine이 대신 기록...")에 위험/목적 설명이 부실하다는 이유로 `risk-note-present`, `note-purpose-effect` 체크가 반려됐고, `no-unrelated-change`도 방별 변경과 측정 요약이 겹친다는 이유로 걸렸다 — 세 항목 모두 `severity: concern` 또는 `blocker`. `verdict: "uncertain"`이라 자동 승인되지 않고 사람 결재로 남는다.

**이 proposal은 승인하지 않고 결재 대기로 남겨뒀다** (`lifecycle: "submitted"`, `currentStage: "patchApproval"`).

## 불변 확인

| 항목 | 명령/방법 | 결과 | 판정 |
| --- | --- | --- | --- |
| game-data.json 무수정 | `git diff --stat dashboard/data/ruined-lab/game-data.json` | 변경 없음 | O |
| 예측/사실 분리 | `grep -ci "predict" dashboard/data/ruined-lab/measurement.json` | 0 | O |
| 레버 밖 경로 탐색 없음 | `changedLevers`가 모두 `tunableLevers`에서 파생된 경로 | 확인 | O |
| 미결 proposal 미승인 | `lifecycle: "submitted"` 유지, approve 미호출 | 확인 | O |

## CLAUDE.md 게이트 (커밋 전)

```
dotnet run --project server --no-build -- measure dev-pack
{"projectId":"dev-pack","violationCount":2,"proposalId":"proposal-1783609223596",...}
```
종료 코드 `1`. 위반 2건은 이번 작업이 만든 게 아니다:
- `skillDomainViolations=2` — 회귀 감지가 자동으로 롤백 proposal(`proposal-1783609223596`, kind: rollback)을 만들어 사람 결재로 직행시켰다. 이미 `docs/incidents/2026-07-09-behavior-snapshot-structural-metric.md`·`docs/verification/refactor-diagnostics-snapshot.md`에 추적 중인 자기 리팩터링 사다리 작업의 산물이다.
- `maxFunctionLength=246` — 같은 계열의 기존 진단 게이트 지표, 동일 문서에 이미 기록됨.

이번 작업(ruined-lab 레버 확장 후속)은 dev-pack 코드·blueprint를 전혀 건드리지 않았으므로 두 위반 모두 사전 존재하는 별도 트랙이다.

## 결론

- 레버 확장은 사람 결재로만 이뤄졌고(에이전트는 definition을 건드리지 않음), 그 확장 전제 위에서 탐색·측정만 재실행했다.
- 완주율 예측이 0 → 99.6으로 실제로 움직였다 — 다만 밴드를 반대쪽으로 넘어선 과이동이며, "딱 맞춘 해"가 아니라는 사실을 그대로 보고한다.
- 새 proposal은 결재 대기 상태로 남았고, 8b 강등·14b uncertain의 구체적 사유를 정직하게 기록했다.
- 예측/사실 분리, 레버 목록 밖 미접근, game-data 원본 무수정 모두 재확인됐다.
