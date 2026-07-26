# 미확인 3건을 실측으로 닫는다

- **주체(actor)**: 조율 세션(Claude Opus 5). 사람 지시. 위조 시험은 **전부 클론에서** 했다.
- **날짜**: 2026-07-26 · 출처: `WORKSTATE.nextActions`의 *"미확인 3건"*

---

## ① `InspectPending`이 reconciliation보다 먼저 `idempotent`를 낼 수 있다 — **사실이었고, 고쳤다**

### 실측 (고치기 전)

세 가지를 위조하면 그 분기를 탄다.

```
.state-applier/pending/PENDING-PROBE.json   postStateSha256 = 현재 WORKSTATE 해시
WORKSTATE.applier-log.jsonl                 같은 4개 해시를 가진 success 항목 추가
                                            (상태 파일은 건드리지 않는다 — 해시가 유지돼야 한다)

handoff-integrity        → exit 1   (reconciliation이 깨졌다)
state-transition apply   → exit 0   status "idempotent" · stateWritten false   ★거짓 OK
```

`InspectPending`이 `RunReconciliation`보다 **먼저** 있었고, `HasMatchingSuccess`는
reconciliation을 돌리기는 하지만 **그 verdict를 보지 않고** SuccessLookup만 쓴다.

### 고친 것

`RECOVERY.md`가 지켜야 할 불변식으로 **"reconciliation before any apply decision"** 을 명시한다.
**코드가 자기 문서를 어기고 있었다.** `RunReconciliation`을 `InspectPending` 앞으로 옮겼다.

| | 고치기 전 | 고친 뒤 |
| --- | --- | --- |
| 위조 시나리오 | **exit 0 `idempotent`** | **exit 1 `rejected`** (`reconciliation-failed`) |
| 정상 재적용(깨끗한 클론) | `idempotent` exit 0 | **`idempotent` exit 0 — 유지** |
| `state-transition --self-test` | 0 | **0** |

**거짓 OK만 막혔고 재시도 생존은 그대로다.** 후자가 없으면 이 수정은 회귀다.

### 남는 것

상태를 쓰지는 않았으므로 **위조된 상태 변경은 아니었다.** 그러나 exit 0을 보고
*"일관되게 적용돼 있다"* 고 믿는 사람·자동화가 있었다면 틀린 판단을 했을 것이다.

---

## ② self-test 개별 케이스의 출력 필드 — **미달이며, 원인은 더 깊었다**

`06C-2-R3`는 *"high-risk 3종 self-test 출력에 `reasonMatched=true` 또는 동등 필드"* 를,
`R4`는 *"high-risk 3종이 exit=1과 정확 reason/code match를 기록"* 을 요구했다.

### 실측

```
trust-origin --self-test 케이스 객체의 키:  case · pass · negative      ← 그뿐이다
high-risk 관련 케이스:  high-risk-stays-closed  1개                    ← "3종"이 아니다
```

그리고 그 케이스가 부르는 함수는

```csharp
private static bool HighRiskFailClosed() => true;      // TrustOriginCli.cs:993
```

**상수다.** 아무것도 검사하지 않는다. `DeclareCore`의 선행조건
`if (!HighRiskFailClosed()) return Fail("high-risk-not-fail-closed")` 도 **결코 실패하지 않는다.**

### 그러나 성질 자체는 검증된다 — 다른 곳에서

```
StateApplierCli self-test:  phase-change-no-receipt · recovery-no-receipt · replay-no-receipt
                            (셋 다 negative, CaseHighRisk(ctx, kind))
StateApplierCli:202         HighRiskKinds{PHASE_CHANGE,RECOVERY,REPLAY} → trusted-human-receipt-required
```

`state-transition-selftest`는 **LAND 게이트 검사**다. 즉 **high-risk fail-closed는 실제로 기계가 검증한다 —
`trust-origin`이 아니라 `state-transition`에서.**

### 정정

`docs/verification/four-di-criteria-recheck.md`에서 06C-2의 이 항목을 *"self-test 통과에 포함"* 으로
적고 **전부 일치**라고 결론했다. **과했다.** 정확히는:

- **요구된 출력 필드는 없다** → 완료 기준 미달
- **`HighRiskFailClosed()`는 상수** → `trust-origin` 쪽 주장은 공허하다
- **성질은 `state-transition`에서 검증된다** → 실질 위험은 없다

**고치지 않았다.** `HighRiskFailClosed()`를 실제 검사로 바꾸거나 제거하는 것은
`declare` 선행조건을 바꾸는 일이라 **사람 결재**다.

---

## ③ 아카이브 지시서의 NUL 바이트 — **원인을 찾아 고쳤다**

```
NUL 포함 .md: 4개, 각각 정확히 4바이트
문맥:  "단일 land gate: <NUL>5H·<NUL>6C-1·<NUL>6C-2·<NUL>6H"
```

