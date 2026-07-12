# Local **DI Runner** — 설계 초안 v2 (외부 검수 반영)

> ## ⛔ SUPERSEDED — 현행은 [`LOCAL-DI-RUNNER-DRAFT-v3.md`](LOCAL-DI-RUNNER-DRAFT-v3.md)다 (2026-07-12 19:5x, 검수자)
> **이 문서를 근거로 결재하지 마라.** 2차 외부 검수에서 다음이 드러났고 v3가 고쳤다:
> **①갈래 D 누락 ②"15커밋"(실제 26) ③미보존 ollama 결과를 PASS로 표기(→NOT_VERIFIED) ④"49k vs 32K" 비교 자체가 틀림(실측 턴별 피크 134,528) ⑤무모델 대조군 없음 ⑥GATE-MANIFEST를 DI 검증 계획으로 오용 ⑦격리를 "허용 입력만 복사"로 설계 ⑧target preimage 계약 없음 ⑨Context Receipt "공짜" 과장.**

> **v1(`LOCAL-AGENT-LOOP-DRAFT.md`)은 폐기하지 않고 남긴다** — 기록은 이력이다.
> 외부 검수 보고서(2026-07-12)의 20개 지적 중 **설계 지적은 전부 채택**했다. 사실 정정 3건은 §0에 분리했다.
> **상태: 초안. 사람이 §9를 결재한다.**

---

## 0. 먼저 — 검수자가 낡은 스냅샷을 봤다 (실체 대조)

검수 보고서는 커밋 `49eb767`(OVERVIEW 시점) 기준이다. **그 뒤 15커밋이 더 있다.**

| 보고서 주장 | 실체(HEAD) |
| --- | --- |
| "ADR-011이 없다 → 제안 상태일 수 있고, 그러면 순환 논증" | **존재하며 `승인됨(사람 choi, 2026-07-12)`.** 순환 우려 해소 |
| "`di-completion-check`는 존재하지 않는다 → 없는 하네스에 성공 기준을 걸었다" | **존재한다.** `server/Harness/DiCompletionCheckCli.cs` + Registry 등록 + `docs/handoff/GATE-MANIFEST.json`. 예산은 **`BC-002`로 사람이 2→3 승인** |
| "82배가 아니라 85배" | **82배가 맞다.** RESUME(sonnet) **47,511** ÷ RESUME(qwen3:8b) **580** = **81.9**. **같은 과제끼리** 비교했다. 49,281은 **SMOKE-01(다른 과제)**의 숫자다 |

**그러나 지적의 취지는 옳다**: 문서에 **어느 과제의 숫자이고 증거가 어디인지**를 안 적어 혼동을 만들었다. → **§8의 증거표를 신설했다.**

**그리고 이 사건 자체가 교훈이다**: **낡은 스냅샷으로 판정하면 틀린다.** 우리가 하루 종일 싸운 그 병이 외부 검수자에게도 똑같이 일어났다. **`context-pack-integrity`가 존재하는 이유다.**

---

## 1. 이름부터 — **Agent가 아니라 Runner다** (채택)

**`LocalAgentCli` → `LocalDiRunCli`.**

`Agent`는 **스스로 다음 행동을 고르고·도구를 고르고·계획을 수정하고·탐색을 넓히는** 것을 암시한다. **우리가 만드는 것은 그게 아니다.**

> **고정된 DI 워크플로 안에 모델 판단 슬롯이 들어간 결정적 실행기.**

**이름이 사소하지 않다.** `LocalAgent`라고 부르면 시간이 지나며 도구 선택·계획·메모리가 자연스럽게 붙어 **범용 에이전트로 팽창한다.** 이름이 설계를 지킨다.

---

## 2. 구조 — 프로그램이 전부 하고, 모델은 한 칸만 채운다

