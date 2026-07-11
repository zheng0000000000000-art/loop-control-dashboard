# FIX-05 검증 문서 — BalanceTuner.cs 함수 분할

## ① 주체 (actor)
- **실행자**: claude-sonnet-4-6 (직접 경로)
- **사유**: 지시서에 직접 경로 허용 명시 + allowlist 파일이 docs/verification/* 포함

## ② 사용한 하네스와 결과

| 하네스 | 명령 | exit code | 핵심 수치 |
|---|---|---|---|
| build-verify | `dotnet run --project server -c Release -- build-verify` | 0 | verdict: PASS, 경고 0, 오류 0 |
| measure dev-pack | `dotnet run --project server -c Release -- measure dev-pack` | 1 | violations: 1 (maxFunctionLength=101, 새로 드러난 함수) |
| verify-behavior | `dotnet run --project server -c Release -- verify-behavior` | 0 | behaviorEqual: true |

## ③ 참조한 스킬
- (이번 작업에서 스킬 파일 참조 없음 — BalanceTuner.cs 함수 분할은 도메인 스킬 트리거 해당 없음)

---

## 검수 기준 자가점검표

| # | 기준 | 결과 |
|---|---|---|
| 1 | `maxFunctionLength` ≤ 80 (Search 함수) | ✓ Search: 43-109행 = 67줄 |
| 1a | 새로 드러난 더 긴 함수 | **보고**: `server/Tier2Approver.cs:38-138` (101줄) — 고치지 않음, 다음 지시서 대상 |
| 2 | verify-behavior → behaviorEqual: true | ✓ exit 0 |
| 3 | build-verify → verdict: PASS, exit 0 | ✓ |
| 4 | functionsWithoutComment = 0 | ✓ (measure: 0) |
| 5 | blueprint.json·workflow-definition.json·DevPackMeasures.cs 무수정 | ✓ git diff --name-only에 없음 |

## measure 결과 JSON
`{"gate":"dev-pack","violations":1,"attempt":1}`

> 위반 1건 잔존: `maxFunctionLength=101` (server/Tier2Approver.cs:38-138) — 이 함수는 이번 작업(FIX-05) allowlist 밖. Search 함수는 115→67줄으로 목표 달성. 새 위반은 FIX-05가 드러낸 것이므로 실패가 아님(지시서 명시: "드러나면 고치지 말고 그 위치를 보고하라").

---

## 작업 내용 요약

**분할 방식**:
- `Search` 함수 (원래 43-157행, 115줄) → `Search` (43-109행, **67줄**) + `BuildTuningResult` 헬퍼 (112-163행, **52줄**)
- 추출 기준: "결과 수집" 의미 단위 (레버 변화 수집 + 예측 지표 + 잔여 위반 + TuningResult 반환)
- 헬퍼에 한국어 기능 주석 1줄 추가: `// 탐색 완료 후 레버 변화·예측 지표·잔여 위반을 수집해 TuningResult를 만든다.`
- 동작 동일: ref/out 없이 값 전달만으로 리팩터링, 결과 동일함을 verify-behavior로 확인

**새로 드러난 위반 (보고)**:
- 위치: `server/Tier2Approver.cs:38-138` (101줄)
- 조치: 고치지 않음 (다음 지시서 대상)
