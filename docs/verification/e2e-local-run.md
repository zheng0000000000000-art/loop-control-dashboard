# E2E 순수 로컬 루프 완주 테스트

검증일: 2026-07-09

## 원칙

- 무료 로컬 실행자만 사용한다: 탐색기, qwen3:8b, qwen3:14b, 시뮬레이터.
- 코드와 blueprint는 테스트 중 수정하지 않는다.
- 결재, 기준 변경, 체크포인트·가드레일 ack는 사람이 한다.
- 에이전트는 approval/reject/acknowledge 액션을 대행하지 않는다.

## A. 사전 상태

현재 ruined-lab은 사람 결재 대기 상태에서 출발한다.

| 항목 | 값 |
| --- | --- |
| currentStage | `patchApproval` |
| loopState | `running` |
| overallStatus | `warning` |
| loopIteration | 3 |
| proposalId | `proposal-1783595466291` |
| proposal lifecycle | `submitted` |
| proposal kind | `tuning` |
| 생성자 | `ollama / qwen3:8b` |
| 14b verdict | `needs_changes` |

현재 실측 지표:

| metric | actual | target |
| --- | ---: | --- |
| completionRate | 0 | 45~60 |
| room1DeathRate | 0 | 0~20 |
| room3DeathRate | 0.8 | 0~35 |
| avgRewardPerRun | 8.076 | 8~20 |

현재 결재 대기 proposal:

| path | before | after | 예측 |
| --- | ---: | ---: | --- |
| `rooms[1].rewards.healAmount` | 8 | 10 | completionRate 0→0, room3DeathRate 0.8→0, avgRewardPerRun 8.076→8.82 |

판단 보조 정보:

- 승인 시 예측 완주율 개선폭: 0%p (`completionRate` 0 → 0)
- 예측상 개선되는 지표: `room3DeathRate` 0.8 → 0, `avgRewardPerRun` 8.076 → 8.82
- 레버 범위 내 잔여 여지: `rooms[1].rewards.healAmount`는 승인 후 10이 되며, max 20까지 10포인트(2 단위 5스텝)가 남는다.
- 14b findings: `note-direction-match` 실패. note의 표현이 "수정이 필요합니다"를 포함해 변경 방향 설명과 충돌한다고 판정했다.

## B. 기동 확인

| 항목 | 명령 | 결과 | 판정 |
| --- | --- | --- | --- |
| Ollama API | `GET http://127.0.0.1:11434/api/tags` | `qwen3:8b`, `llama3.1:8b`, `qwen3:14b` 확인 | O |
| Ollama 목록 | `ollama list` | 위 3개 모델 확인 | O |
| 기본 브라우저 포트 | `GET http://127.0.0.1:5173/api/projects/ruined-lab/state` | 404. 현재 5173에는 API 서버가 아닌 다른 정적 응답이 떠 있다. | X |
| 별도 포트 서버 | `ASPNETCORE_URLS=http://127.0.0.1:5186 dotnet run --project server --no-build` 후 `GET /api/projects/ruined-lab/state` | `currentStage=patchApproval`, `loopState=running`, `overallStatus=warning` | O |

마찰: in-app browser의 5173은 실제 Minimal API 서버가 아니었다. 이번 검증에서는 API 기동 확인만 별도 포트 5186에서 수행하고 즉시 종료했다.

## C. 회차 기록

| iteration | 상태 | 레버 변경 | 예측 | 실측 | AI 소요 | 비용 | 사람 개입 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 3 | 사람 결재 대기 | `rooms[1].rewards.healAmount` 8→10 | completionRate 0→0, room3DeathRate 0.8→0, avgRewardPerRun 8.076→8.82 | 아직 적용 전 | qwen3:8b 생성 4,854ms, qwen3:14b 검토 7,757ms | estimatedUSD 0, subscriptionCalls 0 | 필요: 승인/거절 또는 수정 지시 |

정지 지점:

- 14b verdict가 `needs_changes`이므로 시스템은 사람 검토 대기 상태다.
- 결재는 사람 몫이므로 approve/reject를 호출하지 않았다.
- 다음 진행은 사람이 이 proposal을 승인하거나 거절하거나, note 수정/재생성을 지시해야 한다.

## D. 최종 보고 초안

현재는 완주 전 정지 상태다. 따라서 아래 명제 판정은 1회차 준비 상태 기준이다.

| 명제 | 판정 | 근거 |
| --- | --- | --- |
| ① 수렴 | 진행 중 | 밴드 도달 전이며, 현 proposal 예측 완주율은 0%다. |
| ② 총비용 $0·구독 0 | O | ruined-lab run-log 합산 `estimatedUSD=0`, `subscriptionCalls=0`. |
| ③ 사람 개입 범위 제한 | O | 결재 대기에서 멈췄고, 에이전트가 approval/reject/ack를 호출하지 않았다. |
| ④ 통제 장치 목록 | 일부 | 14b tier1 review가 `needs_changes`로 사람 검토에 세웠다. 가드레일·체크포인트는 이번 준비 단계에서 발동하지 않았다. |

예측-실측 괴리 통계:

- 아직 적용 전이라 예측-실측 비교값은 없다.
- 다음 회차에서 사람이 승인하면 자동 적용·재측정 후 `tuning.applied` 이벤트의 predicted/actual 비교를 기록한다.

레버 확장 전/후 서사:

- 아직 레버 확장 안건은 상정 전이다.
- 현 레버 안에서 completionRate는 예측상 0%에 머물러 있다.
- no_solution 또는 예측 미달이 반복되면 정식 사람 안건으로 `rooms[*].enemies.hp`, `rooms[*].enemies.count` 레버 확장을 상정한다. 사람이 지정한 값만 definition에 반영한다.

## E. 사람 결재 대기

현재 판단 요청:

1. `proposal-1783595466291`을 승인할지 결정한다.
2. 승인하면 시스템이 `rooms[1].rewards.healAmount` 8→10을 적용하고 재측정한다.
3. 거절하거나 수정 지시를 내리면 다음 proposal 생성/검토 흐름으로 이동한다.

추천 판단 보조:

- 승인해도 completionRate 예측 개선은 없다.
- 다만 room3DeathRate와 avgRewardPerRun은 개선 예측이 있다.
- 14b는 note 문구 품질 문제로 `needs_changes`를 냈다. 수치 변경 자체가 무관하다는 판정은 아니다.

## F. 관례 게이트

커밋 전 게이트:

```powershell
dotnet run --project server --no-build -- measure dev-pack
```

결과:

```json
{
  "projectId": "dev-pack",
  "violationCount": 0,
  "proposalLifecycle": "superseded",
  "currentStage": "deviationCheck",
  "overallStatus": "completed"
}
```

판정: O. 이번 E2E 준비 기록은 dev-pack 품질 게이트를 통과했다.
