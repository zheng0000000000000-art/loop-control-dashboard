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
    "server/TrustOriginCli.cs",
    "server/Harness/HandoffIntegrityChecker.cs"
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
- **개정 이력** — *이 지시서는 세 번 틀렸다. 네 번째가 되지 마라.*
  - 초판: **자기가 지적한 구멍을 다시 팠다**(`actualExit`도 타이핑할 수 있다) — §0-4
  - R1: 사람 검수 4건 반영 · **그러나 `declare`가 영원히 통과 못 하는 설계였다** — §0-5
  - R2: 게이트 세트 신설 · **그러나 P0 둘이 남았다** — §0-6
  - R3: reconciliation 분리 · self-test 재귀 차단 · **그러나 같은 표 안에 지뢰를 둘 더 심었다** — §0-7
  - R4: **게이트 분류 규칙** 명문화 · `hs-scan`·`recovery inspect` 제거 · **그러나 replay 비교 키가 성립하지 않았다** — §0-8
  - **R5**: raw stdout 해시 → **게이트별 semantic projector** · **게이트마다 전후 mutation 검사** · 실행 가능한 wall-clock fixture · candidate/pending **canonical schema** · **record·verify에 observation 결속** · **검증용 temp baseline ≠ Baseline A**
- **선행**: 없음. **후행**: Trust Origin 부트스트랩(`06C-2`)은 **이 지시서 이후**다.

---

## 0. 문제

### 0-1. 프로덕션 evidence는 **전부 `NOT_RUN`으로 태어난다** (실측)

`trust-origin evidence`의 유일한 호출부는 `TrustOriginCli.cs:58` — `BuildIntegrationEvidence(ctx, gatesPass: false)`. 리터럴 `false`다. `gatesPass: true`는 **self-test fixture에서만** 나온다. 소스 주석이 인정한다:

> `// 통합 게이트 evidence 초안을 파일로 쓴다. 실제 게이트 실행은 외부 절차가 수행한다.`

### 0-2. `declare`는 **파일에 적힌 문자열을 믿는다**

`ValidateIntegrationEvidence`(:280~296)는 baseline 3종은 재계산해 대조한다(**옳다**). 그러나 게이트 결과는 대조하지 않는다 — `if (Read(evidence, "releaseBuild") != "PASS") ...`

> **Trust Origin의 "통합 검증 통과"는 사람이 타이핑한 문자열이다.**

### 0-3. 게이트 목록이 하드코딩돼 있다

`context-pack-integrity`·`gate-clean`·`hs-scan`·`verify-behavior`가 evidence에 **없다**. **누락은 실수가 아니라 구조다.**

### ★ 0-4. 초판의 구멍 — `actualExit`도 사람이 타이핑할 수 있다

```json
{ "expectedExit": 1, "actualExit": 1, "verdict": "PASS" }   ← 게이트를 한 번도 안 돌리고 타이핑할 수 있다
```

초판의 *"actualExit까지 손으로 맞추면 baseline 해시 3종에서 걸린다"*는 **거짓이다.** baseline 해시는 **게이트 실행 여부와 아무 결속이 없다.** `gatesPass` 불린을 정수로 바꾼 것뿐이다.

### ★ 0-5. R1의 구멍 — 통합 게이트 세트가 정의된 적이 없다

매니페스트의 두 세트는 **전제가 반대**다. `POST-EXECUTOR`는 `gate-clean expectedExit=1`(더러운 트리), `POST-COMMIT`은 `0`(깨끗한 트리). **temp worktree는 깨끗하다** → `gate-clean` 0 → `POST-EXECUTOR` 기대 1과 불일치 → **`declare` 영구 exit 1.**

### ★★ 0-6. R2의 구멍 — P0 둘 (R3가 고치는 것)

#### P0-1. `handoff-integrity`를 게이트 배열에 넣으면 **판사가 둘이 되고, 언젠가 부트스트랩이 막힌다**

R2는 `handoff-integrity`를 `expectedExit=0`으로 세트에 넣었다. **두 가지가 잘못됐다.**

**① 재발명이다.** `TrustOriginCli`에는 **이미 reconciliation exact binding이 있다**(`:293~295`):

```csharp
if (EntrySetHash(evidenceFailures) != FailureSetHash(failures)) return "legacy-failure-set-mismatch";
if (Read(evidence, "baselineReconciliationReportSha256") != ReconciliationReportHash(recon)) return "...";
```

**평범한 exit-code 게이트로 또 넣으면 같은 대상을 두 메커니즘이 판정한다.**

**② `expectedExit=0`은 "baseline은 항상 정합"을 하드코딩하는 것이다.** Trust Origin은 **정상 모드가 둘**이다:

```
VERIFIED_CONSISTENT   reconciliation exit 0, failures 0
DECLARED_LEGACY_GAP   reconciliation exit 1, 정확히 선언된 failure만 존재
```

`expectedExit=0`이면 두 번째 모드에서 **부트스트랩이 영구 차단된다** — **R1의 `gate-clean`과 똑같은 지뢰다.**
그렇다고 **`expectedExit=1`은 더 나쁘다** — **어떤 무결성 실패든 exit 1이기만 하면 통과한다.**

> **실측(검수자, 2026-07-15, HEAD)**: `handoff-integrity` **exit 0** · `verdict PASS` · `failureCount 0` · `reconciliation PASS`
> · `warnings 1건`(`TEST-DI0001-2` / `duplicate-success-in-log`).
> `DI0004-BLOCKED-CODEX`의 legacy gap은 **BC-003(사람 결재)으로 이미 닫혔다**(커밋 `80e9370`).
> **즉 현재 baseline은 `VERIFIED_CONSISTENT`다.** **그래도 두 모드를 다 구현하라.** 한쪽만 만들면 **다음 baseline에서 또 막힌다.**
> **오늘 안 터진다고 지뢰가 아닌 게 아니다.**

#### ★ P0-2. `declare → GateRunner → trust-origin --self-test → DeclareCore → GateRunner → …` **무한 재귀**

현재 self-test 24케이스는 **`DeclareCore`를 직접 호출한다**(`:393`·`:402`·`:425`·`:464`·`:581`·`:610` …).
새 세트에는 **`trust-origin --self-test`가 게이트로 들어간다.** 그리고 새 `declare`는 **GateRunner를 재실행한다.**

```
declare → GateRunner → trust-origin --self-test → CaseConsistent → DeclareCore → GateRunner → trust-origin --self-test → …
```

**결과는 timeout 또는 프로세스 폭증이다.** R2는 이걸 못 봤다.

### ★★★ 0-7. R3의 구멍 — **같은 표 안에 지뢰를 둘 더 심었다** (R4가 고치는 것)

R3는 P0-1을 고치면서 `handoff-integrity`를 뺐다. **그런데 바로 그 표에 같은 종류를 둘 더 남겨뒀다.**

#### `hs-scan expectedExit=1` — **지뢰가 아니라 replay 자체를 깬다** (실측)

```csharp
// server/Harness/HsScanCli.cs
else if (daysSince >= 1.0) signals.Add($"S3 정기: 마지막 심사 후 {daysSince:F1}일 경과");
var triggered = signals.Count > 0;
...
return triggered ? 1 : 0;
```

**exit code가 `DateTime.Now`에 의존한다.**

- **evidence를 T에 만들고 `declare`가 T+Δ에 replay하면 `actualExit`가 달라질 수 있다** → `gate-observation-mismatch` → **정상 baseline인데 declare가 실패한다.**
- stdout에 `"마지막 심사 후 N.N일 경과"`가 박힌다 → **canonical 해시가 매 실행 달라진다.**
- HS-GATE를 수행해 후보를 처리하면 `hs-scan` → **0**. `expectedExit=1`과 어긋나 **verdict FAIL.**

