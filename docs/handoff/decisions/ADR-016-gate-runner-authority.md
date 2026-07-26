# ADR-016 — 게이트 러너가 둘이다: `CODEX-GATE-04` 착륙 전까지 `program-verify`가 권위다

- 상태: **사람 승인 대기**
- 일시: 2026-07-26
- 제안: 조율 세션(Claude Opus 5). **재발명한 당사자가 쓰는 ADR이다** — §4 참조.
- 근거 문서: `CODEX-HARNESS-LAUNCHER-minimal-contract.md` §2-6 · `docs/handoff/queue/directive-CODEX-GATE-04-gate-truth.md` · `ADR-002`

## 1. 상황 (실체 근거)

**같은 게이트를 도는 러너가 둘이고, 서로 다른 답을 낸다.**

| 러너 | 위치 | 호출 방식 |
| --- | --- | --- |
| `di-completion-check` | `server/Harness/` (**코덱스 배타**, `ADR-002`) | `dotnet run --no-build --project server -- <cmd>` (`DiCompletionCheckCli.cs:155`) |
| `program-verify` | `server/` 루트 | `dotnet run --project server -- <cmd>` (빌드함) |

같은 게이트 `WP-STATE-INTEGRITY-LAND`(검사 14개)를 둘 다 돌린 실측:

```
program-verify      verdict: PASS   14/14   exit 0
di-completion-check verdict: FAIL   실패 3   exit 1
   state-transition --self-test  exp 0 act 1
   recovery --self-test          exp 0 act 1
   trust-origin --self-test      exp 0 act 1
```

세 명령을 **직접** 돌리면 전부 **exit 0**이다. 즉 실패는 명령이 아니라 **`--no-build`가 실행한
낡은 바이너리**에서 나왔다.

이것은 새 발견이 아니다. `CODEX-GATE-04`가 고치려는 결함이고
(`docs/context/RUNTIME-INDEX.md`의 `nextActions`: *"di-completion-check가 Debug 바이너리를 실행한다"*),
`DLINT-01` §7도 같은 계열을 적어 두었다(*"`--no-build`를 쓰라고 지시 → 게이트 넷이 exit 2"*).

**위험한 방향은 지금 관측된 쪽이 아니다.** 오늘은 낡은 바이너리가 **거짓 FAIL**을 냈지만,
같은 구조는 **거짓 PASS**도 낸다 — 소스를 고친 뒤 빌드하지 않고 재면 옛 통과가 그대로 나온다.

## 2. 결정

**`CODEX-GATE-04`가 착륙할 때까지 `program-verify`가 게이트 판정의 권위다.**

- `GATE-MANIFEST.json`·`HARNESSES.md`에 등재된 게이트 절차는 그대로 둔다. 등재를 바꾸는 것은
  `server/Harness/`·`GATE-MANIFEST.json`을 건드리는 일이고 `CODEX-GATE-04`의 범위다.
- **판정을 인용할 때는 어느 러너로 쟀는지 함께 적는다.** 러너 이름 없는 게이트 결과는 근거가 아니다.
- `trust-origin evidence --gate-report`는 `verifier == "program-verify"`만 받는다(이미 그렇다).
  **이 결정은 그 제약을 문서화하는 것이지 새로 만드는 것이 아니다.**

**`CODEX-GATE-04` 착륙 후 재검토한다.** 그때 둘이 같은 답을 내면, `program-verify`에 남길 것은
`di-completion-check`에 없는 것뿐이다 — `baselineCommit`·`worktreeCleanAtStart` 기록과
transition request 후보 생성(계약 §2-5·§2-6). 게이트 실행 자체는 넘긴다.

## 3. 근거 — 왜 이 방향인가

- **낡은 바이너리를 재는 러너를 권위로 둘 수 없다.** 오늘 그것이 참인 통과를 거짓 실패로 뒤집었다.
- **고칠 수 없다.** `di-completion-check`는 `server/Harness/`이고 `ADR-002`상 코덱스 배타다.
  `ADR-015` 예외는 2026-07-26 종료됐고 범위도 `05H`·`06H`뿐이었다. 조율자가 손댈 수 없다.
- **대안은 게이트 판정을 멈추는 것**이다. `TRUSTED_BASELINE` 선언이 게이트 판정을 근거로 삼으므로
  멈추면 그 사슬이 끊긴다.

## 4. 위험 (숨기지 않는다)

**`program-verify`는 재발명이다. 만든 사람이 그것을 권위로 정하는 ADR을 쓰고 있다.**

