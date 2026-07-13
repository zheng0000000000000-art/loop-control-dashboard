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

## 하지 않는 것

- ❌ **`HandoffIntegrityCli.cs` 무접촉**(05H). **`StateApplierCli.cs`·`server/Cli/**`·`Program.cs` 무접촉**(06C-1).
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
- docs/handoff/CLI-CONTRACT.json
- docs/handoff/GATE-MANIFEST.json
- docs/qa/codex-gate-04.md
- docs/handoff/sessions/SESSION-2026-07-13-codex-056.md
- docs/verification/codex-gate-04.md
- docs/handoff/queue/directive-CODEX-GATE-04-gate-truth.md

> `server/Harness/HandoffIntegrityCli.cs` **무접촉**(05H) · `server/StateApplierCli.cs`·`server/Cli/**`·`server/Program.cs` **무접촉**(06C-1) · `dashboard/` 무접촉.

## 보고

`docs/verification/codex-gate-04.md` — `docs/verification/_template.md` 형식 그대로. **DI 유형(`harness`) 선언** · 유형별 필수 검증(positive·negative·결정성·격리) · 하네스별 **기대 exit vs 실제 exit** · 실패 분류(v9 §0.3) · 잔여 위험 · **`## 지표는 만족했으나 목적은 미달인 부분`**.
**자기보고는 증거가 아니다.** 못 한 시험은 `NOT_VERIFIED`. **숨기면 반려다.**
