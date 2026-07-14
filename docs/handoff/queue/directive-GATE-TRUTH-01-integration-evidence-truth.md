```context-pack
{
  "diId": "GATE-TRUTH-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/handoff/GATE-MANIFEST.json", "sha256": "1af896a3c90a7121031c92b86ba1faa1e10883bf6ef640683940cee87833b695" },
    { "path": "server/Harness/HarnessRegistry.cs", "sha256": "1c804beaaf8b9d787030071ffbe70467dc5f4d2216b70c2aa2c5668a87a6225b" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-GATE-TRUTH-01-integration-evidence-truth.md",
    "docs/handoff/GATE-MANIFEST.json",
    "server/Harness/HarnessRegistry.cs",
    "server/TrustOriginCli.cs"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → positive·negative·결정성·격리 테스트 전부 필수(v9 §0.1).

---

# GATE-TRUTH-01 — 통합 evidence가 **게이트를 실제로 돌린 결과**여야 한다

- actor: **CORE_INFRA_EXECUTOR (sonnet)**
- 발견: 검수자 실측(2026-07-15, `server/TrustOriginCli.cs` 직독)
- **선행**: 없음. **후행**: `06C-2` Trust Origin 부트스트랩은 **이 지시서 이후**다.

---

## 0. 문제 — evidence는 게이트를 **뭉개는 게 아니라 애초에 돌리지 않는다**

### 0-1. 프로덕션 evidence는 **전부 `NOT_RUN`으로 태어난다** (실측)

`trust-origin evidence`의 유일한 호출부는 `TrustOriginCli.cs:58`이다:

```csharp
var evidence = BuildIntegrationEvidence(ctx, gatesPass: false);   // ← 리터럴 false
```

그리고 `BuildIntegrationEvidence`(:300~316)는 그 불린 하나를 여덟 필드로 복제한다:

```csharp
["releaseBuild"]         = gatesPass ? "PASS" : "NOT_RUN",
["reconciliationFixtures"] = gatesPass ? "PASS" : "NOT_RUN",
["docIntegrity"]         = gatesPass ? "PASS" : "NOT_RUN",
["stateTransitionSelfTest"] = SelfTestNode(gatesPass, 19),
["trustOriginSelfTest"]  = SelfTestNode(gatesPass, 24),
["recoverySelfTest"]     = SelfTestNode(gatesPass, 8),
["devPack"]              = { violationCount = gatesPass ? 0 : -1, ... },
["legacyCallsiteCount"]  = gatesPass ? 0 : -1,
```

`gatesPass: true`는 **self-test fixture(:393~610)에서만** 나온다. **프로덕션 경로에는 없다.**
소스의 주석이 그대로 인정한다:

> `// 통합 게이트 evidence 초안을 파일로 쓴다. 실제 게이트 실행은 외부 절차가 수행한다.`

### 0-2. `declare`는 **그 파일에 적힌 문자열을 믿는다**

`ValidateIntegrationEvidence`(:280~296)는 baseline 3종(`baselineCommit`·`baselineWorkstateSha256`·`baselineApplierLogSha256`)은 **재계산해서 대조한다.** 옳다.
그러나 게이트 결과는 **대조하지 않는다.** 파일에 `"PASS"`가 적혀 있으면 통과한다:

```csharp
if (Read(evidence, "releaseBuild") != "PASS") return "integration-gate-evidence-missing";
```

> **결론 (실체)**: `trust-origin evidence`가 `NOT_RUN` 초안을 뱉고 → **사람이 손으로 `PASS`라고 고쳐 쓰고** → `declare`가 그걸 믿는다.
> **Trust Origin record의 "통합 검증 통과"는 사람이 JSON에 타이핑한 문자열이다.**
> **`--verdict`·`--human-decision` 구멍과 같은 계열이다**(`SESSION-BRIEF` §4·§6.3). 검수자가 이미 그 구멍으로 통과한 전과가 있다.

### 0-3. 등재된 게이트의 절반을 **보지도 않는다**

