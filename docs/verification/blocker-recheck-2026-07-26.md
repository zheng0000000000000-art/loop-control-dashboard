# `DI-00-04` 차단 사유 두 개를 다시 쟀다

- **주체(actor)**: 조율 세션(Claude Opus 5). 사람 지시. **깨끗한 클론에서만 측정했고
  실제 저장소의 `WORKSTATE.json`은 건드리지 않았다.**
- **날짜**: 2026-07-26 · 대상: `docs/handoff/WORKSTATE.json`의 `blockers`(작성 2026-07-13, 검수자)

## 왜 다시 쟀나

`WORKSTATE`는 13일째 `blocked`이고, 차단 사유는 **게이트 통과 여부가 아니라 구체적 결함 두 개**다.
오늘 LAND 게이트가 18/18로 통과했지만 **그것은 이 두 결함에 대한 증거가 아니다.**

## 차단 사유 ① — 손 위조 transition-id가 통과한다

> "state-transition의 멱등이 reconciliation보다 먼저라 손 위조 transition-id가 통과한다(검수자 실증)"

**현재 순서**(`StateApplierCli.ApplyEnvelopeCore`):

```
ValidateEnvelopeStatic → InspectPending → RunReconciliation → CheckExistingTransition(멱등)
                                          ~~~~~~~~~~~~~~~~~   ~~~~~~~~~~~~~~~~~~~~~~~
                                          reconciliation이 멱등보다 먼저다
```

**읽은 것으로 끝내지 않고 실제로 위조했다.** 클론의 `WORKSTATE.json`
`appliedTransitions`에 로그 대응이 없는 `FORGED-BY-HAND-0001`을 손으로 끼워넣었다.

| 단계 | 결과 |
| --- | --- |
| 위조 전 `handoff-integrity` | exit **0** |
| 위조 후 `handoff-integrity` | exit **1** · `state-transition-not-logged` · 위조 id를 정확히 지목 |
| 위조 상태에서 `state-transition prepare` | exit 0 (envelope는 만들어진다) |
| **위조 상태에서 `state-transition apply`** | **rejected · exit 1 · `stateWritten: false`** |

```json
"failures": [{"code":"reconciliation-failed",
              "detail":"FORGED-BY-HAND-0001:state-transition-not-logged"}]
```

**통과하지 않는다.** 차단 사유 ①에 적힌 기전은 **해소됐다.**

## 차단 사유 ② — `--human-decision`이 임의 파일이라 AI가 자기 승인을 위조할 수 있다

> "--human-decision도 임의 파일이라 AI가 자기 승인을 위조할 수 있다"

**그 옵션은 제거됐다**(`StateApplierCli.RemovedOptions`). 실측:

```
state-transition prepare --transition-id FORGE-TEST-1 --human-decision <위조파일>
  → {"error":"removed-option: --human-decision"}   exit 2
```

현재 표면은 `prepare --transition-id --request` → `apply --envelope`이고,
envelope이 **preState·postState·request·contract 해시를 묶는다.** 임의 승인 파일이 낄 자리가 없다.

그리고 고위험 전이는 아예 막혀 있다:

```
HighRiskKinds → rejected: "trusted-human-receipt-required"
                "verified human receipt infrastructure is not available"
```

**AI가 자기 승인을 위조하는 경로는 없다 — 고위험은 승인 자체가 불가능하다.**

## 지표는 만족했으나 목적은 미달인 부분

1. **`InspectPending`은 reconciliation보다 먼저이고 `idempotent`(exit 0)를 낼 수 있다.**
   pending 저널·상태 해시·로그 성공을 **모두** 위조하면 reconciliation을 건너뛴 성공 보고가 가능하다.
   **이 경로는 재보지 않았다.** 다만 그 분기는 `stateWritten: false`이므로
   *"거짓 OK"* 이지 *"위조된 상태 변경"* 은 아니다. **재보지 않았다는 사실을 그대로 적는다.**
2. **차단 사유의 세 번째 조건은 확인하지 않았다.** blockers는
   *"05H+06C-1+06C-2+06H를 통합 branch에서 단일 land gate로 넘겨야 한다"* 고도 적고 있다.
   **그 네 조각이 이 브랜치에 다 들어와 있는지는 재지 않았다.** LAND 18/18은 그 증거가 아니다.
3. **`WORKSTATE`를 옮기지 않았다.** 상태 전이는 사람 결재다. 이 문서는 측정이지 판정이 아니다.
