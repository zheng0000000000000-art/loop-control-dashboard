```context-pack
{
  "diId": "GATE-TRUTH-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
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

> **`docs/handoff/GATE-MANIFEST.json`은 `requiredInputs`에 없다.** 이번 DI의 **쓰기 대상**이기 때문이다(§1-0).
> 읽기 참조와 쓰기 대상은 겹칠 수 없다 — 겹치면 게이트가 **자기 작업에 걸려 넘어진다**(`_header.md`).

---

# GATE-TRUTH-01 — 통합 evidence가 **기계가 관찰한 것**이어야 한다

- actor: **CORE_INFRA_EXECUTOR (sonnet)**
- **개정 이력**
  - 초판(2026-07-15): **자기가 지적한 구멍을 다시 팠다** — §0-4
  - **R1**: 사람 검수 4건 반영(declare 재실행 · 임시 저장소 격리 · temp worktree · manifest 정확 일치)
  - **R2**: 검수자 자체 재검토 3건 반영 — **R1대로 만들면 `declare`가 영원히 통과 못 한다.** §0-5
- **선행**: 없음. **후행**: Trust Origin 부트스트랩(`06C-2`)은 **이 지시서 이후**다.

---

## 0. 문제

### 0-1. 프로덕션 evidence는 **전부 `NOT_RUN`으로 태어난다** (실측)

`trust-origin evidence`의 유일한 호출부는 `TrustOriginCli.cs:58` — `BuildIntegrationEvidence(ctx, gatesPass: false)`. 리터럴 `false`다.
`BuildIntegrationEvidence`(:300~316)는 그 불린 하나를 여덟 필드로 복제한다. `gatesPass: true`는 **self-test fixture에서만** 나온다. 소스 주석이 인정한다:

> `// 통합 게이트 evidence 초안을 파일로 쓴다. 실제 게이트 실행은 외부 절차가 수행한다.`

### 0-2. `declare`는 **파일에 적힌 문자열을 믿는다**

`ValidateIntegrationEvidence`(:280~296)는 baseline 3종은 재계산해 대조한다(**옳다**). 그러나 게이트 결과는 대조하지 않는다 — `if (Read(evidence, "releaseBuild") != "PASS") ...`

> `evidence`가 `NOT_RUN` 초안을 뱉고 → **사람이 손으로 `PASS`라 고쳐 쓰고** → `declare`가 믿는다.
> **Trust Origin의 "통합 검증 통과"는 사람이 타이핑한 문자열이다.**

### 0-3. 등재된 게이트의 절반을 보지도 않는다

게이트 목록이 `BuildIntegrationEvidence` 안에 **하드코딩**돼 있다. `context-pack-integrity`·`gate-clean`·`hs-scan`·`verify-behavior`가 evidence에 **없다**. **누락은 실수가 아니라 구조다.**

### ★ 0-4. 초판의 구멍 — `actualExit`도 사람이 타이핑할 수 있다

초판은 "`declare`가 `verdict` 문자열 대신 `actualExit == expectedExit`로 재판정하라"고 했다. **아무것도 막지 못한다.**

```json
{ "expectedExit": 1, "actualExit": 1, "verdict": "PASS" }   ← 게이트를 한 번도 안 돌리고 타이핑할 수 있다
```

초판의 *"actualExit까지 손으로 맞추면 baseline 해시 3종에서 걸린다"*는 **거짓이다.** `baselineCommit`·WORKSTATE·applier-log 해시는 **게이트 실행 여부와 아무 결속이 없다.**
**`gatesPass` 불린을 정수로 바꾼 것뿐이다.**

### ★★ 0-5. R1의 구멍 — **통합 게이트 세트가 정의된 적이 없다** (R2가 고치는 것)

R1은 *"`GATE-MANIFEST`의 검사를 order대로 실행"*이라고만 썼다. 그런데 매니페스트에는 게이트 세트가 **둘**이고 **전제가 반대**다:

```json
POST-EXECUTOR  order 7: { "command":"gate-clean", "expectedExit": 1,
                          "note":"실행자 직후에는 더러운 것이 정상이다." }   ← 더러운 트리 전제
POST-COMMIT    order 1: { "command":"gate-clean", "expectedExit": 0 }        ← 깨끗한 트리 전제
```

**temp worktree는 baseline commit에서 갓 뽑은 깨끗한 트리다.** `gate-clean` → **exit 0**.
`POST-EXECUTOR`의 기대값은 **1** → `verdict FAIL` → **`declare` exit 1. 영구히.**