| | 개수 | 실측 근거 |
| --- | --- | --- |
| `HarnessRegistry` 등록 하네스 | **16** | `server/Harness/HarnessRegistry.cs` |
| `GATE-MANIFEST` 등재 검사 (POST-EXECUTOR 7 + POST-COMMIT 5) | **12** (중복 제외 8종) | `docs/handoff/GATE-MANIFEST.json` |
| evidence가 담는 필드 | **8** (전부 불린 하나에서 파생) | `TrustOriginCli.cs:308~315` |
| evidence에 **`context-pack-integrity`** | **없다** | — |
| evidence에 **`gate-clean`·`hs-scan`·`verify-behavior`** | **없다** | — |

**게이트 목록이 `BuildIntegrationEvidence` 안에 하드코딩돼 있다.** 그래서 `GATE-MANIFEST`에 게이트를 등재해도 evidence는 모른다.
**`context-pack-integrity`가 빠진 것은 실수가 아니라 구조다. 하드코딩인 한 다음에도 또 빠진다.**

> **이대로 Trust Origin을 선언하면 record가 거짓이 된다.**
> 부트스트랩은 **신뢰의 바닥**이다. **거짓 위에 바닥을 놓을 수 없다.**

---

## 1. 고칠 것

### ★ 1-1. `trust-origin evidence`가 **게이트를 직접 실행한다**

`gatesPass` 파라미터를 **삭제한다.** evidence는 **실행 결과**이지 인자가 아니다.

```
trust-origin evidence --out <절대경로>
  1. docs/handoff/GATE-MANIFEST.json 을 읽는다        ← 게이트 목록의 정본. 하드코딩 금지
  2. 등재된 검사를 order대로 실제로 실행한다
       dotnet run --project server -c Release -- <command> <args...>
  3. 검사마다 실측을 기록한다 (아래 스키마)
  4. evidence 파일을 쓴다
```

**gate 항목 스키마 (schemaVersion 2)** — 검사 하나당 한 객체:

```json
{
  "gateId": "POST-COMMIT",
  "order": 3,
  "command": "context-pack-integrity",
  "args": [],
  "expectedExit": 0,
  "actualExit": 0,
  "verdict": "PASS",
  "mutatesState": false,
  "startedAt": "...",
  "finishedAt": "...",
  "stdoutSha256": "..."
}
```

- **`verdict`는 `actualExit == expectedExit`로 프로그램이 계산한다.** 파일에서 읽지 않는다.
- **`expectedExit`는 `GATE-MANIFEST`에서 읽는다.** `hs-scan`은 **1이 PASS다.** `actualExit==0`을 통과 조건으로 쓰면 반려다.
- **`stdoutSha256`** — 나중에 재실행 대조가 가능해야 한다.
- 기존 self-test 필드 3종(`stateTransitionSelfTest`·`trustOriginSelfTest`·`recoverySelfTest`)과 `legacyCallsiteCount`는 **유지하되 실제 실행 결과로 채운다.** 불린 복제 금지.

### 1-2. `declare`가 **verdict를 재계산한다**

`ValidateIntegrationEvidence`가 문자열 `"PASS"`를 읽는 것을 **전부 없앤다.**

```
declare --evidence <file>
  1. schemaVersion != 2                                    → exit 1  (구 evidence로 우회 금지)
  2. GATE-MANIFEST를 다시 읽어 등재 검사 집합을 만든다
  3. evidence의 gate 집합 ⊇ 등재 집합 이 아니면            → exit 1  "integration-gate-evidence-incomplete"
       ★ 등재됐는데 evidence에 없는 게이트가 하나라도 있으면 죽는다
  4. 게이트마다 actualExit != expectedExit                 → exit 1  "integration-gate-failed"
       ★ verdict 문자열을 믿지 않는다. exit code로 다시 판정한다
  5. baseline 3종 재계산 대조 (지금 하는 것 — 유지)
```

**§3의 "게이트가 늘어나면 evidence도 따라 늘어난다"가 §1-2의 3번 때문에 자동으로 성립한다.**
`GATE-MANIFEST`에 검사를 등재하고 evidence를 다시 만들지 않으면 **declare가 죽는다.** 그게 목적이다.

### 1-3. evidence 실행은 **읽기 전용이 아니다** — 그 사실을 숨기지 마라

`GATE-MANIFEST`의 `measure dev-pack`은 **`mutatesState: true`**다. 실측으로 `dashboard/data/dev-pack/{measurement,run-log,workflow-state}.json`이 바뀐다(검수자 2026-07-15 확인).

