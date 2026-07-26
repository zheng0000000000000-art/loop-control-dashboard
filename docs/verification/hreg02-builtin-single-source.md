# HREG-02 반입 검증 — 게이트가 아는 명령 목록을 한 곳에 뒀다

- **주체(actor)**: 산출은 **코덱스**(`LAUNCH-HREG-02`). `ProgramVerifierCli` 수정과 검증·반입은
  **조율 세션(Claude Opus 5)**. 결재는 **사람**(git user `Jaehyuk`).
- **날짜**: 2026-07-26

## 사용한 하네스 (명령 · exit code · 수치)

| 명령 | exit | 수치 |
| --- | --- | --- |
| `codex-launch validate` / `launch --manual` | 0 / **0** | 변경 4, `scopeViolations` 0, 누락 0 |
| `build-verify` (패치 후) | 0 | 오류 0 |
| `context-pack-integrity` (①) | **1** | `GATE-TRUTH-01`의 `HarnessRegistry.cs` pin stale |
| `context-pack-integrity` (②) | 0 | pin 갱신 후 |
| `gate-witness-check` | 0 | `totalUnwitnessed` 0 |
| `measure dev-pack` | 0 | `{"gate":"dev-pack","violations":0,"attempt":1}` |

## 반증 시험 (지시서 §4)

| # | 시험 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1 | 모르는 명령 매니페스트 | fail-closed | **FAIL**, `('hreg-02-unknown','unknown-command')` |
| 2 | `measure`(내장) 매니페스트 | 안다고 판정 | **PASS** exit 0 |
| 3 | **단일 출처에서 `measure`를 임시로 뺌** | 모르는 명령이 됨 | **FAIL**, `('measure','unknown-command')` |
| 3′ | 되돌린 뒤 | 원복 | **exit 0** |
| 4 | 세 게이트 판정 전후 대조 | 동일 | **미실시** — §아래 |
| 5 | `TryRun("measure")` 미처리 | 유지 | **행위 시험 없음** — §아래 |

**시험 3이 이 지시서의 목적이다.** 한 곳을 고치자 소비자가 따라왔다.
사본을 지우는 대신 같은 값을 다시 적는 우회로였다면 여기서 드러났을 것이다.

정의 개수 실측: `server/**`에 `BuiltInCommands` 정의는 **`HarnessRegistry.cs:5` 하나뿐**이다.
`ProgramVerifierCli`의 사본과 `KnownCommand`의 이중 조회도 지웠다(`HarnessRegistry.GateCommandNames` 사용).

## ★ 이 반입이 드러낸 것 — `di-completion-check`도 자기잠금에 걸린다

시험 4를 하려고 세 게이트의 **수정 전** 판정을 잡다가 나왔다.

```
DiCompletionCheckCli.cs:160-164  →  run --project server --   ("--no-build" 없음)
stdoutTail: error MSB3021 ... Server.exe ... used by another process
```

세 게이트 모두 FAIL이고 사유는 `exit-mismatch`지만 **실제로는 검사가 빌드 단계에서 죽었다.**
개별 실행에서 exit 0인 `gate-clean`·`handoff-integrity`·`context-pack-integrity`·`doc-integrity`가
여기서는 1로 잡혔다. `ADR-016` §11에 적었다.

**`ADR-016` §1이 *"그쪽은 `--no-build`로 낡은 바이너리를 잰다"*고 한 것은 사실 자체가 틀렸다.**
§1은 이로써 세 번 정정됐다(§6·§10·§11). **프록시로 짚은 진단이 한 번도 맞지 않았다.**

`unknown-command` 판정은 자식을 띄우기 전에 나므로 영향받지 않는다 — 시험 1·2·3이 그 증거다.

## 착륙 (두 걸음)

1. 패치 적용 → `context-pack-integrity` **exit 1**
2. stale 경로를 grep해 소유자를 찾음 → **`GATE-TRUTH-01`** (`GWIT-*`도 `HREG-*`도 아니었다).
   pin `5306efeb…` → `25ce489d…` 갱신 → **exit 0**

**소유 지시서를 가정하지 않았다.** 같은 세션에서 가정했다가 두 번 틀렸다(`DLINT-01`, `GWIT-04`).

## 참조한 스킬

`skills/common/directive-authoring.md` §7.

## 지표는 만족했으나 목적은 미달인 부분

1. **시험 4(전후 판정 대조)를 못 했다.** `di-completion-check`가 자기잠금으로 죽어 "수정 전"
   판정이 빌드 실패 산물이었다. 대조할 기준선이 없다. **그쪽이 고쳐진 뒤에 다시 해야 한다.**
2. **시험 5에 행위 시험이 없다.** `Handlers`에 내장 명령이 들어가지 않았음을 패치 구조로만 확인했다
   (`GateCommandNames = BuiltInCommands.Concat(Handlers.Keys)`, `Handlers`는 무변경).
   `TryRun("measure")`가 여전히 미처리라는 것을 밖에서 관측할 방법을 찾지 못했다. **자기보고에 가깝다.**
3. **두 러너를 픽스처로 대조할 수 없다.** `program-verify`는 `--manifest`를 해석하지 않고 언제나
   실제 매니페스트를 읽는다(`ProgramVerifierCli.cs:115`). 대조를 시도해 얻은 exit 2 vs 1은
   판정 불일치가 아니라 **게이트 id를 못 찾은 것**이었다 — 무효한 비교였다. `ADR-016` §11 곁가지에 적었다.
