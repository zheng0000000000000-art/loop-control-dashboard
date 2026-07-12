# Local **DI Runner** — 설계 초안 v3 (2차 외부 검수 반영)

> **현행은 v3다.** v1(`LOCAL-AGENT-LOOP-DRAFT.md`)·v2(`LOCAL-DI-RUNNER-DRAFT-v2.md`)는 이력으로 남긴다.
> v3는 **2차 외부 검수(2026-07-12)의 지적 7건 + 사실 정정 2건 + 거버넌스 충돌 1건**을 반영했다.
> **검수자가 전부 실체로 대조한 뒤 반영했다** — 외부 검수도 모델 출력이고, **모델의 설명은 증거가 아니다**(§0-B).
> **상태: 초안. 사람이 §9를 결재한다.** 목표 구조 **(A) 프로그램 주도 Runner**는 유지된다.

---

## 0-A. v2에서 고친 사실 오류 (실측)

| v2의 서술 | 실측 | 근거 |
| --- | --- | --- |
| "그 뒤 **15커밋**이 더 있다" | **26커밋** | `git rev-list --count 49eb767..223c42b` = 26 |
| RESUME qwen3:8b **PASS** | **`REPORTED_PASS / NOT_VERIFIED`** | ollama 원본 응답을 **보존하지 않았다**. 원본이 없으면 독립 검증이 불가능하다 — 이 저장소의 원칙대로면 PASS라고 쓸 수 없다 |
| RULES qwen3:8b/14b **거부 PASS / 근거 FAIL** | **`REPORTED_RESULT / NOT_VERIFIED`** (관찰 내용은 유지) | 같은 이유. **다음 회차부터 `outputs/local-di/<runId>/model-response.json`에 원본을 저장한다** |
| "**82배**" | **계산은 맞다**(47,511 ÷ 580 = 81.9) | 다만 이것은 **전체 workflow 토큰비**다. Claude Code 쪽은 시스템 지시·도구 정의·캐시 컨텍스트를 포함하고 ollama 직접 호출 쪽은 입력 283토큰이다. **모델 자체 효율이 82배라는 뜻이 아니다** — 문구에 이 단서를 붙인다 |

## 0-B. ★ 컨텍스트 창 — v2의 전제도, 외부 검수의 예상도 틀렸다 (검수자 실측)

**v2/인수인계의 전제**: "`qwen2.5-coder` 32K vs 우리 실행자 **49k** → −35% 감축이 필수다."
**외부 검수의 지적**: 49,281은 한 시점의 컨텍스트가 아니라 `cacheCreation + cacheRead`의 **여러 턴 누적 과금액**이다. 32K 창과 직접 비교할 수 없다. **→ 이 지적은 옳다.**

**그래서 검수자가 옳은 값을 쟀다.** `stream-json`의 턴별 `input_tokens + cache_read_input_tokens + cache_creation_input_tokens`:

| 과제 | assistant 턴 | 최소 | **최대(피크)** | 32K 초과 턴 | 증거 |
| --- | ---: | ---: | ---: | ---: | --- |
| **STATE-01**(실제 DI) | 131 | 26,480 | **134,528** | **124 / 131** | `outputs/sonnet-STATE-01.out.jsonl` |
| RESUME-01(L0 1파일 읽기) | 3 | 23,375 | 23,814 | 0 | `outputs/sonnet-RESUME-01.out.jsonl` |

**결론이 완화된 게 아니라 악화됐다.**

- **실제 DI 한 건의 피크 컨텍스트는 134K다.** 32K 모델은 초반 턴에서 터진다. **64K로도 안 된다.**
- 이건 모델 탓이 아니라 **Claude Code 루프의 구조**다 — 대화·도구 출력이 **턴마다 누적**된다.
- **그래서 `CLAUDE.md` −35% 다이어트로는 근처도 못 간다**(134K → 32K는 −76%가 필요하다). **다이어트는 유효하지만 컨텍스트 문제의 해법이 아니다.**
- **역으로 이것이 (A) Runner의 존재 이유를 숫자로 증명한다.** Runner의 호출당 컨텍스트는 누적 트랜스크립트가 아니라 **Context Pack 크기로 상한이 잡힌다**(§6 대화 누적 금지).

