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

---

# append 2026-07-27 — FindCaseCount 최댓값 문제 정리

앞의 자진 신고 §2를 닫는다. **`==`로 바꾼 뒤 이 파서는 반대 방향으로 위험했다** —
정상 코드가 게이트를 깨는 쪽이다.

## 종전 파서가 한 일

출력 전체를 **재귀로 훑어 최댓값**을 취했다. 그러면 요약이 아니라 **어딘가 깊이 박힌 큰 수**가
답이 된다. `>=` 시절에는 관대해서 드러나지 않았다.

## 새 규칙

- **최상위에 카운터를 선언한 문서가 정확히 하나일 때만** 그 값을 인정한다.
- 중첩된 값은 보지 않는다. 카운터 이름이 둘 이상 동시에 있으면 인정하지 않는다.
- 문서가 여럿이면 **어느 것이 요약인지 프로그램이 알 수 없다 — 모르면 증거가 아니다.**
- 실패는 `0`이 아니라 **`Unmeasured = -1`**이다. `0`은 "세어봤더니 없더라"라서 구분되어야 한다.
  (비영 종료·JSON 파싱 실패도 -1로 바꿨다. 종전에는 0이었다.)

**"하나뿐"이 안전한지 먼저 쟀다**: state-transition 43개 문서 중 최상위 카운터 문서 **1개**,
recovery 1/1, trust-origin 1/1. 셋 다 하나씩이다.

## 실측 — 막았다 / 부쉈다

| 픽스처 | 무엇을 던지나 | 종전 파서 | 현재 |
| --- | --- | --- | --- |
| 정본 매니페스트 | 정상 출력 15/7/21 | 0 | **0** |
| `nested-counter` (새로 만듦) | `summary.negativeCaseCount = 99` (최상위엔 없음) | **0 — 99를 답으로 씀** | **1** |
| `jsonlines-non-json` | `not-json` | 1 | **1** |
| `jsonlines-truncated` | 두 번째 문서가 끊김 | 1 | **1** |

**세 픽스처를 POST-COMMIT order 16·17·18로 물렸다.** 뒤의 둘은 종전에도 올바르게 동작했지만
**어느 게이트에도 물려 있지 않아 실행된 적이 없었다** — 동작하는지 아무도 몰랐다는 뜻이다.

게이트: `measure=0` · 세 self-test=0 · `verify-behavior`·`doc-integrity`·`gate-witness-check`·
`handoff-integrity`=0 · **LAND 18/18 PASS.**

## 지표는 만족했으나 목적은 미달인 부분

1. **`rejectedCaseCount`를 쓰는 곳이 없다.** `CaseCountKeys` 셋 중 하나는 아무도 안 찍는다.
   지우지 않은 이유는 이름 하나가 사라지면 나중에 그 이름으로 찍는 하네스가 조용히 -1이 되기
   때문인데, **반대로 지금은 안 쓰는 이름을 유지하는 비용을 내고 있다.** 결정 안 함.
2. **`server/Harness/`를 또 조율자가 직접 고쳤다.** 앞 항목과 같은 예외다.
3. **POST-COMMIT이 15 → 18 검사가 됐다.** 게이트 정의 확대라 `BASELINE-CHANGES.md`에 남긴다.