- 조율 세션이 `program-verify`를 만들 때 `directive-authoring.md` §0이 요구하는
  **재발명 금지 3종 검색을 하지 않았다.** `di-completion-check`가 이미 `--gate`를 받아
  `GATE-MANIFEST`의 게이트를 exit code로 판정한다는 사실을 몰랐다.
- 그 결과 기능이 겹쳤고, 겹친 뒤에 "기존 것은 결함이 있으니 내 것이 권위"라고 적는 모양이 됐다.
  **순서가 거꾸로다.** 알고 나서 만들었다면 `CODEX-GATE-04` 착륙을 기다리거나 지시서로 냈을 것이다.
- **완화**: ①이 ADR을 사람이 승인해야 효력이 생긴다 ②`CODEX-GATE-04` 착륙 후 재검토를 결정에
  못박았다 ③겹치는 부분(게이트 실행)은 그때 넘긴다고 미리 적었다.
- **완화되는 것이지 없어지지 않는다.** 다음에 같은 자리에서 검색을 건너뛰면 또 생긴다.

## 5. 되돌리는 법

이 ADR을 폐기하고 `program-verify`를 지우면 게이트 판정은 `di-completion-check` 하나로 돌아간다.
그러면 `--no-build` 결함을 그대로 안고 가며, `trust-origin evidence --gate-report`가 받을 보고서를
낼 수단이 없어져 부트스트랩 경로도 함께 막힌다(`BASELINE-CHANGES.md` 2026-07-26 항목).
**되돌리기 전에 `CODEX-GATE-04`를 먼저 착륙시켜라.**

---

## 6. 정정 (2026-07-26, 같은 세션) — **§1의 진단이 틀렸다**

`CG04A`가 `--no-build`를 제거한 뒤 두 러너를 다시 돌렸다. **여전히 갈렸다.**

```
program-verify       실패 1  (context-pack-integrity — 착륙 ①단계의 stale, 예상됨)
di-completion-check  실패 4  (같은 1 + self-test 3)
```

세 명령을 직접 돌리면 전부 exit 0이다. 게이트 보고서(`outputs/gates/adr016.gate.json`)에 실제
사유가 적혀 있었다:

```
state-transition | verdict: FAIL-CLOSED | reason: unknown command
recovery         | verdict: FAIL-CLOSED | reason: unknown command
trust-origin     | verdict: FAIL-CLOSED | reason: unknown command
```

**낡은 바이너리가 아니었다.** `di-completion-check`는 `HarnessRegistry`에 **등록되지 않은 명령을
거부한다.** 세 명령은 `CliRouter` 명령이지 하네스가 아니다. §1이 `--no-build`를 원인으로 지목한 것은
**같은 시점에 눈에 띈 다른 결함을 원인으로 오인한 것**이다 — `CLAUDE.md`가 금지하는 프록시 단정이다.

### 이 정정이 뒤집는 것

- **`di-completion-check`의 그 동작은 결함이 아니라 fail-closed다.** 게이트가 알지 못하는 명령을
  조용히 실행하지 않는다. 옳은 쪽이다.
- **오히려 `program-verify`가 무르다.** 매니페스트에 적힌 것이면 등록 여부를 묻지 않고 실행한다.
  `WP-STATE-INTEGRITY-LAND`의 14/14 PASS는 **등록된 러너라면 거부했을 명령 3개를 포함한 결과**다.
  그 통과를 근거로 `TRUSTED_BASELINE`을 선언했다(`BASELINE-CHANGES.md` 2026-07-26).
- **`--no-build` 제거는 여전히 옳다**(낡은 바이너리는 거짓 PASS를 낼 수 있다). 다만 그것이
  두 러너가 갈린 이유는 아니었다.

### 결정은 유지하되 근거를 바꾼다

`CODEX-GATE-04` 착륙까지 `program-verify`가 권위라는 §2는 유지한다. 다만 이유가 다르다 —
`di-completion-check`가 틀려서가 아니라, **`WP-STATE-INTEGRITY-LAND`가 아직 등록되지 않은 명령을
포함하기 때문**이다. `CG04B`가 §3(등재)을 마치면 그 게이트를 `di-completion-check`로도 돌릴 수 있고,
**그때 권위를 넘긴다.**

**추가로 해야 할 일**(이 ADR이 만들지 않는다 — 별도 결재):
`program-verify`도 등록되지 않은 명령을 거부해야 한다. 지금은 무르다. 고치기 전까지
`program-verify`의 PASS는 "매니페스트에 적힌 명령이 전부 기대 exit code를 냈다"는 뜻이지
"게이트가 아는 검사만 돌았다"는 뜻이 아니다. **인용할 때 이 차이를 적어라.**

---

## 7. 후속 조치 완료 (2026-07-26) — `program-verify`도 미등록 명령을 거부한다

