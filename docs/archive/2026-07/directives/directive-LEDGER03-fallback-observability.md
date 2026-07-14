# LEDGER-03 — 조용한 폴백을 관측 가능하게 만든다 (rule-engine으로 몰래 내려앉는 것을 기록한다)

```context-pack
{
  "diId": "LEDGER-03",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/verification/ledger02-executor-token-wiring.md", "sha256": "24f834c3a9f105b14c43af0fd94c03cef286b0740e7cea5f2986071839686ae3" },
    { "path": "docs/handoff/decisions/ADR-005-metric-vs-purpose.md", "sha256": "0b9fb7c5756b27923ceeafab6c8dcd44d00b3c73708d00abdb0887f1891a4bed" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-LEDGER03-fallback-observability.md",
    "docs/verification/ledger02-executor-token-wiring.md"
  ],
  "forbiddenActions": ["git commit", "git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: implementation. 근거: **검수자 실측(2026-07-12)** — LEDGER-02 검수 중 발견.

## 왜 하는가 — **우리는 LLM을 쓴다고 믿었지만 rule-engine 출력을 받고 있었다**

토큰 계측(LEDGER-01/02)을 켜자마자 이게 나왔다. 검수자 실측:

```
00:57:50  review.tier1_completed   provider=ollama       in=1541 out=147   ← ollama가 불렸다
00:57:56  proposal.generated       provider=rule-engine  in=0    out=0     ← 6초 뒤, 결과물은 rule-engine
00:58:02  review.tier1_completed   provider=ollama       in=1541 out=146
00:58:09  proposal.generated       provider=rule-engine  in=0    out=0
```

**ollama가 호출됐는데 산출물은 rule-engine이다.** 원인:

- `server/OllamaExecutor.cs:395` — `metricId == expectedMetricId` **대소문자 완전일치**.
- `qwen3:8b`가 `functionsWithoutComment`를 **`functionsWithOutComment`(대문자 O)**로 반환한다 → `ParseNoteResponse`가 `null` 반환 → `GenerateProposalWithFallback`이 **rule-engine 제안으로 대체**.
- **그리고 run-log에 `fallback`·`fail`·`warn`·`error` 이벤트가 단 한 건도 없다.** 완전히 조용하다.

**이건 회귀가 아니다** — 마지막 ollama 제안은 2026-07-11 23:40, LEDGER-01 커밋은 00:22. **우리 변경 이전부터 그랬다.** 아무도 몰랐을 뿐이다.

**계측이 없으면 시스템은 조용히 퇴화한다.** 이 지시서는 그 침묵을 없앤다.

## 목표 — **관측만 켠다. 고치지 않는다.**

**중요: 파서를 관대하게 만들지 마라.** `metricId` 대조를 대소문자 무시로 바꾸는 것은 **이번 범위가 아니다.** 지금 그걸 하면 **모델 출력 오류를 코드가 흡수해 감춘다.** 먼저 **얼마나 자주, 왜 폴백하는지 데이터를 만든다.** 그 데이터로 다음을 정한다 — **데이터가 먼저다**(`skills/common/hs-gate.md` 2항).

### 할 일

1. **폴백이 일어나면 run-log에 이벤트를 남긴다.**
   - 대상: `server/Program.cs`의 `GenerateProposalWithFallback`(≈line 1211)·`GenerateTuningProposalWithFallback`(≈line 1088). **이미 폴백을 감지하고 있다 — 기록만 안 할 뿐이다.**
   - 이벤트명: `proposal.fallback` · `level: "warn"`.
   - **run-log 항목의 형태(스키마)를 바꾸지 마라.** 기존 항목 구조(`createdAt`/`event`/`params`/`level`/`producedBy`/`attempt`/`loopIteration`/`cost`)를 그대로 쓰고, **사유는 `params`에 담는다.** 새 최상위 필드 금지.
   - `params`에 담을 것: `reason`(아래 목록 중 하나) · `expectedMetricId` · `actualMetricId`(있으면) · `provider`(폴백 전에 시도한 것 = `ollama`) · `model`.
2. **`reason`을 분해해서 적는다** — "실패했다"로 뭉치지 마라. **원인이 뭉쳐 있으면 다음 사람이 프록시로 추측한다.**
   - `parse-rejected-metricid` — metricId 불일치(지금 실제로 일어나는 것)
   - `parse-rejected-note` — note가 비었거나 금지어·깨진 문자 포함
   - `parse-rejected-json` — JSON 추출 실패
   - `ollama-unreachable` — 엔드포인트 연결 실패·타임아웃
   - `ollama-disabled` — 설정으로 꺼져 있음
   - 그 외는 `unknown`으로 두고 **원문 일부(앞 200자)를 `params.rawHead`에 넣어라.** 모르는 것을 아는 척하지 마라.
3. **폴백이 아닐 때는 이벤트를 남기지 마라.** 정상 경로에 잡음을 만들지 마라.
4. **`OllamaExecutor.ParseNoteResponse`가 거부 사유를 호출부에 전달할 수 있게** 최소한으로 손본다(반환 타입 확장 또는 out 파라미터). **판정 로직 자체는 바꾸지 마라 — 여전히 엄격하다.**

## 하지 않는 것 (명시적 범위 밖)

- ❌ **`metricId` 대조 완화**(대소문자 무시 등). 다음 결정 사항이다. **데이터부터 모은다.**
- ❌ 프롬프트 수정으로 모델이 정확한 metricId를 뱉게 유도하는 것. 그것도 별개 실험이다.
- ❌ `server/Guardrails.cs`·`server/Engine.cs`·`server/Tier2Approver.cs`·`server/Harness/**` 접촉.
- ❌ `cost` 스키마 변경.

## 실체 증명 (이 작업의 핵심 — 그리고 이게 어렵다는 걸 안다)

**문제: 지금 저장소에 위반이 0건이라 제안 생성이 자연 트리거되지 않는다**(dev-pack 0, ruined-lab 0). 그래서 LEDGER-02 실행자는 `__TokenProbe`라는 임시 함수를 코드에 심어 위반을 만들었다. **그 방법을 조건부로 허용한다 — 단 아래를 전부 지켜라.**

1. **위반 주입은 허용한다.** 단 **`server/` 안의 임시 함수 1개**로 최소화하고, **주입 즉시 증명하고 즉시 제거한다.**
2. **제거 후 `measure dev-pack` violationCount가 0으로 복귀하는 것을 확인해 stdout에 적어라.** (LEDGER-02 실행자는 이걸 제대로 했다.)
3. **주입 창(window) 동안 스케줄러가 그 파일에 대해 유령 제안을 만들 수 있다.** 실제로 지난번에 그랬다. **그 제안을 승인하지 마라. 발견하면 보고만 하라.** (근본 해결은 P0-06 `FILE-CLAIMS`이며 네 몫이 아니다.)
4. **증명해야 할 것 (stdout에 run-log 항목 원문으로)**:
   - `event: "proposal.fallback"`, `level: "warn"`, `params.reason: "parse-rejected-metricid"`, `params.expectedMetricId` / `params.actualMetricId`가 **실제 값**으로 찍힌 항목.
   - **`actualMetricId`가 진짜로 `functionsWithOutComment`(대문자 O)인지 확인해서 적어라.** 검수자의 진단이 맞는지 **네가 실체로 검증하는 것이다.** 틀렸으면 틀렸다고 보고하라 — **그게 더 가치 있다.**
   - 폴백이 없는 정상 사이클에서는 이 이벤트가 **찍히지 않는 것**도 확인해라.
5. **값을 지어내지 마라.** 증명 못 하면 `## 지표는 만족했으나 목적은 미달인 부분`에 그대로 써라. **LEDGER-02 실행자가 증명 실패를 정직하게 신고했고, 그 신고가 이 지시서를 낳았다. 정직이 다음 수를 만든다.**

## 검수 기준 (공통 항목 + 아래)

1. `dotnet build server -c Release` → **exit 0, warning 0**.
2. **위 「실체 증명」 4항의 run-log 항목 원문**.
3. `verify-behavior` → `behaviorEqual: true`.
4. `measure dev-pack` → **violationCount 0** (주입 제거 후). **0을 깨뜨리면 실패다.**
5. `gate-clean server` / `handoff-integrity` → **exit 0**.
6. `cost` 스키마 키 불변: `inputTokens, outputTokens, estimatedUSD, subscriptionCalls, role`.

## v9 산출물

WORKSTATE 갱신(`diId: LEDGER-03`, 이전 DI는 `history`로) → `projection` 실행 · `docs/verification/ledger03-fallback-observability.md`(**①주체 ②하네스와 exit code ③참조 스킬** + `## 지표는 만족했으나 목적은 미달인 부분` 필수) · `docs/directives/LEDGER03-fallback-observability.md` 보관본.

## 허용 파일 (allowlist)

- server/Program.cs
- server/OllamaExecutor.cs
- docs/handoff/WORKSTATE.json
- docs/context/RUNTIME-INDEX.md
- docs/handoff/HANDOFF.md
- docs/verification/ledger03-fallback-observability.md
- docs/directives/LEDGER03-fallback-observability.md
- (실행 산출물) dashboard/data/*/\*.json — **서버 실행으로 생성된 변경만. 손 편집 금지(= 위조 = 즉시 반려).**

## 경계 / 보고

`-c Release`. stdout에 수행요약 · 자가점검표 · 하네스별 **실제 exit code** · **`proposal.fallback` 항목 원문** · **주입 제거 후 measure 0 복귀 증거**.
**한도·중단이 임박하면 종료 전 마지막 세 줄: `QUOTA_SIGNAL` / `CHANGED: <수정한 파일>` / `NEXT: <다음 할 일 한 줄>`.**
