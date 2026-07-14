# LEDGER-04 — metricId 정규화 + **계속 기록** (ollama를 되살리되 관측을 끄지 않는다)

```context-pack
{
  "diId": "LEDGER-04",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/verification/ledger03-fallback-observability.md", "sha256": "eba0cad65a60f313bc11b697eb1bea8fa6111f16f0d4543ed398fe7eeffb3815" },
    { "path": "docs/handoff/decisions/ADR-005-metric-vs-purpose.md", "sha256": "0b9fb7c5756b27923ceeafab6c8dcd44d00b3c73708d00abdb0887f1891a4bed" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-LEDGER04-metricid-normalization.md",
    "docs/verification/ledger03-fallback-observability.md"
  ],
  "forbiddenActions": ["git commit", "git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: implementation. 근거: **사람 결정(2026-07-12)** — 세 선택지 중 **"정규화 + 계속 기록"** 채택.

## 왜 하는가

LEDGER-03이 침묵을 걷어냈고, 원인이 **결정적**으로 드러났다(run-log 실측):

```json
{ "event": "proposal.fallback", "level": "warn",
  "params": { "reason": "parse-rejected-metricid",
              "expectedMetricId": "functionsWithoutComment",
              "actualMetricId":   "functionsWithOutComment" } }   ← 대문자 O