§6이 "별도 결재"로 미뤄 둔 항목을 사람 지시로 수행했다.

`program-verify`가 `di-completion-check`와 **같은 기준**으로 판정하게 했다:
`KnownCommand` = `{measure, verify-behavior}` ∪ `HarnessRegistry.RegisteredNames`.
미등록 명령이 매니페스트에 있으면 **게이트를 읽는 단계에서 exit 2로 거부**한다 — 실행하지 않는다.

`BuiltInCommands` 두 항목은 `DiCompletionCheckCli.cs:18`에서 복제했다. 그 필드가 private이라
참조할 수 없었다. **두 목록이 갈리면 두 러너가 다시 다른 답을 내므로, 갈리는 것 자체가 결함이다** —
코드 주석에 그렇게 적었다.

### 실측 결과 — 선언의 근거가 실제로 무너진다

```
program-verify verify --gate WP-STATE-INTEGRITY-LAND
  → exit 2  "게이트를 읽지 못했다: 'state-transition'는 등록된 검사가 아니다."

program-verify verify --gate POST-COMMIT
  → 검사 5개 전부 실행됨. 실패는 gate-clean 하나(조율자 미커밋 변경 때문).
```

**`TRUSTED_BASELINE`을 정당화한 게이트를 이제 두 러너 모두 돌리지 않는다.** 이전의 14/14 PASS는
지금 기준으로는 **재현되지 않는다.**

### 이것이 뜻하는 것

- **선언이 자동으로 무효가 되지는 않는다.** 기록(`TO-2026-001`)은 그대로이고, 그때의 측정도
  거짓이 아니었다 — 세 명령을 **직접 돌리면 지금도 exit 0**이다. 문제는 그것들이 게이트가
  아는 검사가 아니라는 것이다.
- **다음 갈림길은 사람 결재다.** ①세 명령을 `HarnessRegistry`에 등재해 게이트를 온전히 만들고
  다시 잰다(코덱스 영역, 지시서 필요) ②게이트에서 뺀다 — 그러면 land gate의 2·4·5·8·9·10을
  덮던 검사가 사라져 게이트가 얇아진다.
- **①을 권한다.** ②는 숫자만 초록으로 만들고 실질을 줄인다.

---

## 8. 해소 (2026-07-26) — **두 러너가 같은 답을 낸다. 권위를 넘긴다**

`HREG-01` 착륙(`b62b5cc`)으로 self-test 3종이 `HarnessRegistry`에 등재됐고,
`GATE-MANIFEST`의 `WP-STATE-INTEGRITY-LAND`가 새 이름을 가리키도록 교체했다
(`state-transition --self-test` → `state-transition-selftest` 등 3건).

**같은 게이트, 같은 커밋에서 처음으로 두 러너가 일치했다:**

```
program-verify      verdict: PASS  14/14  exit 0
di-completion-check verdict: PASS  14/14  exit 0   failures: []
```

### 결정 변경

**§2의 "`program-verify`가 권위"를 종료한다.** 그 결정의 조건은 *"`CODEX-GATE-04` 착륙까지"*였고,
실제 해소 조건은 §6이 밝힌 대로 **등재**였다. 등재가 끝났으므로:

- **게이트 판정의 권위는 `di-completion-check`다.** 등재된 검사만 도는 쪽이 기본이다.
- `program-verify`에 남는 것은 `di-completion-check`에 없는 것뿐이다 —
  `baselineCommit`·`worktreeCleanAtStart` 기록과 transition request 후보 생성(계약 §2-5·§2-6).
  **게이트 실행 자체는 넘긴다.** 두 러너가 같은 `KnownCommand` 기준을 쓰므로 결과가 갈리지 않는다.
- **판정을 인용할 때 러너 이름을 함께 적는 규칙은 유지한다.** 지금은 일치하지만, 갈리는 순간
  그 사실이 보여야 한다.

### `TRUSTED_BASELINE`에 대하여

선언의 근거였던 14/14 PASS가 **엄격한 기준에서 재현됐다.** §7이 *"지금 기준으로 재현되지 않는다"*고
적은 상태는 해소됐다.

**다만 재선언한 것이 아니다.** `TO-2026-001`은 그대로이고, 이 문단은 *"그때 통과가 지금 기준으로도
성립한다"*는 사실만 기록한다. 기록을 갱신할지는 사람 판단이다.

### 남는 것

`ADR-016` §7의 `BuiltInCommands` 복제는 그대로다. 두 목록이 갈리면 러너가 다시 다른 답을 낸다.
근본 해소는 그 목록을 공유 가능한 자리로 옮기는 것이며 `server/Harness/`(코덱스 영역)에 있다.

