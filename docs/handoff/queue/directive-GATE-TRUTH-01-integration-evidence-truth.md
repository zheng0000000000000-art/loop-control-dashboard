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
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline", "trust-origin-declare-in-production"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → positive·negative·결정성·격리 테스트 전부 필수(v9 §0.1).

---

# GATE-TRUTH-01 — 통합 evidence가 **기계가 관찰한 것**이어야 한다

- actor: **CORE_INFRA_EXECUTOR (sonnet)**
- 발견: 검수자 실측(2026-07-15, `server/TrustOriginCli.cs` 직독)
- **개정 R1 (2026-07-15, 사람 검수)**: 초판은 **자기가 지적한 구멍을 다시 팠다.** §0-4에 적었다. **초판을 읽었다면 버려라.**
- **선행**: 없음. **후행**: Trust Origin 부트스트랩(`06C-2`)은 **이 지시서 이후**다.

---

## 0. 문제

### 0-1. 프로덕션 evidence는 **전부 `NOT_RUN`으로 태어난다** (실측)

`trust-origin evidence`의 유일한 호출부는 `TrustOriginCli.cs:58`이다:

```csharp
var evidence = BuildIntegrationEvidence(ctx, gatesPass: false);   // ← 리터럴 false
```

`BuildIntegrationEvidence`(:300~316)는 그 불린 하나를 여덟 필드로 복제한다. `gatesPass: true`는 **self-test fixture에서만** 나온다. 소스 주석이 인정한다:

> `// 통합 게이트 evidence 초안을 파일로 쓴다. 실제 게이트 실행은 외부 절차가 수행한다.`

### 0-2. `declare`는 **파일에 적힌 문자열을 믿는다**

`ValidateIntegrationEvidence`(:280~296)는 baseline 3종은 재계산해 대조한다. **옳다.** 그러나 게이트 결과는 대조하지 않는다:

```csharp
if (Read(evidence, "releaseBuild") != "PASS") return "integration-gate-evidence-missing";
```

> `evidence`가 `NOT_RUN` 초안을 뱉고 → **사람이 손으로 `PASS`라 고쳐 쓰고** → `declare`가 믿는다.
> **Trust Origin의 "통합 검증 통과"는 사람이 타이핑한 문자열이다.** `--verdict`·`--human-decision` 구멍과 같은 계열이다.

### 0-3. 등재된 게이트의 절반을 보지도 않는다

`HarnessRegistry` 16종 · `GATE-MANIFEST` 등재 12건(8종) · evidence 8필드(전부 불린 하나에서 파생).
**`context-pack-integrity`·`gate-clean`·`hs-scan`·`verify-behavior`가 evidence에 없다.**
게이트 목록이 `BuildIntegrationEvidence` 안에 **하드코딩**돼 있다. **누락은 실수가 아니라 구조다.**

### ★ 0-4. 초판 지시서가 판 구멍 — **이것이 이 지시서의 핵심 요구다**

초판은 "`declare`가 `verdict` 문자열 대신 `actualExit == expectedExit`로 재판정하라"고 했다. **그것으로는 아무것도 막지 못한다.**

```json
{ "expectedExit": 1, "actualExit": 1, "verdict": "PASS" }   ← 게이트를 한 번도 안 돌리고 타이핑할 수 있다
```

초판은 *"actualExit까지 손으로 맞추면 baseline 해시 3종에서 걸린다"*고 적었다. **거짓이다.**
`baselineCommit`·`baselineWorkstateSha256`·`baselineApplierLogSha256`은 **게이트 실행 여부와 아무 결속이 없다.**

> **`gatesPass` 불린을 `actualExit` 정수로 바꾼 것뿐이다. 파일에 적힌 값을 믿는 구조가 그대로 남는다.**
> **evidence 파일은 승인 증거가 아니다. 첫 번째 관찰 기록일 뿐이다.**
> **truth는 `declare` 시점의 재실행이다.**

---

## 1. 고칠 것

### ★ 1-1. `GateRunner` — evidence와 declare가 **같은 코드로 게이트를 실행한다**

신규 파일 **`server/GateRunner.cs`**. 하네스가 아니다(`HarnessRegistry`에 등록하지 않는다). evidence 생성기다.

