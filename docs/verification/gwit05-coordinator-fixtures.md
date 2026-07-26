# 조율자 몫 witness — `measure`·`verify-behavior` 픽스처 모드

- **주체(actor)**: 조율 세션(Claude Opus 5)이 직접 구현·실측. 결재는 사람(git user `Jaehyuk`).
  코덱스 영역(`server/Harness/`)이 아니므로 지시서 없이 직접 경로를 썼다(CLAUDE.md 관례 ①).
- **날짜**: 2026-07-26

## 무엇을 했는가

`GWIT-05` 지시서가 `build-verify`만 다루는 대신 §5에서 조율자 몫으로 넘긴 두 검사를 처리했다.

| 검사 | 추가한 것 | 파일 |
| --- | --- | --- |
| `measure` | `--fixture <dataRoot>` | `server/Cli/CliRouter.cs` |
| `verify-behavior` | `--fixture <snapshot.json>` | `server/BehaviorSnapshotCli.cs` |

픽스처: `docs/qa/gate-witness/measure-violating/`, `docs/qa/gate-witness/behavior-snapshot-mismatch.json`

## 사용한 하네스 (명령 · exit code · 수치)

| 명령 | exit | 비고 |
| --- | --- | --- |
| `gate-witness-check` (등재 전) | 1 | `totalUnwitnessed` **5** |
| `gate-witness-check` (등재 후) | 1 | `totalUnwitnessed` **2** — 남은 건 `build-verify` 2건뿐 |
| `gate-witness-check <픽스처>` | 1 | 반증 witness 자체는 살아 있음 |
| `program-verify verify --gate POST-COMMIT` | 1 | **FAIL 2/12** — HUMAN-INBOX에 신고 |
| `measure dev-pack` | 0 | `{"gate":"dev-pack","violations":0}` |

## 반증 시험 (전부 실측)

| # | 시험 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1 | `measure strict-pack --fixture …` | 1 | **1** (violationCount 1) |
| 2 | `verify-behavior --fixture …mismatch.json` | 1 | **1** (`behaviorEqual:false`) |
| 3 | **경로명은 그대로, blueprint 값만 실측값에 맞춤** | 0 | **0** |
| 4 | `verify-behavior --fixture docs/behavior-snapshot.json` | 0 | **0** (`behaviorEqual:true`) |
| 5 | `measure … --fixture <없는 경로>` | 2 | **2** |
| 6 | `verify-behavior --fixture <없는 파일>` | 2 | **2** |
| 7 | `measure dev-pack` (production) | 0 | **0** |
| 8 | `verify-behavior` (production) | 0 | **0** |
| 9 | 픽스처 실행 후 잔여 사본 | 0 | **0** |

**시험 3·4가 이 작업의 목적이다.** 경로 이름을 바꾸지 않고 내용만 뒤집었더니 exit가 뒤집혔다 —
경로명으로 판정하는 우회 구현이 배제된다.

## 시험이 잡아낸 것 세 가지 (전부 내 실수)

### ① `target`은 상한이 아니라 일치 조건이다

처음 만든 시험 3은 `maxFunctionLength.target`을 0 → 9999로 "느슨하게" 하면 통과할 거라 봤다.
**실측은 여전히 exit 1.** `IsMetricWithinBlueprint`는 `actual == target`이고 `target`이 `band`보다
우선한다(`server/Program.cs:1076`). 값 80은 9999와도 다르다.

**픽스처는 옳았고 시험이 틀렸다.** 고친 시험 3은 target을 실측값과 같게 맞춰 exit 0을 확인한다.
동시에 이 성질 덕에 `target: 0` 픽스처는 **저장소가 변해도 영원히 위반**이다(drift 없음).

### ② 임시 사본 삭제가 조용히 실패하고 있었다 — 그리고 내가 그걸 gitignore로 가렸다

`Directory.Delete`가 `history/restore-*`(불변으로 만들어져 읽기 전용)에서 막혔는데
`catch (IOException) { }`가 삼켰다. 사본 **4개**가 쌓였다.
그런데 `git status`는 깨끗했다 — 내가 안전망이랍시고 `.gitignore`에 넣었기 때문이다.

**오늘 CLAUDE.md가 경고한 함정에 그대로 걸렸다**(gitignore가 필요한 파일을 쓸어간 사례).
`.gitignore` 추가를 **되돌렸고**, 삭제 실패는 이제 stderr에 `fixtureCleanupFailed`로 드러난다.
읽기 전용 속성을 풀고 지우도록 고쳐 잔여 0을 실측했다.

### ③ 내가 추가한 코드가 production 게이트를 깨뜨렸다

시험 7(`measure dev-pack`)이 **exit 1**을 냈다. `RunMeasureCli`가 88줄이 되어 band `[0,80]`을 넘겼다.
`ResolveMeasureDataRoot`로 분리해 80으로 되돌렸다.
**production 회귀 시험을 넣지 않았다면 이 커밋이 게이트를 깨뜨린 채 나갔다.**

## 참조한 스킬

`skills/common/directive-authoring.md` §7. 다만 이번 작업은 지시서 경로가 아니라 직접 경로였다.

## 지표는 만족했으나 목적은 미달인 부분

1. **POST-COMMIT 게이트가 빨갛다.** 지난 세션에 `POST-EXECUTOR`의 `requireFailureWitness`를
   켜면서 `build-verify`를 witness 없이 남겨 둔 결과다. **주체는 나이고, 켠 시점부터 계속 빨갰다.**
   개별 하네스로만 커밋해 와서 이번에 게이트 전체를 돌리기 전까지 몰랐다.
   `expectedExit`를 고쳐 덮지 않았다 — HUMAN-INBOX에 판단을 올렸다.
2. **`LAND`의 `requireFailureWitness`는 여전히 못 켠다.** `build-verify` 1건이 남아 있다.
   `GWIT-05` 반입 전에 켜면 §6이 경고한 영구 적색이 된다 — ①이 바로 그 사례다.
3. **`verify-behavior`의 양성 witness는 매니페스트에 넣지 않았다.** 실재 스냅샷과 비교하는
   픽스처는 저장소가 변할 때마다 drift한다. 시험 4로 1회 실측했을 뿐 상시 검사는 아니다.
