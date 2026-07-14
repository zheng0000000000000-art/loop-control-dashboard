```context-pack
{
  "diId": "DLINT-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "server/Harness/ContextPackIntegrityCli.cs", "sha256": "ab8de3a12b9a6ac528ab2e897445359d119582b27d43f09fa3233504a0322ba4" },
    { "path": "server/Harness/LaunchCheckCli.cs", "sha256": "5e9f189766dd150eea5a3c37974022dbb55d9b1524f34f00c556160c9bef78f7" },
    { "path": "skills/common/directive-authoring.md", "sha256": "df41047823ebfc2bbcea2827c311b401500a364fcd4140aec5eb2db7c9cb7f45" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-DLINT-01-directive-lint.md",
    "skills/common/directive-authoring.md",
    "server/Harness/ContextPackIntegrityCli.cs",
    "server/Harness/HarnessRegistry.cs"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → positive·negative·결정성·격리 테스트 전부 필수(v9 §0.1).

---

# DLINT-01 — 지시서의 **기계로 잡을 수 있는 결함**을 기계가 잡는다

- actor: **CORE_INFRA_EXECUTOR (sonnet)**
- 발견: 검수자 실측(2026-07-15). **`GATE-TRUTH-01` 지시서가 네 번 연속 틀렸고, 매번 사람이 잡았다.**
- **동반 산출물**: `skills/common/directive-authoring.md` (기계가 **못** 잡는 것 — 이미 커밋됨)

## ★ 선행 (동시 발사 금지 — allowlist가 겹친다)

```
GATE-CP-01      outputs/launch/run-executor.ps1 을 먼저 고친다 (발사 전 context-pack 검사 추가)
                → DLINT-01은 그 자리에 lint 호출을 한 줄 더한다. GATE-CP-01 뒤에만 발사한다.
CODEX-GATE-04   docs/handoff/GATE-MANIFEST.json 을 건드린다 (등재 추가)
GATE-TRUTH-01   docs/handoff/GATE-MANIFEST.json 을 건드린다 (TRUST-ORIGIN-BASELINE 신설)
                → 셋 다 같은 파일을 쓴다. 반드시 순차 발사. 뒤에 오는 쪽이 rebase 후 등재한다.
```

> **이 절이 존재하는 것 자체가 이 하네스가 잡으려는 결함의 사례다** — allowlist 충돌은 오늘까지 아무도 검사하지 않았다.

---

## 0. 문제 — **같은 결함이 반복되는데 아무 하네스도 안 본다** (전부 이번 세션 실측)

| # | 결함 | 지금 잡는 것 |
| --- | --- | --- |
| 1 | **지시서 내부 모순**: 완료 기준이 `declare` 실행을 요구하는데 금지 항목이 `declare`를 금지 | **없음** (사람이 잡았다) |
| 2 | **`requiredInputs` ∩ `allowlist` ≠ ∅** — `_header.md`가 금지하는데 검사가 없다 | **없음** |
| 3 | **미완 지시서끼리 allowlist 충돌**: `ContextPackIntegrityCli.cs`를 `GATE-CP-01`·`CODEX-GATE-04`가 동시 소유 | **없음** |
| 4 | **중복 제작**: `--emit-hashes`가 `CODEX-GATE-04` §5-4와 `GATE-TRUTH-01` 초판에 동시에 | **없음** |
| 5 | **인용한 줄번호·명령이 실재하지 않음** | **없음** |
| 6 | **`## 허용 파일` 절 제목에 번호를 붙여 파싱 0개 → 발사 중단** | 발사 직전에야 죽는다(`run-executor.ps1` fail-closed) |
| 7 | **`--no-build`를 쓰라고 지시** → 게이트 넷이 exit 2 (두 세션 연속 사고) | **없음** |
| 8 | **복합 명령을 한 문자열로**(`"recovery inspect"`) → 라우터가 `args[0]`만 본다 | **없음** |

