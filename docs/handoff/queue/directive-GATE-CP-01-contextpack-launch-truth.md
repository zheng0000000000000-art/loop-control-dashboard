```context-pack
{
  "diId": "GATE-CP-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "server/Harness/ContextPackIntegrityCli.cs", "sha256": "ab8de3a12b9a6ac528ab2e897445359d119582b27d43f09fa3233504a0322ba4" },
    { "path": "outputs/launch/run-executor.ps1", "sha256": "bd49b26c8b58bfeda2ea041d9533950f76728f484ef85aaa06e5d0a3ec741e1f" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-GATE-CP-01-contextpack-launch-truth.md",
    "server/Harness/ContextPackIntegrityCli.cs",
    "outputs/launch/run-executor.ps1"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → positive·negative·결정성·격리 테스트 전부 필수(v9 §0.1).

---

# GATE-CP-01 — `context-pack` 검사가 **쏠 것을 검사하게** 한다

- actor: **CORE_INFRA_EXECUTOR (sonnet)**
- 발견: 검수자 실측(2026-07-14)

---

## 0. 문제 — **정확히 거꾸로다**

`context-pack-integrity`의 존재 이유는 `_header.md`에 적혀 있다:

> *"지시서가 **삭제된 파일**을 계속 가리켰다. `docs/STATUS.md`가 'ORCH-01 참조 `.cs` 준비됨'이라고 적었지만
> 그 파일은 커밋 `797e7bc`가 **이미 지운 상태**였다. **발사 직전에 사람이 손으로** 발견했다 —
> 하네스 6개가 돌고 있었는데 아무도 못 잡았다."*

**즉 "발사 전에 낡은 참조를 차단한다"가 목적이다.**

**실측 (2026-07-14):**

```
context-pack-integrity  →  exit 1, stale 5건
  06C-1-R1 · 06C-2-R2 · 06C-2-R3 · 06C-2-R4 · GUARD-02
  → 다섯 개 다 outputs/launch/<id>.exit.json 이 있다.  ★ 전부 이미 쏜 것들이다.
  → 참조 입력이 그 뒤에 정당하게 바뀌었다 (06H가 RECOVERY.md 갱신, 06C-1이 StateApplierCli.cs 재구현)

run-executor.ps1  →  발사 전에 context-pack을 검사하지 않는다.  ★★
```

> **이미 쏜 것을 검사하고(무의미), 쏠 것은 검사하지 않는다(위험).**
> **하네스가 다른 것을 잰다**(`FAIL-2026-015`의 열두 번째 사례).

**그리고 게이트가 영구히 빨간불이다.** 끝난 지시서의 참조는 **앞으로도 계속 낡는다.**
**영구히 빨간 게이트는 무시된다. 무시되는 순간, 진짜 stale — 발사 직전 지시서가 삭제된 파일을 가리키는 것 — 을 놓친다.**
**이 하네스가 막으려던 바로 그 사고다.** (`FAIL-2026-010`: 줄바꿈 차이가 게이트를 영구 잠금 — 같은 계열)

## 1. 고칠 것 — 두 게이트가 **서로를 보완한다**

### ★ 1-1. 발사 게이트: `run-executor.ps1`이 **발사 직전에 그 지시서를 검사한다** (진짜 방어선)

**이게 없어서 하네스가 목적을 잃었다.**

```
run-executor.ps1 -TaskId <X>
  1. 지시서를 찾는다 (이미 함)
  2. allowlist를 파싱한다 (이미 함)
  3. ★ 신규: 그 지시서 하나의 context-pack을 검사한다
       dotnet run --project server -c Release -- context-pack-integrity --directive <지시서경로>
       exit != 0  →  발사 중단. 이유를 출력한다.
  4. 발사
```

- **fail-closed다.** stale/missing이면 **쏘지 않는다.**
- **`--directive <path>` 인자를 `context-pack-integrity`에 추가하라** (단일 지시서 검사 모드).
- **`-c Release`로 불러라.** `--no-build`를 쓰지 마라 — Debug 바이너리를 검사하게 된다(실측 결함).
- **⚠ `run-executor.ps1`은 BOM 있는 UTF-8이다. 유지하라.** BOM을 없애면 **한글 리터럴이 파괴되어
  `Get-Allowlist`가 0개를 반환하고 발사가 죽는다**(실측 사고 — `skills/common/powershell-encoding.md`).

### 1-2. 커밋 게이트: `context-pack-integrity`가 **미발사 지시서만** 검사한다

**"발사된 적 있다"의 실체 = `outputs/launch/<diId>.exit.json` 존재.**
**이 파일은 발사기(프로그램)가 만든다.** 사람·LLM의 자기보고가 아니다.

```
지시서마다:
  outputs/launch/<diId>.exit.json 존재  →  verdict "launched". stale/missing 검사에서 제외. 카운트만 남긴다.
  없음                                  →  지금처럼 검사한다.
