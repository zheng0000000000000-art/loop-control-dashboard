# FAIL-2026-008 — self-refactor dispatch 템플릿이 현행 코드와 어긋나 빌드 실패

- 상태: 확인됨
- 최초 발생일: 2026-07-11
- 최근 발생일: 2026-07-11
- 관련 DI: CODEX-QUEUE 2, R-01~04 호출부 정합성 헌트
- 구성요소: dispatch-executor, executor-orchestration
- failureClass: stale_template, design_learning
- 심각도: medium

## 발생 상황

R-01~04 리팩토링 뒤 호출부 정합성을 확인하던 중 `server/DispatchExecutorCli.cs`가 self-refactor 경로에서 `server/dispatch-templates/*.txt` 템플릿을 현행 코드에 직접 치환하는 것을 확인했다. 이 템플릿은 현행 `TuningResult` 생성자와 `Program.cs` 측정 적용 흐름보다 오래된 형태를 담고 있다.

## 관찰된 증상과 영향

임시 clean worktree에서 다음 순서로 재현했다.

1. `dotnet build server -c Release`
2. `dotnet run --project server -c Release --no-build -- dispatch-executor claude-code "Program.cs Orchestrator.cs ProposalFlow.cs"`
3. `dotnet build server -c Release`

2단계는 `self-refactor templates applied`로 성공 종료했지만, 3단계 빌드는 실패했다.

대표 오류:

```text
server/BalanceTuner.cs(64,16): error CS7036: 'TuningResult.TuningResult(..., int)'의 필수 매개 변수 'RestartAttempts'에 해당하는 인수가 없습니다.
```

추가로 `Program.cs`에는 `ApplyMeasurementRegressionCase`, `ApplyMeasurementTuningCase`, `ApplyMeasurementDevPackCase`, `ApplyMeasurementCompliantCase`가 더 이상 호출되지 않는다는 경고가 발생했다. 이는 템플릿이 R-04의 분할 구조와 다른 `ApplyMeasurementResult` 본문을 덮어쓴 결과다.

## 발생 이유(직접·근본·기여, 미확정은 가설 표시)

- 직접 원인: `server/dispatch-templates/BalanceTunerSearch.txt`가 현행 `TuningResult` 생성자 인자 목록을 따라가지 못했다.
- 직접 원인: `server/dispatch-templates/ApplyMeasurementResult.txt`가 R-04 이후 현행 `ApplyMeasurementResult` 분할 구조와 어긋난다.
- 근본 원인: self-refactor dispatch 템플릿이 일반 빌드와 별도로 검증되지 않아 코드 리팩토링 뒤 stale 상태가 됐다.
- 기여 요인: `DispatchExecutorCli.ApplySelfRefactor()`는 템플릿 적용 성공만 보고하고, 적용 후 빌드 가능성은 자체 검증하지 않는다.

## 검토한 해결 방법

- self-refactor 템플릿을 현행 코드와 수동 동기화한다.
- self-refactor dispatch 검증에 "템플릿 적용 후 `dotnet build server -c Release`"를 필수로 추가한다.
- 오래된 self-refactor deterministic stub을 제거하거나, 실제 지시별 outbox 생성 경로와 분리한다.
- 템플릿 기반 전체 함수 치환 대신 작은 diff 또는 컴파일 테스트가 붙은 생성 경로를 사용한다.

## 선택한 해결 방법

이번 QA 작업에서는 코드를 수정하지 않았다. 구현 실행자에게 템플릿 동기화와 적용 후 빌드 게이트 추가를 이관한다.

## 판단 기준

`dispatch-executor claude-code`가 `self-refactor templates applied`로 성공을 보고하면, 그 결과물은 최소한 `dotnet build server -c Release`를 통과해야 한다. 템플릿 적용 성공 후 빌드 실패는 self-refactor dispatch 산출물이 반입 가능한 후보가 아니라는 뜻이다.

## 검증 결과

| 단계 | 명령 | 결과 |
| --- | --- | --- |
| 기준 빌드 | `dotnet build server -c Release` | 경고 0, 오류 0 |
| 템플릿 적용 | `dotnet run --project server -c Release --no-build -- dispatch-executor claude-code "Program.cs Orchestrator.cs ProposalFlow.cs"` | exit 0, `self-refactor templates applied` |
| 적용 후 빌드 | `dotnet build server -c Release` | 실패, 경고 4, 오류 1 |

원본 작업트리는 수정하지 않고 `C:\Users\1\AppData\Local\Temp\lfwd-qa-dispatch-template` 임시 worktree에서만 재현했다.

## 재발 방지

- self-refactor 템플릿을 수정하는 커밋은 템플릿 적용 후 빌드까지 검증한다.
- `DispatchExecutorCli`가 템플릿 적용 뒤 빌드를 실행하지 않더라도, 검증 문서에는 적용 후 빌드 결과를 필수로 남긴다.
- R-04처럼 함수 분할·시그니처 변경이 있으면 `server/dispatch-templates/`도 호출부 정합성 점검 대상에 포함한다.

## 후속 작업과 잔여 위험

- `BalanceTunerSearch.txt`와 `ApplyMeasurementResult.txt`가 현행 코드와 동기화돼야 한다.
- 다른 템플릿(`EngineApplyStatePatch.txt`, dashboard 함수 변환 템플릿)도 같은 stale 위험이 있으므로 함께 빌드/동작 검증이 필요하다.
- self-refactor dispatch가 실제로 다시 사용되면 실패 outbox 또는 빌드 불가 산출물을 만들 수 있다.

## 발생 이력

- 2026-07-11: CODEX-QUEUE 2 호출부 정합성 헌트 중 `DispatchExecutorCli` 템플릿 경로 발견.
- 2026-07-11: 임시 worktree에서 self-refactor 템플릿 적용 후 빌드 실패 재현.
- 2026-07-11: `FAIL-2026-008`로 실패 위키 등록.