> **6번은 "발사 직전에 사람이 손으로 발견"의 반복이다.** `_header.md`가 `context-pack-integrity`를 만든 이유가 바로 그것이었다.
> **발사 전에 죽는 것보다 커밋 때 죽는 게 싸다.**

### 재발명 검색 결과 (§0 스킬 절차대로 했다 — 결론: **신규 하네스가 맞다**)

- **`LaunchCheckCli.cs`** — `CODEX-GATE-04` §5-3이 *"지시서 lint는 그쪽 확장일 수 있다"*고 추정했다. **열어보니 발사 프롬프트 전송 증거의 sha256과 replay 이벤트 수를 검사한다. 무관하다.**
  (그리고 `launch-check`가 exit 1이던 이유도 여기 있다 — **인자 없이 부르면 usage 실패**다. per-launch 검사라 repo-wide 게이트에 넣을 수 없다. 고장난 게 아니다.)
- **`ContextPackIntegrityCli.cs`** — Context Pack **블록 내부**(`requiredInputs` 해시)만 본다. **지시서 구조·모순·충돌은 안 본다.** 겹치지 않는다.
- **★ `CODEX-GATE-04` §5-3(지시서 lint를 POST-COMMIT에 등재)은 이 지시서가 흡수한다.** 그 지시서에 이관 표시를 남긴다(allowlist 포함).

---

## 1. 만들 것 — `directive-lint` 하네스

신규 파일 **`server/Harness/DirectiveLintCli.cs`** + `HarnessRegistry`에 `["directive-lint"] = DirectiveLintCli.Run` 한 줄.

### 1-1. 두 가지 모드 — **두 게이트가 서로를 보완한다**

```
directive-lint                      전체. docs/handoff/queue/*.md
                                    ★ 미발사 지시서만 판정한다 (outputs/launch/<diId>.exit.json 없는 것)
                                    발사된 것은 카운트만 남긴다 — 끝난 지시서 때문에 게이트가 영구히 빨간불이 되면 안 된다
                                    → GATE-MANIFEST의 POST-COMMIT 에 등재 (expectedExit 0)

directive-lint --directive <path>   단일. ★ 발사 직전 검사이므로 launched 제외를 적용하지 않는다. 무조건 본다
                                    → run-executor.ps1 이 발사 직전에 부른다. exit != 0 이면 쏘지 않는다 (fail-closed)
```

> **왜 둘 다인가**: 커밋 게이트만 있으면 **커밋되지 않은 지시서로 쏠 수 있다.** 발사 게이트만 있으면 **깨진 지시서가 커밋된 채 쌓인다.**
> **하나를 속이면 다른 하나가 잡는다.** (`GATE-CP-01`과 같은 구조. **`run-executor.ps1`에는 두 검사가 나란히 놓인다.**)

### 1-2. 검사 항목 — **전부 기계 판정. 문자열 감으로 세지 마라**

**D1~D5는 exit 1. D6~D8은 exit 1. 하네스 자신의 오류만 exit 2.**

```
D1  구조
    · ```context-pack 블록 존재 + JSON 파싱 가능           없으면 exit 1
    · ^##\s+허용 파일  절 존재 + 경로 1개 이상 파싱        없으면 exit 1  ← 실측 사고 6번
    · diId 가 파일명·context-pack·본문에서 일치            불일치 exit 1

D2  requiredInputs ∩ allowlist = ∅   (glob 확장 후 경로 비교)
    겹치면 exit 1  "required-input-in-allowlist"
    ← _header.md 가 금지한다: "작업 중 바뀌는 파일에 해시를 걸면 게이트가 자기 작업에 걸려 넘어진다"

D3  미완 지시서 간 allowlist 충돌
    미발사 지시서끼리 allowlist 가 겹치면 exit 1  "allowlist-conflict"
    ★ 단, 지시서에 ^##\s+선행 절이 있고 상대 diId 가 거기 적혀 있으면 통과 (순차 발사 선언)
    → 기계가 판정 가능하다. 산문으로 적으면 못 잡는다

