# CODEX-GATE-04 — 게이트가 진실을 말하게 (CODEX-GATE-02의 살아남은 절반)

```context-pack
{
  "diId": "CODEX-GATE-04",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-CODEX-GATE-04-gate-truth.md"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → positive·negative·결정성·격리 테스트 전부 필수(v9 §0.1).

## ⚠️ 경계 — `CODEX-GATE-02`는 폐기됐다. 05H와 겹치지 마라

`CODEX-GATE-02`의 **멱등 대조·`blockers[]` 정정**은 **`05H`(reconciliation)**가 훨씬 정교하게 다시 한다. **중복 제작 금지**(재발명 금지).

- ❌ **`server/Harness/HandoffIntegrityCli.cs` 무접촉** — **05H의 영역이다.**
- ❌ 멱등 대조·reconciliation·`blockers[]`·v2 log — **손대지 마라.**
- ✅ 이 지시서는 **`DiCompletionCheckCli.cs`·`ClaimCheckCli.cs`·`GATE-MANIFEST.json`·`CLI-CONTRACT.json`만** 다룬다.

## 목적 — 게이트가 세 가지를 거짓말한다 (전부 실측)

### 1. ★ 게이트가 **다른 바이너리**를 검사한다 (가장 위험)

`DiCompletionCheckCli.RunDotnetCommand`(:142)가 하위 검사를 이렇게 부른다:

```
dotnet run --no-build --project server -- <command>      ← -c Release 가 없다 = Debug 기본
```

- 우리 관례는 **`-c Release` 빌드**다(`_header.md`·모든 지시서).
- **게이트는 Debug 바이너리를 실행한다. Release만 빌드하면 게이트는 낡은 Debug 코드를 검사한다.**
- **실측 사고(2026-07-12 23:4x)**: GUARD-03 실행자가 `HandoffIntegrityCli.cs`를 고치고 Release로 빌드했는데, `di-completion-check`가 **옛 Debug 바이너리**를 써서 `handoff-integrity` exit 1을 냈다. Debug를 따로 재빌드하고서야 PASS가 나왔다.
- **즉 "게이트 PASS"가 방금 고친 코드에 대한 PASS가 아닐 수 있다.**

**고쳐라:**
- 호출한 것과 **같은 구성**으로 서브프로세스를 부른다(`-c Release` 전달, 또는 실행 중 어셈블리 경로에서 구성 판별).
- **`--no-build`인데 산출물이 소스보다 오래됐으면 fail-closed로 죽어라**(exit 2 + 이유). **조용히 낡은 바이너리로 판정하지 마라.**

### 2. CLI 배선이 사라져도 게이트가 green이다

**실측 사고**: `state-transition` 배선이 통째로 사라졌는데 `build` 0 · `gate-clean` 0 · `di-completion-check POST-COMMIT` **5/5 PASS**였다.

**고쳐라:** `docs/handoff/CLI-CONTRACT.json` 신설(schemaVersion 1). **초기값은 현재 실재 배선에서 열거해 생성한다 — 손으로 적지 마라**(`CliRouter`의 `args[0]` 비교 + `HarnessRegistry` 키).

- **계약에 있는데 배선에 없으면 exit 1** · 배선에 있는데 계약에 없으면 **warning**(새 명령 추가는 정상).
- `critical: true`(`state-transition`·`projection`·`measure`·하네스 전부)가 사라지면 **무조건 실패**.

### 3. 등재되지 않은 검사는 아무도 돌리지 않는다

`di-completion-check`가 `scope-check`·`claim-check`를 **`unlisted` 경고로만** 뱉는다.
`scope-check`는 지시서 제목 하나 때문에 **exit 2로 죽어 있었는데 아무도 몰랐다.**

**고쳐라:** `GATE-MANIFEST.json`의 `POST-EXECUTOR`·`POST-COMMIT`에 등재.
⚠️ **판정할 수 없는 검사는 넣지 마라** — `scope-check`는 dirty 트리에서 늘 exit 1이다. 기대값을 정할 수 없으면 **넣지 말고 `note`에 이유를 적어라.**

### 4. `claim-check`가 untracked를 못 본다

`ClaimCheckCli.cs`의 심볼 검색이 `git grep -l {sym} -- server`를 쓴다 — **untracked 미검색.** 그래서 신규 파일이 포함된 배치마다 **MISMATCH 오탐**(조율자 16회 연속 재현, 커밋이 계속 보류됐다).

**고쳐라:** `--untracked` 추가 → **오탐이 사라지는지 실측**(고치기 전 MISMATCH를 먼저 재현해 보여라).

## 5. ★ 추가 실측 (검수자, 2026-07-13) — 게이트가 거짓말하는 네 가지 방식이 더 있다

### 5-1. `scope-check`는 **죽은 게 아니라 잡음에 잠겨 있다** (앞선 "죽어 있다"는 판정 정정)

`scope-check`는 `HarnessRegistry.cs:13`에 **정상 등록돼 있다.** exit 2는 **인자를 잘못 준 것**이었다
(위치 인자 `<directivePath|diId>`인데 `--directive` 플래그를 준 검수자 실수).

**올바른 호출로 재실측** — `scope-check 05H-R1 --actor sonnet`:

```json
{ "verdict":"FAIL", "allowlistCount":5, "changedFileCount":134,
  "outOfScopeCount":131,          ← 그중 실행자가 만든 것은 0건
  "claimConflictCount":0, "staleClaimCount":3, "unknownAllowlistClaimCount":21 }
