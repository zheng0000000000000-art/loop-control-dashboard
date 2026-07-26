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
