# CODEX-GATE-02 — 게이트가 "사라진 것"을 보게 만든다

```context-pack
{
  "diId": "CODEX-GATE-02",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "cca162b7cfc7387e3d369148c5d0d170cea9dfe73d86feb9c77a40ab44829145" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-CODEX-GATE-02-cli-contract.md"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**선행조건: `GUARD-01`이 커밋된 뒤에 착수한다**(CLI 배선이 그때 확정된다). 아직이면 큐에서 대기하라.

## 목적 — 오늘 게이트가 세 번 거짓말했다

| 사고 | 게이트 반응 |
| --- | --- |
| `scope-check`가 지시서 제목 때문에 **exit 2로 죽어 있었다** | **아무도 몰랐다**(scope-check는 어느 게이트에도 등재돼 있지 않다) |
| `run-executor.ps1` BOM 문제로 **`FILE-CLAIMS.paths`가 항상 0**이었다 | `claim-check`는 **untracked 파일을 못 봐서** 16회 연속 오탐만 냈다 |
| **`state-transition` CLI 배선이 통째로 사라졌다** | `build` 0 · `gate-clean` 0 · `di-completion-check POST-COMMIT` **5/5 PASS** |
| DI-00-01 실행자가 WORKSTATE를 손복구해 **`appliedTransitions`에서 전이 1건이 누락** → **멱등이 깨졌다**(같은 id 재적용이 exit 0) | `handoff-integrity` **exit 0**(changedFiles 해시만 본다) |

**공통점: 검사는 있는데, 없어진 것을 보는 검사가 없다.**

> **hs-gate 2항 확인**: 이 하네스들이 볼 데이터는 **전부 실재한다** — `docs/handoff/WORKSTATE.applier-log.jsonl`(전이 로그, 실재) · `server/Cli/CliRouter.cs`+`server/Harness/HarnessRegistry.cs`(배선, 실재) · `docs/handoff/FILE-CLAIMS.json`(claim, 실재). **없는 데이터 위에 하네스를 세우지 않는다.**

## 할 일 (전부 **기존 하네스 확장**이다 — 신규 하네스 예산을 쓰지 않는다)

### 1. `handoff-integrity` — 멱등 대조 (최우선)

`docs/handoff/WORKSTATE.json`의 `appliedTransitions[].id` 집합과 `docs/handoff/WORKSTATE.applier-log.jsonl`의 `result:"ok"` 전이 id 집합을 **대조**한다.

- **로그에만 있는 id** → `failure` (상태가 되돌아갔다. **그 id로 재적용하면 두 번 적용된다** — 실측된 사고다)
- **상태에만 있는 id** → `failure` (로그 없이 상태가 늘었다)
- 실패 1건이라도 있으면 **exit 1**.

**반증 시험**: `appliedTransitions`에서 항목 하나를 지운 사본 → **exit 1**. 원복 → **exit 0**. (**사본에서 해라. 실 WORKSTATE를 건드리지 마라.** `GUARD-01`이 `state-transition --root`를 만든다.)

### 2. `di-completion-check` — CLI 계약 대조

`docs/handoff/CLI-CONTRACT.json`을 **신설**한다(schemaVersion 1). 초기값은 **현재 실재 배선에서 열거해 생성**한다 — 손으로 적지 마라.

```json
{ "schemaVersion": 1,
  "commands": [ { "name": "state-transition", "source": "CliRouter", "critical": true } ] }