#### `recovery inspect expectedExit=0` — **판사가 셋이 되고, 상태로 뒤집힌다** (실측)

```csharp
// server/RecoveryCli.cs — ExitForReport
return code == 2 ? 2 : code == 1 || pending.Count > 0 ? 1 : 0;
```

**reconciliation 결과에서 파생된다** — §1-4가 이미 정확히 결속하는 그 대상이다. **quarantine pending 1건이면 1로 뒤집힌다.**

> **셋(R1의 `gate-clean`, R2의 `handoff-integrity`, R3의 `hs-scan`·`recovery inspect`)이 전부 같은 실패다.**
> **exit code가 "틀렸다"가 아니라 "지금 이런 상태다"를 뜻하는 검사를, 고정 `expectedExit` 게이트에 넣은 것.**
> **분류 규칙이 없으니 매번 다시 심는다. §1-0′가 그 규칙이다.**

### ★★★ 0-8. R4의 구멍 — **replay 비교 키가 성립하지 않는다** (R5가 고치는 것)

R4는 `actualExit` · `timedOut` · **`stdoutCanonicalSha256`** 로 evidence와 재실행을 비교하게 했다.
**"휘발 필드 몇 개(`measuredAt`·`startedAt`·경로)를 지우면 결정적이 된다"고 가정했다. 실측으로 틀렸다** — §1-3.

`BuildVerifyCli.cs:31`이 **매 실행 새 GUID temp 디렉터리**를 만들고, `buildProject`·`outputDir`에 **그 절대경로**를, `stdoutTail`·`stderrTail`에 **`dotnet build`의 자유 형식 원문 1200자**를 담는다.
**목록으로 지울 수 없고, 정규식으로 뭉개면 의미 있는 오류까지 지운다** — 그리고 **R4 자신이 정규식 스크럽을 금지한다.**

**그리고 R4는 셋을 더 놓쳤다:**

- **게이트별 mutation 검사가 없다.** 마지막 `measure`가 트리를 더럽히므로 **앞 게이트의 불법 부수효과가 "예상된 measure 변경"으로 숨는다** — §1-1′.
- **완료 기준 7-B(`+48h`)가 allowlist 안에서 실행 불가능하다.** `HsScanCli`는 `DateTime.Now`를 직접 쓰는데 `server/Harness/**`가 무접촉이다 — **clock 주입도, 하네스 수정도 못 한다** — §3 7-B.
- **record가 evidence 해시를 하나도 검증하지 않는다.** evidence는 저장소 밖이라 **선언 후 사라진다.** 그러면 record는 **"무엇을 검증했는지"를 스스로 증명하지 못한다** — §1-9.
- (오기) §1-1의 흡수 order를 `7·9·10·11`로 적었다. **10개 배열에서는 `6·7·8·9`다.**

---

## 1. 고칠 것

### ★★ 1-0′. 게이트 배열에 넣을 수 있는 검사 — **분류 규칙** (R4 신설. 이 규칙이 이 지시서의 뼈대다)

**셋을 전부 만족해야 게이트 배열에 들어간다:**

```
C1. exit code가 "정확성"을 뜻한다        — "지금 이런 상태다"를 뜻하면 안 된다
C2. 같은 baseline에서 wall-clock과 무관하게 같은 exit를 낸다   ★ replay의 전제조건
C3. 다른 관찰과 판정 대상이 겹치지 않는다  — 판사가 둘이 되면 안 된다
```

**하나라도 어기면 게이트 배열이 아니라 §1-4의 exact-binding 관찰로 간다.**

| 검사 | C1 | C2 | C3 | 판정 |
| --- | --- | --- | --- | --- |
| `build-verify` · `verify-behavior` · `gate-clean` · `context-pack-integrity` · `doc-integrity` · `state-transition-callsite-check` · self-test 3종 · `measure dev-pack` | ✅ | ✅ | ✅ | **게이트 배열** |
| `handoff-integrity` | ❌ 상태 | ✅ | ❌ 겹침 | **관찰** (§1-4) |
| `hs-scan` | ❌ 상태 | **❌ `DateTime.Now`** | ✅ | **관찰** (§1-4′) |
| `recovery inspect` | ❌ 상태 | ✅ | ❌ reconciliation 파생 | **관찰** (§1-4″) |

> **⚠ 새 검사를 배열에 넣기 전에 이 표를 채워라. 못 채우면 넣지 마라.**
> **⚠ C2를 어기는 검사가 배열에 하나라도 있으면 `declare`의 replay 대조가 무의미해진다.** 시각이 지나면 정상 baseline이 FAIL이 된다.

### ★ 1-0. `GATE-MANIFEST`에 `TRUST-ORIGIN-BASELINE` 세트를 신설한다 (사람 결재: choi, 2026-07-15)

**신규 `gateId` 추가만.** **기존 `POST-EXECUTOR`·`POST-COMMIT` 블록은 바이트 하나도 건드리지 마라**(완료 기준 18이 `git diff`로 검사).

- **전제: 깨끗한 트리**(temp worktree). `gate-clean` 기대값은 **0**.
- **`expectedExit`는 "의미"로 정한다. 실측으로 정하지 마라.** **배열에 들어온 검사는 전부 정상 동작 = 0이다.**
  **0이 아닌 기대값이 필요하다고 느끼는 순간, 그 검사는 §1-0′의 C1을 어긴 것이다 — 배열이 아니라 관찰로 보내라.**
  실측이 0이 아니면 **기대값을 실측에 맞추지 말고 보고해라.** 그건 **상태가 잘못된 것**이다.
- **★ `command`는 라우터가 보는 `args[0]`이다. 복합 명령을 한 문자열로 쓰지 마라.**

```json
{ "command": "recovery", "args": ["inspect"] }      ✅
{ "command": "recovery inspect", "args": [] }        ❌ 라우터가 못 찾는다
```

**멤버십 (10개)** — **전부 `expectedExit = 0`이다**(§1-0′). `gate-clean`이 `measure`보다 앞이다(`measure`는 트리를 더럽힌다):

| order | command | args | expectedExit | mutatesState | 왜 |
| --- | --- | --- | --- | --- | --- |
| 1 | `build-verify` | `[]` | 0 | false | Release 산출물이 소스와 일치 |
| 2 | `verify-behavior` | `[]` | 0 | false | 동작 보존 |
| 3 | `gate-clean` | `["server"]` | 0 | false | temp worktree는 깨끗하다 |
| 4 | `context-pack-integrity` | `[]` | 0 | false | ★ 지금 evidence에 **없는** 것 |
| 5 | `doc-integrity` | `[]` | 0 | false | |
| 6 | `state-transition-callsite-check` | `[]` | 0 | false | `legacyCallsiteCount`의 실체 |
| 7 | `state-transition` | `["--self-test"]` | 0 | false | 19 케이스 |
| 8 | `trust-origin` | `["--self-test"]` | 0 | false | 24 케이스 — **§1-5 재귀 차단 필수** |
| 9 | `recovery` | `["--self-test"]` | 0 | false | 8 케이스 |
| 10 | `measure` | `["dev-pack"]` | 0 | **true** | **마지막.** 트리를 더럽힌다 |

> **★ `handoff-integrity`·`hs-scan`·`recovery inspect`는 이 배열에 없다. 전부 일부러다**(§1-0′ · §1-4).
> **★ 배열에 `expectedExit != 0`인 항목이 하나라도 있으면 반려다.** 0이 아닌 기대값이 필요하면 **그 검사는 관찰로 가야 한다.**
> **⚠ self-test 3종의 실제 호출 형태는 소스에서 확인하고 등재하라.** 위 표는 라우터 배선 기준 추정이다. **짐작으로 박지 마라.**

