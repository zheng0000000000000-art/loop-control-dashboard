```context-pack
{
  "diId": "05H-R1",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/plan/wp/WP-STATE-INTEGRITY-land-gate.md", "sha256": "2549764220878fe6e65e487973eeb0c1769dbeeee0083c5f98bc437b41c3354c" },
    { "path": "docs/handoff/queue/directive-05H-reconciler.md", "sha256": "c06572361823550831cdb33b99600aede0826223cf124e4106fa142ed5a910b9" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/handoff/queue/directive-05H-reconciler.md",
    "docs/handoff/queue/directive-05H-R1-reconciler-rework.md",
    "docs/directives/_header.md"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

> **05H 반려에 따른 재작업.** 원 지시서 `directive-05H-reconciler.md`는 **그대로 유효하다** — 이 문서는 그 위의 **정정분**이다.
> 원 지시서의 §1~§12를 다시 읽어라. 여기서는 **틀린 한 곳과 빠진 한 곳**만 다룬다.
> **절대 수정 금지**: `WORKSTATE.json` · `WORKSTATE.applier-log.jsonl`(append-only 증거) · `StateApplierCli.cs`
> **단일 land gate**: 05H·06C-1·06C-2·06H는 통합 branch에서 함께 넘긴다. **조각 land 금지.**

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.

---

# 05H-R1 — reconciliation 규칙 2 복원 + fixture-f 신설

- actor: **CORE_INFRA_EXECUTOR (sonnet)** — ADR-015 한시 예외 유지
- branch: `wp/state-integrity` (05H와 같은 통합 branch)
- 선행: 없음. **06C-1보다 먼저 끝나야 한다** (06C-1의 idempotency가 이 checker를 소비한다)

---

## 0. 왜 반려됐나 (검수자 실증, 2026-07-13)

**05H는 지시서 §5 규칙 2를 바꿔서 at-rest를 통과시켰다.**

| | |
| --- | --- |
| 원 지시서 §5-2 | `stateIdSet ⊆ **successfulLogIdSet**` |
| 05H 구현 | `HandoffIntegrityChecker.cs:73`이 **`allLogIdSet`**을 넘긴다 (성공/실패 무관 전체 로그) |

**검수자 반증 시험 F** — state에 있으나 log엔 **실패(exitCode=1)**로만 기록된 전이:

```
state: [SNAP-001, FAILED-BUT-APPLIED]
log  : SNAP-001 ok/0
       FAILED-BUT-APPLIED  result="handoff-integrity-failed exit=1"  exitCode=1

원 지시서대로면 기대 exit = 1
실측         → exit = 0, failures = []      ★ 하네스가 PASS를 준다
```

**그리고 현재 저장소가 정확히 그 상태다:**

- `docs/handoff/WORKSTATE.applier-log.jsonl:20` → `{"transitionId":"DI0004-BLOCKED-CODEX","result":"handoff-integrity-failed exit=1","exitCode":1,...}`
- `docs/handoff/WORKSTATE.json:384` → **같은 id가 `appliedTransitions`에 들어 있다**

즉 **적용이 실패했다고 자기 로그가 말하는 전이가 상태에 남아 있다.** 이것은 fixture 문제가 아니라
**WP-STATE-INTEGRITY 근본결함 #2(post-apply 실패 시 rollback 없음)의 실측 흔적**이고,
규칙 2는 **바로 그것을 잡으라고** 있는 규칙이다.

> **at-rest FAIL은 참 양성이다. 게이트가 빨간 게 아니라 저장소가 빨갛다.**
> **at-rest를 초록으로 만들려고 하지 마라.** 그건 이 작업의 목표가 아니다 — 그건 사람 결재다(§3).

**05H를 만든 실행자를 탓하지 않는다.** 자진 신고했고(잔여위험 #3), 원 지시서 §10 fixture 표에
**이 유형이 아예 없었다.** 반증 자료가 없는 규칙은 규칙이 아니라 주석이다 — 그래서 §2로 고친다.

## 1. 고칠 것 (딱 두 곳)

### 1-1. 규칙 2를 원문으로 복원

`server/Harness/HandoffIntegrityChecker.cs`

```csharp
// 현재 (:73)
var pendingApplied = CheckStateToLog(sets.stateIdSet, sets.allLogIdSet, stateIds!, opts.PendingTransitionId, result);

