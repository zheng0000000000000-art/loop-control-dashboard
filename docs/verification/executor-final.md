# 실행자 마감 + 관례 파일 + measure CLI 실행 검증

검증일: 2026-07-09

## 지난 검증(local-executor.md)의 "항상 폴백" 관찰에 대한 원인 확인

지난 로그를 다시 확인한 결과, run-log는 두 번의 별개 측정 호출을 이어붙인 것이었다:
1. 1차 측정(진짜 endpoint, `127.0.0.1:11434`) — `proposal.generated`가 두 라운드 모두 `fallback:false`로 성공(당시 문서에도 명시했었다).
2. 2차 측정 — "Ollama 부재" 폴백 경로를 **의도적으로 재현**하려고 dev-pack definition의 executor `endpoint`를 `127.0.0.1:19999`(리스너 없음)로 일시 변경한 뒤 측정 → 이 구간만 `fallback:true`.

run-log가 시간순으로 이어져 있어 최근 항목만 보면 "항상 실패"로 보였다. `ollama list`로 재확인한 결과 `qwen3:8b`(5.2GB)는 이번 세션 내내 정상 설치돼 있었다 — 모델 미설치(404) 가설은 이번 재검증에서는 해당하지 않았다. 다만 지시대로 원인을 로그만으로 즉시 판별할 수 있도록 `failReason`을 추가했다(아래 A-2).

## A. 실행자 마감

### A-1. 모델 설치 확인

`ollama list` — `qwen3:8b`, `llama3.1:8b`, `qwen3:14b` 모두 설치 확인. `setup-ollama.ps1`의 `Select-ExecutorModel`은 이미 "검토 모델과 다른 qwen3 계열 8b급, 없으면 `qwen3:8b` pull"로 구현돼 있어(직전 커밋에서 실측 보정) 추가 변경 없이 재사용했다.

### A-2. failReason 로깅

`OllamaExecutor.cs`의 `TryGenerateNote`/`TryGenerateSummary`가 이제 `(결과, 에러사유)` 튜플을 반환한다. `ReviewerUnavailableException`의 메시지(HTTP 상태 코드+본문, 타임아웃, 연결 실패 등)를 그대로 상위로 전달해 `Generate()`가 구체적 실패 사유를 담은 `Error`를 반환하고, `Program.cs`의 `GeneratedLogEntry`가 이를 `proposal.generated` 로그의 `params.failReason`에 기록한다. `dashboard/data/lang/{ko,en}.json`의 `proposal.generated` 템플릿도 `{failReason}`을 표시하도록 갱신했다.

**실측**: dev-pack definition의 executor endpoint를 `127.0.0.1:19999`로 일시 변경해 강제로 실패시킨 결과:

```json
"failReason": "koPoliteEndings: Ollama 연결 실패: 대상 컴퓨터에서 연결을 거부했으므로 연결하지 못했습니다. (127.0.0.1:19999)"
```

메트릭 id, 실패 단계(연결/타임아웃/HTTP 오류), 대상 endpoint까지 로그만으로 원인을 판별할 수 있다.

### A-3. maxRegenerations

`Program.cs`의 `ApplyMeasurementResult`는 이미 `Number(bundle.Definition["executorPolicy"]?.AsObject()["maxRegenerations"], 0)`로 definition 값을 참조하고 있었다(직전 커밋에서부터). 하드코딩된 곳이 없어 추가 변경 없음 — 확인만 기록한다.

## B. 관례 파일

저장소 루트에 `CLAUDE.md`, `AGENTS.md`를 지시된 내용 그대로(동일 본문) 생성했다.

## C. measure CLI