D4  경로 실재
    · requiredInputs 의 경로가 실재하는가                  없으면 exit 1 (context-pack-integrity 와 중복이지만
                                                            단일 모드에서 필요하다. 판정은 겹쳐도 된다)
    · allowlist 의 경로가 실재하거나 "신규"로 명시됐는가    아니면 exit 1
      ★ 신규 파일 표기 규약: allowlist 줄 끝에 (신규) 를 붙인다.
        - server/Harness/DirectiveLintCli.cs (신규)

D5  인용 실재
    · 본문의 <파일>:<줄번호> 패턴 (예: TrustOriginCli.cs:308)
      → 그 파일이 실재하고 총 줄 수 >= 그 줄번호               아니면 exit 1  "stale-citation"
      ※ 내용이 맞는지는 못 잡는다. 그건 사람 몫이다 — 보고에 그렇게 적어라

D6  명령 실재
    · 코드블록의  dotnet run --project server ... -- <cmd> [args]
      → <cmd> 가 실재 배선에 있는가                          없으면 exit 1  "unknown-command"
      ★ 배선은 손으로 적지 말고 열거해서 만든다:
        HarnessRegistry 의 키  +  CliRouter 의 args[0] 비교값
      ★ 복합 명령 오류를 잡는다: "recovery inspect" 를 한 토큰으로 쓰면 unknown-command 다

D7  금지 항목 ↔ 완료 기준 모순
    · context-pack 의 forbiddenActions 토큰에 대응하는 명령이
      "완료 기준" 코드블록에 등장하는가
      → 등장하면:  지시서에 ^##\s+금지 예외  절이 있어야 통과. 없으면 exit 1  "forbidden-in-acceptance"
    ★ 오탐을 줄이려고 판정을 빼지 마라. 예외를 "선언하게" 만들어라 — 선언은 기계가 읽는다
    ← 실측: 초판이 declare 실행을 요구하면서 동시에 금지했다. 실행자가 둘 중 하나를 어겨야 완료됐다

D8  알려진 함정
    · 본문이 --no-build 를 지시하는가                       하면 exit 1  "no-build-directed"
      (Release 산출물이 낡으면 게이트 넷이 exit 2. 두 세션 연속 실측 사고)
```

**출력에 반드시 남긴다** (숨기지 말고 카운트한다):

```
pendingCount · launchedCount · violations[] (diId · code · detail)
staleButLaunched[]      ← 발사된 지시서의 위반. 판정에서 빼되 목록으로 보인다
```

> **정보는 남기고 판정에서만 뺀다.** 영구히 빨간 게이트는 무시되고, 무시되는 게이트는 없는 게이트다(`FAIL-2026-010`).

### 1-3. 발사 게이트 배선 (`run-executor.ps1`)

```
run-executor.ps1 -TaskId <X>
  1. 지시서를 찾는다 (이미 함)
  2. allowlist 파싱 (이미 함)
  3. context-pack-integrity --directive <지시서>     ← GATE-CP-01이 넣는다
  4. ★ 신규: directive-lint --directive <지시서>
       dotnet run --project server -c Release -- directive-lint --directive <지시서경로>
       exit != 0 → 발사 중단. 위반 목록을 출력한다
  5. 발사
