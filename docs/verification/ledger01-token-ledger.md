# LEDGER-01 검증 문서 — ollama 토큰 계측

## 주체 (actor)

- **실행자**: LEDGER-01, claude-sonnet-4-6 (Sonnet 4.6)
- **세션**: 2026-07-12 (이전 세션에서 컨텍스트 압축 후 재개)

## 작업 요약

ollama `/api/generate` 응답의 `prompt_eval_count`·`eval_count`를 3개 호출 지점에서 읽어 기존 `cost.inputTokens`·`cost.outputTokens` 필드에 기록하도록 수정했다.

수정 파일:
- `server/OllamaExecutor.cs` — `CallModel()` 반환 타입을 `(string, int, int)` 튜플로 변경. `TryGenerateNote`, `TryGenerateSummary`, `TryGenerateTuningNote`, `TryGenerateTuningSummary`, `RunSelfReviewIfEnabled`, `Generate`, `GenerateForTuning`에 토큰 집계 경로 연결. `ExecutorGenerateResult`, `SelfReviewResult` 레코드에 `InputTokens`, `OutputTokens` 옵션 필드 추가.
- `server/OllamaReviewer.cs` — `CallModel()` 반환 타입을 `(string, int, int)` 튜플로 변경. `RunSingleCheck`, `TryReviewWithModel`에서 토큰 누적. `Review()`가 `LogEntry()`에 실제 토큰 전달. `LogEntry()`, `RuntimeCost()` 파라미터 추가. `SingleCheckResult`, `ModelReviewAttempt` 레코드에 옵션 필드 추가.
- `server/Tier2Approver.cs` — `RequestReviewWithModel()`에서 `prompt_eval_count`·`eval_count` 추출. `ReviewOutcome` 레코드에 `InputTokens`, `OutputTokens` 옵션 필드 추가.

## 사용한 하네스와 exit code

| 하네스 | 명령 | exit code | 핵심 수치 |
|---|---|---|---|
| build | `dotnet build server -c Release` | **0** | warning 0, error 0 |
| measure dev-pack | `dotnet run --project server -c Release -- measure dev-pack` | **0** | violationCount: 0 |
| verify-behavior | `dotnet run --project server -c Release -- verify-behavior` | **0** | behaviorEqual: true |
| gate-clean server | `dotnet run --project server -c Release -- gate-clean server` | **1** | contentDirtyCount: 3 (allowlist 3파일 수정 — 정상) |
| handoff-integrity | `dotnet run --project server -c Release -- handoff-integrity` | **0** | PASS, failureCount: 0 |
| projection | `dotnet run --project server -c Release -- projection` | **0** | stamped: 5, missingFiles: 0 |

`gate-clean server` exit 1은 예상된 결과다. 지시서 허용 파일 3건(OllamaExecutor·OllamaReviewer·Tier2Approver)이 git 기준 대비 수정됐기 때문이다. allowlist 밖 파일은 0건이다.

## 실체 증명 — `cost.inputTokens > 0` 항목 원문

두 번째 measure 실행(`proposal-1783782749011`) 시 OllamaReviewer가 실제로 호출되어 run-log.json에 아래 항목이 기록됐다.

```json
{
  "createdAt": "2026-07-12T00:12:39.5400420+09:00",
  "event": "review.tier1_completed",
  "params": {
    "proposalId": "proposal-1783782749011",
    "verdict": "approved",
    "model": "qwen3:14b",
    "durationMs": 10506,
    "checkCount": 3,
    "uncertainCount": 0,
    "attempts": 3,
    "text": ""
  },
  "level": "info",
  "producedBy": {
    "provider": "ollama",
    "model": "qwen3:14b"
  },
  "attempt": 1,
  "loopIteration": 13,
  "cost": {
    "inputTokens": 1541,
    "outputTokens": 144,
    "estimatedUSD": 0,
    "subscriptionCalls": 0,
    "role": "runtime"
  }
}
```

`cost.inputTokens: 1541`, `cost.outputTokens: 144` — 지시서 요구 조건 충족.

## 게이트 기록

```
{"gate":"dev-pack","violations":0,"attempt":2}
```

첫 번째 measure(TokenProofDummy 함수 추가 후): violations: 1 (예상됨, 증명용 임시 함수)  
두 번째 measure(증명 확인 후 함수 제거 뒤): violations: 0

## 참조한 스킬

- `docs/directives/_header.md` (불변 제약)
- `docs/handoff/queue/directive-LEDGER01-token-ledger.md` (지시서)
- `docs/handoff/WORKSTATE.json` (상태)
- `server/OllamaExecutor.cs`, `server/OllamaReviewer.cs`, `server/Tier2Approver.cs` (수정 대상)

## 지표는 만족했으나 목적은 미달인 부분

1. **OllamaExecutor 토큰이 run-log에 도달하지 않는다.** `server/Program.cs`가 allowlist 밖이어서 수정 불가다. `GeneratedLogEntry()`는 Program.cs 안에 있고 `RuntimeCost()`를 인수 없이 호출해 항상 0을 기록한다. OllamaExecutor.cs에서 토큰을 집계해 `ExecutorGenerateResult`까지 전달했지만, Program.cs가 그 값을 무시하고 0을 기록한다. 따라서 `proposal.generated` 계열 run-log 항목의 `cost.inputTokens`는 여전히 0이다.

2. **Tier2Approver 토큰이 run-log에 도달하지 않는다.** Tier2Approver는 run-log 항목을 생성하지 않는다. 마크다운 감사 로그만 남긴다. `ReviewOutcome` 레코드에 토큰 필드를 추가했지만 이를 run-log에 쓰는 경로가 없다.

3. **증명을 위해 임시 함수를 추가·제거했다.** 현재 dev-pack에 위반이 0건이어서 OllamaReviewer가 호출되지 않는다. 증명을 위해 `TokenProofDummy()` 함수를 OllamaReviewer.cs에 일시 추가해 `functionsWithoutComment` 위반을 1건 만들었고, 두 번째 measure에서 실제 토큰 기록을 확인한 뒤 제거했다. 이 과정에서 첫 번째 measure가 regression 경로(0→1 위반)로 라우팅돼 OllamaReviewer 대신 인간 검토 경로로 갔다. 두 번째 measure에서야 non-regression 경로로 OllamaReviewer가 호출됐다.

4. **`role` 필드가 "runtime"으로 유지됐다.** 지시서 §2에 "호출 주체를 구분하는 기존 문자열 규약을 따른다"고 명시돼 있다. 기존 코드를 확인한 결과 `executor`, `reviewer`, `approver` 구분이 없고 모두 `runtime`이었다. 스키마 변경 금지 조건에 따라 `runtime` 그대로 유지했다.
