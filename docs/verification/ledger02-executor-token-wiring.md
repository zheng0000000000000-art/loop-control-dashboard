# LEDGER-02 검증 문서 — 실행자 토큰 배선

## ① 주체 (actor)

- 실행자: claude-sonnet-4-6 (LEDGER-02 executor)
- 작업일: 2026-07-12

## ② 사용한 하네스와 각각의 exit code

| 하네스 | 명령 | exit code | 핵심 수치 |
|--------|------|-----------|-----------|
| build-verify | `dotnet build server -c Release` | **0** | warning 0, error 0 |
| verify-behavior | `dotnet run --project server -- verify-behavior` | **0** | behaviorEqual: true |
| measure dev-pack | `dotnet run --project server -- measure dev-pack` | **0** | violationCount: 0 |
| gate-clean server | `dotnet run --project server -- gate-clean server` | 1 (예상) | server/Program.cs content-dirty(의도된 변경) |
| handoff-integrity | `dotnet run --project server -- handoff-integrity` | PASS → LEDGER-01 기준(LEDGER-02 갱신 전 실행) |

**gate-clean exit 1 사유**: `server/Program.cs`가 수정된 상태(커밋 전). 이 변경은 LEDGER-02의 의도된 산출물이며 비정상이 아니다.

### 게이트 기록

```json
{"gate":"dev-pack","violations":0,"attempt":1}
```

## ③ 참조한 스킬

- docs/directives/_header.md (불변 제약)
- docs/handoff/queue/directive-LEDGER02-executor-token-wiring.md (지시서)
- docs/verification/ledger01-token-ledger.md (선행 작업 참조)

## 코드 변경 요약

### 변경 파일: `server/Program.cs`

**1. `RuntimeCost()` 시그니처 확장 (line 2256)**

```csharp
// 변경 전
static JsonObject RuntimeCost()

// 변경 후
static JsonObject RuntimeCost(int inputTokens = 0, int outputTokens = 0)
```

- 기본값 0이므로 기존 21곳 호출은 전부 그대로 작동
- LLM 호출이 없는 지점(rule-engine, 결재, 측정 이벤트)은 인수 없이 호출해 0 유지 → 재지 않은 것을 잰 것처럼 적지 않는다

**2. `GeneratedLogEntry()` 시그니처 확장 (line 1238)**

```csharp
// 변경 전
static JsonObject GeneratedLogEntry(string provider, string? model, long durationMs, bool fallback, string? error, bool selfReviewed, bool selfReviewPassed)

// 변경 후
static JsonObject GeneratedLogEntry(..., int inputTokens = 0, int outputTokens = 0)
// 내부: ["cost"] = RuntimeCost(inputTokens, outputTokens)
```

**3. ollama 성공 경로 2곳에서 실제 토큰 전달**

```csharp
// GenerateProposalWithFallback (line 1211) — 표준 제안 생성
return new ProposalGeneration(proposal, GeneratedLogEntry(
    generated.Provider, generated.Model, generated.DurationMs, false, null,
    generated.SelfReviewed, generated.SelfReviewPassed,
    generated.InputTokens, generated.OutputTokens));  // ← 추가

// GenerateTuningProposalWithFallback (line 1088) — 튜닝 제안 생성
return new ProposalGeneration(proposal, GeneratedLogEntry(
    generated.Provider, generated.Model, generated.DurationMs, false, null,
    generated.SelfReviewed, generated.SelfReviewPassed,
    generated.InputTokens, generated.OutputTokens));  // ← 추가
```

- fallback 경로(`"rule-engine"`)는 인수 없이 호출 → 0 유지 (정상)
- OllamaReviewer.cs 372줄의 패턴과 동일

### 불변 확인

- `Guardrails.cs` / `Engine.cs` / `Tier2Approver.cs` 미수정
- cost 스키마 키 `inputTokens, outputTokens, estimatedUSD, subscriptionCalls, role` 추가·삭제·개명 없음
- `estimatedUSD, subscriptionCalls` = 0 유지 (로컬 ollama 과금 없음)
- `role` = `"runtime"` 유지

