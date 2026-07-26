# self-test 케이스 수를 선언에서 실측으로 (2026-07-27)

## 주체

사용자가 재량을 위임했고 **조율자(Claude)가 실행**했다. 지시서 없이 직접 경로를 썼다 —
CLAUDE.md의 예외 조항이 아니라 **위임에 근거한다.** 사유를 여기 남긴다.

## 무엇이 문제였나

`SelfTestGateCounts` 표의 세 항목 중 **둘이 아무와도 대조되지 않았다.**

```
BuildIntegrationEvidence :  SelfTestNode(gatesPass, CasesFor(key))   ← 표에서 읽어 쓴다
SelfTestEvidencePass     :  ReadInt(node,"casesRun") == expectedCases ← 표에서 읽어 비교한다
```

**표를 표와 비교하니 항상 참이다.** `trustOriginSelfTest`만 `selftest-case-count-matches-table`이
실재(`cases.Count + 1`)와 대조했고, `stateTransitionSelfTest`(19)와 `recoverySelfTest`(8)는
**순수 선언**이었다. 케이스를 늘려도 줄여도 아무도 눈치채지 못한다.

이건 이 저장소가 반복해서 밟은 실패다 — `HighRiskFailClosed()`가 `=> true`였던 것,
`handoff-integrity --projection`이 no-op이었던 것과 **같은 부류다.**

## 무엇을 했나

각 CLI가 **케이스를 만드는 목록**과 **개수를 세는 곳**이 같은 것을 쓰도록 갈랐다.

- `StateApplierCli.RunSelfTestCases(root)` / `RecoveryCli.SelfTestCases()` — 케이스 배열을 준다.
  찍는 쪽(`RunSelfTest`)도 이걸 쓴다. **목록을 두 벌 두면 한쪽만 늘어난다.**
- `SelfTestCensus.Measure(prefix, countCases)` — 임시 root, stdout 삼킴, 임시 디렉터리 청소를
  한 곳에 둔다. 세지 못하면 **-1**이다(0을 주면 "케이스 없는 self-test"와 구분이 안 된다).
- trust-origin self-test에 `state-transition-case-count-measured`,
  `recovery-case-count-measured` 두 케이스 추가. 표 값을 **실제로 돌려 센 값**과 비교한다.
- `trustOriginSelfTest` 27 → **29**(케이스 2개 추가분).

## 실측 — 막았다 / 부쉈다

| | verdict | failed | 실패한 케이스 |
| --- | --- | --- | --- |
| 정상 | PASS | 0 | — |
| 표 위조(19→18, 8→7) | **FAIL** | **2** | `state-transition-case-count-measured`, `recovery-case-count-measured` |

**위조한 두 항목만 정확히 실패한다.** 다른 케이스로 번지지 않았다.

게이트: `measure=0` · 세 self-test=0 · `verify-behavior`·`doc-integrity`·`handoff-integrity`=0 ·
`LAND` 18/18 PASS.

비용: trust-origin self-test가 16.7s → 두 self-test(각 1.1s·0.8s)를 더 돈다.

## 참조한 스킬

`skills/common/` (전부). `skills/domains/`는 이번 변경 경로(`server/*.cs`)와 맞는 트리거가 없어 읽지 않았다.

## 지표는 만족했으나 목적은 미달인 부분

1. **negative 케이스를 코드에 심지 못했다.** 위조 시험은 표를 손으로 고쳐 rebuild한 **1회성 수동
   검증**이다. `required-commands-drift-detected`처럼 자기 안에서 표류를 만들어내는 케이스가 아니다.
   실측 자체가 실행 결과라 코드 안에서 위조하려면 케이스 목록을 조작해야 하는데, 그러면
   검사 대상과 검사 도구가 다시 붙는다. **다음에 표를 고치는 사람은 이 표가 실측과 대조된다는
   사실만 알면 되고, 어긋나면 자동으로 실패한다** — 거기까지는 됐다.
2. **`casesRun`을 생산자가 여전히 표에서 읽는다.** evidence의 값은 실측이 아니라 선언 그대로다.
   바꾸지 않은 이유는 evidence 생산 시점에 self-test를 또 돌리게 되어 게이트 실행이 겹치기
   때문이다. 대신 **표가 실재에 못 박혀서** 선언과 실재가 갈라질 수 없게 했다.
3. **`negativeCaseCount`는 여전히 아무와도 대조되지 않는다.** `casesRun`과 같은 부류의 값인데
   이번에 손대지 않았다. 케이스 수보다 약한 지표라 뒤로 미뤘다 — **남은 구멍으로 적어둔다.**
