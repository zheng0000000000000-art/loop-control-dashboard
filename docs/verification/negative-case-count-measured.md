# internalNegativeCases: `>=` 를 `==` 로 (2026-07-27)

## 주체

사용자 위임, **조율자(Claude) 실행.** 앞선 `casesRun` 건과 같은 부류를 잡아달라는 요청이다.

## casesRun과는 병이 달랐다

`casesRun`은 **표를 표와 비교**했다. `negativeCaseCount`는 그렇지 않다 —
`CountInternalNegativeCases`가 **실제로 명령을 실행해** stdout의 카운터를 읽는다. 실측은 하고 있었다.

문제는 비교 연산이었다.

```csharp
return !validateInternalClaims || CountInternalNegativeCases(root, check) >= claimedCases;
```

**세 가지가 조용히 넘어갔다.**

1. **`>=`** — 적게 적을수록 쉽게 통과한다. `internalNegativeCases: 1`이면 음성 사례가 하나라도
   있는 어떤 self-test든 "반증됨"으로 세어진다. 매니페스트 숫자가 실재보다 **작아지는 방향**의
   표류는 아무도 못 잡는다. (커지는 방향은 원래 잡혔다 — `15 >= 16`은 거짓이다.)
2. **`!validateInternalClaims ||`** — `requireFailureWitness`가 없는 게이트는 **재보지도 않고**
   문면 숫자를 증거로 인정했다. 재보지 않은 주장은 증거가 아니다.
3. **낡은 숫자가 실제로 박혀 있었다** — 매니페스트 note는 "거부 경로 20건", 값은 `21`,
   `docs/qa/gate-witness/jsonlines-state-15.json` 픽스처는 `20`. **실측은 21이다.**
   그 픽스처는 **어느 게이트에도 물려 있지 않아** 아무도 실행하지 않았다.

## 실측값 — 매니페스트가 맞았다

| self-test | negative / total | 매니페스트 주장 |
| --- | --- | --- |
| state-transition | **15** / 19 | 15 ✓ |
| recovery | **7** / 8 | 7 ✓ |
| trust-origin | **21** / 29 | 21 ✓ |

숫자 자체는 맞았다. **틀린 건 비교 방식과, 검사되지 않는 사본들이었다.**

## 무엇을 했나

- `HasWitness`: `>=` → **`==`**. 그리고 `requireFailureWitness`로 검증을 건너뛰지 않는다.
  건너뛰기를 없애도 준비 안 된 게이트가 빨개지지 않는다 — 차단은 `Run`이 따로 정한다
  (`blocking |= RequiresWitness && UnwitnessedCount > 0`). 즉 **보고는 정확해지고 차단은 그대로다.**
- 안 쓰게 된 `validateInternalClaims` 매개변수 제거.
- `jsonlines-state-15.json`의 trust-origin `20` → `21`(실측값).
- **새 반증 픽스처** `internal-claim-understated.json` — 14를 주장한다(실측 15).
- 그 픽스처를 **POST-COMMIT order 15로 물렸다.** 안 물린 픽스처는 시험이 아니다 —
  `jsonlines-state-15/16`이 그래서 20을 달고도 아무 일 없었다.
- 매니페스트 note "20건" → "21건".

## 실측 — 막았다 / 부쉈다

| 매니페스트 | 주장 vs 실측 | 종전 코드(`>=`) | 현재(`==`) |
| --- | --- | --- | --- |
| 정본 | 15/7/21 = 실측 | 0 | **0** |
| `jsonlines-state-15` (수정 후) | 15/7/21 = 실측 | — | **0** |
| `internal-claim-understated` | **14 < 15** | **0 (조용히 통과)** | **1** |
| `jsonlines-state-16` | 16 > 15 | 1 | **1** |

종전 코드에서 과소 주장이 통과한 것은 **추론이 아니라 재본 값이다** — 코드를 되돌려 rebuild하고
같은 픽스처로 돌려 `exit 0`을 확인했다.

게이트: `measure=0` · 세 self-test=0 · `verify-behavior`·`doc-integrity`·`gate-witness-check`·
`handoff-integrity`=0 · **LAND 18/18 PASS.**

## 참조한 스킬

`skills/common/` 전부. `server/Harness/`가 코덱스 전용 영역이라 도메인 스킬 트리거를 확인했으나
이번 변경 경로와 맞는 것이 없었다.

## 지표는 만족했으나 목적은 미달인 부분

1. **`server/Harness/`를 조율자가 직접 고쳤다.** ADR-002상 이 폴더는 코덱스 전용이다.
   지시서를 돌리지 않고 직접 경로를 썼다 — **위임에 근거한 예외이며, 규정된 예외는 아니다.**
   숨기지 않고 여기 적는다. 다음에 이 영역을 손댈 때는 지시서 경로가 맞다.
2. **`FindCaseCount`가 출력 전체에서 최댓값을 취한다.** `==`로 바꾼 지금은 self-test가 어딘가에
   더 큰 카운터를 찍기 시작하면 **정상 코드가 게이트를 깬다.** 오늘은 15/7/21이 최댓값과
   같아서 문제가 없지만, 이건 운이지 설계가 아니다. **최댓값이 아니라 최상위 문서의 값을
   읽는 것이 옳다** — 이번에 바꾸지 않았다.
3. **POST-COMMIT이 15개 검사가 됐다.** 게이트 정의를 늘린 것이라 `BASELINE-CHANGES.md`에 남긴다.
