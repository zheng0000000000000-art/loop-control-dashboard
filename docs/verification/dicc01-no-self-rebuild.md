# DICC-01 반입 검증 — 두 러너가 같은 게이트에 같은 답을 낸다

- **주체(actor)**: 산출은 **코덱스**(`LAUNCH-DICC-01`). `ProgramVerifierCli` 정리와 검증·반입은
  **조율 세션(Claude Opus 5)**. 결재는 **사람**(git user `Jaehyuk`).
- **날짜**: 2026-07-26 · 근거: `ADR-016` §10(설계) · §11(결함 실측)

## 사용한 하네스 (명령 · exit code · 수치)

| 명령 | exit | 수치 |
| --- | --- | --- |
| `codex-launch validate` / `launch --manual` | 0 / **0** | 변경 2, `scopeViolations` 0, 누락 0 |
| `build-verify` (패치 후) | 0 | 오류 0 |
| `measure dev-pack` | 0 | `{"gate":"dev-pack","violations":0,"attempt":1}` |
| `doc-integrity` · `context-pack-integrity` · `handoff-integrity` | 0 | 착륙 **한 걸음** (stale pin 없음) |

## ★ 시험 6 — 이 세션에서 처음으로 두 러너를 대조했다

| 게이트 | `di-completion-check` | `program-verify verify` |
| --- | --- | --- |
| `POST-COMMIT` | **PASS** 12검사 실패 0 (exit 0) | **PASS** 12검사 실패 0 (exit 0) |
| `WP-STATE-INTEGRITY-LAND` | **PASS** 18검사 실패 0 (exit 0) | **PASS** 18검사 실패 0 (exit 0) |

**지금까지 한 번도 못 한 대조다.** 한쪽이 늘 빌드 실패로 죽었기 때문이다.
`ADR-016` §6의 사건 — 같은 게이트에 두 러너가 다른 답을 내고 그중 하나가
`TRUSTED_BASELINE` 선언의 근거가 된 일 — 의 구조적 조건이 사라졌다.

## 반증 시험 (지시서 §4 — 전부 실측)

| # | 시험 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1 | 깨끗한 트리·최신 빌드 `--gate POST-COMMIT` | exit 0 | **0**, 12/12 |
| 2 | `server/Engine.cs` mtime을 바이너리보다 새롭게 | exit 2, 검사 0개 | **2**, **검사 0개**, `newestSource: Engine.cs` |
| 3 | 다시 빌드 | 복귀 | **exit 0** |
| 4 | 모르는 명령 매니페스트 | `unknown-command` | **exit 1** — 변화 없음 |
| 5 | `measure` 매니페스트 | PASS | **exit 0** — 변화 없음 |
| 6 | 두 러너 대조 | 동일 | **동일** (위 표) |

**시험 2가 이 수정의 핵심이다.** `--no-build`를 쓰면서 낡은 바이너리를 재지 않는다.
판정은 `FAIL`이 아니라 **`NOT-MEASURED`**다 — 지시서 §2가 요구한 구분이다.
낡아서 못 쟀으면 `exit-mismatch`가 아니다.

**시험 2를 현재 시각으로 touch했다.** 미래로 찍으면 재빌드해도 계속 거부되어 시험 3이 실패한다 —
`program-verify`를 고칠 때 실제로 그렇게 실패했고, 그 함정을 지시서 §4에 적어 뒀다.

## §1-A — 중복을 다시 만들지 않았다

낡음 판정 정의는 **`server/Harness/BinaryFreshness.cs` 하나뿐**이다(실측: `Measure(string root)` 1개).
`ProgramVerifierCli`의 사본 **38줄을 지우고** 그 표면을 쓴다(§6, 조율자 몫).

**바로 앞 작업(`HREG-02`)이 없앤 것이 정확히 이런 중복이었다.** 복사했으면 같은 결함을
다른 이름으로 되살렸을 것이다.

## `ProgramVerifierCli` 머리 주석도 고쳤다

그 자리에 *"그쪽은 `--no-build`로 낡은 바이너리를 잰다(DiCompletionCheckCli.cs:155,
CODEX-GATE-04가 고칠 결함)"*가 박혀 있었다. **사실이 아니었고**(`ADR-016` §11) 이제는
두 러너의 실행 방식이 같다. 파일 맨 위의 틀린 설명은 다음 사람이 가장 먼저 읽는다.

## 참조한 스킬

`skills/common/directive-authoring.md` §7.

## 지표는 만족했으나 목적은 미달인 부분

1. **`ADR-016` §8의 권위 결정은 그대로 남아 있다.** 두 러너가 같은 답을 낸다는 것이
   *"둘 다 둬도 된다"*는 뜻은 아니다. **같은 일을 하는 코드가 두 벌 있는 상태**이고,
   지금 같은 답을 내는 건 방금 맞춰서다. 정본을 정하는 것은 **사람 결재**다.
2. **두 러너의 차이가 아직 남아 있다.** `program-verify`는 `--manifest`를 해석하지 않아
   픽스처 매니페스트로 대조할 수 없고, `di-completion-check`는 받는다.
   실제 게이트 id로만 대조 가능하다는 제약이 그대로다.
3. **`POST-EXECUTOR`는 대조하지 않았다.** 그 게이트의 `gate-clean`은 기대값이 1이라
   **더러운 트리에서만** 통과하는데, 대조는 깨끗한 트리에서 했다. 맥락을 만들지 않았다.