> **R1대로 만들면 Trust Origin을 선언할 수 없다.** 그리고 여기서 *"그럼 `expectedExit`를 0으로 바꾸자"*가 나오면 **그게 §2가 금지한 바로 그 짓이다.**

**그리고 매니페스트만 보면 지금 있는 검증이 오히려 줄어든다.** 현재 evidence가 담는데 매니페스트에 **없는** 것:

```
stateTransitionSelfTest(19) · trustOriginSelfTest(24) · recoverySelfTest(8) · legacyCallsiteCount · recovery inspect
```

**하드코딩을 없앴더니 self-test 3종과 legacy callsite 검사가 통째로 사라진다. 회귀다.**
**셋 다 같은 뿌리다 — 통합 게이트 세트를 아무도 정의하지 않았다.**

---

## 1. 고칠 것

### ★ 1-0. `GATE-MANIFEST`에 **`TRUST-ORIGIN-BASELINE` 세트를 신설한다** (사람 결재됨, 2026-07-15)

**신규 `gateId` 추가만 한다.** **기존 `POST-EXECUTOR`·`POST-COMMIT` 블록은 바이트 하나도 건드리지 마라**(완료 기준 15가 검사한다).

- **전제: 깨끗한 트리**(temp worktree). `gate-clean`의 기대값은 **0**이다.
- **`expectedExit`는 "의미"로 정한다. 실측으로 정하지 마라.**
  정상 동작 = 0. **`hs-scan`만 1**("승격 심사 후보가 있다"는 뜻이지 실패가 아니다 — 기존 note 그대로).
  **실측이 이와 다르면 `expectedExit`를 실측에 맞추지 말고 보고해라.** 그건 **상태가 잘못된 것**이지 기준이 잘못된 게 아니다.
- **`command`·`args`는 실재 배선에서 열거해 적는다. 손으로 짐작하지 마라**(`HarnessRegistry` 키 + `CliRouter`의 `args[0]`). self-test 3종의 실제 호출 형태는 **소스에서 확인하고 등재하라.**

**멤버십** (order는 아래 순서. **`gate-clean`이 `measure`보다 앞이다** — `measure`는 트리를 더럽힌다):

| order | command | args | expectedExit | mutatesState | 왜 |
| --- | --- | --- | --- | --- | --- |
| 1 | `build-verify` | — | 0 | false | Release 산출물이 소스와 일치 |
| 2 | `verify-behavior` | — | 0 | false | 동작 보존 |
| 3 | `gate-clean` | `server` | **0** | false | temp worktree는 깨끗하다 |
| 4 | `handoff-integrity` | — | 0 | false | 상태 원본 정합 |
| 5 | `context-pack-integrity` | — | 0 | false | ★ 지금 evidence에 **없는** 것 |
| 6 | `doc-integrity` | — | 0 | false | |
| 7 | `hs-scan` | — | **1** | false | 1이 기대값이다. 0을 기대하면 반려 |
| 8 | `state-transition-callsite-check` | — | 0 | false | `legacyCallsiteCount`의 실체 |
| 9 | `recovery inspect` | — | 0 | false | 브리프 9종에 있는데 매니페스트에 없었다 |
| 10 | state-transition self-test | (실측) | 0 | false | 19 케이스 |
| 11 | trust-origin self-test | (실측) | 0 | false | 24 케이스 |
| 12 | recovery self-test | (실측) | 0 | false | 8 케이스 |
| 13 | `measure` | `dev-pack` | 0 | **true** | **마지막.** 트리를 더럽힌다 |

**제외 — `note`에 사유를 반드시 적는다** (매니페스트 자신의 원칙: *"판정할 수 없는 검사는 넣지 마라"*):

```
scope-check          제외: byproduct(outputs/**, dashboard/data/*)를 범위 이탈로 세어 항상 FAIL.
                           CODEX-GATE-04 §5-1이 ignore set을 넣는다. 고쳐진 뒤 재검토.
di-completion-check  제외: 하위 검사를 --no-build(Debug 기본)로 부르는 결함(CODEX-GATE-04 §1).
                           다른 바이너리를 검사한다. 고쳐진 뒤 재검토.
launch-check         제외: 현재 exit 1인데 원인 미규명. 매니페스트 미등재라 아무도 안 본다(검수자 실측 2026-07-15).
                           원인을 밝히기 전에는 기대값을 정할 수 없다.
claim-check          제외: git grep이 untracked를 못 봐 MISMATCH 오탐(CODEX-GATE-04 §4). 고쳐진 뒤 재검토.
e2e-usage · path-guard-check · call-integrity-check · template-sync-check · project-api-edge-check
                     제외 또는 편입: baseline clean worktree에서 실측한 뒤 판단하라.
                           ★ 실측 결과를 그대로 expectedExit로 박지 마라. 0이 아니면 왜 0이 아닌지 밝히고 보고해라.
```