**제외 — `note`에 사유를 반드시 적는다**(매니페스트 자신의 원칙: *"판정할 수 없는 검사는 넣지 마라"*):

```
handoff-integrity    제외(C1·C3 위반): exit code가 상태 보고다. §1-4 baselineReconciliation exact binding으로 간다.
                          (expectedExit=0 은 DECLARED_LEGACY_GAP baseline을 영구 차단하고,
                           expectedExit=1 은 어떤 무결성 실패든 통과시킨다. 둘 다 틀렸다.)
hs-scan              제외(★C2 위반 — 실측): HsScanCli 가 DateTime.Now 로 S3 정기 신호를 만든다.
                          `daysSince >= 1.0` → signals → `return triggered ? 1 : 0`.
                          exit code가 시각에 의존한다 → evidence(T)와 declare replay(T+Δ)의 actualExit가
                          달라져 정상 baseline이 gate-observation-mismatch 로 죽는다.
                          stdout의 "마지막 심사 후 N.N일 경과"도 canonical 해시를 매 실행 바꾼다.
                          → §1-4′ hsScan 관찰로 간다.
recovery inspect     제외(C1·C3 위반 — 실측): RecoveryCli.ExitForReport 가
                          `code == 2 ? 2 : code == 1 || pending.Count > 0 ? 1 : 0`.
                          reconciliation 결과에서 파생된다 — §1-4가 이미 결속하는 대상이다(판사가 셋이 된다).
                          quarantine pending 1건이면 1로 뒤집힌다. → §1-4″ 관찰로 간다.
scope-check          제외: byproduct(outputs/**, dashboard/data/*)를 범위 이탈로 세어 항상 FAIL (CODEX-GATE-04 §5-1).
di-completion-check  제외: 하위 검사를 --no-build(Debug)로 부르는 결함 — 다른 바이너리를 검사한다 (CODEX-GATE-04 §1).
launch-check         제외(구조적 — 2026-07-15 원인 규명, 앞선 "원인 미규명"을 정정한다):
                          LaunchCheckCli.Run 은 `launch-check <taskId> <transportEvidencePath>` 를 요구한다.
                          인자 없이 부르면 usage 실패로 exit 1이다. 고장난 게 아니다.
                          per-launch 검사(발사 1건의 전송 증거 해시)라 repo-wide 게이트에 넣을 대상이 아니다.
claim-check          제외: git grep이 untracked를 못 봐 MISMATCH 오탐 (CODEX-GATE-04 §4).
e2e-usage · path-guard-check · call-integrity-check · template-sync-check · project-api-edge-check
                     baseline clean worktree에서 실측한 뒤 편입/제외를 판단하라.
                     ★ 실측 결과를 그대로 expectedExit로 박지 마라. 0이 아니면 왜 0이 아닌지 밝히고 보고해라.
```

> **제외한 검사를 `note` 없이 빼면 반려다.** 조용한 누락이 `context-pack-integrity` 실종을 낳았다.

### ★ 1-1. `GateRunner` — evidence와 declare가 **같은 코드로** 게이트를 실행한다

신규 파일 **`server/GateRunner.cs`**. **하네스가 아니다**(`HarnessRegistry`에 등록하지 않는다).

```
GateRunner.Observe(baselineCommit, manifestPath, gateId="TRUST-ORIGIN-BASELINE") → GateObservation[]

 0. ★ 재진입 가드: 이미 GateRunner 안이면 exit 2 "gate-runner-reentry" (§1-5)
 1. source 저장소 선행검사
      · HEAD == baselineCommit                     아니면 exit 2
      · tracked 변경 0 (git diff --quiet HEAD)      아니면 exit 2
      · untracked는 목록으로 기록만 한다 (판정하지 않는다)
 2. canonical blob hash 기록 (전): WORKSTATE.json · applier-log
 3. $TEMP에 git worktree 생성   git worktree add --detach <temp> <baselineCommit>
 4. ★ temp worktree에서 dotnet build server -c Release  ← 딱 1회
 5. TRUST-ORIGIN-BASELINE 의 검사를 order대로 실행 (cwd = temp worktree)
      ★ 4에서 만든 Release 산출물을 직접 실행한다 (dotnet exec <dll> <cmd> ...)
        게이트마다 dotnet run 을 부르면 build/restore 출력이 게이트 출력에 섞인다 (§1-3)
        ⚠ 이것은 --no-build 우회가 아니다. 4번의 명시적 선행 빌드 산출물을 부르는 것이다
 6. ★ 게이트마다: 실행 전후 tracked tree hash 를 잰다 (§1-1′)
 7. 게이트마다 관찰 기록 (아래 스키마)
 8. temp worktree 폐기   git worktree remove --force  +  git worktree prune
      ⚠ 생성은 source의 .git/worktrees/ 에 메타데이터를 남긴다. 죽어도 안 남게 하라
        (finally 블록 + 시작 시 prune). stale worktree가 쌓이면 다음 실행이 죽는다
 9. canonical blob hash 기록 (후) → 전과 다르면 exit 2 (fail-closed)
10. source가 여전히 tracked-clean 인지 확인 → 아니면 exit 2
```

**왜 temp worktree인가**: `measure dev-pack`은 `mutatesState: true`다 — 실측으로 `dashboard/data/dev-pack/{measurement,run-log,workflow-state}.json`을 바꾼다. **현재 워크트리에서 돌리면 사람이 손으로 원복해야 한다. 사람이 다시 절차의 일부가 된다.**

**게이트 관찰 스키마** (evidence `schemaVersion: 2`):

```json
{
  "gateId": "TRUST-ORIGIN-BASELINE", "order": 4,
  "command": "context-pack-integrity", "args": [],
  "expectedExit": 0, "mutatesState": false,
  "actualExit": 0, "verdict": "PASS", "timedOut": false,
  "startedAt": "...", "finishedAt": "...",

  "semanticObservation": { ... },          ← ★ 게이트별 projector 산출 (§1-3)
  "semanticObservationSha256": "...",      ← ★ 이것만 replay 비교에 쓴다

  "treeHashBefore": "...", "treeHashAfter": "...",   ← ★ §1-1′
  "observedMutation": false, "changedPaths": [],

  "stdoutSha256": "...", "stderrSha256": "..."       ← 포렌식용. 비교하지 않는다
}
```

- **`verdict`는 `actualExit == expectedExit && !timedOut`로 프로그램이 계산한다.** 파일에서 읽지 않는다.
- **timeout은 명시적 실패다.** `timedOut: true` + `verdict: FAIL`. **조용히 매달리지 마라** — 빈 인자 `dotnet run`이 웹서버를 띄워 실행자를 12분간 교착시킨 실측 사고가 있다.
- **`cwd`는 스키마에서 뺐다** — temp worktree 경로는 매번 다르다. 기록하려면 `<TEMP_WORKTREE>` 토큰으로만.
- 기존 `stateTransitionSelfTest`·`trustOriginSelfTest`·`recoverySelfTest`·`legacyCallsiteCount` **별도 필드는 없앤다.** 게이트 배열(**order 6·7·8·9**)이 흡수한다.

### ★★ 1-1′. 게이트마다 **전후 tracked tree**를 잰다 (R5 신설)

**R4는 마지막 `measure` 하나로 `gateWorkspaceMutated: true`만 적었다. 그러면 앞 게이트의 불법 변경이 숨는다:**

```
context-pack-integrity 가 실수로 tracked 파일을 고침  (mutatesState: false 인데)
→ 뒤에 measure 도 tracked 파일을 고침
→ 최종 gateWorkspaceMutated = true
→ "예상된 measure 변경"으로 보인다.  ★ 부수효과가 숨는다
```

