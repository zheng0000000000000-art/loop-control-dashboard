# 로컬 실행자 연결(Ollama 1층 제안 생성) 실행 검증

검증일: 2026-07-09

배경: `POST actions/measure`가 blueprint 괴리를 감지하면 이제 rule-engine 대신 Ollama 실행자(`OllamaExecutor.cs`, qwen3:8b)가 먼저 proposal의 note·title·summary를 생성한다. 검토는 기존과 동일하게 qwen3:14b(`OllamaReviewer.cs`)가 맡는다. 실행자 실패 시 rule-engine으로 강등하고, 1층 검토가 needs_changes면 검토 findings를 실행자 입력에 포함해 1회 재생성한다.

## 환경 확인

| 항목 | 명령 | 결과 | 판정 |
| --- | --- | --- | --- |
| Ollama API | `curl http://127.0.0.1:11434/api/tags` | 즉시 정상 응답 | O |
| 검토 모델 | `ollama list` | `qwen3:14b` 설치됨(기존 검증에서 확보) | O |
| 실행자 모델 준비 | `scripts/setup-ollama.ps1` 실행 | 1차 실행: 리뷰어 fallback으로 `llama3.1:8b`를 pull한 뒤, 실행자 모델도 "설치된 8b급 재사용" 규칙에 따라 `llama3.1:8b`를 재사용함. 아래 "실행자 모델 선택 보정" 참조 | 보정 후 O |

## 실행자 모델 선택 보정 (스크립트 개선)

최초 `Select-ExecutorModel`은 "검토 모델이 아닌 설치된 8b급 아무거나, 없으면 qwen3:8b pull" 규칙으로 구현했다. 실측 결과, 이미 설치돼 있던 `llama3.1:8b`(리뷰어 fallback용으로 방금 pull됨)를 재사용해 실행자 모델로 선택했다.

**문제 발견**: `llama3.1:8b`에 note 생성 프롬프트를 실제로 호출한 결과, 지정한 JSON 스키마(`{"metricId":..., "note":...}`)를 따르지 않고 `{"metrics":[{"metricId":..., "note":""}], "metaData":{...}}` 형태의 다른 구조를 반환했다(note도 빈 문자열). 같은 프롬프트를 `qwen3:14b`에 넣으면 스키마를 정확히 따랐다. 계열이 다른 8b 모델은 JSON 스키마 준수가 불안정하다는 것을 실측으로 확인했다.

**조치**: `Select-ExecutorModel`을 "검토 모델과 같은 qwen3 계열의 설치된 8b급, 없으면 `qwen3:8b` pull"로 수정하고, `qwen3:8b`(5.2GB)를 pull했다. 스크립트를 재실행해 두 definition의 `executorPolicy.tier1.model`이 `qwen3:8b`로 갱신됨을 확인했다.

**qwen3:8b 자체의 신뢰성 실측**: 같은 note 프롬프트를 3회 반복 호출한 결과 2/3회는 유니코드 대체 문자(`�`)가 섞인 깨진 응답을, 1/3회는 정상적인 한국어 문장을 반환했다. 8B 양자화 모델의 생성 안정성 한계로 판단해, `OllamaExecutor.cs`의 응답 파서에 대체 문자 포함 여부를 확인하는 방어 로직(`HasReplacementChar`)을 추가했다 — 깨진 응답은 파싱 실패로 처리되어 `maxRetries`(2회) 안에서 재시도되고, 소진 시 rule-engine으로 강등된다.

**부수 발견**: `Set-Content -Encoding utf8`(Windows PowerShell 5.1)이 대상 JSON 파일에 UTF-8 BOM을 추가하는 것을 발견했다(Node.js `JSON.parse`가 BOM에서 실패). `setup-ollama.ps1`의 파일 쓰기를 `[System.IO.File]::WriteAllText(..., UTF8Encoding($false))`로 바꿔 BOM 없이 쓰도록 고쳤고, 이미 BOM이 붙은 두 definition 파일도 정리했다.

