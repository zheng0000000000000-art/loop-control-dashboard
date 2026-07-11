# LEDGER-04 — 작업 검증 문서

## ① 주체 (actor)

- **실행자**: LEDGER-04 sonnet (claude-sonnet-4-6)
- **세션**: 2026-07-12 04:06 KST
- **지시서**: docs/handoff/queue/directive-LEDGER04-metricid-normalization.md

## ② 사용한 하네스와 exit code

| 하네스 | 명령 | exit code | 핵심 수치 |
|---|---|---|---|
| build-verify | `dotnet build server -c Release` | 0 | warning 0, error 0 |
| verify-behavior | `dotnet run --project server -- verify-behavior` | 0 | behaviorEqual: true |
| measure dev-pack (주입 전) | `dotnet run --project server -- measure dev-pack` | 0 | violationCount=0 |
| measure dev-pack (주입 1차) | `dotnet run --project server -- measure dev-pack` | 1 | violationCount=1, regression 경로 |
| measure dev-pack (주입 2차) | `dotnet run --project server -- measure dev-pack` | 1 | violationCount=1, dev-pack 경로, ollama 트리거 |
| measure dev-pack (주입 제거 후) | `dotnet run --project server -- measure dev-pack` | 0 | violationCount=0 복귀 |
| gate-clean server | `dotnet run --project server -- gate-clean server` | 1 | FAIL (예상) — 실제 코드 변경 |

**gate-clean FAIL 예상 사유**: server/OllamaExecutor.cs, server/Program.cs를 실제로 수정했으므로 content-dirty 상태. 이것이 이번 작업의 정상 결과다.

## ③ 참조한 스킬

- `docs/directives/_header.md` — 불변 제약 및 Context Pack 규약
- `docs/handoff/queue/directive-LEDGER04-metricid-normalization.md` — 지시서
- `docs/verification/ledger03-fallback-observability.md` — 전 단계 검증 문서
- 스킬 파일: 없음 (allowlist 내 파일만 수정하는 implementation 작업)

## 구현 요약

### 변경한 파일

**server/OllamaExecutor.cs**:
- `ParseNoteResponse`: 1차 엄격 대조 후 2차 대소문자 무시 대조(`StringComparison.OrdinalIgnoreCase`) 추가. 2차 통과 시 `reason: "metricid-normalized"` 반환. 기존 `parse-rejected-metricid`는 두 대조 모두 실패한 경우에만 발생.
- `TryGenerateNote`: 반환 타입에 7번째 원소 `string? NormalizedActualMetricId` 추가. `reason == "metricid-normalized"` 시 해당 actualMetricId 전파.
- `TryGenerateTuningNote`: 동일 변경.
- `Generate`: `normalizedMetricIds` 리스트 수집. 성공 시 `ExecutorGenerateResult`에 전달.
- `GenerateForTuning`: 동일 변경.
- `ExecutorGenerateResult` 레코드: `List<(string Expected, string Actual)>? NormalizedMetricIds = null` 추가.

**server/Program.cs**:
- `MetricIdNormalizedLogEntry` 헬퍼 추가 — `event: "proposal.metricid_normalized"`, `level: "warn"`, params에 expectedMetricId/actualMetricId/provider/model.
- `ProposalGeneration` 레코드에 `List<JsonObject>? NormalizationEntries = null` 추가.
- `GenerateProposalWithFallback` 성공 경로: NormalizedMetricIds → NormalizationEntries 구성.
- `GenerateTuningProposalWithFallback` 성공 경로: 동일.
- `ApplyMeasurementDevPackCase`(2곳), `ApplyMeasurementTuningCase`, `RunTuningRegenerationLoop`: NormalizationEntries 조건부 append (proposal.generated 전에 삽입).

### 판정 로직 불변 확인

- 공백 제거, 유사도 매칭, 부분 일치 없음.
- expectedMetricId와의 대조만 수행 — 전체 metricId 목록 스캔 없음.
- 관대화 범위: 대소문자 한 가지로 한정.

## 실체 증명 — run-log 항목 원문

### 위반 주입

임시 함수 `private static void __NormProbe() { }` 를 `server/OllamaExecutor.cs` 끝에 삽입.
- 접근 제한자(`private static`) 포함, 이전 줄이 `}` (주석 아님) → `functionsWithoutComment` 측정에 걸림.