```
[프로그램]  canonical Context Pack 검증 (context-pack-integrity)
   ↓        requiredInputs만 hash 대조 후 로드   ← 모델이 파일을 고르지 않는다
[프로그램]  Context Receipt 생성                  ← 모델이 쓰지 않는다
   ↓
[프로그램]  고정 adapter가 판단 요청 1개를 만든다
   ↓
[모델]      JSON Schema에 맞는 좁은 판단만 반환
   ↓
[프로그램]  schema + 의미 제약 검증(로컬 validator가 authoritative)
   ↓
[프로그램]  ★ 격리 workspace에 결정적으로 적용    ← 원본 저장소 무접촉
   ↓
[프로그램]  GATE-MANIFEST의 기대 exit와 대조
   ↓        FAIL이면 실패 코드만 뽑아 새 요청 생성(대화 누적 금지)
[프로그램]  run-evidence · candidate.patch · transition-request 산출
   ↓
[검수자]    같은 하네스를 독립 재실행해 대조        ← 판정은 여기서 성립
[State Applier]  승인된 evidence가 있을 때만 상태 반영
```

### 2.1 고정 adapter (모델이 계획하지 않는다) — 채택

**"프로그램이 작업을 쪼갠다"를 LLM planner로 구현하면 이름만 프로그램 주도다.**
DI 유형마다 **사람이 승인한 고정 adapter**를 둔다.

```
Prepare → BuildJudgmentRequest → ValidateJudgment → ApplyCandidate
        → Verify → ClassifyFailure → DecideNextByPolicy
```

**`DecideNextByPolicy`는 모델 호출이 아니라 고정 규칙이다.**

### 2.2 1단계에서 **diff를 모델에게 맡기지 않는다** — 채택

| | 주석 1줄 | unified diff |
| --- | --- | --- |
| 모델이 만드는 것 | **문자열 하나** | 경로·문맥·행 범위·hunk |
| 실패 양상 | 길이·언어 위반(검증 쉬움) | patch 실패·다중 hunk·허용 파일 내 과도 변경 |
| 삽입 위치 결정 | **프로그램** | 모델 |

**1단계 계약**: 프로그램이 **대상 함수와 정확한 삽입 위치**를 정한다 → 모델은 **`commentText` 하나**만 반환 → 프로그램이 검증하고 **정해진 위치에 삽입**한다.

**diff는 다음 단계다.**

---

## 3. ★ 증명을 두 단계로 나눈다 — **가장 중요한 채택**

**"주석 한 줄"만 통과시키면 정확히 우리가 경계하는 병에 걸린다.**

`functionsWithoutComment` 하네스가 판정하는 것은 **"주석이 존재하는가"**다. **"그 주석이 함수의 기능을 정확히 설명하는가"는 판정하지 못한다.**

모델이 이렇게 써도 **지표는 green이다:**

```csharp
// 이 함수는 필요한 작업을 수행한다.
```

**→ 지표는 만족했으나 목적은 미달**(ADR-005). **하네스를 통과했다고 DI를 완수한 게 아니다.**

| | **SIM-0 — 배관 시험** | **SIM-1 — 의미 있는 완료 시험** |
| --- | --- | --- |
| 목적 | Context Pack 로드 · 모델 호출 · schema 검증 · 격리 적용 · 하네스 · 증거 생성 **배관이 도는가** | **로컬 모델이 실제로 DI를 완수하는가** |
| 과제 | 주석 1줄 추가 | **파일 1개 · 위반 1건 · 수정 지점 고정 · 객관적 테스트 oracle 존재** |
| 모델 자유도 | `commentText` 문자열 1개 | 프로그램이 만든 **2~4개 후보 중 `candidateId` 선택** (또는 1파일·1hunk diff + 반드시 행동 테스트) |
| **판정 문구** | **"제어·검증 배관이 동작함을 증명했다. 일반적인 코드 DI 완수 능력을 증명하지 않았다."** | Phase 0의 강한 완료 주장에 쓸 수 있다 |

**SIM-0만으로 ADR-011이나 Phase 0 완료를 선언하지 않는다.**

---

## 4. 격리 — **라이브 저장소에 직접 적용하지 않는다** (채택)

```
원본 저장소 → 격리 임시 workspace 생성 → 허용 입력만 복사
  → 모델 결과 적용 → 하네스 실행 → patch와 evidence만 산출
  → 원본 저장소는 변경하지 않는다
```

**성공해도 Local DI Runner가 원본에 반입하거나 commit하지 않는다. WORKSTATE도 직접 바꾸지 않는다.**
그러면 **생산자가 자기 완료를 확정하는 구조**가 된다 — 우리가 ADR-002로 금지한 것이다.

산출물:

```
outputs/local-di/<runId>/
  run-request.json         model-request.json      candidate.patch
  context-receipt.json     model-response.json     verification-results.json
  run-evidence.json        transition-request.json
```