**`0`이 NUL로 쓰였다.** 같은 문장이 네 지시서에 복사돼 있고, 그 문장의 `0` 네 개가 전부 그렇다.
`grep`이 이 파일들을 바이너리로 판정해 **내용을 건너뛰었다** — 아카이브를 grep으로 훑는
도구·사람에게는 없는 문서였다.

**pin 여부를 먼저 확인했다**: 44개 pin 대상 중 **이 넷은 하나도 없다.** 그래서 고쳐도 아무 pin이 깨지지 않는다.

`NUL → '0'` **1:1 치환**만 했다. 파일당 **4바이트**, 크기 불변. `grep`이 이제 읽는다(실측).

---

## 지표는 만족했으나 목적은 미달인 부분

1. **②를 고치지 않았다.** 실측·정정만 했다. 사람 결재 사항이다.
2. **①의 위조는 세 가지를 동시에 만들어야 성립한다.** 난이도가 낮지 않다는 뜻이지만,
   *"어렵다"* 는 fail-closed가 아니다 — 그래서 고쳤다.
3. **③의 원인(`0`이 왜 NUL로 쓰였는가)은 모른다.** 어떤 도구·경로에서 그랬는지 추적하지 않았다.
   같은 일이 다시 일어날 수 있다.

---

# ② 후속 (append, 2026-07-26) — `HighRiskFailClosed`를 실제 검사로 바꿨다

## 무엇을 했나

```
TrustOriginCli   RequiredHighRiskKinds = [PHASE_CHANGE, RECOVERY, REPLAY]   ← 요구 목록을 소유
                 HighRiskFailClosed() => StateApplierCli.HighRiskFailsClosed(RequiredHighRiskKinds)
StateApplierCli  HighRiskFailsClosed(requiredKinds)  → 임시 fixture 저장소에서 CaseHighRisk를 돈다
                 CaseHighRisk  → exit 1 **그리고** FailureCode == "trusted-human-receipt-required"
```

**세 가지를 지켰다.**

1. **사본을 만들지 않았다.** `state-transition` self-test의 `phase-change/recovery/replay-no-receipt`와
   **같은 `CaseHighRisk`** 를 쓴다. 한쪽만 고쳐지면 서로 다른 답을 내는 상황을 만들지 않는다.
2. **검증 대상 집합을 순회하지 않는다.** 요구 목록은 `trust-origin`이 갖고, `StateApplier`의
   `HighRiskKinds`를 참조하지 않는다.
3. **exit code가 아니라 사유를 본다.** `06C-2-R4`가 요구했던 *"exit=1과 정확 reason match"* 다.

## 반증 — 두 번 틀리고 세 번째에 통과했다

| 시도 | 방식 | `REPLAY`를 뺐을 때 |
| --- | --- | --- |
| 1차 | `HighRiskKinds.All(...)` 순회 | **잡지 못함** — 집합이 줄면 검사도 준다 |
| 2차 | 사유 검사 추가 | **잡지 못함** + `pre-state-mismatch` 회귀 |
| **3차** | 요구 목록 분리 + 사유 검사 | **`trust-origin` 1 · `state-transition` 1 — 둘 다 잡는다** |
| — | 되돌린 뒤 | **둘 다 0** |

### 2차의 회귀는 내 편집 실수였다

`return ApplyEnvelopeCore(...) == 1;` 이라는 **유일하지 않은 줄**을 문자열 치환해서
`CaseHighRisk`가 아니라 **`CasePreMismatch`에 들어갔다.** 그 케이스가 엉뚱하게
`trusted-human-receipt-required`를 기대하게 되어 실패했다.

**커밋된 상태와 미커밋을 갈라서(`git stash`) 원인을 좁혔다** — 커밋본 0, 미커밋 1.
그러지 않았으면 앞서 커밋한 reconciliation 재정렬을 의심했을 것이다.

## 구조 변경 — `ApplyEnvelopeResult` 분리

사유를 보려면 판정 결과가 필요해서 `ApplyEnvelopeCore`를 둘로 나눴다.

```
ApplyEnvelopeResult(...)  → ApplyResult   (판정만)
ApplyEnvelopeCore(...)    → WriteResult(envelope, ApplyEnvelopeResult(...))   (출력)
```

외부 동작은 같다 — `state-transition apply`의 출력·exit code 불변(LAND 0으로 확인).

## 지표는 만족했으나 목적은 미달인 부분

1. **`HighRiskFailsClosed`가 임시 저장소를 만든다.** `declare`와 `trust-origin --self-test`가
   느려진다(fixture 3개). 상수였을 때는 공짜였다 — **정확성의 값이다.**
2. **`CaseHighRisk`가 보는 사유는 문자열이다.** `trusted-human-receipt-required`가 바뀌면
   조용히 어긋난다. 상수로 묶지 않았다.
3. **세 번 시도했다.** 1·2차를 커밋하지 않은 것은 `measure`·self-test를 커밋 전에 돌렸기 때문이다.
   그 습관이 없었으면 회귀를 밀어 넣었을 것이다.