```
게이트마다:
  treeHashBefore  = temp worktree 의 tracked tree hash   (git write-tree 또는 동등)
  treeHashAfter   = 실행 후 같은 값
  changedPaths    = 그 게이트가 바꾼 tracked 경로 목록

판정:
  mutatesState == false  AND  treeHashBefore != treeHashAfter
      → exit 2  "undeclared-gate-mutation"          ★ 선언 안 한 부수효과는 fail-closed

  mutatesState == true   AND  changedPaths ⊄ allowedMutationPaths
      → exit 2  "mutation-outside-allowed-paths"    ★ boolean 하나로는 너무 넓다
```

**`GATE-MANIFEST`의 `TRUST-ORIGIN-BASELINE` 항목에 `allowedMutationPaths`를 둔다** (`mutatesState: true`인 것만):

```json
{ "order": 10, "command": "measure", "args": ["dev-pack"],
  "expectedExit": 0, "mutatesState": true,
  "allowedMutationPaths": [
    "dashboard/data/dev-pack/measurement.json",
    "dashboard/data/dev-pack/run-log.json",
    "dashboard/data/dev-pack/workflow-state.json"
  ] }
```

**evidence 최상위에도 boolean 하나로 끝내지 마라:**

```json
"gateWorkspaceMutated": true,
"gateWorkspaceChangedPaths": [ "dashboard/data/dev-pack/measurement.json", ... ],
"undeclaredMutationCount": 0
```

### ★ 1-2. `declare`가 게이트를 **다시 돌려서** evidence와 대조한다

**evidence 파일의 값은 어느 것도 신뢰하지 않는다. `actualExit`도 포함이다.**

```
trust-origin declare --evidence <file>

 0. ★ 자기 출처 검사
      · HEAD == evidence.baselineCommit          아니면 exit 2
      · source tracked 변경 0                    아니면 exit 2
      ← 없으면 손댄 declare 바이너리가 자기 재실행 결과를 자기가 판정한다
 1. schemaVersion != 2                                     → exit 1
 2. baseline 3종 재계산 대조 (기존 로직 유지)              → 불일치 exit 1
 3. gateManifestSha256 / gateSetSha256 재계산 대조         → 불일치 exit 1
 4. ★ GateRunner 재실행 (같은 baseline · manifest · gateId)
 5. ★ 재실행 관찰값 vs evidence 관찰값 비교                 → 불일치 exit 1 "gate-observation-mismatch"
 6. 모든 게이트 verdict PASS 인가                           → 아니면 exit 1 "integration-gate-failed"
 7. ★ 관찰 exact 대조 (게이트 배열이 아니다 — §1-4 / 1-4′ / 1-4″)
      · baselineReconciliation  (HandoffIntegrityChecker 직접 재실행)
      · hsScan                  (후보 집합만. 시각 파생 신호는 비교에서 뺀다)
      · recoveryPending         (pending 집합)
 8. ValidateDeclarationCore (순수 검증 — GateRunner 호출 없음, §1-5)
 9. 전부 통과할 때만 record candidate 생성
```

> **★ 3번의 `gateManifestSha256`은 `baselineCommit`의 blob에서 계산한다** — 워킹트리 파일이 아니라
> `git show <baselineCommit>:docs/handoff/GATE-MANIFEST.json`. **워킹트리를 해싱하면 커밋과 다른 것을 결속하게 된다.**
> (0단계가 tracked-clean을 강제하므로 지금은 같지만, **같다고 가정하지 말고 blob에서 읽어라.**)

**5번의 비교 키** (이 목록 밖의 필드로 판정하지 마라):

```
gateId · order · command · canonical args · expectedExit · mutatesState
actualExit · timedOut · semanticObservationSha256            ★ raw stdout/stderr 해시는 비교하지 않는다 (§1-3)
observedMutation · changedPaths                              ★ §1-1′
```

> **evidence는 사람이 검토하라고 있는 첫 관찰 기록이다. 판정의 근거가 아니다.**
> **판정의 근거는 `declare`가 방금 자기 손으로 돌린 결과다.**

### ★★ 1-3. **raw stdout 해시로는 replay가 성립하지 않는다** — 게이트별 `semantic projector` (R5 재작성)

**R4는 "휘발 필드 몇 개를 지우면 결정적이 된다"고 가정했다. 실측으로 틀렸다:**

```csharp
// server/Harness/BuildVerifyCli.cs
var tempRoot = Path.Combine(Path.GetTempPath(), "lfwd-build-verify", Guid.NewGuid().ToString("N"));  // :31
...
["buildProject"] = Path.Combine(tempProjectDir, projectFileName),   // 매번 다른 GUID 절대경로
["outputDir"]    = outputDir,                                       // 매번 다른 GUID 절대경로
["stdoutTail"]   = Tail(result.Stdout, 1200),                       // ★ dotnet build 자유 형식 원문
["stderrTail"]   = Tail(result.Stderr, 1200),                       // ★ 경과시간·restore 상태가 섞인다
```

**`stdoutTail`은 자유 형식 텍스트다. 휘발 필드 목록으로 지울 수 없다.**
**정규식으로 뭉개면 의미 있는 오류까지 지운다 — 그리고 R4 자신이 정규식 스크럽을 금지한다.**

> **`stdoutCanonicalSha256`을 판정의 중심에 두는 설계는 폐기한다.**

**게이트마다 `projector`를 코드에 명시 선언한다.** projector가 **의미 있는 필드만** 뽑아 `semanticObservation`을 만들고, **그 해시만 replay 비교에 쓴다.**

```
stdoutSha256 · stderrSha256      → 포렌식용. 기록만 한다. 판정에 쓰지 않는다
semanticObservationSha256        → ★ evidence ↔ declare 재실행 비교의 유일한 출력 키
```

**projector 목록 (10개 게이트 전부. 하나라도 빠지면 반려):**

| 게이트 | `semanticObservation`에 담는 것 | 반드시 **빼는** 것 |
| --- | --- | --- |
| `build-verify` | `verdict` · `exitCode` · `locked` · `project` · `configuration` · `sourceCopied` | **`buildProject`·`outputDir`(GUID 절대경로) · `stdoutTail`·`stderrTail`(원문)** |
| `verify-behavior` | `behaviorEqual` · snapshot **상대**경로 | 절대경로 · 시각 |
| `gate-clean` | 변경 수 · **canonical 상대경로 집합**(정렬) | 절대경로 |
| `context-pack-integrity` | directive별 `diId`·`verdict`·count (**diId로 정렬**) · 총 count | 시각 · 절대경로 |
| `doc-integrity` | broken count · canonical 문제 집합(정렬) | 절대경로 |
| `state-transition-callsite-check` | legacy callsite count · canonical 위치 집합(정렬) | 절대경로 |
| self-test 3종 | case 이름 · pass 여부 · `casesRun` · `failed` (**case 이름으로 정렬**) | 소요시간 |
| `measure dev-pack` | `violationCount` · `overallStatus` · lifecycle · stage | **`measuredAt`** · 절대경로 |

- **projector는 "무엇을 넣을지"를 선언한다.** allowlist다. **"무엇을 뺄지"(blocklist)로 만들지 마라** — 새 필드가 추가되면 조용히 새어 들어온다.
- **집합은 전부 정렬한다.** 순서가 흔들리면 해시가 흔들린다.
- ⚠ **projector를 통과했는데도 결정적이지 않은 게이트가 있으면 비교에서 빼지 말고 보고해라.** 빼는 것은 **검사를 죽이는 것**이다. 완화는 **사람이 결정한다.**

**그리고 게이트를 `dotnet run`으로 부르지 마라** — 매 호출이 build/restore를 시도해 **그 출력이 게이트 출력에 섞인다.**
**§1-1의 4번에서 Release를 한 번 빌드하고, 그 산출물을 `dotnet exec <dll> <cmd>`로 직접 실행한다.**
**이것은 `--no-build` 우회가 아니다** — 명시적 선행 빌드의 산출물을 부르는 것이다.