1차 measure: regression 경로 (before=0, after=1).
2차 measure: 이전=1, 현재=1 → 회귀 없음 → `ApplyMeasurementDevPackCase` → 제안 생성 → ollama 호출 → ParseNoteResponse 1차 실패(대소문자 불일치) → 2차 통과(OrdinalIgnoreCase) → `metricid-normalized` 반환 → 정규화 이벤트 emit → proposal.generated provider=ollama.

### 증명 1: proposal.metricid_normalized

```json
{"createdAt":"2026-07-12T04:06:30.3074687+09:00","event":"proposal.metricid_normalized","params":{"expectedMetricId":"functionsWithoutComment","actualMetricId":"functionsWithOutComment","provider":"ollama","model":"qwen3:8b"},"level":"warn","producedBy":{"provider":"rule-engine","model":null},"attempt":1,"loopIteration":13,"cost":{"inputTokens":0,"outputTokens":0,"estimatedUSD":0,"subscriptionCalls":0,"role":"runtime"}}
```

- `level: "warn"` ✅
- `actualMetricId: "functionsWithOutComment"` (대문자 O) ✅
- `expectedMetricId: "functionsWithoutComment"` ✅

### 증명 2: proposal.generated (LEDGER-02 배선 첫 검증)

```json
{"createdAt":"2026-07-12T04:06:30.3160615+09:00","event":"proposal.generated","params":{"provider":"ollama","model":"qwen3:8b","durationMs":4524,"fallback":false,"selfReviewed":false,"selfReviewPassed":false,"reasonCode":"","text":"","failReason":""},"level":"info","producedBy":{"provider":"ollama","model":"qwen3:8b"},"attempt":1,"loopIteration":13,"cost":{"inputTokens":351,"outputTokens":95,"estimatedUSD":0,"subscriptionCalls":0,"role":"runtime"}}
```

- `producedBy.provider: "ollama"` ✅
- `cost.inputTokens: 351` (> 0) ✅
- `cost.outputTokens: 95` (> 0) ✅
- **LEDGER-02 배선(`040d017`)이 처음으로 실행되었음을 확인.**

### 증명 3: 비-LLM 항목은 여전히 0

```json
{"createdAt":"2026-07-12T04:06:25.7581336+09:00","event":"measure.completed","params":{"providerId":"dev-pack-checks","violationCount":1,"durationMs":289},"level":"warning","producedBy":{"provider":"rule-engine","model":null},"attempt":1,"loopIteration":13,"cost":{"inputTokens":0,"outputTokens":0,"estimatedUSD":0,"subscriptionCalls":0,"role":"runtime"}}
```

- `cost.inputTokens: 0` ✅
- `cost.outputTokens: 0` ✅

### 주입 제거 후 violations=0 복귀

```
{"projectId":"dev-pack","violationCount":0,"proposalId":"proposal-1783796790296","proposalLifecycle":"superseded","createdBy":{"provider":"ollama","model":"qwen3:8b"},"currentStage":"deviationCheck","overallStatus":"completed"}
```

## 게이트 기록 형식

```json
{"gate":"dev-pack","violations":0,"attempt":3}
```

- attempt 1: 주입 전 measure (violations=0)
- attempt 2: 주입 후 measure 1차 (violations=1, regression path)
- attempt 3: 주입 후 measure 2차 (violations=1, dev-pack path, 정규화 증명)
- 주입 제거 후 measure: violations=0 복귀 확인

## 유령 제안 경보

주입 창(04:06 KST) 동안 스케줄러 자동 활동 없음 — 수동 측정만 진행. `proposal-1783796790296`은 주입 제거 후 superseded 상태 전환 확인.

## 직접 경로 사유

allowlist 포함 파일만 수정 (server/OllamaExecutor.cs, server/Program.cs, docs/ 문서). 관례 예외 조건 충족.

## 지표는 만족했으나 목적은 미달인 부분

없음.

- 지표 만족: build exit 0 (warning 0), verify-behavior behaviorEqual=true, measure dev-pack violations=0 (주입 제거 후).
- 목적 만족:
  1. `proposal.metricid_normalized` level=warn, actualMetricId="functionsWithOutComment"(대문자 O) 실체 확인.
  2. `proposal.generated` provider=ollama, inputTokens=351, outputTokens=95 — LEDGER-02 배선 첫 실행 확인.
  3. `measure.completed` inputTokens=0 — 비-LLM 항목 오염 없음 확인.