`server/Program.cs` 최상단에 `args[0] == "measure"` 분기를 추가해 서버 기동 없이 측정을 실행한다. 실행 본체는 기존 HTTP 라우트가 쓰던 로직을 그대로 재사용한다 — `Measure()`(HTTP)와 CLI 모두 새로 추출한 `RunMeasureCore()`를 호출하며, `ApplyMeasurementResult`·`GenerateProposalWithFallback`·`OllamaExecutor`·`OllamaReviewer` 등 기존 함수는 전혀 중복 구현하지 않았다. 다만 Program.cs의 최상위 지역 함수는 다른 파일에서 호출할 수 없는 C# top-level statements 제약 때문에, CLI 진입점(`RunMeasureCli`)과 출력 요약(`BuildCliSummary`)도 부득이 Program.cs 안에 위치한다 — 대신 그 안에서 새 로직을 추가하지 않고 기존 `RunMeasureCore`만 호출하도록 최소화했다.

사용법: `dotnet run --project server -- measure <projectId>` (`--no-build` 병용 가능). 출력은 위반 요약 JSON 한 줄(`stdout`), 오류는 `{"error":...}` 한 줄(`stderr`). 종료 코드: 위반 0 → `0`, 위반 존재 → `1`, 실행 오류(프로젝트 없음, 인자 누락 등) → `2`.

## D. 실행 검증

### D-1. CLI 인자 검증

| 명령 | stdout/stderr | 종료 코드 |
| --- | --- | --- |
| `measure` (인자 없음) | `{"error":"사용법: measure <projectId>"}` | 2 |
| `measure dev-pack` (위반 없음) | `{"projectId":"dev-pack","violationCount":0,...}` | 0 |

### D-2. 핵심 판정 — 8b가 14b의 지적을 실제로 반영하는가

`ko.json`에 `"__verificationTemp": "검증용 임시 문장이에요."`를 추가해 위반을 주입한 뒤 `dotnet run --project server --no-build -- measure dev-pack`으로 측정했다.

| 라운드 | proposal id | createdBy | 폴백 여부 | note (요지) | 검토 verdict | 실패 체크 |
| --- | --- | --- | --- | --- | --- | --- |
| 1차 | proposal-1783568433787 | `{ollama, qwen3:8b}` | false | "문장 끝에 존칭 표현이 부족하여 측정값이 1로 유지되고 있으며 ... 목표인 0에 도달하지 못했다" | needs_changes | `after-matches-goal`(note가 암시하는 방향과 실제 after=0이 불일치한다고 검토자가 판단) |
| 2차(재생성) | proposal-1783568446399 | `{ollama, qwen3:8b}` | false | "목표는 0이지만 이전 시도에서 after 값이 1로 설정되어 일치하지 않아 거절되었습니다. 이에 따라 after 값은 0으로 유지해야 합니다." | **approved** | 없음 — 3개 체크 모두 통과 |

**판정: True로 바뀌었다.** 1차에서 `after-matches-goal`이 실패하자, 그 지적(needs_changes의 finding note)이 실행자 재호출 프롬프트의 피드백으로 들어갔고, 2차 note는 명시적으로 "이전 시도에서 ... 일치하지 않아 거절되었습니다"라고 지적을 인용하며 방향을 바로잡았다. 14b는 2차에서 `note-nonempty`·`after-matches-goal`·`no-scope-creep` 세 항목 모두 `true`로 판정해 `verdict: approved`를 냈다. 두 라운드 모두 `proposal.generated`의 `fallback`은 `false`였다 — 실제 qwen3:8b가 두 번 다 성공했다(폴백 아님).

run-log 이벤트 순서:
```
stage.warning
proposal.generated  { provider: ollama, model: qwen3:8b, durationMs: 4288, fallback: false }
proposal.created    { proposalId: proposal-1783568433787 }
review.tier1_completed { verdict: needs_changes, model: qwen3:14b, durationMs: 8050 }
proposal.superseded { proposalId: proposal-1783568433787, reasonCode: review.needs_changes_regenerate }
proposal.generated  { provider: ollama, model: qwen3:8b, durationMs: 4556, fallback: false }
proposal.created    { proposalId: proposal-1783568446399 }
review.tier1_completed { verdict: approved, model: qwen3:14b, durationMs: 7819 }
```

### 성능 실측

| 구간 | 1라운드 | 2라운드(재생성) |
| --- | --- | --- |
| 실행자 생성(qwen3:8b) | 4288ms | 4556ms |
| 검토(qwen3:14b, 체크 3개) | 8050ms | 7819ms |
| 전체(생성+검토+재생성+재검토) | 24713ms | |

