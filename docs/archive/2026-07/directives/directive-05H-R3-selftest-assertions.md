```context-pack
{
  "diId": "05H-R3",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/handoff/queue/directive-05H-R2-pending-exemption.md", "sha256": "04b3ec2ba984ef55e9ef739fb76093a93beffae6fb612de009f498d539bb4b86" },
    { "path": "outputs/review/05H-R2.codex.md", "sha256": "75b42bdd7686dbfefdc094cbe7df7b751d0918ae270c7d8a44dbb95e5dd79dd9" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "outputs/review/05H-R2.codex.md",
    "docs/handoff/queue/directive-05H-R2-pending-exemption.md",
    "docs/handoff/queue/directive-05H-R3-selftest-assertions.md",
    "docs/directives/_header.md"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

> **05H-R2 조건부 반려에 따른 재작업.** 본체 수정(`!allLogIdSet.Contains(id)`)은 **옳다. 건드리지 마라.**
> 반려는 **계측기**에 대한 것이다 — `--self-test`가 실패의 *원인*을 단언하지 않는다.
> **절대 수정 금지**: `WORKSTATE.json` · `WORKSTATE.applier-log.jsonl` · `StateApplierCli.cs`(06C-1-R1 동시 작업).
> **단일 land gate**: 조각 land 금지.

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.

---

# 05H-R3 — `--self-test`가 **실패의 원인**을 단언하게 한다

- actor: **CORE_INFRA_EXECUTOR (sonnet)** — ADR-015 한시 예외 유지
- 발견: **코덱스 독립 검수**(`outputs/review/05H-R2.codex.md`) + 검수자 실증

---

## 0. 무엇이 문제인가

`server/Harness/HandoffIntegrityCli.cs:361-363`:

```csharp
var actualPass = r.Failures.Count == 0 && r.HarnessErrors.Count == 0;
var actualExemption = r.Metrics?.PendingExemptionApplied ?? false;
if (actualPass != expectPass || actualExemption != expectExemption)   // ← 실패 코드를 안 본다
```

**"실패했는가"만 묻고 "왜 실패했는가"는 묻지 않는다.**

**검수자 반증 시험 (실증됨):**

```
docs/qa/fixtures/reconciliation/pending/pending-duplicate/workstate.json → malformed JSON으로 오염
  기대 실패: duplicate-in-state       (Failures)
  실제 실패: workstate-malformed      (HarnessErrors) ← 완전히 다른 이유
handoff-integrity --self-test → exit 0, {"selfTest":"...","verdict":"PASS","casesRun":5}
```

**잘못된 이유로 실패했는데 초록을 줬다.**
`HarnessErrors`(입력이 깨짐)와 `Failures`(검사가 잡음)를 **둘 다 "실패"로 뭉갠 것**이 원인이다.

> **반증 자료 없는 규칙을 막으려고 만든 도구가, 그 자신이 반증되지 않는다.**
> 이 저장소에서 일곱 번 반복된 실패 양식이다 — **하네스가 다른 것을 잰다.**

## 1. 고칠 것

### 1-1. case별 **기대 결과를 코드로 확장**하라

지금은 `(name, pendingId, expectPass, expectExemption)` 4-튜플이다. **부족하다.** 최소한:

```csharp
(name, pendingId,
 expectExemption : bool,
 expectFailureCodes : string[],      // 기대하는 Failure 코드 집합 (순서 무관, 정확히 이 집합)
 expectHarnessErrors : bool)         // HarnessError가 나야 하는가 (기본 false)
