# GUARD-01 — fail-silent를 fail-closed로 (검수를 신뢰할 수 있게)

```context-pack
{
  "diId": "GUARD-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "cca162b7cfc7387e3d369148c5d0d170cea9dfe73d86feb9c77a40ab44829145" },
    { "path": "docs/handoff/decisions/ADR-013-canonical-di-coordinate.md", "sha256": "fd40d98d748f497743f0b4d229c20abebc05b8c5f9f86d21830bc56baf76d3fb" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-GUARD-01-failclosed.md"
  ],
  "forbiddenActions": ["git commit", "git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.

## 목적 (판정선)

**오늘 세 번, 게이트가 green인데 안전장치가 죽어 있었다.** 전부 같은 병이다 — **검사는 있는데 입력이 없거나, 사라진 것을 검사하지 않는다.**

1. `scope-check`가 지시서 제목 하나 때문에 **exit 2로 죽어 있었다**(= 검사가 안 돈 것).
2. `run-executor.ps1`이 BOM 없는 UTF-8이라 **PS 5.1이 한글 리터럴을 깨뜨렸고**, `Get-Allowlist`가 **항상 빈 배열**을 반환했다 → `FILE-CLAIMS`의 예약 파일 목록이 **늘 비어 있었다**(P0-06의 사전 차단이 한 번도 작동하지 않았다).
3. `state-transition` **CLI 배선이 통째로 사라졌는데** `build` 0 · `gate-clean` 0 · `di-completion-check POST-COMMIT` **5/5 PASS**였다. 게다가 `Program.cs:14`는 `CliRouter.TryRun(args)`가 `null`을 반환하면 **그대로 웹서버를 띄운다** → 프로세스가 끝나지 않는다.

**이 작업의 목적은 기능 추가가 아니라 "조용한 실패"를 없애는 것이다.**

## 할 일

### 1. `server/Program.cs` + `server/Cli/CliRouter.cs` — 미인식 명령은 죽는다

- **인자가 있는데 어떤 라우터도 인식하지 못하면**: stderr에 JSON 오류(`{"error":"unknown command: <cmd>","known":[...]}`) + **exit 2**.
- **인자가 없을 때만 웹서버를 띄운다.** 그 경로는 **절대 바꾸지 마라**(`verify-behavior`가 지킨다).
- `known` 목록은 **실제 배선에서 열거한다**(`CliRouter` + `HarnessRegistry`). 손으로 적은 목록을 두 벌 만들지 마라.

### 2. `server/StateApplierCli.cs` — `--root`와 `--dry-run`

**지금은 검수 자체가 상태를 오염시킨다.** `GitTools.FindRepoRoot()`로 실 WORKSTATE를 잡기 때문에 **반증 시험을 사본에서 할 수 없다.** 검수자가 오늘 멱등 구멍을 실증하다가 실제 WORKSTATE에 전이를 적용했다.

- **`--root <path>`**: 주면 그 경로를 저장소 루트로 쓴다. 없으면 기존 동작(`FindRepoRoot`).
- **`--dry-run`**: 검증만 하고 **아무것도 쓰지 않는다**(WORKSTATE·applier-log·projection 전부). **exit code 판정은 실제 실행과 동일해야 한다** — 그래야 시험이 의미가 있다.
- 두 옵션은 **조합 가능**해야 한다.

### 3. `outputs/launch/run-executor.ps1` — 발사기가 조용히 빈 claim을 만들지 않는다

- **파일을 UTF-8 **with BOM**으로 저장한다.** (실측: BOM 없는 `.ps1`은 PS 5.1이 ANSI로 파싱해 `'^##\s+허용 파일'` 리터럴이 깨진다. BOM 있으면 MATCH, 없으면 NO-MATCH.)
- **`Get-Allowlist`가 빈 배열을 반환하면 발사를 중단한다**(throw). 지시서를 못 찾아도(`$dPath` null) 중단한다.
  → **빈 paths로 claim을 등록하지 마라.** 그게 P0-06을 무력화한 경로다.
- 중단 메시지는 사람이 읽을 수 있게: 어느 지시서를 찾으려 했고, 왜 실패했는지.

### 4. `docs/handoff/RECOVERY.md` — WORKSTATE 복구 절차 (신설)

**`git checkout`이 단일 writer의 뒷문이다.** DI-00-01 실행자가 `git checkout docs/handoff/WORKSTATE.json`으로 작업본을 날리고 **Write 툴로 손복구**했고, 그 결과 `appliedTransitions`에서 항목 하나가 누락돼 **멱등이 실제로 깨졌다**(같은 transition-id 재적용이 exit 0으로 통과).

문서에 적어라(코드가 아니라 절차다 — 이번 범위에서는 문서로 충분하다):

- **WORKSTATE를 손으로 쓰지 마라. Write 툴도, 에디터도 안 된다.**
- `git checkout`으로 WORKSTATE가 되돌아갔으면: **`docs/handoff/WORKSTATE.applier-log.jsonl`과 대조**해 어느 전이가 누락됐는지 찾고, **`state-transition`으로 다시 적용**한다.
- 그래도 안 되면 **HUMAN-INBOX에 올리고 멈춘다.** 손으로 고치는 것보다 멈추는 게 낫다.
- **`appliedTransitions`와 `applier-log`가 어긋난 상태를 방치하지 마라** — 그 상태에서는 멱등 보장이 없다.

## 하지 않는 것 (범위 밖 — 명시)

- ❌ **`server/Harness/**` 무접촉** — 코덱스 배타 영역(ADR-002). `handoff-integrity`의 멱등 대조와 CLI 계약 하네스는 **코덱스 지시서(`CODEX-GATE-02`)의 몫이다.**
- ❌ **웹서버 기동 경로(인자 없음)를 바꾸는 것.** `verify-behavior`가 깨지면 멈추고 보고하라.
- ❌ **`docs/behavior-snapshot.json`(기준 파일)을 고쳐서 게이트를 통과시키는 것.** 기준 변경은 사람 결재다 — 필요하면 **멈추고 HUMAN-INBOX**.
- ❌ 새 CLI 명령 추가. 새 하네스 제작(예산).
- ❌ git commit/push · 결재 · 반입 · 발사.

## 필수 반증 시험 (전부 exit code로. **실 WORKSTATE를 바꾸지 마라 — 이제 `--root`가 있으니 사본에서 해라**)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | `-- nonexistent-command` | **exit 2**, 프로세스가 **5초 내 종료**(웹서버 안 뜸), stderr에 known 목록 |
| 2 | 인자 없이 실행 | 웹서버 기동(기존 동작 보존) — 5초 후 kill해서 확인 |
| 3 | `state-transition --root <임시사본> ...` 정상 전이 | 사본의 WORKSTATE만 바뀐다. **실 WORKSTATE sha256 불변** |
| 4 | `state-transition --dry-run` 정상 전이 | **exit 0**, WORKSTATE·applier-log **무변경** |
| 5 | `state-transition --dry-run` 잘못된 전이(`waiting → completed`) | **exit 1**, 무변경. **실제 실행과 같은 exit** |
| 6 | allowlist 절이 없는 지시서로 `run-executor.ps1` 발사 | **발사 중단**(실행자 프로세스 미생성, claim 미등록) |
| 7 | `run-executor.ps1`의 `Get-Allowlist`가 `directive-DI-00-01-worktracking.md`에서 | **9개 추출**(현재는 0개다) |
| 8 | 기존 명령 전부 여전히 인식 | `build-verify`·`measure dev-pack`·`projection`·`state-transition`·`handoff-integrity` 각각 **기대 exit** |

**통과를 믿기 전에 실패시킬 수 있음을 먼저 증명해라.**

## 검수 기준

1. `build-verify` **exit 0**, warning 0
2. `verify-behavior` **exit 0** (`behaviorEqual: true`) — **웹서버 경로 동작 보존**
3. `measure dev-pack` **violationCount 0**
4. 위 반증 시험 8개 — **1·2·6·7은 실측이다. 코드 검토로 갈음하지 마라.**
5. `di-completion-check --gate POST-EXECUTOR --task GUARD-01` → **gateVerdict PASS**
6. 파일을 다 쓴 뒤 마지막에 **`projection`**
7. **목적 기준(사람·검수자 판정)**: 이 작업 후 **"게이트가 green인데 안전장치가 죽어 있는" 세 경로가 전부 소리를 내는가.**

## 허용 파일 (allowlist)

- server/Program.cs
- server/Cli/CliRouter.cs
- server/StateApplierCli.cs
- outputs/launch/run-executor.ps1
- docs/handoff/RECOVERY.md
- docs/verification/guard01-failclosed.md
- docs/handoff/queue/directive-GUARD-01-failclosed.md
- docs/handoff/WORKSTATE.json
- docs/context/RUNTIME-INDEX.md
- docs/handoff/HANDOFF.md
- docs/STATUS.md

> `server/Harness/**` 무접촉(코덱스). `dashboard/` 무접촉. `docs/behavior-snapshot.json` 무접촉(기준 파일).

## 보고

`docs/verification/guard01-failclosed.md`에 `docs/verification/_template.md` 형식으로:
**①주체 ②사용한 하네스와 각각의 exit code ③참조한 스킬** + **`## 지표는 만족했으나 목적은 미달인 부분`**(ADR-005).

- **자기보고는 증거가 아니다.** 검수자가 전부 재실행해 대조한다.
- **못 한 시험은 "코드 검토로 갈음"이 아니라 `NOT_VERIFIED`라고 써라.**
- **WORKSTATE를 손으로 고치지 마라.** 실수로 날렸으면 `docs/handoff/RECOVERY.md`(네가 이번에 쓴다)의 절차대로 하고, 안 되면 멈추고 보고해라.
- 한도가 임박하면 마지막 세 줄로 `QUOTA_SIGNAL` / `CHANGED:` / `NEXT:`.