## 실행자 성공 경로 실측 (qwen3:8b 생성 → qwen3:14b 검토 → 재생성 1회)

`dashboard/data/lang/ko.json`에 `"__verificationTemp": "검증용 임시 문장이에요."`를 추가해 `koPoliteEndings` 위반을 주입하고 `POST /api/projects/dev-pack/actions/measure`를 호출했다.

| 단계 | proposal id | createdBy | note 내용 | 1층 검토 verdict | note-nonempty |
| --- | --- | --- | --- | --- | --- |
| 1차 생성 | proposal-1783567253967 | `{provider: ollama, model: qwen3:8b}` | "존댓말 사용 여부를 정확히 반영하기 위해 마크를 추가해야 한다고 명시했으나, after 값이 0으로 감소해 일치하지 않아 수정이 필요하다." | needs_changes | **true (통과)** |
| 2차 생성(재생성) | proposal-1783567266096 | `{provider: ollama, model: qwen3:8b}` | 동일 취지의 서술형 문장(재생성 후에도 방향 오류 유지) | needs_changes | **true (통과)** |

**핵심 판정**: 두 라운드 모두 `note-nonempty`가 **통과**했다 — 지난 검증(rule-engine이 evidence 경로 문자열만 넣었을 때)에서 검토자가 지적했던 결함이 실행자 도입으로 해소됐다. 다만 `after-matches-goal`은 두 라운드 모두 실패했다: qwen3:8b가 "요/습니다 표시를 **추가**해야 한다"는 취지로 서술했지만 실제로는 위반을 **제거**(before=1 → after=0)하는 방향이라, 모델이 지표 방향을 반대로 이해했다. `no-scope-creep`은 두 라운드 모두 통과. 최종 verdict는 `needs_changes`로 사람 검토에 도달했다(정상 — 1층은 통과 게이트가 아니라 사람 결재 전 필터).

재생성은 definition의 `maxRegenerations: 1` 설정대로 정확히 1회만 일어났고, 2차도 needs_changes이므로 더 재생성하지 않고 사람 검토(`changeReview: pending_review`)로 넘어갔다 — "무한 루프 금지" 요구 충족.

### run-log 이벤트 순서 (실측)

```
stage.warning
proposal.generated   { provider: ollama, model: qwen3:8b, durationMs: 1358, fallback: false }
proposal.created     { proposalId: proposal-1783567253967 }
review.tier1_completed { verdict: needs_changes, model: qwen3:14b, durationMs: 7287 }
proposal.superseded  { proposalId: proposal-1783567253967, reasonCode: review.needs_changes_regenerate }
proposal.generated   { provider: ollama, model: qwen3:8b, durationMs: 4837, fallback: false }
proposal.created     { proposalId: proposal-1783567266096 }
review.tier1_completed { verdict: needs_changes, model: qwen3:14b, durationMs: 7023 }
```

### 성능 실측 (AI 구간 총 소요)

| 구간 | 1라운드 | 2라운드(재생성) |
| --- | --- | --- |
| 실행자 생성(qwen3:8b) | 1358ms | 4837ms |
| 검토(qwen3:14b, 체크 3개) | 7287ms | 7023ms |
| 라운드 합계 | 8645ms | 11860ms |
| 전체(생성+검토+재생성+재검토) | 20505ms | |

`<think>` 블록은 두 모델 모두 응답에 유출되지 않았다(`think:false` 적용).

## Ollama 부재/실행자 실패 시 rule-engine 폴백 실측

실제 Ollama 서비스(`ollama app.exe`가 `ollama.exe`를 자동 재기동하는 트레이 앱)를 완전히 내리는 대신, dev-pack definition의 `executorPolicy.tier1.endpoint`를 일시적으로 `http://127.0.0.1:19999`(리스너 없음), `timeoutSeconds`를 `3`으로 낮춰 실행자만 연결 불가 상태로 재현했다(검토자 endpoint는 실제 `127.0.0.1:11434`로 유지 — Ollama 완전 다운 시 검토자 강등 경로는 직전 검증(`docs/verification/ollama-tier1.md`)에서 이미 확인됨).

