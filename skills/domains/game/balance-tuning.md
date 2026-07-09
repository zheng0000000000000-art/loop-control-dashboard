# 스킬: 게임 밸런스 튜닝

버전: 1 | 도메인: game | 트리거: server/GameSimulator.cs, server/BalanceTuner.cs, dashboard/data/ruined-lab/**, docs/verification/balance-tuning.md | 대상: 게임 밸런스 튜닝

## 절차

1. 기준 지표가 문턱형인지 먼저 확인한다. 완주율, 생존율처럼 마지막 조건을 통과해야만 값이 움직이는 지표는 단일 스텝 그리디 탐색에서 거리 변화가 0으로 남을 수 있다.
2. 목표와 탐색 휴리스틱을 분리한다. blueprint는 목표이고, 평균 진행 방 수 같은 shaping 보조항은 후보 선택을 돕는 내부 점수다.
3. 보조항은 주항을 역전하지 않게 둔다. blueprint 위반 거리가 더 나쁜 후보를 진행도만 좋다는 이유로 채택하지 않는다.
4. 단일 스텝 후보에서 주항 개선이 없으면 스텝을 키운다. ±2, ±3 순서로 확인하고, 그래도 부족하면 레버 2개 조합을 제한된 후보 예산 안에서 시도한다.
5. 변경이 0건이고 밴드에 닿지 못하면 proposal을 만들지 않는다. 이 경우 `tuning.no_solution`으로 보고하고, 레버 범위 확장은 사람 결재가 필요한 기준 변경으로 남긴다.
6. 변경이 있지만 밴드에 닿지 못하면 proposal을 만들 수 있다. 단, 각 note에 최선 후보이며 밴드 미달임을 명시한다.

## 지켜야 할 것

- dry-run 예측은 `measurement.json`에 기록하지 않는다. 예측값은 proposal의 `predictedMetrics`에만 둔다.
- `tunableLevers`에 선언되지 않은 경로는 조작하지 않는다.
- blueprint 밴드나 definition 레버 범위를 임의로 완화하지 않는다.
- approval/reject 액션은 검증 중에도 호출하지 않는다. 결재는 사람 몫이다.

## 완료 판정

- simtune 출력에 baseline, 단계별 후보 수, distance, progress, score가 남아 있다.
- proposal의 `predictedMetrics`는 blueprint 지표만 포함한다.
- 변경 0건 proposal이 새로 생성되지 않는다.
- 해 없음은 `tuning.no_solution` 이벤트나 검증 문서에 보고된다.