## §9 정정 (2026-07-26) — `--no-build`를 결함으로 본 §1은 두 번째로 틀렸다

§1은 `di-completion-check`의 `--no-build`를 "낡은 바이너리를 잰다"고 지목했고,
§6에서 그 진단이 틀렸음을 이미 정정했다(진짜 사유는 `unknown command`).
오늘 세 번째 사실이 나왔다: **`--no-build`는 결함이 아니라 그쪽이 도는 이유다.**

`program-verify verify --gate POST-COMMIT` 실측:

```
verdict FAIL | 실패 6 / 12 | worktreeCleanAtStart True
  gate-clean, handoff-integrity, context-pack-integrity, doc-integrity,
  handoff-integrity(fixture-malformed), gate-witness-check
  stderrTail: "빌드하지 못했습니다. 빌드 오류를 수정하고 다시 실행하세요."
```

같은 12개를 **직접 순차 실행하면 전부 기대값과 일치한다(실패 0/12).**

원인은 실재다. `ProgramVerifierCli`는 검사마다 `dotnet run --project server`(빌드 포함)로
자식을 띄우는데(`ProgramVerifierCli.cs:156`), **자기 자신이 그 프로젝트의 exe로 돌고 있다.**
자식의 빌드가 부모가 잡은 산출물을 덮으려다 막힌다. 빌드가 이미 최신이면 통과하므로
**간헐적으로만 실패한다** — 게이트 러너로서 최악의 성질이다.

**결론**: `program-verify`의 PASS도 FAIL도 그대로 믿을 수 없다. §8에서 권위를
`di-completion-check`에 준 결정은 이 사실로 더 강해진다.
`--no-build`를 쓰려면 **호출 전에 빌드를 한 번 보장**하는 것이 맞는 설계이고,
검사마다 빌드하는 것은 자기 프로세스와 충돌한다.

수정은 별도 결재다. 그 전까지 게이트 판정은 `di-completion-check` 또는 직접 순차 실행으로 한다.

## §10 정정 (2026-07-26) — §9의 진단도 정밀하지 않았다. 잠금의 주인은 부모 자신이다

§9는 원인을 *"자식의 빌드가 부모가 잡은 산출물을 덮으려다 막힌다"*고 적었다. 방향은 맞았지만
**"앞에서 한 번만 빌드하면 된다"**는 처방이 따라 나왔고, 그 처방은 **틀렸다.** 실측:

```
{"error":"사전 빌드에 실패해 게이트를 잴 수 없다","buildExitCode":1,
 "stderrTail":"... LocalFirstWorkflowDashboard.Server.exe ... because it is being used by another process"}
```

`program-verify`는 **자기가 곧 `server`의 exe다.** 언제 빌드하든 — 앞이든 검사마다든 —
자기 자신을 덮을 수 없다. 순서 문제가 아니라 **자기참조 문제**였다.
`BuildOnce`를 넣은 커밋(`dbbff7c`)은 그래서 게이트를 아예 못 돌리게 만들었다.

**채택한 설계**: 빌드하지 않는다. 대신 **낡았는지 잰다.**

- `server/**`의 `*.cs`·`*.csproj` 중 가장 최근 수정 시각과 **돌고 있는 바이너리**의 시각을 비교한다
  (`bin/`·`obj/`는 산출물이라 제외 — 넣으면 언제나 낡았다고 나온다).
- 소스가 더 새로우면 **검사를 하나도 돌리지 않고 exit 2**, 어떤 파일이 더 새로운지 함께 낸다.
- 빌드는 **호출자가 미리** 한다.

이로써 §1이 걱정한 "낡은 바이너리를 잰다"는 **막히면서도** `--no-build`를 쓸 수 있다.
§1이 결함으로 지목한 것은 `--no-build` 자체가 아니라 **낡음을 재지 않는 것**이었다는 게
세 번의 정정 끝에 나온 결론이다.

실측(2026-07-26):

| 상황 | exit | 검사 실행 |
| --- | --- | --- |
| 고치기 전, POST-COMMIT | 1 | 실패 **6/12**, 6건 전부 사유가 빌드 실패 |
| 고친 뒤, 더러운 트리 | 1 | 실패 **1/12** — `gate-clean` 하나, **참인 실패** |
| 소스를 바이너리보다 새롭게 | **2** | **0개** — 검사를 돌리지 않는다 |
| 다시 빌드 | 1 | 실패 1/12로 복귀 |

**남은 성질**: 소스 mtime이 미래로 찍혀 있으면 다시 빌드해도 계속 거부한다(실측으로 확인).
fail-closed 방향이고 메시지가 어느 파일인지 말해 주지만, 시계가 어긋난 환경에서는 막힌다.