### D-3. 재생성 1회 제한 확인

`maxRegenerations: 1`대로 2차에서 이미 `approved`였으므로 3차 재생성은 발생하지 않았다(코드상 `for` 루프 조건이 `verdict == "needs_changes"`일 때만 반복되므로, approved 순간 자연 종료 — 직전 세션에서 needs_changes가 유지된 경우 정확히 1회에서 멈추는 것도 이미 확인됨).

### D-4. failReason 폴백 경로 (재확인)

dev-pack definition의 executor endpoint를 `127.0.0.1:19999`, `timeoutSeconds:3`으로 일시 변경 후 재측정:

- `proposal.generated`: `fallback:true`, `reasonCode:system.executor_degraded`, `failReason:"koPoliteEndings: Ollama 연결 실패: 대상 컴퓨터에서 연결을 거부했으므로 연결하지 못했습니다. (127.0.0.1:19999)"`
- createdBy: `{rule-engine, null}` — 정직하게 기록
- 종료 코드 1(위반 존재), 루프 무중단, 사람 검토로 정상 도달

### 원복 확인

1. dev-pack definition의 executor `endpoint`/`timeoutSeconds`를 `http://127.0.0.1:11434`/`90`으로 복원.
2. `ko.json`의 `__verificationTemp` 제거.
3. `dotnet run --project server --no-build -- measure dev-pack` → `{"violationCount":0,...,"proposalLifecycle":"superseded",...}`, 종료 코드 0.
4. 모든 라운드에서 생성된 proposal은 최종적으로 `superseded`로 자연 정리됐다. 어떤 proposal도 사람이 승인/거절하지 않았다.

## 불변 확인

| 항목 | 명령 | 결과 | 판정 |
| --- | --- | --- | --- |
| Core 격리 | `rg -n "ollama\|reviewChecklist\|note-nonempty\|after-matches-goal\|no-scope-creep\|OllamaExecutor" server/Engine.cs server/Storage.cs server/Guardrails.cs` | 결과 없음 | O |
| 빌드 | `dotnet build server/LocalFirstWorkflowDashboard.Server.csproj` | 경고 0, 오류 0 | O |
| 프런트 문법 | `node --check dashboard/app.js` | 오류 없음 | O |

## E. CLAUDE.md 준수 (최초 사례)

최종 커밋 전, `CLAUDE.md`의 품질 게이트에 따라 `dotnet run --project server --no-build -- measure dev-pack`을 실행했다.

- 결과: `{"projectId":"dev-pack","violationCount":0,"proposalId":"proposal-1783568549708","proposalLifecycle":"superseded","createdBy":{"provider":"rule-engine","model":null},"currentStage":"deviationCheck","overallStatus":"completed"}`
- 종료 코드: `0`
- 위반 0건 — 게이트 통과. blueprint·definition·측정 코드는 수정하지 않았고, approve/reject도 호출하지 않았다.

## 결론

- `proposal.generated`가 폴백 없이 qwen3:8b로 성공한 사례를 이번 검증에서 2회(같은 측정 사이클의 1·2차 생성) 확인했다 — 완료 기준 충족.
- 핵심 판정(D-2): 14b가 1차에서 지적한 `after-matches-goal` 결함을 8b가 피드백을 받아 2차에서 정확히 해소해 `verdict: approved`를 받았다. "AI가 만들고 AI가 검토하고 지적이 반영되는" 닫힌 루프가 실측으로 완성됐다.
- `failReason`이 폴백 시 구체적 원인(연결 거부/타임아웃/HTTP 상태)을 로그에 직접 남긴다.
- `maxRegenerations`는 이미 definition 소속이었다.
- `measure` CLI가 종료 코드 0/1/2로 정상 동작하고, 기존 measure 로직을 재사용하며 신규 중복 구현이 없다.
- `CLAUDE.md`/`AGENTS.md`가 저장소 루트에 존재하며, 이번 커밋 자체가 그 규칙의 첫 준수 사례다.
