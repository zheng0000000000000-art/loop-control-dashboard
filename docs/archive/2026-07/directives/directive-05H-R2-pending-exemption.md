```context-pack
{
  "diId": "05H-R2",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/handoff/queue/directive-05H-reconciler.md", "sha256": "c06572361823550831cdb33b99600aede0826223cf124e4106fa142ed5a910b9" },
    { "path": "docs/handoff/queue/directive-05H-R1-reconciler-rework.md", "sha256": "25e40e34411ef480e83933e95b61630046e3347ad2c744e21a89a5b6c2eda8e9" },
    { "path": "outputs/review/05H-R1.codex.md", "sha256": "79a4c2edccf198e4976032dbed3868a1e718fb892efb535d022242fc496dc7dc" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "outputs/review/05H-R1.codex.md",
    "docs/handoff/queue/directive-05H-reconciler.md",
    "docs/handoff/queue/directive-05H-R2-pending-exemption.md",
    "docs/directives/_header.md"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

> **05H-R1 재반려에 따른 재작업.** 원 지시서 `directive-05H-reconciler.md`와 `directive-05H-R1-...`은 **그대로 유효하다.**
> 이 문서는 그 위의 **두 번째 정정분**이다. **절대 수정 금지**: `WORKSTATE.json` · `WORKSTATE.applier-log.jsonl` · `StateApplierCli.cs`(06C-1 영역).
> **단일 land gate**: 05H·06C-1·06C-2·06H는 통합 branch에서 함께 넘긴다. **조각 land 금지.**

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.

---

# 05H-R2 — pending 면제의 뒷문을 막는다 + **그 경로를 실증 가능하게 만든다**

- actor: **CORE_INFRA_EXECUTOR (sonnet)** — ADR-015 한시 예외 유지
- branch: `wp/state-integrity`
- 발견: **코덱스 독립 검수**(`outputs/review/05H-R1.codex.md`). 검수자(claude-opus)가 코드로 대조해 확정.

---

## 0. 무엇이 뚫렸나

**계약** — 05H 원 지시서 §5-2 (:81):

> `PendingTransitionId`는 면제(내부 checker 전용, **state에 정확히 1회 · log엔 없음**, 어기면 면제 미적용).

**구현** — `server/Harness/HandoffIntegrityChecker.cs:254-256`가 검사하는 것:

```csharp
if (!string.IsNullOrEmpty(pendingId)                                   // ① pendingId 있음
    && string.Equals(id, pendingId, StringComparison.Ordinal)          // ② id 일치
    && stateIds.Count(s => string.Equals(s, id, StringComparison.Ordinal)) == 1)  // ③ state에 1회
{
    pendingApplied = true;
    continue;                                                          // ← 면제
}
```

**빠진 것: "log엔 없음".** `allLogIdSet.Contains(id)`가 false인지 **검사하지 않는다.**

**결과 — 실패 로그를 가진 전이를 pending으로 지정하면 PASS가 난다:**

```
state: appliedTransitions = [ { "id": "PENDING-FAILED", "appliedAt": ... } ]
log  : {"transitionId":"PENDING-FAILED","result":"handoff-integrity-failed exit=1","exitCode":1,...}
호출 : ReconciliationOptions(..., PendingTransitionId: "PENDING-FAILED")

:253  successfulLogIdSet에 없음        → 안 걸림
:254  pending 면제 (log 검사가 없다!)   → continue
      → failures 없음 → exit 0 → PASS