```
GateRunner.Observe(baselineCommit, manifestPath) → GateObservation[]

 1. source worktree 검사
      · HEAD == baselineCommit          아니면 exit 2
      · tracked 변경 0                  아니면 exit 2  (git diff --quiet HEAD)
      · untracked는 목록으로 기록만 한다 (판정하지 않는다)
 2. canonical 상태 blob hash 기록 (전)   WORKSTATE.json · applier-log
 3. ★ $TEMP에 git worktree 생성          git worktree add --detach <temp> <baselineCommit>
 4. temp worktree에서 dotnet build server -c Release      ← --no-build 금지
 5. GATE-MANIFEST의 검사를 order대로 실행 (cwd = temp worktree)
      dotnet run --project server -c Release -- <command> <args...>
 6. 게이트마다 관찰 기록 (아래 스키마)
 7. ★ temp worktree 폐기                 git worktree remove --force
 8. canonical 상태 blob hash 기록 (후) → 전과 다르면 exit 2 (fail-closed)
 9. source worktree가 여전히 tracked-clean인지 확인 → 아니면 exit 2
```

**왜 temp worktree인가**: `GATE-MANIFEST`의 `measure dev-pack`은 `mutatesState: true`다 — 실측으로 `dashboard/data/dev-pack/{measurement,run-log,workflow-state}.json`을 바꾼다(검수자 2026-07-15 확인).
**현재 워크트리에서 돌리면 evidence 실행이 트리를 더럽히고, 사람이 손으로 원복해야 한다. 사람이 다시 절차의 일부가 된다.** 격리하면 그 문제가 **사라진다.**

**게이트 관찰 스키마 (evidence `schemaVersion: 2`)** — 검사 하나당 한 객체:

```json
{
  "gateId": "POST-COMMIT", "order": 3,
  "command": "context-pack-integrity", "args": [],
  "expectedExit": 0, "mutatesState": false,
  "cwd": "<TEMP_WORKTREE>",
  "actualExit": 0, "verdict": "PASS", "timedOut": false,
  "startedAt": "...", "finishedAt": "...",
  "stdoutSha256": "...", "stderrSha256": "...",
  "stdoutCanonicalSha256": "...", "stderrCanonicalSha256": "..."
}
```

- **`verdict`는 `actualExit == expectedExit && !timedOut`로 프로그램이 계산한다.** 파일에서 읽지 않는다.
- **`expectedExit`는 `GATE-MANIFEST`에서 읽는다.** **`hs-scan`은 1이 PASS다.** `actualExit == 0`을 통과 조건으로 쓰면 반려다.
- **timeout은 명시적 실패다.** 게이트마다 timeout을 정하고, 넘으면 `timedOut: true` + `verdict: FAIL`. **조용히 매달리지 마라**(빈 인자 `dotnet run`이 웹서버를 띄워 실행자를 12분간 교착시킨 실측 사고가 있다).

### ★ 1-2. `declare`가 **게이트를 다시 돌려서 evidence와 대조한다**

**evidence 파일의 값은 어느 것도 신뢰하지 않는다. `actualExit`도 포함이다.**

```
trust-origin declare --evidence <file>

 1. schemaVersion != 2                                     → exit 1  (구 스키마 거부)
 2. baseline 3종 재계산 대조 (지금 하는 것 — 유지)          → 불일치 exit 1
 3. gateManifestSha256 / gateSetSha256 재계산 대조         → 불일치 exit 1
 4. ★ GateRunner를 다시 실행한다 (같은 baseline, 같은 manifest)
 5. ★ 재실행 관찰값 vs evidence 관찰값 비교                 → 불일치 exit 1 "gate-observation-mismatch"
 6. 모든 게이트가 verdict PASS 인가                         → 아니면 exit 1 "integration-gate-failed"
 7. legacy failure set 재계산 (지금 하는 것 — 유지)
 8. 전부 통과할 때만 record candidate 생성
```

**5번의 비교 키** (이 목록 밖의 필드로 판정하지 마라):

```
gateId · order · command · canonical args · expectedExit · mutatesState
actualExit · timedOut · stdoutCanonicalSha256 · stderrCanonicalSha256
```

> **evidence는 사람이 검토하라고 있는 첫 관찰 기록이다. 판정의 근거가 아니다.**
> **판정의 근거는 `declare`가 방금 자기 손으로 돌린 결과다.** 파일을 조작해도 재실행 결과와 어긋나면 죽는다.

**`stdoutCanonicalSha256`은 왜 따로 있는가 — 이걸 안 하면 declare가 영원히 실패한다.**
게이트 stdout에는 **시각·temp 경로·소요시간**이 섞인다(`measuredAt` 등). 원본 stdout 해시를 비교 키로 쓰면 **두 번 돌릴 때마다 무조건 불일치**다. 그래서:

- **휘발 필드 목록을 코드에 명시적으로 선언한다**(`measuredAt`·`startedAt`·`finishedAt`·`durationMs`·`elapsedMs`·절대경로). **정규식으로 뭉개지 마라.**
- temp worktree 경로는 고정 토큰(`<TEMP_WORKTREE>`)으로 치환한다.
- 그 canonical 형태를 해시한다. **원본 `stdoutSha256`도 함께 기록한다**(포렌식용). **단 비교 키로 쓰지 않는다.**
- ⚠ **canonical화 후에도 결정적이지 않은 게이트가 있으면, 비교 대상에서 빼지 말고 보고해라.** 게이트를 비교에서 빼는 것은 **검사를 죽이는 것**이다. 완화 여부는 **사람이 결정한다.**

### ★ 1-3. manifest ↔ evidence는 **정확 일치**다 (부분집합 아님)

초판은 `evidence ⊇ manifest`(부분집합)를 요구했다. **그러면 evidence에 임의 게이트·중복 게이트·다른 인자 조합을 끼워 넣을 수 있다.**

**canonical identity 튜플**로 **순서 있는 정확 일치**를 요구한다:

```
(gateId, order, command, canonical args, expectedExit, mutatesState)

누락 0 · 중복 0 · 예상 밖 항목 0 · 순서 불일치 0     → 하나라도 어긋나면 exit 1
```

evidence와 Trust Origin record에 **둘 다** 넣는다:

```
gateManifestSha256   GATE-MANIFEST.json 원본 바이트의 sha256
gateSetSha256        위 튜플을 order대로 canonical 직렬화한 것의 sha256
```

> **"`GATE-MANIFEST`가 게이트 목록의 정본"이 산문이면 지켜지지 않는다. 해시로 결속해야 지켜진다.**
> **게이트 목록을 코드에 하드코딩하면 반려다.** 하드코딩인 한 `context-pack-integrity` 누락이 또 일어난다.

### 1-4. `stateMutated` 하나로는 뜻이 모호하다 — **셋으로 쪼갠다**

temp worktree가 더러워진 것과 canonical 상태가 바뀐 것은 **같은 사건이 아니다.**

```json
"executionIsolation": "TEMP_GIT_WORKTREE",
"gateWorkspaceMutated": true,        ← measure 때문에 정상적으로 true
"sourceWorkspaceMutated": false,     ← true면 격리 실패. exit 2
"canonicalWorkstateMutated": false,  ← true면 exit 2
"canonicalApplierLogMutated": false  ← true면 exit 2
```

**지금처럼 무조건 `"stateMutated": false`라고 쓰는 것은 거짓말이다.**

---

## 2. 하지 않을 일 (하면 반려)

- ❌ **evidence 파일의 값을 판정 근거로 쓰는 것.** `verdict`든 `actualExit`든 마찬가지다. **`declare`는 반드시 재실행한다.**
- ❌ **`gatesPass` 불린을 남기는 것.** 이름만 바꾸는 것도 반려다.
- ❌ **게이트 목록 하드코딩.** `GATE-MANIFEST`가 정본이고 **해시로 결속**한다.
- ❌ **`GATE-MANIFEST`의 `expectedExit`를 실측에 맞춰 바꾸는 것.** **검사를 죽이는 짓이다.** (`GATE-MANIFEST.json`은 **allowlist에 없다. 무접촉.**)
- ❌ **`--force`·`--assume-pass`·`--skip-gates`·`--no-replay` 류 우회 플래그 신설.** 사람이 손으로 evidence를 써서 통과하는 경로를 **하나도 남기지 마라.** 그게 이 지시서의 전부다.
- ❌ **★ 프로덕션 저장소에서 `trust-origin declare` 실행 / Trust Origin record 생성.** **부트스트랩은 사람이 한다**(land gate 12번). 이 DI는 **도구만 고친다.** 시험은 **폐기 가능한 임시 저장소**에서만(§3).
- ❌ **결정적이지 않은 게이트를 비교 대상에서 빼는 것.** 빼지 말고 **보고해라.**
- ❌ **`--no-build` 사용.** Release 산출물이 낡으면 게이트 넷이 exit 2로 나온다(실측 함정, 두 세션 연속 사고).
- ❌ **`--emit-hashes` 제작** — **`CODEX-GATE-04` §5-4 소관. 중복 제작 금지.**
- ❌ **무접촉**: `server/Harness/**` 전부(05H·GATE-CP-01·CODEX-GATE-04 영역) · `server/StateApplierCli.cs`·`server/Cli/**`·`server/Program.cs`(06C-1) · `dashboard/` · `WORKSTATE.json`·`applier-log`.
- ❌ push·결재·반입·발사.