```

- `CliRouter`(`args[0]` 비교)와 `HarnessRegistry`(딕셔너리 키)에서 **실제 배선 명령을 열거**한다.
- **계약에 있는데 배선에 없으면 exit 1**(← 오늘의 사고). **배선에 있는데 계약에 없으면 warning**(새 명령 추가는 정상이다).
- `critical: true`인 명령(`state-transition`·`projection`·`measure`·하네스 전부)이 사라지면 **무조건 실패**.

**반증 시험**: `CLI-CONTRACT.json`에 존재하지 않는 명령(`ghost-command`, critical)을 넣는다 → **exit 1**. 뺀다 → **exit 0**.

### 3. `GATE-MANIFEST.json` — 안 도는 검사를 등재한다

`POST-EXECUTOR`·`POST-COMMIT`에 다음을 추가한다:

- **`scope-check`** — `expectedExit`는 게이트 성격에 맞게 정하고 **`note`에 근거를 적어라**(dirty 트리에서 항상 1이 나오는 성질을 감안하라. **판정할 수 없는 검사를 넣지 마라** — 그럴 바엔 넣지 말고 이유를 적어라).
- **`claim-check`** — 아래 §4를 고친 뒤에 등재한다.

지금 `di-completion-check`는 이 둘을 **`unlisted` 경고로만** 뱉는다(경고 10건). **등재되지 않은 하네스는 아무도 돌리지 않는다.**

### 4. `claim-check` — untracked 파일을 본다

`server/Harness/ClaimCheckCli.cs`의 심볼 검색이 `git grep -l {sym} -- server`를 쓴다. **이 명령은 untracked 파일을 검색하지 않는다.** 그래서 신규 파일이 포함된 배치마다 **MISMATCH 오탐**이 났다(조율자가 16회 연속 재현했고, 그 때문에 커밋이 계속 보류됐다).

`--untracked`를 추가하고, **오탐이 사라지는지 실측**하라(`StateApplierCli.cs`가 untracked였던 시점을 재현하거나, 새 untracked 파일로 시험).

## 하지 않는 것

- ❌ **신규 하네스 제작.** 넷 다 **기존 확장**이다. 예산(`BC-002`, 2→3)을 더 쓰지 마라. 새 하네스가 필요하다고 판단되면 **만들지 말고 근거와 함께 큐에 올려라.**
- ❌ **`server/Cli/CliRouter.cs`·`server/Program.cs` 수정** — `GUARD-01`(실행자)의 영역이다. **읽기만 해라.**
- ❌ 기준 파일(`blueprint.json`·`workflow-definition.json`·`docs/behavior-snapshot.json`) 수정.
- ❌ push · 결재 · 반입 · 발사.

## 검수 기준

1. `build-verify` **exit 0**, warning 0
2. `verify-behavior` **exit 0**
3. `measure dev-pack` **violationCount 0**
4. **반증 시험 3개**(§1·§2·§4) — **전부 실측. 사본에서. 코드 검토로 갈음하지 마라.**
5. `handoff-integrity` **exit 0**(정상 상태에서) · `di-completion-check --gate POST-EXECUTOR --task CODEX-GATE-02` **PASS**
6. **목적 기준**: 이 작업 후 **오늘의 사고 4개가 전부 게이트에서 소리를 내는가.** 하나라도 조용하면 **미달로 신고하라.**

## 허용 파일 (allowlist)

- server/Harness/HandoffIntegrityCli.cs
- server/Harness/DiCompletionCheckCli.cs
- server/Harness/ClaimCheckCli.cs
- docs/handoff/CLI-CONTRACT.json
- docs/handoff/GATE-MANIFEST.json
- docs/qa/codex-gate-02.md
- docs/handoff/sessions/SESSION-2026-07-12-codex-0XX.md
- docs/verification/codex-gate-02.md
- docs/handoff/queue/directive-CODEX-GATE-02-cli-contract.md

> `server/Cli/**`·`server/Program.cs` **무접촉**(GUARD-01 영역). `dashboard/` 무접촉.

## 보고

`docs/verification/codex-gate-02.md`(`docs/verification/_template.md` 형식): **①주체 ②하네스별 exit code ③참조 스킬** + **`## 지표는 만족했으나 목적은 미달인 부분`**.
**자기보고는 증거가 아니다 — 검수자가 반증 시험을 직접 재실행한다.**