```

**131건 전부가 `outputs/`(발사기·하네스 자신의 산출물)와 `dashboard/data/*`(measure가 쓰는 런타임 데이터)다.**
**하네스가 자기 배설물을 범위 이탈로 센다.** 그래서 늘 FAIL이고, 그래서 아무도 못 쓴다.

**고쳐라:** byproduct **ignore set**을 도입한다(`outputs/**`, `dashboard/data/*/{measurement,run-log,workflow-state,patch-proposal,review-report}.json`,
`*.pid` 등). **ignore set은 지시서가 아니라 하네스 설정에 둔다** — 지시서마다 다시 쓰게 하면 아무도 안 쓴다.
`staleClaim`·`unknownAllowlistClaim`은 **FAIL이 아니라 warning**으로 강등한다(과거 찌꺼기이지 이번 작업의 이탈이 아니다).

### 5-2. `FILE-CLAIMS`의 liveness가 **PID 프록시**다 — PID 재사용으로 거짓말한다

`GUARD-03-15956` claim: `status=active`, `claimedAt=2026-07-12T23:16`.
**PID 15956은 지금도 살아 있다.** 그런데 그 프로세스는 **2026-07-13 21:52에 시작된 다른 `claude`**다.
**PID가 재사용됐다.** GUARD-03의 프로세스는 이미 죽었는데 claim은 "살아있다"고 말한다.

그 claim은 `server/Harness/HandoffIntegrityCli.cs`를 소유했다고 주장한다 — **05H가 방금 고친 그 파일이다.**

**고쳐라:** liveness를 PID로 판정하지 마라. 최소한 **PID + 프로세스 시작시각 ≥ claimedAt**을 함께 본다.
`expiresAt`(이미 필드에 있다)이 지났으면 **PID 상태와 무관하게 만료**로 본다.
**"프록시로 단정하지 마라"가 CLAUDE.md 규칙인데 하네스가 그걸 어기고 있다.**

### 5-3. ~~지시서 allowlist 절 제목이 문자열 계약인데 아무도 검사하지 않는다~~ → **`DLINT-01`로 이관 (2026-07-15)**

> **⛔ 이 절은 폐기됐다. 여기서 구현하지 마라 — 중복 제작이다.**
> `docs/handoff/queue/directive-DLINT-01-directive-lint.md`가 **지시서 lint 전체**(구조·모순·allowlist 충돌·인용·명령 실재)를 다룬다.
> **`LaunchCheckCli`의 확장일 수 있다는 아래 추정은 틀렸다** — 검수자가 열어봤다. 그 파일은 **발사 프롬프트 전송 증거의 sha256과 replay 이벤트 수**를 검사한다. 지시서와 무관하다. **`DLINT-01`은 신규 하네스가 맞다.**
> 아래 원문은 근거 보존용으로 남긴다.

#### (원문 — 폐기)

`run-executor.ps1`의 `Get-Allowlist`는 `^##\s+허용 파일`로 찾는다.
검수자가 `## 5. 허용 파일 (allowlist)`이라고 번호를 붙였더니 **파싱 결과 0개 → 발사가 중단됐다**(:120-122, fail-closed).
fail-closed 자체는 옳다. **문제는 발사 직전까지 아무도 몰랐다는 것이다.**

**고쳐라:** 지시서 lint를 `GATE-MANIFEST`의 **POST-COMMIT**에 넣는다 —
`^##\s+허용 파일` 절 존재 · 경로 1개 이상 파싱 · Context Pack 블록 존재. **없으면 exit 1.**
발사 전에 죽는 것보다 **커밋 때 죽는 게 싸다.**

### 5-4. `context-pack`의 sha256을 **어느 런타임이 계산하는지**가 정해져 있지 않다

`_header.md`는 "sha256은 프로그램이 계산한다. LLM이 적지 않는다"고 옳게 쓴다. **한 줄이 빠졌다 — "어느 프로그램이냐"도 정해야 한다.**

**실측**: 같은 파일을 리눅스 마운트에서 `sha256sum`으로 재니 `00d377d6…`, `context-pack-integrity`(Windows)는 `a426289a…`를 봤다.
**줄끝 차이가 아니다**(CRLF=0 확인). 마운트 뷰가 **지연된 바이트**를 돌려줬다.
그 해시를 그대로 박았더니 `context-pack-integrity` **exit 1 (stale)** — 하네스가 잡았다.

**고쳐라:** `context-pack-integrity`에 **`--emit-hashes <directivePath>`** 를 추가한다 —
그 지시서의 `requiredInputs` 경로들의 해시를 **하네스 자신이 계산해 출력**한다.
사람·LLM이 다른 도구로 재서 옮겨적는 경로 자체를 없앤다. **계산하는 쪽과 검사하는 쪽이 같은 프로그램이어야 한다.**