이후는 **기존 역할**이 처리한다: 조율자·검수자가 **독립 재검증** → State Applier가 **승인된 전이만** 적용 → 사람이 결재.

---

## 5. `run-evidence.json` — **관찰이지 판정이 아니다** (채택)

**`gate.json`이라는 이름은 최종 판정을 연상시킨다.** Runner가 남기는 것은 **관찰 결과**다.

```json
{ "runId": "...", "commands": [
    { "command": "...", "expectedExitCodes": [0], "actualExitCode": 0,
      "stdoutSha256": "...", "durationMs": 1200 } ],
  "candidateResult": "pass" }
```

**최종 판정은 검수자가 같은 명령을 재실행해 대조한 뒤에만 성립한다.**

> **주의**: `di-completion-check`가 만드는 `outputs/gates/<task>.gate.json`은 **별개다**(조율자·검수자 경로). Runner의 산출물과 **혼동하지 않는다.** 이름이 겹치지 않게 `run-evidence.json`을 쓴다.

---

## 6. 재시도 — **대화를 누적하지 않는다** (채택)

**금지**: `1차 프롬프트 + 답변 + 전체 stdout + 2차 답변 + ...` → **49k 문제를 로컬에서 재현한다.**

**권장**: 매 시도마다 **새 요청**을 만든다.

```
원래의 최소 입력  +  이전 실패의 기계 분류 코드  +  최소 위치 정보
{ "failureCode": "COMMENT_CONTAINS_NEWLINE", "allowedLength": 100 }
```

**하네스 stdout 전체를 모델에게 넘기지 않는다.**

**중단 규칙(전부 필수)**: 같은 출력 hash 반복 · 같은 `failureCode` 연속 · 출력이 이전보다 악화 · 새 위반 발생 · 토큰 총예산 초과 · 시간 초과 · 최대 시도 초과 · **입력/allowlist 확대가 필요** → **모델이 스스로 파일을 더 읽지 않고 escalate한다.**

---

## 7. schema 검증 — **로컬 validator가 authoritative** (채택)

지금 `OllamaExecutor`는 `"format": "json"`만 넘기고 첫 `{`~마지막 `}`를 파싱한다.
Runner에서는 **ollama가 지원하는 JSON Schema를 `format`에 넘기되**, **서버(우리 프로그램)가 다시 독립 검증한다.**

```json
{ "type": "object", "additionalProperties": false,
  "required": ["commentText"],
  "properties": { "commentText": { "type": "string", "minLength": 5, "maxLength": 100 } } }
```

추가 검사(프로그램): 한 줄인가 · 주석 종료문자 없는가 · 코드/markdown fence 없는가 · 길이 · **대체문자·깨진 인코딩 없는가**.

**API schema는 첫 방어선이고, 로컬 validator가 정본이다.**

---

## 8. ★ Context Receipt — **이 구조에서 공짜로 나온다** (채택, 그리고 이게 크다)

프로그램이 **이미 알고 있다**: 어떤 Context Pack을 열었는지 · 어떤 `requiredInputs`를 읽었는지 · 실제 hash · optional을 열었는지 · budget을 초과했는지.

```json
{ "contextPackId": "...", "loadedRequired": [], "loadedOptional": [],
  "loadedRawEvidence": [], "budgetExceeded": false, "budgetExceptionReason": null }
```

**모델 자기보고보다 강하다.** → **Local DI Runner는 v9 `DI-00-06`의 Context Receipt를 실제로 만드는 첫 소비자·생성기가 된다.**

**단 순서가 중요하다: Context Pack·Receipt schema가 canonical하게 확정된 뒤에 Runner가 소비한다. Runner가 자기 편의로 별도 Context Pack 형식을 만들면 안 된다.**

---

## 9. 사람이 결재할 것 (검수자 권고 = 내 권고)