```

- **`-c Release`로 불러라.** `--no-build` 금지.
- **⚠ `run-executor.ps1`은 BOM 있는 UTF-8이다. 유지하라.** BOM을 없애면 **한글 리터럴이 파괴돼 `Get-Allowlist`가 0개를 반환하고 발사가 죽는다**(실측 사고 — `skills/common/powershell-encoding.md`).

---

## 2. 하지 않을 일 (하면 반려)

- ❌ **기존 지시서를 고쳐서 lint를 통과시키는 것.** **먼저 현재 큐 8건에 lint를 돌려 위반을 그대로 보고하라.** 고치는 것은 그다음이고, **사람이 판단한다.**
- ❌ **오탐이 난다고 판정 항목을 빼는 것.** **예외를 "선언하게" 만들어라**(`## 선행`·`## 금지 예외`). 선언은 기계가 읽는다.
- ❌ **발사된 지시서의 위반을 출력에서 숨기는 것.** 판정에서만 빼고 `staleButLaunched`에 남긴다.
- ❌ **`--directive` 모드에 launched 제외를 적용하는 것.** 발사 직전 검사다. **무조건 본다.**
- ❌ **배선 목록(`HarnessRegistry`·`CliRouter`)을 손으로 적는 것.** 열거해서 만든다.
- ❌ **`run-executor.ps1`의 BOM 제거.**
- ❌ **`GATE-CP-01`보다 먼저 발사하는 것.** `run-executor.ps1`이 충돌한다.
- ❌ **무접촉**: `server/TrustOriginCli.cs`·`server/GateRunner.cs`(GATE-TRUTH-01) · `server/Harness/HandoffIntegrityChecker.cs`(05H) · `server/StateApplierCli.cs`·`server/Cli/**`(06C-1) · `dashboard/` · `WORKSTATE.json`·`applier-log`.
- ❌ push·결재·반입·발사.

---

## 3. 완료 기준 (exit code)

```text
 1. dotnet build server -c Release                                         → 0, warning 0

 2. ★ 현재 큐 8건에 lint 를 돌린 결과를 그대로 보고하라 (고치지 말고)
      dotnet run --project server -c Release -- directive-lint
      → 위반이 나오면 그대로 적어라. 이 하네스가 실제로 무언가를 잡는다는 증거다
      ※ 위반 0이면 그것도 적어라 — 단, D3(allowlist 충돌)는 최소 1건 나와야 정상이다
        (GATE-CP-01 · CODEX-GATE-04 가 ContextPackIntegrityCli.cs 를 동시 소유한다 — 실측)

 3. D1 negative: ## 5. 허용 파일 (번호 붙인 제목) 지시서                  → exit 1
 4. D1 negative: context-pack 블록 없는 지시서 (신규)                     → exit 1
      ※ 구 지시서(#1~#19)는 skipped — 과거 때문에 게이트가 영구 잠기면 안 된다
 5. D2 negative: requiredInputs 와 allowlist 가 겹치는 지시서             → exit 1
 6. D3 negative: allowlist 가 겹치는 미발사 지시서 2건                    → exit 1
 7. D3 positive: 위 2건 중 하나에 ## 선행 절로 상대 diId 를 선언          → exit 0
 8. D5 negative: 존재하지 않는 파일:줄번호 인용                           → exit 1
 9. D6 negative: dotnet run ... -- ghost-command                          → exit 1
10. D6 negative: "recovery inspect" 를 한 토큰으로 쓴 지시서              → exit 1  (복합 명령 오류)
11. D7 negative: forbiddenActions 의 명령이 완료 기준에 등장 + 금지 예외 절 없음  → exit 1
12. D7 positive: 같은 지시서에 ## 금지 예외 절 추가                       → exit 0
13. D8 negative: --no-build 를 지시하는 지시서                            → exit 1
14. launched 제외: exit.json 있는 지시서의 위반                           → 전체 모드 exit 0,
      단 staleButLaunched 목록에 보인다 (숨기지 않았다는 증거)
15. --directive <발사된 지시서> (위반 상태)                               → exit 1
      ← 단일 모드는 launched 제외를 적용하지 않는다
16. --directive <없는 경로>                                               → exit 2
17. ★ 발사 게이트: 위반 있는 지시서로 run-executor.ps1 발사 시도          → 중단. exit != 0. 이유 출력
      ★ 고치기 전엔 그대로 발사된다는 것을 먼저 보여라
18. 정상 지시서로 발사                                                    → 정상 진행 (회귀 없음)
19. run-executor.ps1 첫 3바이트가 EF BB BF (BOM 유지)                     → 확인
20. 결정성: 같은 입력 2회                                                 → 같은 exit·같은 출력
21. 격리: lint 실행 전후 git status 동일 (읽기 전용)
22. ★ 자기 적용: directive-lint --directive <이 지시서(DLINT-01)>         → exit 0
      ← 자기 규칙을 자기가 통과하지 못하면 반려다
23. handoff-integrity / context-pack-integrity / doc-integrity / gate-clean server → 0 / 0 / 0 / 0
24. measure dev-pack (-c Release)                                         → 0, violationCount 0
```

