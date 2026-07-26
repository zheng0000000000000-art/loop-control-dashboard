# `program-verify` 수정 검증 — 빌드하지 말고 낡았는지 재라

- **주체(actor)**: 조율 세션(Claude Opus 5)이 직접 구현·실측(`server/` 루트는 코덱스 영역 밖).
  결재는 사람(git user `Jaehyuk`).
- **날짜**: 2026-07-26 · 관련: `ADR-016` §9(진단) · §10(정정과 채택 설계)

## 사용한 하네스 (명령 · exit code · 수치)

| 상황 | exit | 결과 |
| --- | --- | --- |
| 고치기 전 `program-verify verify --gate POST-COMMIT` | 1 | 실패 **6/12** — 6건 전부 사유가 빌드 실패 |
| 같은 12개를 직접 순차 실행 | — | 실패 **0/12** |
| `BuildOnce` 판(`dbbff7c`) | **2** | **게이트를 아예 못 돌림** — 자기 exe를 덮을 수 없다 |
| 낡음 검사 판(`c18eee8`), 더러운 트리 | 1 | 실패 **1/12** — `gate-clean` 하나, **참인 실패** |
| 〃 깨끗한 트리 | **0** | **PASS 0/12**, `worktreeCleanAtStart` true |

## 반증 시험 (전부 실측)

| # | 시험 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1 | `POST-COMMIT` 깨끗한 트리 | PASS | **PASS 0/12** |
| 2 | 소스를 바이너리보다 새롭게(`touch server/Engine.cs`) | exit 2, 검사 0개 | **exit 2**, `checks` 없음, `newestSource: Engine.cs` |
| 3 | 다시 빌드 | 원래대로 | **1/12로 복귀** |
| 4 | 실제로 어긋난 검사가 있을 때 | FAIL | **FAIL** (`gate-clean` exp 0 got 1, 더러운 트리) |
| 5 | `WP-STATE-INTEGRITY-LAND` 18개 전체 | — | **PASS 0/18** (`worktreeCleanAtStart` false로 기록됨) |
| 6 | `POST-EXECUTOR` 13개 전체 | — | **FAIL 1/13** — §아래 |

**시험 2가 이 수정의 목적이다.** `--no-build`를 쓰면서도 낡은 바이너리를 재지 않는다는 것을
보이는 자리이며, **검사를 하나도 돌리지 않는다**는 것까지 확인했다(모르는 것을 PASS로 적지 않는다).

## §1이 두 번, §9가 한 번 틀렸다

`ADR-016` §1은 `--no-build`를 결함으로 지목했고 §6이 그 진단을 정정했다(진짜 사유는 `unknown command`).
§9는 원인을 자식/부모 산출물 충돌로 좁혔지만 **"앞에서 한 번만 빌드하면 된다"**는 처방이 틀렸다.
`program-verify`는 **자기가 곧 `server`의 exe**라 언제 빌드하든 자기 자신을 덮을 수 없다.
`BuildOnce`를 넣은 커밋 `dbbff7c`는 그래서 게이트를 아예 못 돌리게 만들었고, `c18eee8`이 정정이다.

세 번의 정정 끝에 남은 결론: **§1이 지목했어야 할 것은 `--no-build`가 아니라 "낡음을 재지 않는 것"이었다.**

## `POST-EXECUTOR` FAIL 1/13은 러너 결함이 아니다

```
gate-clean ['server'] | exp 1 | got 0
```

`POST-EXECUTOR`의 `gate-clean`은 **기대값이 1**이다 — 실행자가 방금 산출물을 냈고 아직 커밋 전인
상태를 전제하기 때문이다(`POST-COMMIT`은 같은 명령에 0을 기대한다). 나는 **커밋 직후 깨끗한 트리**에서
돌렸으므로 어긋나는 것이 맞다. **실행 맥락이 틀린 것이지 검사가 틀린 것이 아니다.**

## 지표는 만족했으나 목적은 미달인 부분

1. **`BuildOnce` 판을 커밋했다가 되돌렸다**(`dbbff7c` → `c18eee8`). 커밋 전에 깨끗한 트리에서
   한 번만 돌려 봤으면 exit 2를 바로 봤을 것이다. **더러운 트리 결과 하나만 보고 "6 → 1로 줄었다"에
   만족했다.** 히스토리는 지우지 않고 §10에 사유를 남겼다.
2. **소스 mtime이 미래면 다시 빌드해도 계속 거부한다.** 실측으로 확인했다. fail-closed 방향이고
   메시지가 어느 파일인지 말해 주지만, 시계가 어긋난 환경에서는 막힌다. 고치지 않았다.
3. **`BuiltInCommands` 중복은 그대로다**(`ADR-016` §7). `DiCompletionCheckCli.cs:18`과
   `ProgramVerifierCli.cs`에 같은 목록이 두 벌 있다. 근본 수정은 코덱스 영역이다.