> **제외한 검사를 `note` 없이 빼면 반려다.** 조용히 빠진 검사가 `context-pack-integrity` 누락을 낳았다.

### ★ 1-1. `GateRunner` — evidence와 declare가 **같은 코드로** 게이트를 실행한다

신규 파일 **`server/GateRunner.cs`**. **하네스가 아니다**(`HarnessRegistry`에 등록하지 않는다). evidence 생성기다.

```
GateRunner.Observe(baselineCommit, manifestPath, gateId="TRUST-ORIGIN-BASELINE") → GateObservation[]

 1. source 저장소 선행검사
      · HEAD == baselineCommit          아니면 exit 2
      · tracked 변경 0 (git diff --quiet HEAD)   아니면 exit 2
      · untracked는 목록으로 기록만 한다 (판정하지 않는다)
 2. canonical blob hash 기록 (전): WORKSTATE.json · applier-log
 3. ★ $TEMP에 git worktree 생성   git worktree add --detach <temp> <baselineCommit>
 4. temp worktree에서 dotnet build server -c Release          ← --no-build 금지
 5. TRUST-ORIGIN-BASELINE 의 검사를 order대로 실행 (cwd = temp worktree)
 6. 게이트마다 관찰 기록 (아래 스키마)
 7. ★ temp worktree 폐기   git worktree remove --force  +  git worktree prune
      ⚠ worktree 생성은 source의 .git/worktrees/ 에 메타데이터를 남긴다. 죽어도 남지 않게 하라
        (finally 블록 + 시작 시 prune). stale worktree가 쌓이면 다음 실행이 죽는다
 8. canonical blob hash 기록 (후) → 전과 다르면 exit 2 (fail-closed)
 9. source가 여전히 tracked-clean 인지 확인 → 아니면 exit 2
```

**왜 temp worktree인가**: `measure dev-pack`은 `mutatesState: true`다 — 실측으로 `dashboard/data/dev-pack/{measurement,run-log,workflow-state}.json`을 바꾼다(검수자 2026-07-15 확인). **현재 워크트리에서 돌리면 evidence 실행이 트리를 더럽히고, 사람이 손으로 원복해야 한다. 사람이 다시 절차의 일부가 된다.**

**게이트 관찰 스키마** (evidence `schemaVersion: 2`):

```json
{
  "gateId": "TRUST-ORIGIN-BASELINE", "order": 5,
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
- **timeout은 명시적 실패다.** 게이트마다 timeout을 두고, 넘으면 `timedOut: true` + `verdict: FAIL`. **조용히 매달리지 마라** — 빈 인자 `dotnet run`이 웹서버를 띄워 실행자를 12분간 교착시킨 실측 사고가 있다.
- 기존 evidence의 `stateTransitionSelfTest`·`trustOriginSelfTest`·`recoverySelfTest`·`legacyCallsiteCount` **별도 필드는 없앤다.** 게이트 배열(order 8·10·11·12)이 흡수한다. **불린 복제 금지.**

### ★ 1-2. `declare`가 **게이트를 다시 돌려서 evidence와 대조한다**

**evidence 파일의 값은 어느 것도 신뢰하지 않는다. `actualExit`도 포함이다.**

```
trust-origin declare --evidence <file>

 0. ★ 자기 출처 검사 (R2 신설 — R1이 빠뜨렸다)
      · HEAD == evidence.baselineCommit          아니면 exit 2
      · source tracked 변경 0                    아니면 exit 2
      ← 이게 없으면 손댄 declare 바이너리가 자기 재실행 결과를 자기가 판정한다
 1. schemaVersion != 2                                     → exit 1
 2. baseline 3종 재계산 대조 (지금 하는 것 — 유지)          → 불일치 exit 1
 3. gateManifestSha256 / gateSetSha256 재계산 대조         → 불일치 exit 1
 4. ★ GateRunner 재실행 (같은 baseline · 같은 manifest · 같은 gateId)
 5. ★ 재실행 관찰값 vs evidence 관찰값 비교                 → 불일치 exit 1 "gate-observation-mismatch"
 6. 모든 게이트 verdict PASS 인가                           → 아니면 exit 1 "integration-gate-failed"
 7. legacy failure set 재계산 (지금 하는 것 — 유지)
 8. 전부 통과할 때만 record candidate 생성