```

**출력에 반드시 남겨라**: `launchedCount`, `pendingCount`, 그리고 **`launched` 지시서 중 stale인 것의 목록**
(제외하되 **숨기지는 않는다** — `staleButLaunched: [...]`. **정보는 남기고 판정에서만 뺀다.**)

> **왜 우회 위험이 없는가**: 누군가 `exit.json`을 손으로 만들어 커밋 게이트를 속여도,
> **§1-1의 발사 게이트가 발사 시점에 그 지시서를 다시 검사한다.** 낡은 지시서로는 **쏠 수 없다.**
> **두 게이트가 서로를 보완한다. 하나를 속이면 다른 하나가 잡는다.**
>
> **⚠ 그러나 §1-1 없이 §1-2만 하면 우회로가 생긴다.** **반드시 둘 다 해라.** 하나만 하면 반려다.

### 1-3. `--directive <path>` 단일 검사 모드

`context-pack-integrity --directive docs/handoff/queue/directive-X.md`

- **그 지시서 하나만** 검사한다. **`launched` 제외 규칙을 적용하지 않는다** — 발사 직전 검사이므로 **무조건 본다.**
- stale/missing → **exit 1** + 어느 파일이 왜 어긋났는지(기대 hash / 실측 hash).
- 지시서가 없거나 context-pack 블록이 없으면 → **exit 2**(usage/하네스 오류). **skipped로 조용히 통과시키지 마라.**

## 2. 하지 않을 일 (하면 반려)

- **끝난 지시서의 sha256을 다시 박아서 초록을 만드는 것.** 그건 **이력 조작**이고, 다음에 또 낡는다.
- **`--no-build`로 하네스를 부르는 것**(Debug 바이너리를 검사하게 된다).
- **`run-executor.ps1`의 BOM을 없애는 것.**
- **§1-2만 하고 §1-1을 안 하는 것.** 우회로가 생긴다.
- `launched` 판정을 **verification 문서 존재**로 하는 것 — 그건 **실행자의 자기보고**다.
  **발사기가 만든 `exit.json`이 실체다.**
- `WORKSTATE.json`·`applier-log` 수정. **at-rest handoff-integrity를 exit 0으로 만들려는 시도**(exit 1이 정상이다).

## 3. 완료 기준 (exit code) — ★ 표시는 **"고치기 전엔 뚫렸다"를 먼저 보여라**

```text
 1. dotnet build server -c Release                                              → 0
 2. ★ 고치기 전 재현: run-executor.ps1이 stale 지시서로도 발사한다              → 보고에 그대로 적어라
      (미발사 지시서 하나의 requiredInput sha256을 손으로 틀리게 바꾸고 발사 시도)
 3. ★ 고친 뒤: 같은 조건에서 발사                                               → 중단. exit != 0. 이유 출력
 4. 정상 지시서로 발사                                                          → 정상 진행 (회귀 없음)
 5. context-pack-integrity (전체)                                              → 0
      staleButLaunched에 5건이 그대로 보인다 (숨기지 않았다)
      pendingCount / launchedCount 출력
 6. ★ 미발사 지시서의 requiredInput을 틀리게 바꾸고 context-pack-integrity      → 1 (여전히 잡는다)
 7. ★ 발사된 지시서의 requiredInput을 틀리게 바꾸고 context-pack-integrity      → 0 (제외된다)
      단 staleButLaunched 목록에는 나타난다
 8. context-pack-integrity --directive <발사된 지시서> (stale 상태)             → 1
      ← ★ 단일 모드는 launched 제외를 적용하지 않는다
 9. context-pack-integrity --directive <context-pack 없는 지시서>               → 2 (조용히 통과 금지)
10. context-pack-integrity --directive <없는 경로>                              → 2
11. 결정성: 같은 입력 2회 연속                                                  → 같은 exit·같은 출력
12. 격리: 하네스 실행 전후 git status 동일 (읽기 전용)
13. measure dev-pack (-c Release)                                              → 0
```

> **⚠ 시험용 파일 수정은 `$TEMP` 사본에서 하라.** 저장소 안에서 시험하다
> **검수자가 `docs/wiki` 42파일을 지운 사고**가 있었다. 부득이 저장소에서 했다면
> **원복을 blob 해시로 확인하라**(`git hash-object` vs `git rev-parse HEAD:<path>`).
> **`git status`는 프록시다.**

## 목적 기준 (사람 판정 — ADR-005)

**"게이트는 쏠 것을 검사해야 한다. 이미 쏜 것을 검사하는 게이트는 잡음이고, 잡음은 무시되고, 무시되는 게이트는 없는 게이트다."**

우회로: 끝난 지시서 해시를 다시 박기 · `launched` 판정을 자기보고(verification 문서)로 하기 ·
§1-1을 빼고 §1-2만 하기 · `--directive` 모드에서도 `launched`를 제외하기.
**전부 반려다. 자진 신고 없이 하면 반려다. 신고하면 감점이 아니다.**

**요구가 서로 모순되거나 빠진 게 있으면 완화하지 말고 보고해라.**

## 허용 파일 (allowlist)

- server/Harness/ContextPackIntegrityCli.cs
- outputs/launch/run-executor.ps1
- docs/verification/gate-cp-01-contextpack-launch-truth.md
- docs/handoff/queue/directive-GATE-CP-01-contextpack-launch-truth.md

> `server/StateApplierCli.cs`·`server/Harness/HandoffIntegrityCli.cs`·`TrustOriginCli.cs` **무접촉.**
> **끝난 지시서 파일들 무접촉** — 해시를 다시 박지 마라.

## 보고

`docs/verification/gate-cp-01-contextpack-launch-truth.md` — `_template.md` 형식 그대로.

**반드시 적을 것:**
- **완료 기준 2·3·6·7·8의 실제 출력.** **"고치기 전엔 뚫렸다"를 보인 다음에만 "막았다"가 성립한다.**
- **`staleButLaunched` 5건이 출력에 그대로 보인다는 증거** — 제외했지 숨기지 않았다.
- **`run-executor.ps1`이 여전히 BOM 있는 UTF-8인지** 확인한 증거(첫 3바이트 `EF BB BF`).
- **`## 지표는 만족했으나 목적은 미달인 부분`**(ADR-005).

**자기보고는 증거가 아니다.** 검수자가 재실행하고 **코덱스가 read-only로 독립 검수한다.**
못 한 시험은 `NOT_VERIFIED`. **숨기면 반려다.**