```

`qwen3:8b`가 metricId의 **대소문자를 일관되게** 틀린다 → 엄격 대조가 거부 → **rule-engine 폴백.** 그래서 **우리는 LLM을 쓴다고 믿으면서 rule-engine 출력을 받아왔다.**

**사람이 선택한 길: 파서를 그냥 관대하게 만들지 않는다. 정규화해서 살리되, 정규화가 필요했다는 사실을 계속 기록한다.**

> **왜 이 길인가**: 그냥 대소문자 무시로 바꾸면 **모델 출력 오류를 코드가 흡수해 감춘다.** 다음에 모델이 다른 방식으로 틀리면 **또 조용해진다.** 우리는 오늘 그 침묵 때문에 몇 시간을 잃었다. **복구하되 눈을 감지 않는다.**

## 목표

### 1. 정규화 — **딱 대소문자만**

`server/OllamaExecutor.cs`의 `ParseNoteResponse`에서:

- **1차: 지금처럼 엄격 대조**(`metricId == expectedMetricId`) → 통과하면 **아무 이벤트도 남기지 마라**(정상 경로에 잡음 금지).
- **2차: 대소문자 무시 대조**(`StringComparison.OrdinalIgnoreCase`)로만 재시도 → 통과하면 **수용하되 아래 3번의 이벤트를 남긴다.**
- **그 외는 전부 지금처럼 거부**(`parse-rejected-*`). **공백 제거·유사도 매칭·부분 일치 같은 건 하지 마라.** 관대함은 **대소문자 한 가지로 한정한다.**
- **기대값(`expectedMetricId`)과의 대조만 한다.** 모델이 뱉은 문자열을 metricId 목록 전체와 대조해 "가장 비슷한 것"을 고르는 짓은 **절대 금지** — 그건 추측이다.

### 2. **정규화는 성공이 아니다 — 계속 기록한다**

정규화로 통과시켰으면 run-log에 **새 이벤트**를 남긴다:

- 이벤트명: `proposal.metricid_normalized` · `level: "warn"`
- **`warn`이어야 한다.** `info`로 낮추지 마라 — **모델 품질 저하가 계속 보여야 한다.** 이게 이 선택지의 존재 이유다.
- `params`: `expectedMetricId` · `actualMetricId` · `provider` · `model`
- **run-log 항목 스키마를 바꾸지 마라.** 사유는 `params`에. 최상위 필드 추가 금지.
- 기존 `proposal.fallback` 이벤트(LEDGER-03)는 **그대로 둔다.** 정규화로도 못 살리는 실패는 여전히 폴백이고, 여전히 기록된다.

### 3. **이것으로 LEDGER-02가 비로소 검증된다**

`Program.cs`의 토큰 배선(LEDGER-02, `040d017`)은 **한 번도 실행된 적이 없다** — ollama 제안 경로가 폴백으로 죽어 있었기 때문이다. **정규화로 그 경로가 살아나면, `proposal.generated`에 `provider: ollama` 이면서 `cost.inputTokens > 0`인 항목이 처음으로 찍혀야 한다.**

**그걸 증명하는 것이 이 작업의 최종 목표다.**

## 하지 않는 것

- ❌ 프롬프트 수정으로 모델을 길들이는 것(별개 실험).
- ❌ `server/Guardrails.cs`·`server/Engine.cs`·`server/Tier2Approver.cs`·`server/Harness/**` 접촉.
- ❌ `cost` 스키마 변경. `role`은 `runtime` 유지.
- ❌ 대소문자 외의 어떤 관대화도 금지.

## 실체 증명 (stdout에 run-log 원문으로)

**저장소 위반이 0건이라 제안 생성이 자연 트리거되지 않는다.** LEDGER-03처럼 **위반 주입을 조건부 허용**한다:

- `server/` 안 임시 함수 **1개**로 최소화 → 증명 → **즉시 제거** → `measure dev-pack` **violationCount 0 복귀 확인**(stdout에 적어라).
- 주입 창 동안 스케줄러가 그 파일에 **유령 제안**을 만들 수 있다. **승인하지 마라. 발견하면 보고만.**

**증명해야 할 3건:**

1. **`proposal.metricid_normalized`** — `level: warn`, `expectedMetricId`/`actualMetricId`가 실제 값(대문자 O 포함)인 항목 원문.
2. **★ `proposal.generated`** — `producedBy.provider: "ollama"` 이면서 **`cost.inputTokens > 0` 이고 `cost.outputTokens > 0`** 인 항목 원문. **이것이 LEDGER-02 배선의 첫 검증이다.**
3. **비-LLM 항목은 여전히 0인가** — `measure.completed` 같은 rule-engine 항목의 `cost.inputTokens`가 **0 유지**. (토큰 채우기가 목표가 되면 안 쓴 곳에도 숫자를 채우게 된다 — ADR-005.)

**증명 못 하면 지어내지 말고 `## 지표는 만족했으나 목적은 미달인 부분`에 그대로 써라.** LEDGER-02·03 실행자가 정직하게 신고했고, **그 신고가 이 지시서를 낳았다.**

## 검수 기준 (공통 항목 + 아래)

1. `dotnet build server -c Release` → **exit 0, warning 0**.
2. 위 「실체 증명」 3건.
3. `verify-behavior` → `behaviorEqual: true`.
4. `measure dev-pack` → **violationCount 0**(주입 제거 후). **0을 깨뜨리면 실패다.**
5. `gate-clean server` → exit 0.
6. **`handoff-integrity` → exit 0.** ⚠️ **`projection`을 반드시 마지막에 실행하라.** LEDGER-03 실행자가 문서를 추가한 뒤 `projection`을 다시 돌리지 않아 `sha256: null`로 게이트가 **exit 1로 깨졌다**(검수자가 발견해 복구). **파일을 다 쓴 다음 `projection`, 그 다음 `handoff-integrity`로 자기 확인.**

## v9 산출물

WORKSTATE 갱신(`diId: LEDGER-04`, 이전 DI는 `history`로) → **모든 파일 작성 후** `projection` 실행 · `docs/verification/ledger04-metricid-normalization.md`(**①주체 ②하네스와 exit code ③참조 스킬** + `## 지표는 만족했으나 목적은 미달인 부분` 필수) · `docs/directives/LEDGER04-metricid-normalization.md` 보관본.

## 허용 파일 (allowlist)

- server/OllamaExecutor.cs
- server/Program.cs
- docs/handoff/WORKSTATE.json
- docs/context/RUNTIME-INDEX.md
- docs/handoff/HANDOFF.md
- docs/verification/ledger04-metricid-normalization.md
- docs/directives/LEDGER04-metricid-normalization.md
- (실행 산출물) dashboard/data/*/\*.json — **서버 실행으로 생성된 변경만. 손 편집 금지(= 위조 = 즉시 반려).**

## 경계 / 보고

`-c Release`. stdout에 수행요약 · 자가점검표 · 하네스별 **실제 exit code** · **증명 3건의 run-log 원문** · **주입 제거 후 measure 0 복귀 증거**.
**한도·중단이 임박하면 종료 전 마지막 세 줄: `QUOTA_SIGNAL` / `CHANGED: <수정한 파일>` / `NEXT: <다음 할 일 한 줄>`.** 부분 작업물은 되돌리지 말고 verification 문서에 `상태: 미완(한도)`로 적어라 — **한도로 멈추는 것은 고장이 아니다**(`docs/handoff/QUOTA-POLICY.md`).