---

## 3. 완료 기준 (exit code)

> **★ 구멍 재현(2·3)은 반드시 `$TEMP`의 폐기 가능한 임시 저장소에서 한다.**
> `git clone` 또는 `git worktree add --detach` 로 baseline commit을 뽑는다.
> **프로덕션 저장소에는 Trust Origin record가 단 한 번도 생기지 않는다.**

```text
 1. dotnet build server -c Release                                          → 0, warning 0

 2. ★ 구멍 실증 (임시 저장소, 고치기 전 코드):
      trust-origin evidence --out <abs>            → 모든 게이트 NOT_RUN 초안
      → 손으로 releaseBuild/reconciliationFixtures/docIntegrity 를 "PASS",
        devPack.violationCount=0, legacyCallsiteCount=0 으로 고친다
      → trust-origin declare --evidence <abs>
      → ★ 통과한다. record가 생긴다. 그 출력을 보고에 그대로 붙여라.
         "게이트를 한 번도 안 돌리고 Trust Origin record가 생겼다"의 증거다.

 3. ★ 초판 설계도 뚫린다는 실증 (임시 저장소, 고친 뒤 코드):
      schemaVersion 2 evidence를 만들고, 게이트를 돌리지 않은 채
      actualExit 를 전부 expectedExit 와 같게, verdict 를 PASS 로 타이핑한다
      (baseline 3종 해시는 실제 값 그대로 둔다 — 조작할 필요조차 없다)
      → trust-origin declare --evidence <abs>
      → ★ exit 1  "gate-observation-mismatch"       ← 재실행 대조가 잡는다
      ※ §1-2의 재실행이 없으면 이 시험이 통과해 버린다. 이 시험이 이 지시서의 심장이다.

 4. 정상 경로: trust-origin evidence --out <절대경로>                       → 0
      · executionIsolation = TEMP_GIT_WORKTREE
      · GATE-MANIFEST 등재 검사가 순서대로 전부 있다 (누락0 중복0 예상밖0)
      · ★ context-pack-integrity 가 evidence에 있다
      · ★ hs-scan: expectedExit 1 / actualExit 1 / verdict PASS   ← 0을 기대하지 않는다
      · gateManifestSha256 · gateSetSha256 이 있다
      · sourceWorkspaceMutated=false · canonicalWorkstateMutated=false
        canonicalApplierLogMutated=false · gateWorkspaceMutated=true

 5. 정상 경로 declare (임시 저장소): 4의 evidence 그대로                    → 0, record 생성
      · 재실행이 실제로 일어났다는 증거(재실행 타임스탬프가 evidence보다 나중)

 6. ★ evidence의 gate 배열에 항목 하나 추가 / 하나 삭제 / 순서 바꿈 (각각)  → 전부 exit 1
      (부분집합 허용이 아니라는 증거)

 7. ★ GATE-MANIFEST를 바꾼 임시 저장소 + 옛 evidence 로 declare            → exit 1
      (gateManifestSha256 / gateSetSha256 결속의 증거. 코드 검토로 갈음하지 마라)

 8. 게이트 하나를 실제로 실패시킨 baseline 에서 evidence                    → 그 게이트 verdict FAIL
      (예: 미발사 지시서의 requiredInput sha256 을 틀리게)                 → declare exit 1

 9. 결정성: 같은 baseline 에서 evidence 2회                                → 게이트 집합·actualExit·
      canonical 해시 전부 동일 (원본 stdoutSha256 은 달라도 된다)
      ※ canonical화 후에도 다른 게이트가 있으면 → 비교에서 빼지 말고 보고

10. 격리: evidence·declare 전후 프로덕션 저장소에서
      · WORKSTATE.json / applier-log  blob hash 동일  (git hash-object 로 확인)
      · tracked 변경 0                                (git status 는 프록시다)
      · outputs/trust-origin/ 에 record 없음          ★ 프로덕션 record 미생성

11. timeout: 응답하지 않는 게이트를 fixture로 주입                         → timedOut=true, verdict FAIL,
      프로세스가 끝난다 (매달리지 않는다)

12. trust-origin --self-test                                              → 0. 기존 24 케이스 회귀 없음 + 신규
13. handoff-integrity / context-pack-integrity / doc-integrity / gate-clean server → 0 / 0 / 0 / 0
14. measure dev-pack (-c Release)                                         → 0, violationCount 0
```