### 반증 시험 (§5용 — 위 표에 추가)

| # | 시험 | 기대 |
| --- | --- | --- |
| 7 | 실행자 산출물 0건 + `outputs/` 잡음 다수인 트리에서 `scope-check <diId>` | **verdict PASS** (byproduct는 이탈이 아니다) · 진짜 이탈 1건을 심으면 **FAIL** |
| 8 | `claimedAt`보다 **나중에 시작된** 프로세스가 그 PID를 쓰는 상황 | claim을 **살아있다고 보지 않는다**(만료 판정) |
| ~~9~~ | ~~allowlist 절 제목에 번호를 붙인 지시서~~ | **DLINT-01로 이관. 여기서 하지 마라** |
| 10 | `--emit-hashes`로 뽑은 해시를 그대로 넣은 지시서 | `context-pack-integrity` **exit 0** (stale 0) |

## 하지 않는 것

- ❌ **`HandoffIntegrityCli.cs`·`HandoffIntegrityChecker.cs` 무접촉**(05H·05H-R2). **`StateApplierCli.cs`·`server/Cli/**`·`Program.cs` 무접촉**(06C-1).
- ❌ **신규 하네스 제작.** 넷 다 기존 확장이다. 필요하면 만들지 말고 **`CODEX-QUEUE.md`에 근거와 함께 올려라.**
- ❌ 기준 파일(`blueprint.json`·`workflow-definition.json`·`behavior-snapshot.json`) 수정. push·결재·반입·발사.

## 필수 반증 시험 (전부 실측. **코드 검토로 갈음하지 마라**)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | 소스를 고치고 **한 구성만 빌드**한 뒤 `di-completion-check` | **낡은 바이너리로 PASS를 주지 않는다**(올바른 구성 실행 또는 fail-closed exit 2) |
| 2 | `CLI-CONTRACT.json`에 없는 명령(`ghost-command`, critical) 추가 | **exit 1**. 빼면 **exit 0** |
| 3 | 실제 배선에서 명령 하나를 임시로 제거(사본/브랜치) | **exit 1** — 오늘의 사고가 소리를 낸다 |
| 4 | 새 untracked 파일로 `claim-check` | **오탐 없음**(수정 전 MISMATCH를 먼저 재현) |
| 5 | 같은 입력 2회 연속 | 같은 exit·같은 출력(결정성) |
| 6 | 하네스 실행 전후 `git status` 동일 | 격리(읽기 전용) |

## 검수 기준

1. `build-verify` **exit 0**, warning 0 · `verify-behavior` **exit 0**
2. `measure dev-pack` **violationCount 0** — ⚠️ `dotnet run --project server -c Release -- measure dev-pack`으로 실행(exe 직접 호출은 부모 폴더를 봐서 exit 2)
3. 반증 6개 전부 실측
4. `di-completion-check --gate POST-EXECUTOR --task CODEX-GATE-04` **gateVerdict PASS**
5. **목적 기준**: **"게이트 PASS"가 방금 고친 코드에 대한 PASS임을 프로그램이 보장하는가.**

## 허용 파일 (allowlist)

- server/Harness/DiCompletionCheckCli.cs
- server/Harness/ClaimCheckCli.cs
- server/Harness/ScopeCheckCli.cs
- server/Harness/ContextPackIntegrityCli.cs
- docs/handoff/CLI-CONTRACT.json
- docs/handoff/GATE-MANIFEST.json
- docs/qa/codex-gate-04.md
- docs/handoff/sessions/SESSION-2026-07-13-codex-056.md
- docs/verification/codex-gate-04.md
- docs/handoff/queue/directive-CODEX-GATE-04-gate-truth.md

> **§5 추가로 `ScopeCheckCli.cs`·`ContextPackIntegrityCli.cs`가 allowlist에 들어왔다.**
> 둘 다 실재를 확인했다(`server/Harness/` 목록 + `HarnessRegistry` 등록: `scope-check`, `context-pack-integrity`).
> `LaunchCheckCli.cs`가 이미 있다 — **§5-3의 지시서 lint는 신규 하네스가 아니라 그쪽 확장일 수 있다. 먼저 읽어보고 판단하라(재발명 금지).**

> `server/Harness/HandoffIntegrityCli.cs` **무접촉**(05H) · `server/StateApplierCli.cs`·`server/Cli/**`·`server/Program.cs` **무접촉**(06C-1) · `dashboard/` 무접촉.

## 보고

`docs/verification/codex-gate-04.md` — `docs/verification/_template.md` 형식 그대로. **DI 유형(`harness`) 선언** · 유형별 필수 검증(positive·negative·결정성·격리) · 하네스별 **기대 exit vs 실제 exit** · 실패 분류(v9 §0.3) · 잔여 위험 · **`## 지표는 만족했으나 목적은 미달인 부분`**.
**자기보고는 증거가 아니다.** 못 한 시험은 `NOT_VERIFIED`. **숨기면 반려다.**