동일한 위반이 남아 있는 상태에서 재측정한 결과:

| 항목 | 결과 |
| --- | --- |
| `proposal.generated` 로그 | `{provider: rule-engine, model: null, durationMs: 2040, fallback: true, reasonCode: system.executor_degraded, text: "koPoliteEndings note 생성 실패"}` |
| 생성된 proposal의 note | `"dashboard/data/lang/ko.json:3"` (rule-engine의 evidence 경로 — 기존 폴백 동작과 동일) |
| createdBy | `{provider: rule-engine, model: null}` — 정직하게 실제 생성 주체 기록 |
| 재생성 1회 | 실행자가 여전히 unreachable이라 다시 강등, 동일 결과로 needs_changes 유지 |
| 최종 상태 | `changeReview: pending_review`, 루프 정지 없음, 가드레일 미발동 |

**핵심 판정**: 실행자가 unreachable이어도 `proposal.generated`(fallback=true) → `proposal.created` → 검토 → 재생성 시도(재차 강등) → 사람 검토 경로까지 전체 흐름이 끊기지 않고 완주했다.

## 원복 확인

1. dev-pack definition의 `executorPolicy.tier1.endpoint`/`timeoutSeconds`를 원래 값(`http://127.0.0.1:11434`, `90`)으로 되돌림.
2. `ko.json`의 `__verificationTemp` 제거.
3. 재측정: `koPoliteEndings = 0/0`, `deviationCheck: passed`, `changeReview: not_started`.
4. 검증 중 생성된 모든 proposal은 최종적으로 `lifecycle: superseded`로 자연 정리됐다 — 어느 라운드에서도 사람이 승인/거절 조치를 하지 않았다.

## 불변 확인

| 항목 | 명령 | 결과 | 판정 |
| --- | --- | --- | --- |
| Core 격리 | `rg -n "ollama\|reviewChecklist\|note-nonempty\|after-matches-goal\|no-scope-creep\|OllamaExecutor" server/Engine.cs server/Storage.cs server/Guardrails.cs` | 결과 없음 | O |
| 빌드 | `dotnet build server/LocalFirstWorkflowDashboard.Server.csproj` | 경고 0, 오류 0 | O |
| 프런트 문법 | `node --check dashboard/app.js` | 오류 없음 | O |
| definition/lang JSON 유효성 | `node -e "JSON.parse(...)"` (4개 파일) | 모두 유효(BOM 제거 후) | O |
| 신원 분리 | 성공 경로 createdBy=`{ollama, qwen3:8b}` vs 검토자=`{ollama, qwen3:14b}` — `HasIdentityConflict`가 다른 model로 판정해 검토 정상 진행 | 실측 확인 | O |

## 결론

- 위반 시 proposal이 실행자(qwen3:8b, ollama)로 생성되고, 검토자는 별도 모델(qwen3:14b)로 신원이 분리된다 — 실증됨.
- 실행자가 만든 note는 서술형 한국어 문장으로, `note-nonempty` 검토를 통과한다 — rule-engine 대비 개선 실증됨. 다만 `after-matches-goal`(방향 일치)은 8B 모델의 이해 오류로 이번 실측에서는 통과하지 못했고, 이는 1층 검토가 정상적으로 걸러내 사람 결재로 넘겼다.
- needs_changes → 자동 1회 재생성(revisionOf 연결) → 재검토 경로가 정확히 1회로 제한되어 동작한다.
- Ollama/실행자 unreachable 시 rule-engine으로 정직하게 강등되며 루프는 멈추지 않는다.
- 코어 3파일은 청결하게 유지됐다.