> **⚠ 시험용 파일 수정은 `$TEMP` 사본에서 하라.** 저장소 안에서 시험하다 **검수자가 `docs/wiki` 42파일을 지운 사고**가 있었다.
> **⚠ 게이트를 재기 전에 반드시 `dotnet build server -c Release`.**
> **⚠ `--out` 상대경로는 `server/` 기준으로 해석된다. 절대경로를 줘라.**

---

## 4. 이 작업이 끝나면 어디까지인가 (경계 — 넘지 마라)

```text
INTEGRATION_GATE_RUNNER_READY   = true
GATE_EVIDENCE_MACHINE_OBSERVED  = true
GATE_EVIDENCE_REPLAY_VERIFIED   = true
TRUST_ORIGIN_DECLARE_TOOL_READY = true

TRUST_ORIGIN_DECLARED           = false   ← 여기는 사람이 한다
TRUSTED_BASELINE                = false
NORMAL_TRANSITION_READY         = false
```

그다음(**이 DI 밖**): 새 baseline commit A 확정 → **사람이** `evidence` 실행 → **사람이** 결과 검토 → **사람이** `declare` 실행 → record-only Declaration commit B → `trust-origin verify`.

---

## 목적 기준 (사람 판정 — ADR-005)

> **"Trust Origin record가 '통합 검증 통과'라고 쓰면, 그것은 `declare`가 방금 자기 손으로 게이트를 돌린 결과여야 한다.
> 파일에 적힌 값을 믿는 구조가 한 군데라도 남으면, 이름만 바뀐 같은 구멍이다."**

**우회로 (전부 반려)**:
evidence의 `actualExit`를 믿기 · `declare`에서 재실행 생략 · 게이트 목록 하드코딩 · `gatesPass`를 다른 이름으로 살려두기 ·
manifest 비교를 부분집합으로 · `hs-scan`을 exit 0 기대로 판정 · 결정적이지 않은 게이트를 조용히 비교에서 빼기 ·
`GATE-MANIFEST`의 `expectedExit` 수정 · 완료 기준 3번(초판 설계도 뚫린다는 실증)을 건너뛰기 ·
**프로덕션 저장소에서 `declare`를 돌려 record를 만들기.**

**자진 신고 없이 하면 반려다. 신고하면 감점이 아니다.**
**요구가 서로 모순되거나 빠진 게 있으면 완화하지 말고 보고해라.** (초판이 실제로 모순이었다 — §0-4.)

---

## 허용 파일 (allowlist)

- server/TrustOriginCli.cs
- server/GateRunner.cs
- docs/verification/gate-truth-01-integration-evidence-truth.md
- docs/handoff/queue/directive-GATE-TRUTH-01-integration-evidence-truth.md

> `server/GateRunner.cs`는 **신규 파일**이다. **하네스가 아니다** — `HarnessRegistry`에 **등록하지 않는다.**
> (`CODEX-GATE-04`의 "신규 하네스 제작 금지"는 하네스 얘기다. 이건 evidence 생성기다. 혼동하지 마라.)
> `docs/handoff/GATE-MANIFEST.json` **무접촉** — 읽기 전용 정본이다(`requiredInputs`에 있다).
> `server/Harness/**` **무접촉** — 이 DI는 하네스를 고치지 않는다. **부르기만 한다.**

---

## 보고

`docs/verification/gate-truth-01-integration-evidence-truth.md` — `docs/verification/_template.md` 형식 그대로.

**반드시 적을 것:**

```text
EXPLOIT_REPRODUCTION_REPOSITORY        = disposable-temp-repo
PRODUCTION_DECLARE_EXECUTED            = false
PRODUCTION_TRUST_ORIGIN_RECORD_CREATED = false
```

- **완료 기준 2번의 실제 출력** — 고치기 전엔 게이트 없이 record가 생긴다는 증거
- **★ 완료 기준 3번의 실제 출력** — **초판 설계(`actualExit` 재판정)로도 뚫린다는 증거.** 이게 없으면 재실행이 왜 필요한지 증명되지 않는다
- 완료 기준 6·7·8·11의 **기대 exit vs 실제 exit 표**
- **evidence 파일 실물 1개**(완료 기준 4) — `context-pack-integrity`와 `hs-scan(expected 1)`이 보이는 것
- 완료 기준 9의 **canonical화 규칙(휘발 필드 목록)** 과 결정성 결과
- 완료 기준 10의 **blob 해시 대조 결과**
- **`## 지표는 만족했으나 목적은 미달인 부분`**(ADR-005). 없으면 "없음".

**자기보고는 증거가 아니다.** 검수자가 재실행한다. 못 한 시험은 `NOT_VERIFIED`. **숨기면 반려다.**