### ★ 1-4. reconciliation은 **exit code 게이트가 아니라 exact binding 관찰**이다 (R3 신설)

**게이트 배열에 넣지 마라.** 별도 구조로 기록하고, **`declare`가 `HandoffIntegrityChecker`를 직접 재실행해 정확히 대조한다.**
**기존 메커니즘을 재사용하라** — `EntrySetHash` · `FailureSetHash` · `ReconciliationReportHash`는 **이미 있다. 다시 만들지 마라.**

```json
"baselineReconciliation": {
  "mode": "VERIFIED_CONSISTENT",
  "exitCode": 0,
  "failures": [],
  "warnings": [
    { "code": "duplicate-success-in-log", "subject": "TEST-DI0001-2", "detailSha256": "..." }
  ],
  "reportSha256": "..."
}
```

**두 모드만 정상이다. 그 밖은 전부 exit 1:**

```
VERIFIED_CONSISTENT   exitCode 0 · failures 0                        ← 현재 baseline이 여기다 (실측)
DECLARED_LEGACY_GAP   exitCode 1 · 선언된 failure 집합과 정확히 일치   ← 반드시 함께 구현하라
그 외 (선언 안 된 failure 1건이라도 있으면)                            → exit 1
```

- **`warnings`도 정확히 대조한다.** 현재 1건(`TEST-DI0001-2` / `duplicate-success-in-log`)이 있다. **경고가 늘거나 줄면 그것도 baseline 변화다.**
- **현재 baseline은 `VERIFIED_CONSISTENT`다**(BC-003으로 legacy gap이 닫혔다, 커밋 `80e9370`). **그래도 `DECLARED_LEGACY_GAP` 경로를 함께 만들고 fixture로 시험하라.** 한쪽만 만들면 다음 baseline에서 부트스트랩이 막힌다.

### ★ 1-4′. `hsScan` 관찰 — **후보 집합만 결속한다. 시각은 결속하지 않는다** (R4 신설)

`hs-scan`의 exit code는 **시각 함수**다(`daysSince >= 1.0` → `triggered` → `return triggered ? 1 : 0`).
**exit code도, `signals`도, `lastGate`/`daysSince`도 비교하지 마라 — 시각이 지나면 정상 baseline이 죽는다.**

```json
"hsScan": {
  "exitCode": 1,                     ← 기록만 한다. 판정에 쓰지 않는다
  "failureCaseCount": 0,
  "candidates": [ ... ],
  "candidateSetSha256": "...",       ← ★ 이것만 비교한다
  "timeDerivedSignalsExcluded": true ← 비교에서 뺐다는 것을 evidence에 명시한다
}
```

- **비교 키는 `candidateSetSha256` + `failureCaseCount` 뿐이다.**
- **`exitCode`·`signals`·`lastGate`·`daysSince`는 evidence에 남기되 비교하지 않는다.** **뺐다는 사실을 `timeDerivedSignalsExcluded`로 명시하라 — 숨기지 마라.**
- **왜 아예 빼지 않는가**: 후보 집합이 바뀌면 그건 **진짜 baseline 변화**다. 시각만 빼고 실체는 결속한다.

**★ `candidateSetSha256`의 canonical schema (R5 신설 — 스키마 없는 해시는 해시가 아니다):**

```
넣는 것 (allowlist):
  S1 후보:  signal · failureClass · occurrences · caseIds(정렬)
  S4 후보:  signal · component    · occurrences · caseIds(정렬)

빼는 것:  why · suggestedType · signals · action · lastGate · daysSince
정렬:     signal → failureClass|component → caseId 목록
```

### ★ 1-4″. `recoveryPending` 관찰 (R4 신설 · R5 스키마 확정)

`recovery inspect`의 exit는 reconciliation에서 파생된다(`ExitForReport`). **§1-4가 이미 그 실체를 결속한다.**
**추가로 결속할 실체는 `pending` 집합 하나다.**

**★ `pendingSetSha256`의 canonical schema — 절대경로를 넣으면 replay가 항상 깨진다:**

```
넣는 것:  pending 파일의 저장소 상대경로   ★ 절대경로 금지 (temp worktree마다 달라진다)
          transitionId · classification code
          requestSha256 · preStateSha256 · postStateSha256 · transitionContractSha256
정렬:     상대경로 → transitionId → code
```

> **실측**: `RecoveryCli.cs:22`의 `PendingInfo(Path, TransitionId, RequestSha256, PreStateSha256, PostStateSha256, TransitionContractSha256)`가 **이미 이 필드를 다 담는다.** `Path`만 상대경로로 바꾸면 된다.
> **필요한 binding이 `recovery inspect` 출력에 없으면 `TrustOriginCli`가 pending 파일을 직접 읽어 구성하라.**
> **`RecoveryCli.cs`는 무접촉이다**(`server/Harness/**`가 아니지만 06H 영역이다 — 읽고 호출만).

- **비교 키는 `pendingSetSha256` 뿐이다.** exit code는 비교하지 않는다(파생값이다).
- **현재 실측: `recovery inspect` exit 0** — pending 0건.

### ★ 1-5. self-test 재귀 차단 — `DeclareCore`를 **둘로 쪼갠다** (R3 신설)

```
RunDeclare  (프로덕션 경로 — 반드시 GateRunner를 실행한다)
 ├─ 자기 출처 검사
 ├─ GateRunner 재실행
 ├─ evidence ↔ observation 대조
 └─ ValidateDeclarationCore(evidence, observations)      ← 순수 검증

ValidateDeclarationCore  (GateRunner를 부르지 않는다)
 ├─ baseline hash 검증
 ├─ reconciliation exact match (§1-4)
 ├─ record schema 생성
 └─ record candidate write
```

- **self-test는 `ValidateDeclarationCore`만** fixture observation으로 검사한다. **`RunDeclare`를 부르지 않는다.**
- **★ 공개 우회 플래그를 만들지 마라** — `--skip-gates`·`--test-mode`·`--assume-pass`·`--no-replay` 전부 **금지**.
  분리는 **`internal` 함수 또는 주입 가능한 내부 인터페이스**로만 한다. **CLI 표면에 우회로가 생기면 그 순간 이 지시서의 목적이 죽는다.**
- **GateRunner에 재진입 가드**(§1-1의 0단계): 이미 GateRunner 안이면 **exit 2 `gate-runner-reentry`**. 벨트와 멜빵 둘 다 채운다.

**신규 fixture (필수)**:

```
self-test-does-not-recursively-run-integration-gate
  기대: exit 0 · timeout 없음
       ★ validation fixture 안에서 GateRunner invocation count == 0
       ★ 하위 trust-origin 프로세스 수가 상한을 넘지 않는다
```

### 1-6. `stateMutated` 하나로는 뜻이 모호하다 — 넷으로 쪼갠다

```json
"executionIsolation": "TEMP_GIT_WORKTREE",
"gateWorkspaceMutated": true,        ← measure 때문에 정상적으로 true
"sourceWorkspaceMutated": false,     ← true면 격리 실패. exit 2
"canonicalWorkstateMutated": false,  ← true면 exit 2
"canonicalApplierLogMutated": false  ← true면 exit 2
```

**지금처럼 무조건 `"stateMutated": false`라고 쓰는 것은 거짓말이다.**

### ★ 1-7. evidence 출력 경로는 **저장소 밖**이어야 한다 (R3 신설)

절대경로여도 `/repo/outputs/evidence.json`은 **저장소 안**이다. 그러면 **untracked evidence가 생겨 `declare`의 clean 조건과 충돌한다.**