> **⚠ 시험용 지시서는 `$TEMP` 사본에서 만들어라.** 저장소 안에서 시험하다 **`docs/wiki` 42파일을 지운 사고**가 있었다.
> **⚠ 게이트를 재기 전에 반드시 `dotnet build server -c Release`.**

---

## 목적 기준 (사람 판정 — ADR-005)

> **"같은 결함이 반복되는데 사람이 매번 다시 잡는 것은 절차가 아니다.**
> **`GATE-TRUTH-01`이 네 번 틀렸고 네 번 다 사람이 잡았다. 그중 기계가 잡을 수 있었던 것은 기계가 잡아야 한다."**

**우회로 (전부 반려)**:
오탐이 난다고 판정 항목 빼기 · 기존 지시서를 고쳐서 초록을 만들고 위반을 보고 안 하기 ·
발사된 지시서의 위반을 출력에서 숨기기 · `--directive` 모드에 launched 제외 적용하기 ·
배선 목록을 손으로 적기 · **완료 기준 2번(현재 큐의 실제 위반)을 건너뛰기** ·
**완료 기준 22번(자기 적용)을 건너뛰기.**

**요구가 서로 모순되거나 빠진 게 있으면 완화하지 말고 보고해라.**

---

## 허용 파일 (allowlist)

- server/Harness/DirectiveLintCli.cs (신규)
- server/Harness/HarnessRegistry.cs
- docs/handoff/GATE-MANIFEST.json
- outputs/launch/run-executor.ps1
- docs/handoff/queue/directive-CODEX-GATE-04-gate-truth.md
- docs/verification/dlint-01-directive-lint.md
- docs/handoff/queue/directive-DLINT-01-directive-lint.md

> `outputs/launch/run-executor.ps1` · `docs/handoff/GATE-MANIFEST.json` 은 **다른 미완 지시서와 겹친다.**
> **`## 선행` 절을 지켜라 — `GATE-CP-01` 이후, `CODEX-GATE-04`·`GATE-TRUTH-01`과 순차.**
> `docs/handoff/queue/directive-CODEX-GATE-04-gate-truth.md` 는 **§5-3 이관 표시 1건만.** 다른 절 무접촉.
> `server/Harness/ContextPackIntegrityCli.cs` **무접촉** — `GATE-CP-01`·`CODEX-GATE-04` 영역이다. **읽기만.**

---

## 보고

`docs/verification/dlint-01-directive-lint.md` — `_template.md` 형식 그대로.

**반드시 적을 것:**

- **★ 완료 기준 2번** — **현재 큐 8건의 실제 위반 목록.** 이 하네스가 무언가를 잡는다는 증거다. **고치지 말고 보고만 해라**
- 완료 기준 3~16의 **기대 exit vs 실제 exit 표** (positive·negative 쌍으로)
- **★ 완료 기준 17** — **고치기 전엔 위반 지시서로도 발사된다**는 실증
- **★ 완료 기준 22** — **자기 적용 결과**
- **D5의 한계를 명시하라** — 줄번호 범위는 잡지만 **인용 내용이 맞는지는 못 잡는다**
- **오탐이 난 항목이 있으면 그대로 적어라.** 빼지 말고 보고해라
- **`## 지표는 만족했으나 목적은 미달인 부분`**(ADR-005). 없으면 "없음"

**자기보고는 증거가 아니다.** 검수자가 재실행한다. 못 한 시험은 `NOT_VERIFIED`. **숨기면 반려다.**
