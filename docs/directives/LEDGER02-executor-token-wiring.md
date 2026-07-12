# LEDGER-02 — 실행자 토큰을 run-log까지 배선한다 (LEDGER-01의 잔여 2/3)

```context-pack
{
  "diId": "LEDGER-02",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/verification/ledger01-token-ledger.md", "sha256": "8656b81485ef8f3ed110b4ec54ee7f361c743dba30e8f903558dcbf2b0e05094" },
    { "path": "docs/handoff/decisions/ADR-006-resource-ledger-p0.md", "sha256": "0a4438498bc4fbb2322b9d2713e07e96ea8426f677526a58cc821d492c88ff58" },
    { "path": "docs/handoff/QUOTA-POLICY.md", "sha256": "4bc62f76041527b6984eb2e9d3e0dc1d5a985c7329b62ae2ac65450711462a5c" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-LEDGER02-executor-token-wiring.md",
    "docs/verification/ledger01-token-ledger.md",
    "docs/verification/_template.md"
  ],
  "forbiddenActions": ["git commit", "git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: implementation. 근거: **LEDGER-01의 자진 신고(ADR-005)** — `docs/verification/ledger01-token-ledger.md` §「지표는 만족했으나 목적은 미달인 부분」.

## 왜 하는가 — **가장 많이 쓰는 놈을 아직 못 재고 있다**

LEDGER-01은 하네스를 **전부 통과**했다(build 0/0 · measure 0 · verify-behavior true · handoff-integrity exit 0). 그리고 토큰이 **실제로 1건 찍혔다**(`review.tier1_completed`, in=1541 out=144).

**그런데 검수자가 실측한 결과, 토큰이 기록된 항목은 958건 중 단 1건이다.**

| ollama 호출부 | 토큰을 읽는가 | run-log에 도달하는가 | 이유 |
| --- | --- | --- | --- |
| `OllamaReviewer.cs` | ✅ | **✅ 유일하게 성공** | 자기가 run-log 항목을 쓴다 |
| `OllamaExecutor.cs` | ✅ (`totalIn`/`totalOut` → `ExecutorGenerateResult`) | ❌ **0으로 기록됨** | **run-log를 쓰는 곳은 `server/Program.cs`인데 LEDGER-01 allowlist 밖이었다.** `Program.cs`가 `RuntimeCost()`를 **인수 없이** 호출해 항상 0을 적는다 |
| `Tier2Approver.cs` | ✅ (`ReviewOutcome`에 필드 추가됨) | ❌ **경로 자체가 없음** | run-log 항목을 만들지 않는다(마크다운 감사 로그만) |

**실측**: `proposal.generated` 항목의 `cost.inputTokens = 0`. **제안 생성이 토큰을 가장 많이 쓰는 경로인데 그게 0이다.**

**allowlist를 잘못 쓴 것은 검수자다.** ADR-006이 "`Engine.cs`(cost 배선부)"라고 적었고 검수자가 그대로 옮겼는데, **실제 배선부는 `Program.cs`였다.** 실행자는 allowlist를 지키고 **막힌 사실을 정확히 신고했다** — 지표를 green으로 만들려고 범위를 넘지 않았다. **그게 옳은 행동이다.** 이 지시서는 그 신고에 대한 응답이다.

## 목표

**신규 스키마 0개. 신규 필드 0개. 신규 하네스 0개.** LEDGER-01과 같다 — 이미 계산된 값을 **이미 있는 필드까지 배달**하기만 한다.

1. **`server/Program.cs`의 `RuntimeCost()`에 토큰 인수를 받는 오버로드를 추가**한다(`OllamaReviewer.cs:372`가 이미 쓰는 패턴 그대로: `RuntimeCost(int inputTokens = 0, int outputTokens = 0)`).
2. **ollama 실행자가 만든 run-log 항목에만** `ExecutorGenerateResult`의 `totalIn`/`totalOut`을 넘긴다. 대상은 **제안 생성 계열**(`proposal.generated` 등 `ExecutorGenerateResult`가 실제로 손에 있는 호출 지점).
3. **나머지 `RuntimeCost()` 호출 지점은 그대로 둔다.** `Program.cs`에는 `RuntimeCost()` 호출이 21곳 있는데 **대부분은 LLM 호출이 아니다**(rule-engine·측정·결재 이벤트). **거기에 토큰을 억지로 채우지 마라 — 0이 정답이다.** 재지 않은 것을 잰 것처럼 적는 것이 이 프로젝트가 싸우는 병이다.
4. `Guardrails.cs`의 `RuntimeCost()`도 **건드리지 마라** — 가드레일 이벤트에는 LLM 호출이 없다.

## 하지 않는 것 (이번 범위 밖)

- **Tier2Approver의 run-log 경로 신설** — run-log 항목 자체가 없어서 **새 이벤트 타입을 만들어야 한다.** 그건 스키마 변경이고 **별도 결정(ADR)이 필요하다.** 이번에 하지 마라. `LEDGER-03` 후보로 남긴다.
- **`role` 필드를 `executor`/`reviewer`로 분화** — 기존 코드가 전부 `runtime`이다. 분화는 스키마 의미 변경이라 **사람 결재 사항**이다. **`runtime` 그대로 둬라.** (LEDGER-01 실행자가 정확히 이 이유로 손대지 않았다.)

## 검수 기준 (공통 항목 + 아래) — **판정은 exit code와 기록된 숫자로**

1. `dotnet build server -c Release` → **exit 0, warning 0**.
2. **실체 증명(핵심)**: ollama 제안 생성 경로를 실제로 한 번 돌려, `run-log.json`에 **`event`가 `proposal.generated` 계열이면서 `cost.inputTokens > 0` 이고 `cost.outputTokens > 0` 인 항목**이 새로 생기는 것을 보여라. 그 항목 JSON 원문을 stdout에 붙여라.
   - **코드가 그렇게 되어 있다는 설명은 증거가 아니다.** LEDGER-01도 코드는 맞았지만 숫자는 0이었다.
   - ollama가 응답하지 않으면 중단하고 보고하라. **값을 지어내지 마라.**
3. **비-LLM 항목은 여전히 0인가** — `measure.completed` 같은 rule-engine 항목의 `cost.inputTokens`가 **0으로 유지**되는지 확인해 stdout에 적어라. (0이어야 정상이다.)
4. `verify-behavior` → `behaviorEqual: true`.
5. `measure dev-pack` → **비악화**(현재 violationCount **0**. **네가 0을 깨뜨리면 실패다.**)
6. `gate-clean server` / `handoff-integrity` → **exit 0 유지**.
7. `cost` 스키마 키가 그대로인지 확인: `inputTokens, outputTokens, estimatedUSD, subscriptionCalls, role` — **추가·삭제·개명 금지.** `estimatedUSD`·`subscriptionCalls`는 **0 유지**(로컬 ollama는 과금이 없다).

## v9 산출물

- WORKSTATE 갱신(`diId: LEDGER-02`, `changedFiles`에 **네가 실제로 바꾼 파일만**. **이전 DI의 항목은 `history`로 내려라** — LEDGER-01 실행자가 이걸 제대로 했다). 그 뒤 `projection` 실행.
- `docs/verification/ledger02-executor-token-wiring.md` — **①주체 ②사용한 하네스와 각각의 exit code ③참조한 스킬** + **`## 지표는 만족했으나 목적은 미달인 부분`** 자진 신고 절 필수.
- `docs/directives/LEDGER02-executor-token-wiring.md` 보관본.

## 허용 파일 (allowlist)

- server/Program.cs
- server/OllamaExecutor.cs
- docs/handoff/WORKSTATE.json
- docs/context/RUNTIME-INDEX.md
- docs/handoff/HANDOFF.md
- docs/verification/ledger02-executor-token-wiring.md
- docs/directives/LEDGER02-executor-token-wiring.md
- (실행 산출물) dashboard/data/dev-pack/*.json — **서버 실행으로 생성된 변경만. 손 편집 금지(= 위조 = 즉시 반려).**

> `server/Guardrails.cs`·`server/Engine.cs`·`server/Tier2Approver.cs`·`server/Harness/**`·`dashboard/`(코드) **무접촉.**

## 경계 / 보고

`-c Release`. stdout에 수행요약 · 자가점검표 · 위 하네스의 **실제 exit code** · **토큰이 기록된 `proposal.generated` 항목 원문** · **비-LLM 항목이 0인 증거**.
**한도·중단이 임박하면 종료 전 마지막 세 줄: `QUOTA_SIGNAL` / `CHANGED: <수정한 파일>` / `NEXT: <다음 할 일 한 줄>`.**