// 복원
var pendingApplied = CheckStateToLog(sets.stateIdSet, sets.successfulLogIdSet, stateIds!, opts.PendingTransitionId, result);
```

- `CheckStateToLog`의 파라미터명·주석(`:245-253`)도 `successfulLogIdSet` 기준으로 갱신한다.
- **`PendingTransitionId` 면제는 그대로 유지한다** (state에 정확히 1회 · log엔 없음. 어기면 면제 미적용).
- **`allLogIdSet`을 지우지는 마라** — Metrics·malformed 판정에 쓰이면 그대로 둔다. **규칙 2에서만 쓰지 않는다.**
- 실패 코드는 원문대로 `state-transition-not-logged`.

### 1-2. fixture-f 신설 (빠졌던 반증 자료)

`docs/qa/fixtures/reconciliation/fixture-f/{workstate.json,applier-log.jsonl}`

| fixture | 구성 | 기대 |
| --- | --- | --- |
| **F 실패-적용** | log에 `FAILED-BUT-APPLIED`가 **exitCode=1 / result="…failed…"**로만 존재 · state의 `appliedTransitions`엔 **있음** | **exit 1** `state-transition-not-logged` ★ |

- `blockers`·`status`는 fixture-c를 본떠 **잡음 없는 정상값**으로 둔다(이 fixture가 재는 것은 규칙 2 하나다).
- 같은 파일에 성공 전이 1건(`SNAP-001` ok/0)을 함께 둬서 **"전부 실패"가 아니라 "한 건만 오염"**임을 분리한다.

## 2. 하지 않을 일 (하면 반려)

- **at-rest를 exit 0으로 만들려고 규칙을 완화하거나 예외를 추가하는 것.** ← 05H가 반려된 이유 그 자체다.
- `WORKSTATE.json`에서 `DI0004-BLOCKED-CODEX`를 지우거나 고치는 것. **상태 원본 수정은 `state-transition`으로만, 그리고 이건 사람 결재다.**
- known-exception·allowlist·`--ignore` 류의 우회 플래그를 checker에 넣는 것. **필요하면 06C-2 trust-origin이 사람 영수증으로 처리한다.**
- 원 지시서 §1~§9의 다른 규칙을 손보는 것. 이 작업은 **규칙 2와 fixture-f만** 다룬다.
- `HandoffIntegrityChecker`를 `HarnessRegistry`에 등재하는 것(원 지시서 §2 — CLI 표면·pending 우회 방지).

## 3. at-rest는 이제 exit 1이 정상이다

**이 작업이 끝나면 `handoff-integrity` at-rest는 exit 1을 낸다. 그것이 올바른 결과다.**

- 실패 내용은 `state-transition-not-logged` **1건**(`DI0004-BLOCKED-CODEX`)이어야 한다. **2건 이상이면 보고하라 — 오염이 더 있다는 뜻이다.**
- 이 빨간불을 끄는 방법은 두 갈래이고 **둘 다 사람 결재다**(`HUMAN-INBOX.md` 등재됨):
  1. **clean replay** — 전이를 처음부터 재적용해 상태 재구성
  2. **trust-origin 부트스트랩(06C-2)** — 사람이 1회 "여기부터 믿는다" 선언 + `DI0004-BLOCKED-CODEX`를 명시적 known-exception receipt로 기록
- **실행자는 고르지 않는다.** at-rest exit 1을 **결함이 아니라 산출물로 보고하라.**
- 이 때문에 `handoff-integrity`를 부르는 다른 게이트가 잠길 수 있다. **잠기면 잠긴 대로 보고하라 — 풀려고 하지 마라.** 어디가 잠기는지가 06C-2가 알아야 할 정보다.

## 4. 완료 기준 (exit code)

```text
1. dotnet build server -c Release                                → 0
2. fixture-f                                                     → 1  state-transition-not-logged   ★신규
3. fixture A → 1 · B → 1 · C → 0 · D → 1 · E → 1 · malformed → 2  (회귀 없음)
4. CLI --pending-transition                                      → 1  pending-not-allowed-on-cli
5. 내부 checker: state에 X 1회 + log에 X 없음 + Pending=X         → PASS(면제)
                 같은 조건 + Pending 없음                          → FAIL state-transition-not-logged
6. at-rest current repo                                          → 1  ★ 이것이 정상이다 (§3)
                                                                    failures = state-transition-not-logged 정확히 1건
7. dotnet run --project server -c Release -- measure dev-pack     → 0   (-c Release 필수)
```

### 목적 기준 (사람 판정 — ADR-005)

**"실패했다고 기록된 전이가 상태에 적용돼 있으면 하네스가 반드시 빨간불을 켠다."**
지표를 만족시키는 우회로가 있다 — 규칙 2에 예외를 파거나, fixture-f를 규칙 2가 아닌 다른 경로로 통과시키는 것.
**둘 다 목적 미달이며, 자진 신고 없이 하면 반려다.**

## 허용 파일 (allowlist)

> 제목에 번호를 붙이지 마라. `run-executor.ps1`의 `Get-Allowlist`가 `^##\s+허용 파일`로 찾는다 —
> `## 5. 허용 파일`은 **매치되지 않아 발사가 중단된다**(검수자가 발사 전 실측으로 걸렀다, 2026-07-13).

- server/Harness/HandoffIntegrityChecker.cs
- server/Harness/HandoffIntegrityCli.cs
- docs/qa/fixtures/reconciliation/**
- docs/verification/05h-r1-reconciler-rework.md
- docs/handoff/queue/directive-05H-R1-reconciler-rework.md

## 6. 보고

`docs/verification/_template.md` 형식 그대로. **DI 유형 선언**(harness) · 하네스별 **기대 exit vs 실제 exit** ·
실패 분류(v9 §0.3) · 잔여 위험 · **`## 지표는 만족했으나 목적은 미달인 부분`**(ADR-005).

**at-rest exit 1을 "실패"로 적지 마라 — "설계된 참 양성"으로 적고, failures 내용을 그대로 붙여라.**

**자기보고는 증거가 아니다.** 검수자가 반증 시험 F를 포함해 land gate를 직접 재실행한다. 못 한 시험은 `NOT_VERIFIED`. **숨기면 반려다.**