```
evidence --out <path> 검사:
  source repository root 안       → exit 2  "evidence-output-inside-source-repository"
  gate temp worktree 안           → exit 2  "evidence-output-inside-gate-worktree"
  이미 파일이 존재                 → exit 2  "evidence-output-already-exists"   (덮어쓰기 금지)
  상대경로                        → exit 2  (server/ 기준으로 해석되는 실측 함정)
```

### 1-8. manifest ↔ evidence는 **순서 있는 정확 일치**다

```
canonical identity = (gateId, order, command, canonical args, expectedExit, mutatesState)
누락 0 · 중복 0 · 예상 밖 항목 0 · 순서 불일치 0     → 하나라도 어긋나면 exit 1
```

### ★★ 1-9. **record와 `verify`에 observation을 결속한다** (R5 신설)

evidence 파일은 **저장소 밖**에 있고(§1-7) **선언 후 삭제될 수 있다.** Declaration commit B에는 **record만** 남는다.
**그러면 record가 "무엇을 검증했는지"를 스스로 증명하지 못한다.**

**record에 반드시 남는 해시 8종:**

```
gateManifestSha256                  ★ baselineCommit 의 blob 에서 계산 (워킹트리 아님)
gateSetSha256                       TRUST-ORIGIN-BASELINE 튜플을 order대로 canonical 직렬화
gateObservationSetSha256            ★ 아래 tuple 을 order대로 canonical 직렬화
baselineReconciliationReportSha256
declaredLegacyFailureSetSha256
baselineWarningSetSha256
hsCandidateSetSha256
recoveryPendingSetSha256
```

```
gateObservationSetSha256 의 tuple:
  gateId · order · command · canonical args · expectedExit · mutatesState
  actualExit · timedOut · semanticObservationSha256
```

**`trust-origin verify`에 최소 이 계약을 더한다** (현재 verify는 baseline state/log hash·ancestry·prefix·delta만 본다 — **gate observation을 전혀 검증하지 않는다**):

```
verify:
  · record schema + 필수 해시 8종 존재            없으면 exit 1
  · baselineCommit 의 manifest blob hash 재계산 → gateManifestSha256 대조
  · gateSetSha256 재계산 대조
  · record 내부 observation set hash 재계산 대조
  · reconciliation / warning / candidate / pending 해시의 내부 정합 대조
```

> **`verify` 때마다 게이트를 전부 replay할지는 별도 정책이다**(이 DI 밖). **그러나 record가 evidence 해시를 하나도 검증하지 않는 상태는 허용하지 않는다.**

---

## 2. 하지 않을 일 (하면 반려)

- ❌ **evidence 파일의 값을 판정 근거로 쓰는 것.** `verdict`든 `actualExit`든. **`declare`는 반드시 재실행한다.**
- ❌ **★ `--skip-gates`·`--test-mode`·`--assume-pass`·`--no-replay` 류 공개 우회 플래그 신설.** self-test 분리는 **`internal` 경로로만.**
- ❌ **★ `handoff-integrity`·`hs-scan`·`recovery inspect`를 게이트 배열에 넣는 것.** §1-0′의 C1·C2·C3를 어긴다. **관찰로 간다.**
- ❌ **★ 게이트 배열에 `expectedExit != 0`인 항목을 두는 것.** 0이 아닌 기대값이 필요하면 **그 검사는 배열에 있으면 안 된다.**
- ❌ **★ wall-clock에 의존하는 검사를 게이트 배열에 넣는 것.** replay 대조가 무의미해진다 — **시각이 지나면 정상 baseline이 FAIL이 된다.**
- ❌ **★ `hs-scan`의 시각 파생 신호를 비교에서 빼면서 그 사실을 evidence에 안 적는 것.** `timeDerivedSignalsExcluded`로 **명시하라. 숨기면 반려다.**
- ❌ **★ `DECLARED_LEGACY_GAP` 모드를 "지금 필요 없으니" 안 만드는 것.** 다음 baseline에서 부트스트랩이 막힌다.
- ❌ **`gateManifestSha256`을 워킹트리 파일에서 계산하는 것.** `baselineCommit`의 **blob**에서 계산한다.
- ❌ **★ raw stdout/stderr 해시를 replay 비교 키로 쓰는 것.** 게이트별 **semantic projector**를 만든다(§1-3). **정규식 스크럽 금지.**
- ❌ **★ projector를 blocklist("이건 빼자")로 만드는 것.** **allowlist("이것만 넣는다")로 만들어라** — 새 필드가 조용히 새어 들어온다.
- ❌ **★ 게이트별 mutation 검사 없이 최종 boolean 하나로 끝내는 것.** 앞 게이트의 부수효과가 `measure` 변경에 묻힌다(§1-1′).
- ❌ **★ 시스템 시각을 바꿔서 7-B를 시험하는 것.** `HsScanCli` 무접촉이라 clock 주입도 불가다. **fixture 두 개로 하라.**
- ❌ **★ `candidateSetSha256`·`pendingSetSha256`을 스키마 없이 만드는 것.** **스키마 없는 해시는 해시가 아니다.** pending에 **절대경로 금지.**
- ❌ **★ record가 evidence 해시를 하나도 검증하지 않는 상태로 두는 것.** evidence는 저장소 밖이라 **사라진다**(§1-9).
- ❌ **★ 이 DI의 시험을 프로덕션 브랜치에서 하는 것.** verification 문서를 쓰면 HEAD가 바뀌어 **기준이 자기 꼬리를 문다.** **폐기 가능한 임시 저장소에서.**
- ❌ **`RecoveryCli.cs` 수정** — 06H 영역이다. pending binding이 부족하면 **`TrustOriginCli`가 pending 파일을 직접 읽어라.**
- ❌ **`gatesPass` 불린을 남기는 것.** 이름만 바꾸는 것도 반려.
- ❌ **게이트 목록 하드코딩.** `GATE-MANIFEST`가 정본이고 **해시로 결속**한다.
- ❌ **기존 `POST-EXECUTOR`·`POST-COMMIT` 블록 수정.** **바이트 하나도.** 신규 `gateId` **추가만**(사람 결재 2026-07-15).
- ❌ **`expectedExit`를 실측에 맞춰 정하는 것.** 기대값은 **의미로** 정한다. 실측이 다르면 **상태가 잘못된 것**이다 — 보고해라.
- ❌ **제외한 검사를 `note` 없이 빼는 것.**
- ❌ **`command`에 복합 명령을 한 문자열로 쓰는 것**(`"recovery inspect"`). 라우터는 `args[0]`을 본다.
- ❌ **★ 프로덕션 저장소에서 `trust-origin declare` 실행 / Trust Origin record 생성.** 부트스트랩은 **사람이 한다**(land gate 12번). 시험은 **폐기 가능한 임시 저장소**에서만.
- ❌ **결정적이지 않은 게이트를 조용히 비교에서 빼는 것.**
- ❌ **`--no-build` 사용.** Release 산출물이 낡으면 게이트 넷이 exit 2다(두 세션 연속 사고).
- ❌ **`--emit-hashes` 제작** — `CODEX-GATE-04` §5-4 소관. **중복 제작 금지.**
- ❌ **무접촉**: `server/Harness/**` 전부(**`HandoffIntegrityChecker.cs`는 읽기만 — 05H 영역이다. 호출만 하고 고치지 마라**) · `server/StateApplierCli.cs`·`server/Cli/**`·`server/Program.cs`(06C-1) · `dashboard/` · `WORKSTATE.json`·`applier-log`.
- ❌ push·결재·반입·발사.

---

## 3. 완료 기준 (exit code)