```

**05H의 원래 구멍(`allLogIdSet` 오용)이 규칙 2 본체에서는 막혔는데, 면제 조항으로 살아남았다.**
현 저장소의 `DI0004-BLOCKED-CODEX`가 정확히 이 형태다 — pending으로 주는 순간 통과한다.

### 왜 아무도 못 잡았나 — **이것이 이 작업의 진짜 과제다**

**pending 면제 경로는 지금까지 한 번도 실행된 적이 없다.** 내부 checker 전용이라 CLI에 노출돼 있지 않고
(`--pending-transition` → `pending-not-allowed-on-cli` exit 1, 이건 **옳다**), 테스트 프로젝트도 없다.
05H·05H-R1의 검증 문서 둘 다 이 경로를 **`NOT_VERIFIED`**로 남겼다. 검수자도 그걸 받아들이고 넘어갔다.

**반증 자료가 없는 규칙은 규칙이 아니라 주석이다.** 조건절 하나가 통째로 빠져 있었는데 fixture 7종·게이트 전부가 초록이었다.

**그러므로 §2(실증 수단 신설)가 §1(조건 추가)보다 중요하다.** 조건만 고치고 실증 수단을 안 만들면
**다음 사람이 같은 자리에서 같은 방식으로 다시 뚫린다.**

## 1. 고칠 것 — 면제 조건에 "log엔 없음"을 추가

`server/Harness/HandoffIntegrityChecker.cs`

- `CheckStateToLog`에 **`allLogIdSet`을 추가로 전달**한다(현재는 `successfulLogIdSet`만 받는다).
- 면제 조건에 **`!allLogIdSet.Contains(id)`** 를 추가한다.
- **"log엔 없음"은 성공/실패를 가리지 않는다.** 어떤 종류든 그 id의 로그 항목이 **하나라도** 있으면 면제하지 않는다.
  (근거: 로그에 흔적이 있다 = 그 전이는 이미 시도됐다 = "아직 기록 전인 진행 중 전이"가 아니다.)
- 면제가 **미적용**되면 기존대로 `state-transition-not-logged` Failure를 낸다.
- `PendingExemptionApplied`(:36·:302·CLI :164)는 **실제로 면제가 적용됐을 때만 true**여야 한다. 지금도 그렇지만 회귀시키지 마라.

### 실패 메시지도 고쳐라 (작지만 거짓말이다)

현재: `"id '{id}' is in appliedTransitions but has no log entry"`
**로그 항목은 있다 — 실패한 항목이.** → `"...but has no successful log entry"` 로 정정한다.

## 2. ★ 실증 수단 — `handoff-integrity --self-test` 신설

**pending 경로를 외부에서 돌릴 수 있게 만든다. 단, canonical 데이터에 pending을 주입하는 통로는 열지 않는다.**

```
dotnet run --project server -c Release -- handoff-integrity --self-test
```

- 내부 checker를 **in-process로** 호출한다 — `HandoffIntegrityChecker.Run(ReconciliationOptions{...})`.
- 입력은 **`docs/qa/fixtures/reconciliation/pending/<case>/` 고정 경로만.** 인자로 경로·id를 받지 않는다.
- **각 case의 `PendingTransitionId`와 기대 결과는 코드에 하드코딩한다.** 이것은 질의 도구가 아니라 **단언 실행기**다.
- 결과: 모든 case가 기대와 일치 → **exit 0**. 하나라도 어긋나면 → **exit 1** + 어긋난 case·기대·실제를 JSON으로 출력.
- **`--self-test`는 `--pending-transition`이 아니다.** 사용자가 임의 id를 canonical WORKSTATE에 면제 적용할 통로는 **여전히 없다**
  (`--pending-transition` → `pending-not-allowed-on-cli` exit 1 **유지**). 이건 우회로가 아니라 **반증 자료다.**

### pending fixture (신규 — `docs/qa/fixtures/reconciliation/pending/`)

| case | state | log | Pending | 기대 |
| --- | --- | --- | --- | --- |
| **`pending-ok`** | X 1회 | X **없음** | X | **PASS** (exit 0) · `pendingExemptionApplied=true` ← 정당한 면제. **첫 실증** |
| **`pending-failed-log`** | X 1회 | X가 **exitCode=1**로 존재 | X | **FAIL** `state-transition-not-logged` ★ **코덱스가 찾은 구멍. 이게 핵심이다** |
| **`pending-success-log`** | X 1회 | X가 **ok/0**으로 존재 | X | **PASS** · `pendingExemptionApplied=false` ← 면제가 **불필요**했다(이미 성공로그). 면제를 남용하지 않는지 본다 |
| **`pending-duplicate`** | X **2회** | X 없음 | X | **FAIL** — state 중복이라 면제 미적용(`duplicate-in-state` + `state-transition-not-logged`) |
| **`pending-mismatch`** | X 1회 | X 없음 | **Y**(다른 id) | **FAIL** `state-transition-not-logged`(X에 대해) — 엉뚱한 id를 줘도 X가 면제되지 않는다 |

**`pending-failed-log`가 지금 코드에서 PASS를 낸다는 것을 먼저 재현해 보여라.** 고치기 전에.
**"고쳤다"의 증거는 "고치기 전엔 뚫렸다"를 보인 다음에만 성립한다.**

## 3. 하지 않을 일 (하면 반려)

- **`--pending-transition`을 외부 CLI에서 허용하는 것.** 그건 canonical 우회 통로다. `pending-not-allowed-on-cli` exit 1 **유지.**
- `--self-test`가 **경로·id를 인자로 받게** 만드는 것. 그 순간 우회로가 된다. **고정 fixture + 하드코딩 기대값.**
- 규칙 1·2 본체, `allLogIdSet`의 다른 용도(Metrics·malformed), v2 log 계약, `blockers[]` 판정을 손대는 것.
- **`HandoffIntegrityChecker`를 `HarnessRegistry`에 등재하는 것**(원 지시서 §2 — CLI 표면·pending 우회 방지).
- `WORKSTATE.json`·`WORKSTATE.applier-log.jsonl` 수정. **`DI0004-BLOCKED-CODEX`를 지우지 마라.**
- `server/StateApplierCli.cs` 접촉 — **06C-1이 지금 그 파일에서 작업 중이다.** 충돌하면 둘 다 죽는다.
- **at-rest를 exit 0으로 만들려는 모든 시도.** at-rest exit 1은 정상이다(05H-R1 §3).

## 4. 완료 기준 (exit code)

```text
1. dotnet build server -c Release                                     → 0
2. ★ 고치기 전 재현: pending-failed-log 가 현 코드에서 PASS(exit 0)   → 보고에 그대로 적어라
3. pending-ok            → 0 · pendingExemptionApplied=true    ★ 면제 경로 첫 실증
4. pending-failed-log    → 1 · state-transition-not-logged     ★ 구멍이 막혔다
5. pending-success-log   → 0 · pendingExemptionApplied=false
6. pending-duplicate     → 1 · duplicate-in-state 포함
7. pending-mismatch      → 1 · state-transition-not-logged
8. handoff-integrity --self-test                                      → 0 (위 5종 전부 기대 일치)
9. 회귀: fixture a→1 b→1 c→0 d→1 e→1 f→1 malformed→2                 (변화 없음)
10. CLI --pending-transition                                          → 1 pending-not-allowed-on-cli (유지)
11. at-rest 현재 저장소                                               → 1 · failures 정확히 1건 (정상)
12. dotnet run --project server -c Release -- measure dev-pack        → 0
```

**통과를 믿기 전에 실패시킬 수 있음을 먼저 증명해라.** (완료 기준 2번이 그것이다 — **건너뛰지 마라.**)

## 목적 기준 (사람 판정 — ADR-005)

**"면제는 아직 기록되지 않은 진행 중 전이에만 준다. 이미 시도돼 실패한 전이에는 주지 않는다."**

우회로: `--self-test`에 인자를 열어 canonical에 pending을 먹이거나, 면제 조건을 "실패 로그는 예외" 식으로 다시 느슨하게 하는 것.
**둘 다 목적 미달이다. 자진 신고 없이 하면 반려다. 신고하면 감점이 아니다.**

## 허용 파일 (allowlist)

- server/Harness/HandoffIntegrityChecker.cs
- server/Harness/HandoffIntegrityCli.cs
- docs/qa/fixtures/reconciliation/**
- docs/verification/05h-r2-pending-exemption.md
- docs/handoff/queue/directive-05H-R2-pending-exemption.md

> **`server/StateApplierCli.cs` 무접촉** — 06C-1이 동시에 그 파일에서 작업 중이다.
> **`server/Harness/DiCompletionCheckCli.cs`·`ClaimCheckCli.cs` 무접촉** — `CODEX-GATE-04` 영역.

## 보고

`docs/verification/05h-r2-pending-exemption.md` — `docs/verification/_template.md` 형식 그대로.
**DI 유형(`harness`) 선언** · 하네스별 **기대 exit vs 실제 exit** · 실패 분류(v9 §0.3) · 잔여 위험 ·
**`## 지표는 만족했으나 목적은 미달인 부분`**(ADR-005). **「직접 경로 사용 사유」에 ADR-015 예외 명시.**

**반드시 적을 것:**
- **완료 기준 2(고치기 전 재현)의 실제 출력.** "뚫렸다"를 보이지 못하면 "막았다"도 증명되지 않는다.
- **`pending-ok`의 결과** — 이것이 pending 면제 경로의 **최초 실행 기록**이다. 05H·05H-R1이 둘 다 `NOT_VERIFIED`로 남긴 그 경로다.

**자기보고는 증거가 아니다.** 검수자가 직접 재실행하고, **코덱스가 read-only로 독립 검수한다**(`run-codex-review.ps1`).
못 한 시험은 `NOT_VERIFIED`. **숨기면 반려다.**