**`LAUNCH-BUDGET.json`은 두 예산을 분리한다** — 하나로 묶으면 위 혼동이 재발한다.

```json
{
  "cumulativeBilledTokenBudget": 0,
  "maxContextTokensPerTurn": 0,
  "contextLimit": 32768,
  "contextTokensByTurn": [],
  "firstLimitExceededAtTurn": null
}
```

---

## 0-C. 거버넌스 충돌 — `ADR-010`이 승인 대기인데 `ADR-011`이 완료로 인용한다

- `ADR-010` 헤더: **`상태: 사람 승인 대기`**
- `ADR-011:37`: "수신 증명: source hash == replay hash (**`ADR-010` ✅ 완료**)"

**구현은 되었고 검증 기록도 있지만, 정책은 승인되지 않았다.** 이 상태를 두면 다음 세션이 "코드는 있는데 이걸 기준으로 삼아도 되나?"를 **다시 추론**한다.
**→ `HUMAN-INBOX`에 등재했다. 사람이 ①ADR-010을 승인하거나 ②ADR-011의 문구를 "구현 완료·정책 미승인"으로 분리한다.** 대행하지 않는다.

---

## 1. 이름 — **Agent가 아니라 Runner다** (v2 유지)

**`LocalAgentCli` → `LocalDiRunCli`.** 고정된 DI 워크플로 안에 **모델 판단 슬롯**이 들어간 결정적 실행기다. 이름이 설계를 지킨다.

---

## 2. 구조 — 프로그램이 전부 하고, 모델은 한 칸만 채운다

```
[프로그램]  고정 commit에서 격리 worktree 생성 (§4)      ← dirty source 거부
[프로그램]  canonical Context Pack 검증 (context-pack-integrity)
   ↓        requiredInputs만 hash 대조 후 로드            ← 모델이 파일을 고르지 않는다
[프로그램]  writeTargets의 preimage 대조 (§4.2)          ← STALE_TARGET이면 abort
[프로그램]  Context Receipt 생성 (§8)                     ← 모델이 쓰지 않는다
   ↓
[프로그램]  고정 adapter가 판단 요청 1개를 만든다
   ↓
[모델]      JSON Schema에 맞는 좁은 판단만 반환
   ↓
[프로그램]  schema + 의미 제약 검증(로컬 validator가 authoritative, §7)
   ↓
[프로그램]  격리 worktree에 결정적으로 적용               ← 원본 저장소 무접촉
   ↓
[프로그램]  ★ DI-local verification plan의 기대 exit와 대조 (§4.3)
   ↓        FAIL이면 실패 코드만 뽑아 새 요청 생성(대화 누적 금지, §6)
[프로그램]  run-evidence · candidate.patch · transition-request 산출
   ↓
[검수자]    같은 명령을 독립 재실행해 대조                ← 판정은 여기서 성립
[전역 gate] GATE-MANIFEST(POST-EXECUTOR/POST-COMMIT)      ← 반입 이후, 저장소 불변식
[State Applier]  승인된 evidence가 있을 때만 상태 반영
```

### 2.1 고정 adapter (모델이 계획하지 않는다)

```
Prepare → BuildJudgmentRequest → ValidateJudgment → ApplyCandidate
        → Verify → ClassifyFailure → DecideNextByPolicy
```

**`DecideNextByPolicy`는 모델 호출이 아니라 고정 규칙이다.**

### 2.2 1단계에서 diff를 모델에게 맡기지 않는다

프로그램이 **대상 함수와 정확한 삽입 위치**를 정한다 → 모델은 **`commentText` 하나**만 반환 → 프로그램이 검증하고 삽입한다. **diff는 다음 단계다.**

---

## 3. ★ 증명 — SIM-0 / SIM-1, 그리고 **무모델 대조군** (신설, ADR-012)

