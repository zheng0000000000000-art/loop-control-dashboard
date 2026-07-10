# "내보내기/적용 단계가 넘어가지 않는 것처럼 보인다" — stageDetails 불일치 수정

## 배경

사람이 모바일 스크린샷으로 ruined-lab의 "내보내기"(unityExport) 단계가 "진행 중"으로
표시되면서, 그 아래 상세 패널에는 "변경 승인이 완료될 때까지 내보내기가 차단된다" /
"이슈: 변경 검토 단계가 아직 완료되지 않았다"는 모순된 문구가 함께 떠 있다고 지적했다
(스크린샷 상 단계 배지 "진행 중", 상세 텍스트는 "아직 완료 안 됨"). 직전 턴에서는 이걸
백그라운드 탭 캐시 문제로 설명했지만, 실제 서버 데이터를 다시 조회해 보니 **탭 캐시와
무관하게, 서버에 저장된 데이터 자체가 이미 모순돼 있었다**:
`stages.unityExport: "completed"`인데 `stageDetails.unityExport`는 여전히
`"차단됨"/"아직 완료되지 않았다"` 텍스트를 담고 있었다.

## 원인

`stageDetails`는 각 단계 종류별로 별도 함수가 채운다(`SetMeasurementDetails`,
`SetTier1Details`, `SetRegressionReviewDetails`, `SetNoSolutionDetails` 등) — 그런데
적용/내보내기 단계(`apply`/`unityExport`, 워크플로우의 마지막 단계)만은 **그 상세를
채우는 코드가 아예 없었다**. 이 단계의 `stageDetails` 값은 아주 예전에(현재 코드베이스에
남아 있지 않은 어떤 시점에) 한 번 기록된 뒤로 다시는 갱신되지 않는 방치된 필드였다 —
그래서 단계 배지(`stages.unityExport`)는 completed/blocked/in_progress로 정상 전환돼도
상세 텍스트만 그 자리에 그대로 남아 화면에 모순된 정보를 보여줬다.

## 수정

`server/Program.cs`에 `SetApplyStageDetails(state, stageId)`를 추가해, 적용/내보내기
단계의 **현재 실제 상태**(completed/in_progress/blocked/그 외)에 맞는 요약·지표·이슈를
매번 다시 쓴다. 처음에는 `ApplyMeasurementStagePatch` 직후에 한 번만 호출했는데, 실측
중 **회귀 롤백·튜닝·"기준 지표 추가 제안"(`ApplySuggestedBlueprintProposalState`) 등
이후 분기가 그 단계 상태를 또 바꿀 수 있어 여전히 어긋나는 경우를 발견**했다 — 그래서
호출 위치를 `ApplyMeasurementResult` 함수의 **맨 끝**(모든 분기가 끝난 뒤, `bundle.State`에
반영하기 직전)으로 옮겨, 어느 분기를 타든 최종 확정된 단계 상태를 기준으로 한 번만
정확히 맞춘다. `Approve` 핸들러에서 적용 단계로 갓 들어갈 때도 즉시 한 번 반영한다.

## 실측

실제 서버(포트 5173)에서:

1. 수정 전 상태: `ruined-lab.stages.unityExport: "completed"` (또는 이후 재측정에서
   `"blocked"`) vs `stageDetails.unityExport`는 옛 "차단됨" 텍스트 그대로 — 재현.
2. 1차 수정(호출 위치를 `ApplyMeasurementStagePatch` 직후에 둠) 후 재측정 →
   `stages.unityExport: "blocked"`인데 `stageDetails.unityExport`는 여전히 이전 호출
   시점의 "완료" 텍스트를 담고 있음을 발견 — `ApplySuggestedBlueprintProposalState`가
   이후에 단계를 또 바꾼 탓. **1차 수정 자체가 불완전했다.**
3. 호출 위치를 함수 맨 끝으로 옮긴 뒤 재측정 → `stages.unityExport: "not_started"`,
   `stageDetails.unityExport: {"summary":"아직 시작되지 않았다.","metrics":[{"label":"적용
   상태","value":"대기"}],"issues":[]}` — **일치**.
4. dev-pack에서도 재측정 → `stages.apply: "blocked"`,
   `stageDetails.apply: {"summary":"변경 승인이 완료될 때까지 차단된다.",...,"issues":
   ["이전 단계가 아직 완료되지 않았다."]}` — **일치**.

## 게이트

```json
{"gate":"dev-pack","violations":4,"attempt":7}
```

기존과 동일한 4건(`smallTouchTargets`/`skillDomainViolations`는 순수 기존,
`programCsLines`=2801·`maxFunctionLength`=261은 이번을 포함한 누적 세션 수정으로
계속 커진 기존 위반 — 새 위반 카테고리는 없음).

## 참조한 스킬

- `skills/common/verification.md`