```

**판정**:

- `HarnessErrors.Count > 0` 인데 `expectHarnessErrors == false` → **mismatch** (`unexpected-harness-error`).
  **입력이 깨진 것을 "검사가 잡았다"로 세지 마라.**
- `Failures`의 **코드 집합**이 `expectFailureCodes`와 다르면 → **mismatch** (기대/실제 둘 다 출력).
- `PendingExemptionApplied`가 기대와 다르면 → mismatch (기존과 동일).

case별 기대 코드 (05H-R2 지시서 §2 표대로):

| case | expectExemption | expectFailureCodes |
| --- | --- | --- |
| `pending-ok` | **true** | (없음) |
| `pending-failed-log` | false | `state-transition-not-logged` |
| `pending-success-log` | **false** | (없음) — 면제가 **불필요**했다 |
| `pending-duplicate` | false | `duplicate-in-state`, `state-transition-not-logged` |
| `pending-mismatch` | false | `state-transition-not-logged` |
| **`pending-nonok-zero`** (신설, §1-2) | false | `state-transition-not-logged` |

### 1-2. fixture 추가 — `result != "ok"` 인데 `exitCode == 0`

코덱스가 지적한 빈틈이다. **현 코드는 올바르게 처리하지만 반증 자료가 없다.**

`docs/qa/fixtures/reconciliation/pending/pending-nonok-zero/`:

```json
// workstate.json — appliedTransitions에 PENDING-NONOK 1회
// applier-log.jsonl
{"transitionId":"PENDING-NONOK","result":"error-but-zero","exitCode":0,"at":"2026-01-01T00:00:01Z"}
```

`successfulLogEntries`는 **`result=="ok" && exitCode==0`** 이므로 이건 성공이 아니다.
그러나 `allLogIdSet`에는 있다 → **면제 미적용** → `state-transition-not-logged`. **기대: exit 1.**

### 1-3. 자기 자신을 반증하라 — **`--self-test`가 fixture 오염을 잡는지 보여라**

**완료 기준 5번이 이 작업의 핵심이다. 건너뛰면 반려다.**

`pending-duplicate/workstate.json`을 malformed JSON으로 **일시 오염**시키고 `--self-test`를 돌려라.
**exit 1이 나와야 한다** (`unexpected-harness-error`). 지금은 exit 0이 나온다.
**시험 후 fixture를 정확히 원복하고, 원복됐음을 `git status`가 아니라 **blob 해시**로 확인하라.**

## 2. 하지 않을 일 (하면 반려)

- **`HandoffIntegrityChecker.cs`의 규칙 1·2·면제 조건·malformed 판정을 바꾸는 것.** **본체는 옳다.**
  (`CheckStateToLog`의 `!allLogIdSet.Contains(id)`를 건드리지 마라.)
- `--self-test`에 **경로·id 인자를 여는 것.** 지금은 다른 인자를 전부 무시한다 — **그대로 둬라.**
- `--pending-transition` CLI 금지 해제 (`pending-not-allowed-on-cli` exit 1 **유지**).
- **`HandoffIntegrityChecker`를 `HarnessRegistry`에 등재하는 것.**
- `server/StateApplierCli.cs` 접촉 — **06C-1-R1이 동시에 작업 중이다.**
- `WORKSTATE.json`·`WORKSTATE.applier-log.jsonl` 수정. **at-rest를 exit 0으로 만들려는 시도.**
- **fixture를 "기대에 맞게" 고쳐서 통과시키는 것.** fixture는 반증 자료다 — 기대가 틀렸으면 **보고하라.**

## 3. 완료 기준 (exit code)

```text
1. dotnet build server -c Release                                        → 0
2. handoff-integrity --self-test                                         → 0 (6 case 전부 기대 일치)
3. pending-nonok-zero 단독                                              → 1 state-transition-not-logged
4. ★ pending-failed-log 의 기대 코드를 일부러 틀리게 적고 --self-test    → 1 (코드 불일치를 잡는다)
5. ★ pending-duplicate/workstate.json 을 malformed로 오염 후 --self-test → 1 unexpected-harness-error
      ← **지금은 exit 0 PASS가 나온다. "뚫렸다"를 먼저 보여라.**
6. 회귀: fixture a→1 b→1 c→0 d→1 e→1 f→1 malformed→2                    (변화 없음)
7. CLI --pending-transition                                             → 1 pending-not-allowed-on-cli (유지)
8. at-rest handoff-integrity                                            → 1 · failures 정확히 1건 (정상)
9. measure dev-pack (-c Release)                                        → 0
```

## 목적 기준 (사람 판정 — ADR-005)

**"계측기는 자기가 틀렸을 때 그것을 말할 수 있어야 한다."**
`--self-test`가 초록을 주는 이유가 **"기대한 그 이유로 실패했기 때문"**이어야 한다.
"어쨌든 실패했으니 통과"는 목적 미달이다.

우회로: `expectHarnessErrors`를 모든 case에 true로 두기 · 기대 코드 집합을 부분집합 비교로 느슨하게 하기.
**둘 다 반려다. 자진 신고 없이 하면 반려다. 신고하면 감점이 아니다.**

## 허용 파일 (allowlist)

- server/Harness/HandoffIntegrityCli.cs
- docs/qa/fixtures/reconciliation/**
- docs/verification/05h-r3-selftest-assertions.md
- docs/handoff/queue/directive-05H-R3-selftest-assertions.md

> **`server/Harness/HandoffIntegrityChecker.cs` 무접촉 — 본체는 옳다.** 고쳐야 한다고 판단되면 **고치지 말고 보고하라.**
> `server/StateApplierCli.cs` **무접촉**(06C-1-R1 동시 작업).

## 보고

`docs/verification/05h-r3-selftest-assertions.md` — `_template.md` 형식 그대로.

**반드시 적을 것:**
- **완료 기준 4·5의 실제 출력.** **"뚫렸다"를 보인 다음에만 "막았다"가 성립한다.**
- fixture를 오염시켰다가 원복했다면 **blob 해시로 원복을 확인한 증거**(`git hash-object` vs `git rev-parse HEAD:<path>`).
  **`git status`는 프록시다** — mtime만 바뀌어도 `M`이 뜨고, 반대로 stat 캐시 때문에 놓칠 수도 있다.

**자기보고는 증거가 아니다.** 검수자가 재실행하고 **코덱스가 read-only로 독립 검수한다.**
못 한 시험은 `NOT_VERIFIED`. **숨기면 반려다.**
