# Ollama 1층 체크리스트 검토 실행 검증

검증일: 2026-07-09 (실연동 재검증 포함)

## 실연동 재검증 (2026-07-09, qwen3:14b)

이전 검증에서 보류됐던 "실제 Ollama 기동 상태 report" 항목을 갱신한다.

| 항목 | 명령/조치 | 결과 | 판정 |
| --- | --- | --- | --- |
| endpoint 수정 | dev-pack·ruined-lab definition의 `reviewerPolicy.tier1.endpoint`를 `http://127.0.0.1:11434`로 변경 | 두 definition 모두 반영 | O |
| Ollama API 확인 | `curl http://127.0.0.1:11434/api/tags` | `qwen3:14b` 1개 확인, 응답 즉시 성공(이전 `localhost` 타임아웃과 달리 `127.0.0.1`은 정상 응답) | O |
| think:false 동작 확인 | `curl .../api/generate -d '{"model":"qwen3:14b",...,"think":false}'` | 응답에 `<think>` 블록 없이 `{"answer": 2}` 형태로 즉시 반환됨. `/api/generate`에서도 `think` 필드가 적용됨을 실측 확인 | O |
| 서버 기동 | `dotnet run --project server --no-build` | `http://localhost:5173`에서 응답 | O |
| 위반 주입 | `dashboard/data/lang/ko.json`에 `"__verificationTemp": "검증용 임시 문장이에요."` 임시 추가 후 `POST /api/projects/dev-pack/actions/measure` | `koPoliteEndings=1`, evidence `dashboard/data/lang/ko.json:3`. proposal `proposal-1783565097100` 생성 | O |
| AI report 실측 | 위 measure 응답 | `review-report.json`에 `reviewer={ type: ai, provider: ollama, model: qwen3:14b }`, `verdict=needs_changes`. findings 3개(`note-nonempty`, `after-matches-goal`, `no-scope-creep`) 모두 `answer`(bool)와 `note` 보유. rule-engine이 만든 note(증거 경로 문자열)가 "의미 있는 설명"이 아니라고 모델이 정확히 판단함 | O |
| run-log 기록 | `review.tier1_completed` 로그 | `model=qwen3:14b`, `durationMs=2563`, `checkCount=3`, `attempts=3`(체크당 1회, 재시도 없음), `role=runtime`, `cost.estimatedUSD=0` | O |
| think 블록 유출 | findings의 `note`/`comment` 필드 육안 확인 | `<think>` 태그 없음. `think:false`만으로 이미 깨끗한 JSON 응답이었고, 응답 파서의 `<think>` 제거 방어는 이번 실측에서는 발동하지 않음(옵션이 먹은 버전이므로 정상) | O |
| 원복 확인 | `__verificationTemp` 제거 후 재측정 | `koPoliteEndings=0/0`, `deviationCheck=passed`, `changeReview=not_started`, 이전 proposal `lifecycle=superseded`로 전이. 결재 대기 proposal 없음, 승인 조치 없음 | O |
| 성능 실측 | 3개 체크리스트 항목, 순차 호출 | 총 2563ms → 체크당 평균 약 854ms (14B 모델, Q4_K_M 양자화, 로컬 CPU/GPU 기준) | 참고용 |

**결론**: 이전 보류 사유(localhost IPv6 해석 문제로 추정)는 endpoint를 `127.0.0.1`로 바꾸는 것으로 해소됐다. `qwen3:14b`는 thinking 모델이지만 `think:false` 필드만으로 `<think>` 블록 없는 깨끗한 JSON을 반환했다. 방어적으로 추가한 파서의 `<think>` 제거 로직은 안전망으로 유지하되, 이번 실측에서는 발동하지 않았다.

## 환경 확인

| 항목 | 명령 | 결과 | 판정 |
| --- | --- | --- | --- |
| Ollama 설치 | `ollama --version` | `ollama version is 0.31.1` | O |
| Ollama 모델 목록 | `ollama list` | `qwen3:14b` 1개 확인. definition의 model을 `qwen3:14b`로 맞춰 해소(아래 실연동 재검증 참조). `llama3.1:8b`(fallback)는 미설치 — `setup-ollama.ps1` 실행 시 자동 pull됨. | O(모델 불일치는 해소, fallback 미설치는 별도) |
| Ollama API (localhost) | `GET http://localhost:11434/api/tags` | 타임아웃. `ollama serve` 재시작 후에도 동일했다(원인: Windows에서 `localhost`가 IPv6로 해석되는 문제로 추정). | X |
| Ollama API (127.0.0.1) | `GET http://127.0.0.1:11434/api/tags` | 즉시 정상 응답. endpoint를 `127.0.0.1`로 바꿔 해소함(아래 실연동 재검증 참조). | O |

## 서버 검증

| 단계 | 명령 | 응답 요약 | 판정 |
| --- | --- | --- | --- |
| 서버 기동 | `dotnet run --project server --no-build` | 검증 서버가 `http://localhost:5184`에서 응답했다. `GET /api/projects/dev-pack/state`는 `200`이었다. | O |
| Ollama 부재 강등 | dev-pack definition의 `timeoutSeconds`를 임시로 `2`로 낮추고, `ko.json`에 `"검증용 임시 문장이에요."`를 추가한 뒤 `POST /api/projects/dev-pack/actions/measure` | `koPoliteEndings=1`, evidence `dashboard/data/lang/ko.json:179`. `deviationCheck=warning`, `changeReview=pending_review`, proposal `proposal-1783563851352` 생성. `review.tier1_completed` 로그의 `reasonCode=system.reviewer_unavailable`, `verdict=uncertain`, `durationMs=4036`, `role=runtime`. review-report는 생성되지 않았다. | O |
| AI report 생성 경로 | Ollama API 형태의 stub을 `http://127.0.0.1:11555`에 띄우고 definition endpoint를 임시 변경한 뒤 `POST /api/projects/dev-pack/actions/measure` | AI report 1개 생성. `reviewer={ type: ai, provider: ollama, model: qwen2.5:14b-instruct }`, `verdict=approved`, findings 3개: `note-nonempty`, `after-matches-goal`, `no-scope-creep`. 상태는 `changeReview=pending_review`로 유지됐다. | O |
| 실제 Ollama 기동 상태 report | 실제 `localhost:11434` API가 계속 타임아웃이라 수행하지 못했다. | 보류 해소됨 — endpoint를 `127.0.0.1:11434`로, model을 `qwen3:14b`로 바꾼 뒤 재검증 완료. 상세는 상단 "실연동 재검증" 절 참조. | O |

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

검증 중 생성된 proposal은 어느 라운드에서도 승인하지 않았다. 1차 검증에서는 실제 Ollama API가 타임아웃인 상태에서 `system.reviewer_unavailable`로 사람 검토에 강등되는 것을 확인했다. 2차 실연동 재검증에서는 `qwen3:14b`가 실제로 응답해 `needs_changes` report를 만들었고, 위반을 원복한 뒤 재측정하여 해당 proposal은 `lifecycle=superseded`로 자연히 정리됐다(사람이 승인/거절 조치를 하지 않아도 되는 경로). 검증용 임시 데이터 변경은 모두 원복했다.