> **★ 구멍 재현(2·3)은 반드시 `$TEMP`의 폐기 가능한 임시 저장소에서.** **프로덕션 저장소에는 record가 단 한 번도 생기지 않는다.**
>
> ### ★★ 순환 차단 — **검증용 temp baseline ≠ 실제 Baseline Commit A** (R5 신설)
>
> `GateRunner`는 `HEAD == baselineCommit` + tracked-clean 을 요구한다. **그런데 시험 결과를 verification 문서에 적으면 HEAD가 바뀐다:**
>
> ```
> commit → GateRunner 실행 → 결과를 verification 문서에 기록 → 새 commit
>        → 이전 evidence의 baselineCommit이 더 이상 HEAD가 아니다 → 기준이 계속 움직인다
> ```
>
> **그래서 아래 시험(2·3·5·6·7·7-B·8·9·10·11·12)은 전부**
> **"이 DI의 작업 내용을 반영한 **폐기 가능한 임시 저장소**"에서 수행한다.**
> **원본 브랜치의 verification 문서는 그 임시 저장소에서 나온 결과를 기록만 한다.**
>
> **실제 Trust Origin용 evidence는 이 DI가 끝나고 사람이 Baseline Commit A를 확정한 뒤 별도로 실행한다**(§4).
> **둘을 섞으면 기준이 자기 꼬리를 문다.**

```text
 1. dotnet build server -c Release                                          → 0, warning 0

 2. ★ 구멍 실증 (임시 저장소, 고치기 전 코드):
      evidence 초안(전부 NOT_RUN)을 손으로 "PASS"로 고쳐 declare
      → ★ 통과한다. record가 생긴다. 출력을 보고에 그대로 붙여라.

 3. ★ 초판 설계도 뚫린다는 실증 (임시 저장소, 고친 뒤 코드):
      게이트를 돌리지 않고 actualExit 를 전부 expectedExit 와 같게 타이핑한 evidence
      → declare → ★ exit 1 "gate-observation-mismatch"
      ※ §1-2의 재실행이 없으면 이 시험이 통과해 버린다. 이 시험이 이 지시서의 심장이다.

 4. ★ 재귀 차단 (P0-2):
      trust-origin --self-test                    → 0. timeout 없음
      · validation fixture 안 GateRunner invocation count == 0
      · 하위 trust-origin 프로세스 수 상한 이내
      · declare(임시저장소) 실행 시 프로세스 폭증 없음
      ※ 이 시험 없이 "고쳤다"고 쓰면 반려. 재귀는 코드 검토로 갈음할 수 없다

 5. ★ 자기 출처: HEAD != baselineCommit 에서 declare                        → exit 2
      ★ tracked 변경이 있는 저장소에서 declare                              → exit 2

 6. ★ evidence 출력 경로 (§1-7):
      --out <저장소 안 절대경로>      → exit 2 "evidence-output-inside-source-repository"
      --out <이미 있는 파일>          → exit 2 "evidence-output-already-exists"
      --out <상대경로>                → exit 2

 7. 정상 evidence: trust-origin evidence --out <저장소 밖 절대경로>          → 0
      · executionIsolation = TEMP_GIT_WORKTREE
      · TRUST-ORIGIN-BASELINE 10개가 order대로 전부 (누락0 중복0 예상밖0)
      · ★ 배열의 expectedExit 가 전부 0이다 (하나라도 0이 아니면 반려)
      · ★ handoff-integrity · hs-scan · recovery inspect 가 게이트 배열에 없다
      · ★ baselineReconciliation.mode = VERIFIED_CONSISTENT, exitCode 0, failures []
           warnings 에 TEST-DI0001-2 / duplicate-success-in-log 가 그대로 있다
      · ★ hsScan.candidateSetSha256 존재 · timeDerivedSignalsExcluded = true
      · ★ recoveryPending.pendingSetSha256 존재 (상대경로 기반. 절대경로가 들어갔으면 반려)
      · ★ context-pack-integrity 가 있다 (order 4)
      · ★ command/args 가 분리 형식이다 (self-test: "trust-origin" + ["--self-test"])
      · ★ 게이트 10개 전부 semanticObservation + semanticObservationSha256 이 있다
           build-verify 의 semanticObservation 에 buildProject/outputDir/stdoutTail/stderrTail 이
           들어 있으면 반려 (§1-3)
      · ★ 게이트마다 treeHashBefore/After · observedMutation · changedPaths 가 있다
      · ★ undeclaredMutationCount = 0 · gateWorkspaceChangedPaths 가 measure 3파일뿐이다
      · gateManifestSha256 (baselineCommit blob에서 계산) · gateSetSha256 · gateObservationSetSha256 존재
      · sourceWorkspaceMutated=false · canonicalWorkstateMutated=false
        canonicalApplierLogMutated=false · gateWorkspaceMutated=true

 7-C. ★★ mutation 탐지 (§1-1′ — R5 신설):
      · mutatesState=false 인 게이트가 tracked 파일을 고치는 fixture       → exit 2 "undeclared-gate-mutation"
      · measure 가 allowedMutationPaths 밖을 고치는 fixture                → exit 2 "mutation-outside-allowed-paths"
      ★ R4 설계(마지막 boolean 하나)로는 앞 게이트의 변경이 measure 변경에 묻혀 안 잡힌다는 것을 먼저 보여라

 7-D. ★ record·verify 결속 (§1-9 — R5 신설):
      · record 에 해시 8종이 전부 있다
      · evidence 파일을 삭제한 뒤 trust-origin verify                      → 0 (record 만으로 검증된다)
      · record 의 gateObservationSetSha256 을 손으로 한 글자 고치고 verify  → 1

 7-B. ★★ wall-clock 비의존 실증 (§1-0′ C2 — R5 재작성. 이 시험이 없으면 반려)
      ⛔ 시스템 시각을 바꾸지 마라. HsScanCli 는 무접촉이라 clock 주입도 불가능하다.
         (R4는 "+48h 로 옮겨라"라고 썼는데 allowlist 안에서 실행 불가능한 요구였다)

      ★ 대신 폐기 가능한 fixture 저장소 둘을 만든다. 다른 것은 HS-CANDIDATES.md 의 lastGate 뿐이다:
           fixture A:  lastGate = 실행 시각과 가까운 값
           fixture B:  lastGate = A 보다 48시간 이전
           failure index · judgedClasses 는 동일
      기대:
           hsScan.exitCode          달라도 된다   (비교 키가 아니다)
           hsScan.signals           달라도 된다
           hsScan.candidateSetSha256   ★ 동일해야 한다
           게이트 10개의 semanticObservationSha256  ★ 동일해야 한다
      ※ 두 fixture 가 같은 git commit 일 필요는 없다. 이 시험의 목적은 baseline 해시 동일성이 아니라
        candidate projector 의 wall-clock 독립성이다
      ※ R3 설계(hs-scan 이 게이트 배열에 있던)로 같은 시험을 하면 actualExit 가 뒤집혀 replay 가 깨진다는 것을
        먼저 보여라 — 그래야 §1-0′ C2 가 왜 필요한지 증명된다

 8. 정상 declare (임시 저장소): 7의 evidence 그대로                          → 0, record 생성
      · 재실행이 실제로 일어난 증거 (재실행 타임스탬프 > evidence 타임스탬프)

 9. ★ DECLARED_LEGACY_GAP fixture: failure 1건이 있는 baseline
      · evidence: mode=DECLARED_LEGACY_GAP, exitCode 1, 그 failure만 정확히
      · declare                                                             → 0 (선언된 것과 정확히 일치)
      · 선언 안 된 failure를 1건 더 심으면 declare                          → 1
      · warnings 를 1건 지우거나 더하면 declare                             → 1

10. ★ evidence gate 배열에 항목 추가 / 삭제 / 순서 변경 (각각)              → 전부 exit 1

11. ★ GATE-MANIFEST를 바꾼 임시 저장소 + 옛 evidence 로 declare             → exit 1
      (gateManifestSha256 / gateSetSha256 결속. 코드 검토로 갈음하지 마라)

12. 게이트 하나를 실제로 실패시킨 baseline 에서 evidence                    → 그 게이트 verdict FAIL
                                                                            → declare exit 1

13. 결정성: 같은 baseline 에서 evidence 2회                                → 게이트 집합·actualExit·
      canonical 해시 전부 동일 (원본 stdoutSha256 은 달라도 된다)
      ※ canonical화 후에도 다른 게이트가 있으면 → 빼지 말고 보고

14. timeout: 응답하지 않는 게이트 fixture                                   → timedOut=true, verdict FAIL,
      프로세스가 끝난다

15. worktree 위생: evidence 실행 중 강제 종료 후 재실행                     → 정상 동작
      (.git/worktrees 에 stale이 쌓이지 않는다)

16. 격리: evidence·declare 전후 프로덕션 저장소에서
      · WORKSTATE.json / applier-log  blob hash 동일  (git hash-object)
      · tracked 변경 0                               (git status 는 프록시다)
      · outputs/trust-origin/ 에 record 없음         ★ 프로덕션 record 미생성

17. trust-origin --self-test                                              → 0. 기존 24 케이스 회귀 없음 + 신규
18. ★ git diff 로 GATE-MANIFEST의 POST-EXECUTOR·POST-COMMIT 블록 무변경 실증
19. handoff-integrity / context-pack-integrity / doc-integrity / gate-clean server → 0 / 0 / 0 / 0
20. measure dev-pack (-c Release)                                         → 0, violationCount 0
```

