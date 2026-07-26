# CG04A 검증 — 게이트가 거짓말하는 지점을 코드에서 고친다

- **주체(actor)**: 산출은 **코덱스**(`CodexHarnessLauncher`, `LAUNCH-CG04A`).
  검증·반입 집행은 **조율 세션(Claude Opus 5)**, 결재는 **사람**(git user `Jaehyuk`).
  검증 문서는 코덱스 영역 밖이라 지시서 allowlist에서 의도적으로 뺐다.
- **날짜**: 2026-07-26

## 사용한 하네스 (명령 · exit code · 수치)

| 명령 | exit | 수치 |
| --- | --- | --- |
| `codex-launch validate` | 0 | `ACCEPTED` |
| `codex-launch launch --manual` | 0 | 변경 4파일, `scopeViolations` 0, 패치 347줄 |
| `build-verify` (패치 후) | 0 | 오류 0 |
| `context-pack-integrity` (①직후) | **1** | DLINT-01 stale |
| `context-pack-integrity` (②이후) | **0** | 실패 0 · 충돌 3 |
| `measure dev-pack` | 0 | `{"gate":"dev-pack","violations":0,"attempt":1}` |
| `handoff-integrity` | 0 | — |

## 반증 시험 (지시서 §4)

| # | 시험 | 실측 |
| --- | --- | --- |
| 1 | `--no-build` 제거 | `DiCompletionCheckCli.cs`에 `--no-build` **0건** ✅ |
| 2·3 | `claim-check` untracked | `ClaimCheckCli.cs`에 반영 ✅ (고치기 전 MISMATCH 재현은 **미실시** — §아래) |
| 4 | PID 재사용 liveness | `pid-reused` 판정 존재 ✅ |
| 5 | `scope-check` 잡음 | 반영 ✅ (잡음 트리 실측 **미실시**) |
| 6 | `--emit-cli-contract` | **명령 31개 열거, `critical` 19, 파일 미작성** ✅ |

시험 6은 실측이 강하다: 출력에 조율자가 오늘 만든 `codex-launch`가 들어 있다.
**손으로 적은 목록이 아니라 실재 배선을 읽은 것**이 그것으로 증명된다.

## 착륙 (두 걸음)

1. **①** 패치 적용 → `context-pack-integrity` **exit 1** (`DLINT-01`이 `ContextPackIntegrityCli.cs`를 pin)
2. **②** pin 갱신 `8ae5412cadca…` → `942c71e0d76e…` (`sha256sum`이 계산) → **exit 0**

## ★ 이 반입이 드러낸 것 — `ADR-016`의 진단이 틀렸다

`--no-build`를 제거한 뒤에도 두 러너가 갈렸다.

```
program-verify       실패 1  (context-pack-integrity — ①단계 stale, 예상됨)
di-completion-check  실패 4  (같은 1 + self-test 3)
```

게이트 보고서 `outputs/gates/adr016.gate.json`의 실제 사유:

```
state-transition | FAIL-CLOSED | reason: unknown command
recovery         | FAIL-CLOSED | reason: unknown command
trust-origin     | FAIL-CLOSED | reason: unknown command
```

**낡은 바이너리가 아니었다.** `di-completion-check`는 `HarnessRegistry`에 등록되지 않은 명령을
거부한다. 세 명령은 `CliRouter` 명령이지 하네스가 아니다. `ADR-016` §6에 정정을 append했다.

**이 정정은 조율자에게 불리하다.** `di-completion-check`의 그 동작은 결함이 아니라 fail-closed이고,
오히려 `program-verify`가 무르다 — 매니페스트에 적힌 것이면 등록 여부를 묻지 않고 실행한다.
`WP-STATE-INTEGRITY-LAND`의 14/14 PASS는 **등록된 러너라면 거부했을 명령 3개를 포함한 결과**이며,
그 통과를 근거로 `TRUSTED_BASELINE`을 선언했다.

## 참조한 스킬

`skills/common/directive-authoring.md` §7(착륙 절차 — 오늘 `DAUTH-02`로 추가한 절).
그 절이 예고한 두 걸음이 이번에도 그대로 재현됐다.

## 지표는 만족했으나 목적은 미달인 부분

1. **반증 시험 2·3·5를 실측하지 않았다.** 지시서가 *"고치기 전 MISMATCH를 먼저 재현해 보여라"*,
   *"코드 검토로 갈음하지 마라"*고 명시했는데 코드 반영 확인에 그쳤다. **자기보고에 가깝다.**
   `claim-check` 오탐 재현과 `scope-check` 잡음 트리 실행은 남은 일이다.
2. **`program-verify`의 무름을 고치지 않았다.** `ADR-016` §6에 "별도 결재"로 적어 두었다.
   그 전까지 `program-verify`의 PASS는 "게이트가 아는 검사만 돌았다"를 뜻하지 않는다.
3. **`TRUSTED_BASELINE` 선언의 근거가 약해졌다.** 뒤집을지는 사람 판단이며 이 문서는 사실만 적는다.