`functionsWithoutComment` 하네스는 **"주석이 존재하는가"**만 본다. `// 이 함수는 필요한 작업을 수행한다`도 green이다 → **지표는 만족, 목적은 미달**(ADR-005). **SIM-0만으로 Phase 0 완료를 선언하지 않는다.**

### 3.1 SIM-1의 함정 — 2차 외부 검수의 핵심 지적 (채택)

SIM-1은 **프로그램이 만든 2~4개 후보 중 모델이 `candidateId`를 고른다.** 그런데 **프로그램이 후보를 만들고 하네스가 정답을 판정할 수 있다면, 모델 없이 후보를 차례로 시험해도 DI가 끝난다.**

> 그러면 증명된 것은 **"프로그램이 DI를 완수했고 모델이 옆에 있었다"**이지,
> **"로컬 AI가 프로그램의 도움으로 DI를 완수했다"**(ADR-011의 완료 기준)가 **아니다.**

### 3.2 필수 대조군 (같은 frozen task, 같은 격리 worktree)

| 경로 | 선택 방식 |
| --- | --- |
| **BASELINE-0** | 첫 후보를 무조건 선택 |
| **BASELINE-1** | deterministic heuristic 또는 **전 후보 순차 시험** |
| **MODEL** | 로컬 모델이 후보 선택 |

**모델의 기여는 다음 중 하나가 실측될 때만 인정한다**(ADR-012):

1. deterministic baseline이 **실패한** 과제를 성공
2. 정답 후보를 **더 적은 적용·하네스 실행 횟수**로 선택
3. 토큰·시간을 포함한 **전체 비용** 감소
4. 잘못된 후보로 인한 **새 위반** 감소

**하나도 못 보이면 결론은 "모델은 장식이었다"이고, 그것도 정직한 결과다.** 숨기지 않는다.

---

## 4. 격리 — **고정 commit의 전체 worktree** (v2에서 변경)

**v2("허용 입력만 복사")는 틀렸다.** build·measure·테스트는 allowlist 밖의 tracked 파일을 필요로 한다. 필요한 것만 골라 복사하면 **실제 저장소와 다른 환경**을 시험하게 된다.

```
승인된 sourceCommit
  → 임시 git worktree(또는 전체 tracked snapshot)
  → 전체 저장소 읽기 가능 / 쓰기는 allowlist만 허용
  → 실행 전후 tree diff로 scope 검사
  → 원본 저장소·WORKSTATE 무접촉
```

### 4.1 dirty source 거부 (기본값)

**지금 저장소는 깨끗한 기준점이 아니다** — 실측(2026-07-12 19:5x): tracked modified **18**, untracked **84**.

```json
{ "sourceCommit": "...", "sourceTreeHash": "...", "sourceDirty": false,
  "runnerCommit": "...", "adapterVersion": "..." }
```

**`sourceDirty: true`면 기본 거부.** 굳이 dirty로 돌려야 하면 **모든 tracked·untracked 입력의 manifest hash**를 남긴다.

### 4.2 ★ `writeTargets` — target 파일의 preimage 계약 (신설)

`requiredInputs`(읽기 참조)와 allowlist(쓰기 대상)는 **겹치지 않는다** — 자기 작업 때문에 stale이 되기 때문이다. 그런데 Runner가 target을 고치려면 **어느 버전을 기준으로, 어느 앵커에** 삽입하는지 알아야 한다.

```json
{ "writeTargets": [
  { "path": "server/Example.cs",
    "preimageSha256": "...",
    "anchor": "MethodName(...)",
    "anchorSha256": "...",
    "allowedOperation": "insert-comment-before-method" } ] }
```

**적용 직전 preimage가 다르면 모델에게 재시도시키지 않는다** → `STALE_TARGET` → **abort 또는 새 Context Pack 요청.** 그렇지 않으면 **낡은 Context Pack으로 새 파일을 고치는 race**가 생긴다.

### 4.3 ★ DI-local verification plan ≠ 전역 `GATE-MANIFEST` (신설)