```

**5번의 비교 키** (이 목록 밖의 필드로 판정하지 마라):

```
gateId · order · command · canonical args · expectedExit · mutatesState
actualExit · timedOut · stdoutCanonicalSha256 · stderrCanonicalSha256
```

> **evidence는 사람이 검토하라고 있는 첫 관찰 기록이다. 판정의 근거가 아니다.**
> **판정의 근거는 `declare`가 방금 자기 손으로 돌린 결과다.**

**`stdoutCanonicalSha256`은 왜 따로 있는가 — 이게 없으면 declare가 영원히 실패한다.**
게이트 stdout에는 시각·temp 경로·소요시간이 섞인다(`measuredAt` 등). 원본 해시를 비교 키로 쓰면 **두 번 돌릴 때마다 무조건 불일치**다.

- **휘발 필드 목록을 코드에 명시 선언한다**(`measuredAt`·`startedAt`·`finishedAt`·`durationMs`·`elapsedMs`·절대경로). **정규식으로 뭉개지 마라.**
- temp worktree 경로는 고정 토큰(`<TEMP_WORKTREE>`)으로 치환한다.
- 원본 `stdoutSha256`도 기록한다(포렌식용). **단 비교 키로 쓰지 않는다.**
- ⚠ **canonical화 후에도 결정적이지 않은 게이트가 있으면 비교에서 빼지 말고 보고해라.** 빼는 것은 **검사를 죽이는 것**이다. 완화는 **사람이 결정한다.**

### ★ 1-3. manifest ↔ evidence는 **순서 있는 정확 일치**다 (부분집합 아님)

`evidence ⊇ manifest`(부분집합)를 허용하면 **임의 게이트·중복 게이트·다른 인자 조합을 끼워 넣을 수 있다.**

```
canonical identity = (gateId, order, command, canonical args, expectedExit, mutatesState)
누락 0 · 중복 0 · 예상 밖 항목 0 · 순서 불일치 0     → 하나라도 어긋나면 exit 1
```

evidence와 Trust Origin record에 **둘 다** 넣는다:

```
gateManifestSha256   GATE-MANIFEST.json 원본 바이트의 sha256
gateSetSha256        TRUST-ORIGIN-BASELINE 튜플을 order대로 canonical 직렬화한 것의 sha256
```

> **"`GATE-MANIFEST`가 정본"이 산문이면 안 지켜진다. 해시로 결속해야 지켜진다.**

### 1-4. `stateMutated` 하나로는 뜻이 모호하다 — **넷으로 쪼갠다**

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
- ❌ **★ 기존 `POST-EXECUTOR`·`POST-COMMIT` 블록 수정.** **바이트 하나도.** 신규 `gateId` **추가만** 허용된다(사람 결재 2026-07-15).
- ❌ **★ `expectedExit`를 실측에 맞춰 정하는 것.** 기대값은 **의미로** 정한다. 실측이 다르면 **상태가 잘못된 것**이다 — 보고해라. **검사를 실측에 맞추면 그 검사는 그 순간 죽는다.**
- ❌ **제외한 검사를 `note` 없이 빼는 것.** 조용한 누락이 지금 이 사태를 만들었다.
- ❌ **`--force`·`--assume-pass`·`--skip-gates`·`--no-replay` 류 우회 플래그 신설.** 사람이 손으로 evidence를 써서 통과하는 경로를 **하나도 남기지 마라.**
- ❌ **★ 프로덕션 저장소에서 `trust-origin declare` 실행 / Trust Origin record 생성.** 부트스트랩은 **사람이 한다**(land gate 12번). 시험은 **폐기 가능한 임시 저장소**에서만.
- ❌ **결정적이지 않은 게이트를 조용히 비교 대상에서 빼는 것.** 보고해라.
- ❌ **`--no-build` 사용.** Release 산출물이 낡으면 게이트 넷이 exit 2다(실측 함정, 두 세션 연속 사고).
- ❌ **`--emit-hashes` 제작** — `CODEX-GATE-04` §5-4 소관. **중복 제작 금지.**
- ❌ **무접촉**: `server/Harness/**` 전부 · `server/StateApplierCli.cs`·`server/Cli/**`·`server/Program.cs`(06C-1) · `dashboard/` · `WORKSTATE.json`·`applier-log`.
- ❌ push·결재·반입·발사.

---

## 3. 완료 기준 (exit code)

> **★ 구멍 재현(2·3)은 반드시 `$TEMP`의 폐기 가능한 임시 저장소에서 한다.** `git clone` 또는 `git worktree add --detach`.
> **프로덕션 저장소에는 Trust Origin record가 단 한 번도 생기지 않는다.**

```text
 1. dotnet build server -c Release                                          → 0, warning 0

 2. ★ 구멍 실증 (임시 저장소, 고치기 전 코드):
      trust-origin evidence --out <abs>     → 모든 게이트 NOT_RUN 초안
      → 손으로 releaseBuild/reconciliationFixtures/docIntegrity="PASS",
        devPack.violationCount=0, legacyCallsiteCount=0 으로 고친다
      → trust-origin declare --evidence <abs>
      → ★ 통과한다. record가 생긴다. 출력을 보고에 그대로 붙여라.

 3. ★ 초판 설계도 뚫린다는 실증 (임시 저장소, 고친 뒤 코드):
      게이트를 돌리지 않고 schemaVersion 2 evidence를 만들어
      actualExit 를 전부 expectedExit 와 같게, verdict 를 PASS 로 타이핑한다
      (baseline 3종 해시는 실제 값 그대로 — 조작할 필요조차 없다)
      → trust-origin declare --evidence <abs>       → ★ exit 1 "gate-observation-mismatch"
      ※ §1-2의 재실행이 없으면 이 시험이 통과해 버린다. 이 시험이 이 지시서의 심장이다.

 4. ★ HEAD != baselineCommit 인 저장소에서 declare                         → exit 2 (자기 출처 검사)
      ★ tracked 변경이 있는 저장소에서 declare                             → exit 2

 5. 정상 evidence: trust-origin evidence --out <절대경로>                   → 0
      · executionIsolation = TEMP_GIT_WORKTREE
      · TRUST-ORIGIN-BASELINE 13개가 order대로 전부 있다 (누락0 중복0 예상밖0)
      · ★ context-pack-integrity 가 있다 (order 5)
      · ★ hs-scan: expectedExit 1 / actualExit 1 / verdict PASS   ← 0을 기대하지 않는다
      · ★ self-test 3종 + state-transition-callsite-check 가 게이트 배열에 있다 (별도 필드 아님)
      · gateManifestSha256 · gateSetSha256 이 있다
      · sourceWorkspaceMutated=false · canonicalWorkstateMutated=false
        canonicalApplierLogMutated=false · gateWorkspaceMutated=true

 6. 정상 declare (임시 저장소): 5의 evidence 그대로                         → 0, record 생성
      · 재실행이 실제로 일어났다는 증거 (재실행 타임스탬프 > evidence 타임스탬프)

 7. ★ evidence gate 배열에 항목 추가 / 삭제 / 순서 변경 (각각)              → 전부 exit 1

 8. ★ GATE-MANIFEST를 바꾼 임시 저장소 + 옛 evidence 로 declare             → exit 1
      (gateManifestSha256 / gateSetSha256 결속의 증거. 코드 검토로 갈음하지 마라)

 9. 게이트 하나를 실제로 실패시킨 baseline 에서 evidence                    → 그 게이트 verdict FAIL
      (예: 미발사 지시서의 requiredInput sha256 을 틀리게)                  → declare exit 1

10. 결정성: 같은 baseline 에서 evidence 2회                                → 게이트 집합·actualExit·
      canonical 해시 전부 동일 (원본 stdoutSha256 은 달라도 된다)
      ※ canonical화 후에도 다른 게이트가 있으면 → 비교에서 빼지 말고 보고

11. timeout: 응답하지 않는 게이트를 fixture로 주입                          → timedOut=true, verdict FAIL,
      프로세스가 끝난다 (매달리지 않는다)

12. worktree 위생: evidence 실행 중 강제 종료 후 재실행                     → 정상 동작
      (stale worktree가 쌓이지 않는다. .git/worktrees 확인)

13. 격리: evidence·declare 전후 프로덕션 저장소에서
      · WORKSTATE.json / applier-log  blob hash 동일   (git hash-object 로 확인)
      · tracked 변경 0                                 (git status 는 프록시다)
      · outputs/trust-origin/ 에 record 없음           ★ 프로덕션 record 미생성

14. trust-origin --self-test                                              → 0. 기존 24 케이스 회귀 없음 + 신규
15. ★ git diff 로 GATE-MANIFEST의 POST-EXECUTOR·POST-COMMIT 블록 무변경 실증
      (신규 gateId 추가분만 diff에 나온다)
16. handoff-integrity / context-pack-integrity / doc-integrity / gate-clean server → 0 / 0 / 0 / 0
17. measure dev-pack (-c Release)                                         → 0, violationCount 0
```

> **⚠ 시험용 파일 수정은 `$TEMP` 사본에서.** 저장소 안에서 시험하다 **`docs/wiki` 42파일을 지운 사고**가 있었다.
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
evidence의 `actualExit`를 믿기 · `declare`에서 재실행 생략 · 게이트 목록 하드코딩 · manifest 비교를 부분집합으로 ·
`hs-scan`을 exit 0 기대로 판정 · **기존 게이트 세트의 `expectedExit` 수정** · **`expectedExit`를 실측에 맞추기** ·
제외한 검사를 `note` 없이 빼기 · 결정적이지 않은 게이트를 조용히 비교에서 빼기 ·
완료 기준 3번(초판 설계도 뚫린다는 실증) 건너뛰기 · **프로덕션 저장소에서 `declare`를 돌려 record 만들기.**

**자진 신고 없이 하면 반려다. 신고하면 감점이 아니다.**
**요구가 서로 모순되거나 빠진 게 있으면 완화하지 말고 보고해라.** — **초판과 R1이 실제로 모순이었다**(§0-4·§0-5). **다음은 네 차례다.**

---

## 허용 파일 (allowlist)

- server/TrustOriginCli.cs
- server/GateRunner.cs
- docs/handoff/GATE-MANIFEST.json
- docs/handoff/BASELINE-CHANGES.md
- docs/verification/gate-truth-01-integration-evidence-truth.md
- docs/handoff/queue/directive-GATE-TRUTH-01-integration-evidence-truth.md

> **`docs/handoff/BASELINE-CHANGES.md`는 append만.** 통째로 다시 쓰면 반려다(저장소에 이미 깨진 한글이 박혀 있어 재작성하면 조용히 오염된다 — `CLAUDE.md`).
> `GATE-MANIFEST`에 세트를 신설하는 것은 **기준 추가**다. **①주체(사람 결재 2026-07-15, choi) ②근거(§0-5 — R1대로면 declare가 영구 실패) ③되돌리는 법(추가한 `TRUST-ORIGIN-BASELINE` 블록만 지우면 정확히 이전 상태)** 을 append 하라.

> `server/GateRunner.cs`는 **신규 파일**이고 **하네스가 아니다** — `HarnessRegistry`에 **등록하지 않는다.**
> (`CODEX-GATE-04`의 "신규 하네스 제작 금지"는 하네스 얘기다. 이건 evidence 생성기다.)
> `docs/handoff/GATE-MANIFEST.json`은 **신규 `gateId` 추가만** 허용된다. **기존 블록 무접촉**(완료 기준 15).
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
- **★ 완료 기준 3번의 실제 출력** — **초판 설계(`actualExit` 재판정)로도 뚫린다는 증거.** 없으면 재실행의 필요가 증명되지 않는다
- 완료 기준 4·7·8·9·11·12의 **기대 exit vs 실제 exit 표**
- **evidence 파일 실물 1개**(완료 기준 5)
- **`TRUST-ORIGIN-BASELINE` 세트의 `expectedExit`를 어떻게 정했는지** — **의미로 정했다는 근거.** 실측과 달랐던 게 있으면 **그대로 적어라**
- **제외한 검사와 `note` 사유 전체**
- 완료 기준 10의 **canonical화 규칙(휘발 필드 목록)** 과 결정성 결과
- 완료 기준 13의 **blob 해시 대조 결과**, 완료 기준 15의 **git diff**
- **`## 지표는 만족했으나 목적은 미달인 부분`**(ADR-005). 없으면 "없음".

**자기보고는 증거가 아니다.** 검수자가 재실행한다. 못 한 시험은 `NOT_VERIFIED`. **숨기면 반려다.**