- evidence 출력에 **`stateMutated`를 실체대로** 적는다. 지금처럼 무조건 `false`라고 쓰면 **거짓말이다.**
- **`gate-clean`을 `measure`보다 먼저 실행한다**(`GATE-MANIFEST`의 `order`가 이미 그렇다 — 순서를 지켜라).
- **`WORKSTATE.json`·`applier-log`는 evidence 실행으로 바뀌지 않아야 한다.** 바뀌면 fail-closed(exit 2).

---

## 2. 하지 않을 일 (하면 반려)

- ❌ **`gatesPass` 불린을 남기는 것.** 이름만 바꾸는 것도 반려다. **evidence는 인자가 아니라 실행 결과다.**
- ❌ **게이트 목록을 코드에 하드코딩하는 것.** `GATE-MANIFEST`가 정본이다. 하드코딩인 한 다음 게이트도 또 빠진다.
- ❌ **`GATE-MANIFEST`의 `expectedExit`를 실측에 맞춰 바꾸는 것.** **검사를 죽이는 짓이다.** 고쳐야 할 것은 상태다. (`GATE-MANIFEST.json`은 **allowlist에 없다. 무접촉.**)
- ❌ **`--force`·`--assume-pass`·`--skip-gates` 류 우회 플래그 신설.** 사람이 손으로 evidence를 쓸 수 있는 경로를 **하나도 남기지 마라.** 그게 이 지시서의 전부다.
- ❌ **`--no-build` 사용.** Release 산출물이 낡으면 게이트 넷이 exit 2로 나온다(실측 함정). 서브프로세스는 **`-c Release` 명시**.
- ❌ **`--emit-hashes` 제작** — **`CODEX-GATE-04` §5-4 소관이다. 중복 제작 금지.**
- ❌ **무접촉**: `server/Harness/HandoffIntegrityCli.cs`·`HandoffIntegrityChecker.cs`(05H) · `server/StateApplierCli.cs`·`server/Cli/**`·`server/Program.cs`(06C-1) · `server/Harness/ContextPackIntegrityCli.cs`(GATE-CP-01·CODEX-GATE-04) · `server/Harness/DiCompletionCheckCli.cs`(CODEX-GATE-04) · `dashboard/`.
- ❌ `WORKSTATE.json`·`applier-log` 수정. push·결재·반입·발사.
- ❌ **`trust-origin declare` 실행** — **부트스트랩은 사람이 한다**(land gate 12번). 이 DI는 **도구만 고친다.**

---

## 3. 완료 기준 (exit code) — ★는 **"고치기 전엔 뚫렸다"를 먼저 보여라**

```text
 1. dotnet build server -c Release                                          → 0, warning 0
 2. ★ 고치기 전 재현 — 구멍 실증:
      trust-origin evidence --out $TEMP/e.json          (모든 게이트 NOT_RUN)
      → 손으로 releaseBuild/reconciliationFixtures/docIntegrity를 "PASS"로,
        devPack.violationCount를 0으로, legacyCallsiteCount를 0으로 고친다
      → trust-origin declare --evidence $TEMP/e.json
      → ★ 오늘은 통과한다. 그 출력을 보고에 그대로 붙여라.
        게이트를 한 번도 안 돌리고 Trust Origin record가 생긴다는 증거다.
 3. ★ 고친 뒤 — 같은 손질 evidence(schemaVersion 1)로 declare              → 1 (구 스키마 거부)
 4. ★ 고친 뒤 — schemaVersion 2 evidence의 actualExit를 손으로 0으로 고침   → 1
      (verdict 문자열이 "PASS"여도 죽는다. exit code를 재판정하므로 조작이 무의미해야 한다.
       ※ actualExit까지 손으로 맞추면 baseline 해시 3종에서 걸린다 — 그것도 보여라)
 5. 정상 경로: trust-origin evidence --out <절대경로>                       → 0
      · 게이트를 실제로 실행한 흔적(startedAt/finishedAt/stdoutSha256)이 있다
      · GATE-MANIFEST 등재 검사가 하나도 빠지지 않았다
      · ★ context-pack-integrity 가 evidence에 있다
      · ★ hs-scan: expectedExit 1 / actualExit 1 / verdict PASS   ← 0을 기대하지 않는다
      · stateMutated 가 실체대로 적혀 있다(measure 때문에 true)
 6. ★ GATE-MANIFEST에 검사를 하나 추가한 fixture로 evidence(옛 목록) → declare
                                                                            → 1 "…-incomplete"
      ← 하드코딩이 아니라는 증거. 코드 검토로 갈음하지 마라
 7. 게이트 하나를 일부러 실패시킨 트리에서 evidence 생성                     → 그 게이트 actualExit≠expectedExit,
      (예: 미발사 지시서의 requiredInput sha256을 틀리게)                      verdict FAIL 로 기록 → declare 1
 8. 결정성: 같은 baseline에서 evidence 2회 (시각 필드 제외)                 → 같은 게이트 집합·같은 exit
 9. 격리: evidence 실행 전후 WORKSTATE.json·applier-log **바이트 동일**
      (`git hash-object`로 확인. `git status`는 프록시다)
10. trust-origin --self-test                                                → 0. 기존 24 케이스 회귀 없음 + 신규 케이스
11. handoff-integrity / context-pack-integrity / doc-integrity / gate-clean server → 0 / 0 / 0 / 0
12. measure dev-pack (-c Release)                                          → 0, violationCount 0
```

