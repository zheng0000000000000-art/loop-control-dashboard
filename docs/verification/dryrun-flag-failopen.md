# 값 없는 후행 옵션이 조용히 버려졌다 — 상태를 실제로 썼다

- **주체(actor)**: 조율 세션(Claude Opus 5). 사람 지시. 사고 당사자도 나다.
- **날짜**: 2026-07-26 · 대상: `server/StateApplierCli.cs`(`server/` 루트 = 조율자 영역)

## 무엇이 일어났나

`DI-00-04` 전이를 **먼저 dry-run으로 확인하려고** 이렇게 실행했다.

```
state-transition apply --envelope <...> --dry-run-flag
  → status "applied" · stateWritten true · successLogAppended true
```

**실제로 적용됐다.** `WORKSTATE.json`이 쓰이고 applier 로그에 성공 항목이 붙었다
(`DI0004-VERIFYING-20260726`, 12:54:46Z). 결과가 의도한 전이와 같았을 뿐,
**의도는 "쓰지 말고 보기"였다.**

## 원인 (프록시 아님 — 코드)

```csharp
// 고치기 전 ParseFlagMap
if (!args[i].StartsWith("--") || i + 1 >= args.Length) continue;   // ← 값 없으면 버린다
```

`--dry-run-flag`가 **마지막 인자**여서 `i + 1 >= args.Length`에 걸려 **조용히 버려졌다.**
버려졌으므로 map에 없고, map에 없으므로 `ValidateOptions`의 `unknown-option` 검사에도
걸리지 않고, `dryRun`은 `false`로 남아 **정상 apply**가 됐다.

**dry-run 전용 결함이 아니다.** 값 없이 맨 끝에 온 **모든** `--옵션`이 그렇게 사라진다.

두 번째 요인: `dry-run-flag`는 **내부 키 이름**인데 `ApplyKnownKeys`에 들어 있어
`--dry-run-flag X`처럼 값을 주면 **알려진 옵션으로 받아들여진다.** CLI 철자는 `--dry-run`이다.

## 고친 것

1. 값 없는 후행 `--옵션` → **`missing-option-value: --x`, exit 2.** 버리지 않는다.
2. `--dry-run-flag`(내부 키 이름) → **거부하고 `--dry-run`을 알려준다.** 값이 있어도 거부한다.

## 반증 시험 (클론에서 — 틀리면 실제 상태가 쓰이는 시험이다)

시작 status `verifying`. **각 시험 후 status를 매번 다시 읽었다.**

| 시험 | exit | status | 사유 |
| --- | --- | --- | --- |
| **`--dry-run-flag` (후행 무값)** | **2** | **verifying (불변)** | `unknown-option: --dry-run-flag (dry-run은 --dry-run이다)` |
| `--dry-run-flag X` (값 있음) | **2** | 불변 | 〃 |
| `--bogus` (후행 무값) | **2** | 불변 | `missing-option-value: --bogus` |
| `--bogus-flag 1` (회귀) | 2 | 불변 | `unknown-option: --bogus-flag` |
| `--dry-run` (정식) | **0** | **불변** | 정상 dry-run |
| 정상 `prepare` (대조군) | 0 | — | 동작 유지 |
| `--request` 누락 | 2 | — | 사용법 |

**첫 줄이 이 수정의 전부다.** 같은 명령이 고치기 전에는 **상태를 썼고**, 지금은 exit 2에
**status가 그대로다.** 회귀 없음: `state-transition --self-test` 0(본 저장소·클론 양쪽),
`measure` 0, `verify-behavior` 0, `doc-integrity` 0, `handoff-integrity` 0.

## 지표는 만족했으나 목적은 미달인 부분

1. **내 잘못이 먼저다.** 철자를 확인하지 않고 상태를 쓰는 명령에 넘겼다. 오늘 하루 종일
   *"확인하지 않은 추정"* 을 경계했는데 **마지막에 또 했다.**
2. **다른 CLI의 같은 패턴은 안 봤다.** `ParseFlagMap`류의 "값 없으면 continue"는
   다른 하네스에도 있을 수 있다. **`StateApplierCli`만 고쳤다.**
3. **적용된 전이를 되돌리지 않았다.** 결과가 사람이 지시한 것과 같았기 때문이다.
   만약 달랐다면 되돌리는 절차(`RECOVERY.md`)를 밟아야 했다 — **이번엔 운이 좋았다는 뜻이다.**