**v2는 "GATE-MANIFEST의 기대 exit와 대조"라고 적었다. 그대로 쓰면 안 된다.** 현재 `GATE-MANIFEST.json`의 `POST-EXECUTOR`는 **범용 DI manifest가 아니다**:

- `measure dev-pack` — 대상 프로젝트가 `dev-pack`이라고 **고정**
- `gate-clean server` **expectedExit 1** — 실행자가 `server/`를 바꿔 트리가 더러운 것이 정상이라고 **고정**

**문서 DI·다른 프로젝트 DI에는 맞지 않는다.** 두 층으로 분리한다:

| 층 | 무엇 | 어디에 |
| --- | --- | --- |
| **DI-local verification plan** | 이 DI 하나를 검증하는 명령·기대 exit·timeout | **Context Pack**(또는 별도 승인 schema) |
| **전역 운영 gate** | 저장소 전체 불변식(POST-EXECUTOR / POST-COMMIT) | **기존 `GATE-MANIFEST.json` 유지** |

```json
{ "verificationCommands": [
  { "command": "build-verify", "args": [], "expectedExitCodes": [0], "timeoutSeconds": 120 } ] }
```

순서: **DI-local verification → candidate evidence → 독립 검수 → 전역 gate.**

### 4.4 산출물

```
outputs/local-di/<runId>/
  run-request.json   context-receipt.json   model-request.json
  model-response.json   candidate.patch   verification-results.json
  run-evidence.json   transition-request.json
```

**성공해도 Runner가 원본에 반입·commit하지 않고 WORKSTATE도 바꾸지 않는다.** 그러면 **생산자가 자기 완료를 확정하는 구조**가 된다(ADR-002 금지).

---

## 5. `run-evidence.json` — 관찰이지 판정이 아니다

**최종 판정은 검수자가 같은 명령을 재실행해 대조한 뒤에만 성립한다.**
`di-completion-check`의 `outputs/gates/<task>.gate.json`과 **이름을 겹치지 않게** 한다.

---

## 6. 재시도 — 대화를 누적하지 않는다

매 시도마다 **새 요청**: `원래의 최소 입력 + 이전 실패의 기계 분류 코드 + 최소 위치 정보`.
**하네스 stdout 전체를 모델에게 넘기지 않는다.** (§0-B의 134K가 바로 이걸 안 지켰을 때의 값이다.)

**중단 규칙**: 같은 출력 hash 반복 · 같은 `failureCode` 연속 · 출력 악화 · 새 위반 발생 · 토큰 총예산 초과 · **`maxContextTokensPerTurn` 초과** · 시간 초과 · 최대 시도 초과 · 입력/allowlist 확대 필요 → **escalate.**

---

## 7. schema 검증 — 로컬 validator가 authoritative

ollama의 `format` JSON Schema는 **첫 방어선**이고, **우리 프로그램의 검증이 정본이다.**

**모델 출력은 완전히 불신한다**(2차 검수 반영). `commentText` validator에 다음을 넣는다:

- NUL·제어문자 · **Unicode bidi override** · 비정상 zero-width 문자
- 최대 **UTF-8 byte length**(문자 수가 아니다) · 대체문자·깨진 인코딩
- 한 줄인가 · 주석 종료문자 없는가 · 코드/markdown fence 없는가
- **대상 언어별 comment escape** · **symlink·경로 탈출 검사**

**SIM-1(코드 후보 실행)의 실행 환경**: 비밀정보 없는 격리 · **외부 네트워크 차단** · 명시적 timeout · **프로세스 트리 종료** · CPU·메모리 상한.

---

## 8. Context Receipt — **"공짜"가 아니다. 층을 나눠야 증거가 된다** (v2에서 변경)

v2는 "이 구조에서 공짜로 나온다"고 썼다. **과장이다.** 다음은 **서로 다른 증거**다:

| 단계 | 증거 |
| --- | --- |
| Context Pack이 요구함 | `declared` |
| hash를 검증함 | `verified` |
| Runner가 파일을 읽음 | `loaded` |
| **모델 요청에 실제로 포함함** | **`serialized`** |
| API에 전달함 | `transported`(ADR-010) |
| 모델이 이해함 | **증명 불가능** |