> **⚠ 시험용 파일 수정은 `$TEMP` 사본에서 하라.** 저장소 안에서 시험하다 **검수자가 `docs/wiki` 42파일을 지운 사고**가 있었다.
> 부득이 저장소에서 했다면 **원복을 blob 해시로 확인하라**(`git hash-object` vs `git rev-parse HEAD:<path>`).
> **⚠ 게이트를 재기 전에 반드시 `dotnet build server -c Release`.** `--no-build`로 첫 측정을 망친 사고가 두 세션 연속 있었다.
> **⚠ `trust-origin evidence --out`의 상대경로는 `server/` 기준으로 해석된다. 절대경로를 줘라.**

---

## 목적 기준 (사람 판정 — ADR-005)

> **"Trust Origin record가 '통합 검증 통과'라고 쓰면, 그것은 프로그램이 게이트를 실제로 돌린 결과여야 한다.
> 사람이 타이핑한 문자열이면 그 record는 거짓이고, 신뢰의 바닥이 거짓 위에 놓인다."**

**우회로 (전부 반려)**:
게이트 목록 하드코딩 · `gatesPass`를 다른 이름으로 살려두기 · evidence를 손으로 쓸 수 있는 플래그 신설 ·
`hs-scan`을 `exit 0` 기대로 잘못 판정 · `GATE-MANIFEST`의 `expectedExit`를 실측에 맞춰 바꾸기 ·
`stateMutated`를 무조건 `false`로 적기 · 완료 기준 2번(구멍 실증)을 건너뛰고 "고쳤다"고만 쓰기.

**자진 신고 없이 하면 반려다. 신고하면 감점이 아니다.**
**요구가 서로 모순되거나 빠진 게 있으면 완화하지 말고 보고해라.**

---

## 허용 파일 (allowlist)

- server/TrustOriginCli.cs
- docs/verification/gate-truth-01-integration-evidence-truth.md
- docs/handoff/queue/directive-GATE-TRUTH-01-integration-evidence-truth.md

> `docs/handoff/GATE-MANIFEST.json` **무접촉** — 읽기 전용 정본이다(`requiredInputs`에 있다).
> `server/Harness/**` **무접촉** — 이 DI는 하네스를 고치지 않는다. **evidence 생성기만 고친다.**

---

## 보고

`docs/verification/gate-truth-01-integration-evidence-truth.md` — `docs/verification/_template.md` 형식 그대로.

**반드시 적을 것:**

- **완료 기준 2번의 실제 출력** — **"게이트를 한 번도 안 돌리고 Trust Origin record가 생겼다"를 보인 다음에만 "막았다"가 성립한다.**
- 완료 기준 3·4·6·7의 실제 출력 (**기대 exit vs 실제 exit** 표)
- **evidence 파일 실물 1개** (정상 경로, 완료 기준 5) — `context-pack-integrity`와 `hs-scan(expected 1)`이 보이는 것
- 완료 기준 9의 **blob 해시 대조 결과** (WORKSTATE·applier-log 무변경)
- **`## 지표는 만족했으나 목적은 미달인 부분`**(ADR-005). 없으면 "없음".

**자기보고는 증거가 아니다.** 검수자가 재실행한다. 못 한 시험은 `NOT_VERIFIED`. **숨기면 반려다.**