> **⚠ 시험용 파일 수정은 `$TEMP` 사본에서.** 저장소 안에서 시험하다 **`docs/wiki` 42파일을 지운 사고**가 있었다.
> **⚠ 게이트를 재기 전에 반드시 `dotnet build server -c Release`.**

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

> **그리고 게이트 배열은 "정확성"만 판정한다. "지금 이런 상태다"는 관찰로 결속한다.**
> **이 둘을 섞는 순간, 상태가 정상적으로 변하는 날 정상 baseline이 FAIL이 된다.** — 이 지시서가 **세 번** 그렇게 틀렸다.

**우회로 (전부 반려)**:
evidence의 `actualExit`를 믿기 · `declare`에서 재실행 생략 · **공개 우회 플래그로 self-test 재귀를 피하기** ·
**상태 보고형 검사(`handoff-integrity`·`hs-scan`·`recovery inspect`)를 exit-code 게이트로 넣기** ·
**wall-clock 의존 검사를 배열에 넣기** · **`hs-scan` 시각 신호를 몰래 비교에서 빼기** ·
**`DECLARED_LEGACY_GAP`을 안 만들기** · 게이트 목록 하드코딩 · manifest 비교를 부분집합으로 ·
기존 게이트 세트의 `expectedExit` 수정 · `expectedExit`를 실측에 맞추기 · 제외한 검사를 `note` 없이 빼기 ·
결정적이지 않은 게이트를 조용히 비교에서 빼기 · 완료 기준 3·4·7-B를 건너뛰기 ·
**프로덕션 저장소에서 `declare`를 돌려 record 만들기.**

**자진 신고 없이 하면 반려다. 신고하면 감점이 아니다.**
**요구가 서로 모순되거나 빠진 게 있으면 완화하지 말고 보고해라.** — **이 지시서는 초판·R1·R2가 전부 틀렸다**(§0-4·§0-5·§0-6). **네 번째가 되지 마라.**

---

## 허용 파일 (allowlist)

- server/TrustOriginCli.cs
- server/GateRunner.cs
- docs/handoff/GATE-MANIFEST.json
- docs/handoff/BASELINE-CHANGES.md
- docs/verification/gate-truth-01-integration-evidence-truth.md
- docs/handoff/queue/directive-GATE-TRUTH-01-integration-evidence-truth.md

> `server/GateRunner.cs`는 **신규 파일**이고 **하네스가 아니다** — `HarnessRegistry`에 **등록하지 않는다.**
> `docs/handoff/GATE-MANIFEST.json`은 **신규 `gateId` 추가만.** 기존 블록 무접촉(완료 기준 18).
> `docs/handoff/BASELINE-CHANGES.md`는 **append만.** 통째로 다시 쓰면 반려(깨진 한글이 조용히 오염된다 — `CLAUDE.md`).
> **①주체(사람 결재 2026-07-15, choi) ②근거(§0-5·§0-6) ③되돌리는 법(추가한 `TRUST-ORIGIN-BASELINE` 블록만 지우면 정확히 이전 상태)** 을 append 하라.
> `server/Harness/**` **무접촉** — **`HandoffIntegrityChecker.cs`는 읽고 호출만 한다. 고치지 마라**(05H 영역).

---

## 보고

`docs/verification/gate-truth-01-integration-evidence-truth.md` — `_template.md` 형식 그대로.

**반드시 적을 것:**

```text
EXPLOIT_REPRODUCTION_REPOSITORY        = disposable-temp-repo
PRODUCTION_DECLARE_EXECUTED            = false
PRODUCTION_TRUST_ORIGIN_RECORD_CREATED = false
GATE_RUNNER_INVOCATIONS_IN_SELFTEST    = 0
```

- **완료 기준 2번의 실제 출력** — 고치기 전엔 게이트 없이 record가 생긴다는 증거
- **★ 완료 기준 3번** — 초판 설계(`actualExit` 재판정)로도 뚫린다는 증거. 없으면 재실행의 필요가 증명되지 않는다
- **★ 완료 기준 4번** — 재귀 차단 실측. **프로세스 수·timeout·invocation count.** 코드 검토로 갈음하지 마라
- **★ 완료 기준 7-B** — **wall-clock 비의존 실증**(fixture A/B). R3 설계로는 `hs-scan`이 뒤집혀 replay가 깨진다는 것을 **먼저 보여라**
- **★ 완료 기준 7-C** — mutation 탐지. **R4 설계(최종 boolean 하나)로는 안 잡힌다는 것을 먼저 보여라**
- **★ 완료 기준 7-D** — evidence 파일을 **삭제한 뒤** `verify`가 통과하는가 (record 자립)
- **★ projector 10개의 필드 목록을 그대로 붙여라** — 특히 `build-verify`에서 **뺀 것**(`buildProject`·`outputDir`·`stdoutTail`·`stderrTail`)
- **§1-0′ 분류표를 채워서 붙여라** — 배열에 넣은 검사·뺀 검사 **전부**, C1/C2/C3 판정과 함께
- **`EXPLOIT_REPRODUCTION_REPOSITORY`가 `disposable-temp-repo`라는 것** — 이 DI의 모든 시험이 임시 저장소에서 돌았다는 증거
- **★ 완료 기준 9번** — `DECLARED_LEGACY_GAP` 양성/음성 둘 다
- 완료 기준 5·6·10·11·12·14·15의 **기대 exit vs 실제 exit 표**
- **evidence 파일 실물 1개**(완료 기준 7)
- **`TRUST-ORIGIN-BASELINE`의 `expectedExit`를 어떻게 정했는지** — **의미로 정했다는 근거.** 실측과 달랐던 게 있으면 **그대로 적어라**
- **제외한 검사와 `note` 사유 전체**
- 완료 기준 13의 **canonical화 규칙(휘발 필드 목록)**, 16의 **blob 해시 대조**, 18의 **git diff**
- **`## 지표는 만족했으나 목적은 미달인 부분`**(ADR-005). 없으면 "없음".

**자기보고는 증거가 아니다.** 검수자가 재실행한다. 못 한 시험은 `NOT_VERIFIED`. **숨기면 반려다.**