v2의 예시는 `loadedRequired`만 적는다 → **Runner가 읽었지만 adapter 버그로 요청에서 빠뜨린 경우를 못 잡는다.**

```json
{ "requiredInputs": [
  { "path": "...", "declaredSha256": "...", "actualSha256": "...",
    "loaded": true,
    "includedInModelRequest": true,
    "requestSegmentSha256": "...",
    "includedRanges": ["L120-L165"] } ],
  "budgetExceeded": false, "budgetExceptionReason": null }
```

**정확한 문구**: *Context Receipt는 모델 자기보고 없이 **결정적으로 생성 가능하다.** 다만 canonical schema와 **요청 직렬화 계측**은 구현해야 한다.*

---

## 9. 사람이 결재할 것

| 안건 | 권고 |
| --- | --- |
| **갈래** | **(A) 프로그램 주도 Local DI Runner 채택** — v2 유지. 조건: 고정 adapter · 도구 호출 없음 · schema 제약 · **고정 commit worktree 격리** · 프로그램 생성 Receipt/evidence · 독립 재실행 · WORKSTATE 직접 변경 금지 |
| **(D) D-PROBE** | **복구하되 목표 구조가 아니다.** 아래 §9.1 |
| **무모델 대조군** | **필수**(ADR-012). 대조군 없는 SIM-1 결과로 "로컬 AI가 DI를 완수했다"를 주장하지 않는다 |
| **(B) tool calling** | "A가 실패하면"이 아니다. 진입조건은 **"고정 adapter로 표현할 수 없는 작업에서 동적 도구 선택이 실제로 필요하다"는 실증** |
| **(C) MCP** | 후순위. 지금은 **깨끗한 JSON CLI 계약**만 |
| **코더 모델** | 1단계에는 쓰지 않는다. **새 Runner와 새 모델을 동시에 바꾸면 원인을 분리할 수 없다.** `qwen3:8b`로 구조 검증 후 같은 frozen task로 비교 |
| **임베딩** | Phase 0 보류. Context Pack이 파일을 지정하므로 검색이 필요 없다 |
| **첫 과제** | SIM-0(배관) → **BASELINE-0/1** → SIM-1(의미). **SIM-0만으로 Phase 0 완료 선언 금지** |
| **하네스** | `e2e-usage`는 **fake model + 고정 fixture**로 프로그램 논리만 결정적으로 검사. **라이브 ollama를 넣지 않는다.** 신규 하네스 예산 증액 없음 |
| **`LAUNCH-BUDGET` 숫자** | **§0-B 실측 기반으로 정한다.** 누적 예산과 **턴별 컨텍스트 상한**을 분리한 뒤 결재 |
| **`ADR-010` 상태** | §0-C. 승인 또는 문구 분리 — **사람 결재** |

### 9.1 ★ D-PROBE — 기존 Claude Code 루프 + 로컬 모델 (복구)

**v2에서 갈래 D가 조용히 사라졌다**(커밋 `0e31c42`에서 발견해 놓고 `223c42b`에서 누락). **실측 확인: v2 본문에 D 언급 0건.**

**되살리되 목표 구조로 채택하지 않는다.** D는 **모델이 도구를 고르므로** "프로그램이 루프를 결정한다"는 목표와 충돌한다. 대신 **비교 실험군**으로 남긴다 — **이미 있는 루프를 시험하지 않고 새 루프를 만드는 것은 이 저장소가 금지하는 "동등물 재발명"에 가깝다.**

> **D-PROBE — 목표 구조가 아니다. A를 새로 만드는 비용이 정당한지 재는 기준선이다.**
> (`ollama launch claude --model ...` 형태로 Claude Code를 로컬 모델에 연결. **미검증 — 우리 환경에서 되는지부터 확인한다.**)

같은 frozen task로 비교한다:

| 항목 | D-PROBE | A Runner |
| --- | --- | --- |
| 계약 준수율 | 측정 | 측정 |
| 도구 호출 오류 | 측정 | 원칙적으로 없음 |
| 범위 위반 | 측정 | **프로그램 차단** |
| 턴 수 · 총 토큰 | 측정 | 측정 |
| **턴별 최대 컨텍스트** | 측정 | 측정 |
| 완주율 | 측정 | 측정 |
| 증거 생성 | 기존 CLI 의존 | **프로그램 직접 생성** |

**사전 예측(적어두고 돌린다 — 사후에 서사를 맞추지 않기 위해)**: §0-B 실측대로면 **D-PROBE는 RESUME급(23.8K) 과제만 완주하고, STATE-01급(피크 134K) DI에서는 32K 모델로 완주하지 못한다.** 예측이 빗나가면 그것이 더 중요한 정보다.

---

## 10. 실행 신원 기록

매 run에 기록: **ollama 버전 · model tag · model digest · quantization · `num_ctx` · `num_gpu` · parallelism · temperature · seed · think 설정 · request schema version · prompt/payload hash · runner commit · adapter ID/version · sourceCommit/sourceTreeHash · workspace snapshot ID · host hardware.**

**`seed`·`temperature`를 고정해도 GPU 추론이 완전히 결정적이라는 보장은 없다** → **같은 조건의 반복 횟수와 분산**을 함께 기록한다.

---

## 11. 숫자에는 증거 경로를 붙인다 (판정 표기 정정)

| 시험 | 주체 | 총 토큰 | 증거 경로 | 판정 |
| --- | --- | ---: | --- | --- |
| RESUME | sonnet | **47,511** | `outputs/launch/RESUME-01.exit.json` | PASS |
| RESUME | qwen3:8b | **580** (in 283 / out 297) | **원본 미보존** | **`REPORTED_PASS / NOT_VERIFIED`** |
| RULES | sonnet | **23,924** | `outputs/launch/RULES-01.exit.json` | PASS |
| RULES | qwen3:8b / 14b | 약 **3,800** | **원본 미보존** | **`REPORTED_RESULT / NOT_VERIFIED`** — 관찰: 거부는 맞고 **근거를 날조**("규칙 1.1·4.2·5.1·6.1") |
| SMOKE-01 | sonnet | 작업 193 + 누적 컨텍스트 과금 49,281 | `outputs/launch/usage-ledger.jsonl` | 참고치 — **컨텍스트 창과 비교하지 마라**(§0-B) |
| **STATE-01 턴별 피크** | sonnet | **134,528** | `outputs/sonnet-STATE-01.out.jsonl` | **컨텍스트 예산의 정본** |

**RESUME 82배**(47,511 ÷ 580 = 81.9)는 **같은 과제끼리의 전체 workflow 토큰비**다. **모델 자체 효율비가 아니다.**
**RULES의 날조는 Runner의 핵심 fixture가 된다**: 실제 답변 · 날조된 규칙 번호 · authoritative 원문 · "거부는 맞고 근거는 틀렸다"의 분리.

---

## 12. 역할 분리 (구현 지시서는 최소 2개로 쪼갠다)

- `server/`(Runner 본체) → **실행자 레인**
- `server/Harness/`(`e2e-usage` fake-model 확장) → **코덱스 배타 영역**(ADR-002)

---

## 13. 실행 순서 (결재 후)

```
0. Context Pack · Context Receipt · writeTargets schema 확정 (canonical)
1. D-PROBE — 사전 정의된 횟수만큼 (1회 아님). 턴별 컨텍스트 계측 필수
2. 무모델 baseline (BASELINE-0/1)
3. A Runner 최소판 구현
4. fake model로 프로그램 논리 반증 시험
5. qwen3:8b로 SIM-0
6. baseline과 나란히 SIM-1
7. 독립 재검수 (검수자가 같은 명령 재실행)
8. 사람의 HS-GATE-P00 판정
```

---

## 14. 한 줄

> **로컬화는 모델을 키우는 게 아니라 자리를 좁히는 일이다.**

그리고 v3가 더한 한 줄:

> **모델을 빼도 같은 결과가 나오면, 그건 로컬 AI가 한 게 아니다.**