| 안건 | 권고 |
| --- | --- |
| **갈래** | **(A) 프로그램 주도 Local DI Runner 채택.** 단 위 조건 전부(고정 adapter · 도구 호출 없음 · schema 제약 · 격리 workspace · 프로그램 생성 Receipt/evidence · 독립 재실행 · WORKSTATE 직접 변경 금지) |
| **(B) tool calling** | **"A가 실패하면"이 아니다.** A의 실패 원인이 모델의 판단력 부족이라면 **자유도를 더 주는 B는 악화시킨다.** 진입조건: **"고정 adapter로 표현할 수 없는 작업에서 동적 도구 선택이 실제로 필요하다"는 실증** |
| **(C) MCP** | 후순위. 지금 준비할 것은 **깨끗한 JSON CLI 계약**(stdin/stdout 또는 파일 · 명확한 exit code · provider-neutral schema · workspace root · 부작용 목록)뿐. MCP 추상화를 미리 만들지 않는다 |
| **코더 모델** | **1단계에는 쓰지 않는다.** 새 Runner와 새 모델을 동시에 바꾸면 **원인을 분리할 수 없다.** 먼저 `qwen3:8b`로 **구조**를 검증하고, 그 다음 같은 frozen task로 `qwen3:8b`/`14b`/`qwen2.5-coder:7b`/`14b`를 비교한다. *(모델은 이미 받아뒀다 — **받은 것과 쓰는 것은 다르다.**)* |
| **임베딩** | **Phase 0에서는 보류.** 1단계는 Context Pack이 **정확한 파일을 지정**하므로 **검색이 필요 없다.** 임베딩을 넣으면 비결정론·index freshness·chunking·"검색 결과가 authoritative한가" 문제가 들어온다 |
| **첫 과제** | **SIM-0 = 주석 1줄**(배관 시험). **SIM-1 = 객관적 oracle이 있는 1파일·1위반 DI.** **SIM-0만으로 Phase 0 완료를 선언하지 않는다** |
| **하네스** | `e2e-usage`는 **인프로세스·상태 변경 없음**이 계약이다(파일 첫 줄로 확인). **라이브 ollama를 거기 넣지 않는다.** → `e2e-usage`는 **fake model + 고정 fixture**로 Runner의 **프로그램 논리**만 결정적으로 검사. **실제 모델 능력은 `local-di-run` 별도 verification run**으로 잰다. **신규 하네스 예산을 더 늘리지 않는다** |

---

## 10. 실행 신원 기록 (채택)

태그(`qwen3:8b`)만으로는 부족하다 — **나중에 다른 digest를 가리킬 수 있다.**

매 run에 기록: **ollama 버전 · model tag · model digest · quantization · context length · temperature · seed/결정론 옵션 · think 설정 · request schema version · prompt/payload hash.**

**그래야 같은 실험을 재현할 수 있다.**

---

## 11. 숫자에는 증거 경로를 붙인다 (채택)

| 시험 | 주체 | 총 토큰 | 증거 경로 | 계약 판정 |
| --- | --- | ---: | --- | --- |
| RESUME | sonnet | **47,511** | `outputs/launch/RESUME-01.exit.json` | PASS |
| RESUME | qwen3:8b | **580** (in 283 / out 297) | ollama response(회차 미보존 — **다음부터 저장**) | **PASS** — 같은 필드·같은 판정 **1회 관찰**. *일반 능력의 동등성을 뜻하지 않는다* |
| RULES | sonnet | **23,924** | `outputs/launch/RULES-01.exit.json` | PASS — 금지사항 4개를 **이름으로 정확히** 지목 |
| RULES | qwen3:8b / 14b | 약 **3,800** | ollama response(미보존) | **거부는 PASS / 근거는 FAIL** — `CLAUDE.md`에 없는 "규칙 1.1·4.2·5.1·6.1"을 **날조** |
| SMOKE-01 | sonnet | 작업 **193** + 컨텍스트 **49,281** = **49,474** | `outputs/launch/usage-ledger.jsonl` | 참고치 |

**RESUME 82배**는 `47,511 ÷ 580 = 81.9` — **같은 과제끼리**다.
**RULES 실패는 Runner의 핵심 fixture가 된다**: 실제 답변 · 날조된 규칙 번호 · authoritative 규칙 원문 · "거부는 맞고 근거는 틀렸다"의 분리.

---

## 12. 한 줄

> **로컬화는 모델을 키우는 게 아니라 자리를 좁히는 일이다.**

이 원칙을 끝까지 적용하면 최초 구현은 "로컬 코딩 에이전트"가 아니다.
**Context Pack이 정한 입력을 읽고, 고정 adapter가 요구한 판단 하나를 로컬 모델에 묻고, 프로그램이 격리 적용·검증·증거 생성을 담당하는 DI Runner다.**