## 비-LLM 항목 토큰 확인

`measure.completed` 등 비-LLM 항목의 `RuntimeCost()` 호출 지점(line 300, 324, 374, 393, 421, 445, 481, 529, 898, 1001, 1424, 1554, 1679, 1710, 1759, 1821, 1852, 1870, 2180):
- **전부 인수 없이 호출** → `inputTokens=0, outputTokens=0` 보장
- 변경 없음

run-log의 최근 `measure.completed` 항목 예시:
```json
{
  "event": "measure.completed",
  "cost": {
    "inputTokens": 0,
    "outputTokens": 0,
    "estimatedUSD": 0,
    "subscriptionCalls": 0,
    "role": "runtime"
  }
}
```

## 지표는 만족했으나 목적은 미달인 부분

**목적**: `proposal.generated` 이벤트의 `cost.inputTokens > 0, cost.outputTokens > 0` 인 항목이 run-log에 실제로 새로 생기는 것을 보여라.

**달성하지 못한 이유** (자진 신고):

코드 배선은 올바르게 완료됐으나, 실제 동작 증명이 불가능한 두 가지 조건이 겹쳤다.

### 조건 1: 현재 저장소 violations=0 — 자연 트리거 없음

현재 dev-pack 모든 메트릭이 밴드 안에 있어 `proposal.generated` 이벤트가 발생하지 않는다.

### 조건 2: `__TokenProbe` 임시 함수로 인위적 위반 생성 시도 → ollama 일관 실패

`server/OllamaExecutor.cs`(allowlist 포함)에 임시 private 함수 `__TokenProbe`를 추가해 `functionsWithoutComment=1` 위반을 만들고 12회 이상 measure를 실행했다.

결과: ollama `qwen3:8b`가 `functionsWithoutComment` metricId를 **일관되게 `functionsWithOutComment`(대문자 O)로 반환**해 `ParseNoteResponse`의 정확히 일치 검사(`metricId == expectedMetricId`)에서 거부됐다.

직접 테스트(3회): `functionsWithOutComment` / `functionsWithOutComment` / `functionsWithOutComment`  
한 번 단순 프롬프트 성공: `functionsWithoutComment` — 그러나 `note` 필드에 `\uFFFD`(교체 문자) 4개 포함 → `HasReplacementChar` 거부

이는 LEDGER-02의 코드 변경 결함이 아니라 **qwen3:8b 모델의 한국어 metricId 대소문자 불안정**이다. 동일 모델로 note를 성공적으로 생성한 유일한 경로(`review.tier1_completed` — OllamaReviewer.cs)는 LEDGER-01에서 증명됐다.

### 현재 run-log 상태 증거

fallback 경로(rule-engine)의 최근 항목 — `inputTokens=0, outputTokens=0`은 **정상** (LLM 호출 없음):
```json
{
  "createdAt": "2026-07-12T00:58:34.5411555+09:00",
  "event": "proposal.generated",
  "params": {
    "provider": "rule-engine",
    "model": null,
    "durationMs": 7180,
    "fallback": true,
    "reasonCode": "system.executor_degraded",
    "text": "functionsWithoutComment: 응답 JSON 스키마 불일치 또는 빈/손상된 note"
  },
  "cost": {
    "inputTokens": 0,
    "outputTokens": 0,
    "estimatedUSD": 0,
    "subscriptionCalls": 0,
    "role": "runtime"
  }
}
```

### LEDGER-03 후보

`ParseNoteResponse`에서 `StringComparison.OrdinalIgnoreCase`를 사용하거나, 또는 ollama 프롬프트에서 metricId 반환 방식을 개선하면 성공 케이스 증명이 가능해질 것이다. 단, 이 변경은 스키마/동작 변경이므로 별도 지시서 필요.
