# Ollama 1층 체크리스트 검토 실행 검증

검증일: 2026-07-09

## 환경 확인

| 항목 | 명령 | 결과 | 판정 |
| --- | --- | --- | --- |
| Ollama 설치 | `ollama --version` | `ollama version is 0.31.1` | O |
| Ollama 모델 목록 | `ollama list` | `qwen3:14b` 1개 확인. definition의 `qwen2.5:14b-instruct`, `llama3.1:8b`는 아직 없음. | X |
| Ollama API | `GET http://localhost:11434/api/tags` | 타임아웃. `ollama serve` 재시작 후에도 동일했다. | X |

## 서버 검증

| 단계 | 명령 | 응답 요약 | 판정 |
| --- | --- | --- | --- |
| 서버 기동 | `dotnet run --project server --no-build` | 검증 서버가 `http://localhost:5184`에서 응답했다. `GET /api/projects/dev-pack/state`는 `200`이었다. | O |
| Ollama 부재 강등 | dev-pack definition의 `timeoutSeconds`를 임시로 `2`로 낮추고, `ko.json`에 `"검증용 임시 문장이에요."`를 추가한 뒤 `POST /api/projects/dev-pack/actions/measure` | `koPoliteEndings=1`, evidence `dashboard/data/lang/ko.json:179`. `deviationCheck=warning`, `changeReview=pending_review`, proposal `proposal-1783563851352` 생성. `review.tier1_completed` 로그의 `reasonCode=system.reviewer_unavailable`, `verdict=uncertain`, `durationMs=4036`, `role=runtime`. review-report는 생성되지 않았다. | O |
| AI report 생성 경로 | Ollama API 형태의 stub을 `http://127.0.0.1:11555`에 띄우고 definition endpoint를 임시 변경한 뒤 `POST /api/projects/dev-pack/actions/measure` | AI report 1개 생성. `reviewer={ type: ai, provider: ollama, model: qwen2.5:14b-instruct }`, `verdict=approved`, findings 3개: `note-nonempty`, `after-matches-goal`, `no-scope-creep`. 상태는 `changeReview=pending_review`로 유지됐다. | O |
| 실제 Ollama 기동 상태 report | 실제 `localhost:11434` API가 계속 타임아웃이라 수행하지 못했다. | 실제 모델 호출 검증은 보류. `scripts/setup-ollama.ps1`로 요구 모델 설치 후 재검증해야 한다. | X |

## 단위 경로 검증

임시 콘솔 프로젝트가 `server/OllamaReviewer.cs`를 직접 참조해 실행했다. repo에는 테스트 파일을 남기지 않았다.

| 항목 | 입력 | 결과 | 판정 |
| --- | --- | --- | --- |
| note 비움 실패 | `note=""`, stub 응답 `answer=false` | report verdict `needs_changes`, finding `checkId=note-nonempty`, `answer=false`. | O |
| 비JSON 응답 | stub 응답 `not json at all`, `maxRetries=2` | report verdict `uncertain`, finding `attempts=2`, `answer=null`. | O |
| 신원 충돌 | proposal `createdBy={ provider: ollama, model: qwen2.5:14b-instruct }` | report 미생성, `ReasonCode=review.identity_conflict`, log params `reasonCode=review.identity_conflict`. | O |

## 라우팅 확인

- proposal 자동 생성 뒤 tier1 검토가 실행된다.
- tier1 통과 report가 있어도 `changeReview=pending_review`가 유지된다.
- tier1 실패, 불확실, unavailable의 종착지는 사람 검토다.
- 모든 tier1 로그의 `cost.role`은 `runtime`, `estimatedUSD=0`, `subscriptionCalls=0`이다.

## 불변 확인

| 항목 | 명령 | 결과 | 판정 |
| --- | --- | --- | --- |
| Core 격리 | `rg -n "ollama|reviewChecklist|note-nonempty|after-matches-goal|no-scope-creep" server/Engine.cs server/Storage.cs server/Guardrails.cs` | 결과 없음. | O |
| 빌드 | `dotnet build server/LocalFirstWorkflowDashboard.Server.csproj` | 경고 0, 오류 0. | O |
| 프런트 문법 | `node --check dashboard/app.js` | 오류 없음. | O |

## 사람 결재 대기

검증 중 생성된 proposal은 승인하지 않았다. 실제 Ollama API가 타임아웃인 상태에서는 `system.reviewer_unavailable`로 사람 검토에 강등되는 것을 확인했다. 검증용 임시 데이터 변경은 원복했으며, 최종 커밋에는 기능 코드와 이 문서만 남긴다.
