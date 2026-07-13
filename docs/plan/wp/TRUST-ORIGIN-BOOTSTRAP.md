# TRUST-ORIGIN-BOOTSTRAP — 신뢰 원점 1회 부트스트랩 의식

> 순환 끊기: trust-origin에 verified receipt를 요구하면 → receipt 발행 시스템을 신뢰할 trust-origin이 필요
> → 순환. 최초 한 번은 사람이 신뢰의 바닥을 직접 놓는다. 이후는 그 원점 위에서 program 검증 + receipt provenance.

## 무엇인가 / 아닌가

- 정본 상태명 **`BOOTSTRAP_TRUST_ORIGIN`** — `VERIFIED_HUMAN_APPROVAL`(서버 receipt)과 구분.
- **신뢰 epoch 생성권이지 상태 변경 승인권이 아니다.**
- 정직한 딱지: `declarationStatus = "HUMAN_DECLARED_NOT_CRYPTOGRAPHICALLY_VERIFIED"`. "프로그램이 사람임을
  증명했다"가 아니라 "사람이 이 지점부터 신뢰를 시작한다고 선언했고, 당시의 저장소·상태 증거를 고정했다".
- `declaredBy`는 부트스트랩 운영자 표기이지 verified actor가 아니다. git author/커밋 메시지도 사람 증명이
  아니라 governance 기록.

## 부트스트랩이 못 하는 것 (명시적 금지)

recovery transition · phase change · replay · 제품 코드 변경 · 자동 실행 허용 · 과거 이력 소급 승인 ·
검증 실패 override. 이 중 무엇도 부트스트랩으로 할 수 없다.

## 선언 전 필수 조건 (10개, 전부 충족)

```text
1. clean clone/worktree에서 build exit 0
2. 현재 WORKSTATE ↔ applier-log reconciliation exit 0
3. StateApplier·ReconciliationChecker가 tracked
4. canonical 파일 직접 수정 없음
5. WORKSTATE hash 계산
6. applier-log hash 계산
7. commit hash 고정
8. legacy history가 exact replay 불가임을 명시
9. 자동 launcher 비활성
10. high-risk transition fail-closed 확인
```

하나라도 미충족 → 선언하지 않는다.

## commit 분리 (자기참조 제거)

record에 자신을 담은 commit hash를 넣으면 순환한다. 두 지점을 나눈다.

```text
baselineCommit    : 통합 구현이 land된 commit A (신뢰하려는 snapshot). record가 참조.
declarationCommit : record만 추가한 commit B. record 밖에서 git annotated tag로 연결.
```

## trust-origin record

```json
{
  "schemaVersion": 1,
  "trustOriginId": "TO-2026-001",
  "trustEpoch": 1,
  "declarationType": "BOOTSTRAP_TRUST_ORIGIN",
  "baselineCommit": "<A: 05H+06C-1+06C-2+06H가 land된 commit>",
  "workstateSha256": "…",
  "applierLogSha256": "…",
  "stateApplierSchemaVersion": 2,
  "reconciliationSchemaVersion": 2,
  "legacyHistory": "NOT_EXACTLY_REPLAY_VERIFIED",
  "buildVerdict": "VERIFIED_PASS",
  "reconciliationVerdict": "VERIFIED_PASS",
  "normalTransitionReady": true,
  "verifiedHumanApprovalReady": false,
  "recoveryApplyReady": false,
  "automatedExecutionReady": false,
  "declaredBy": { "actorType":"human", "actorId":"bootstrap-operator", "actorPath":"local-manual" },
  "declarationStatus": "HUMAN_DECLARED_NOT_CRYPTOGRAPHICALLY_VERIFIED",
  "declaredAt": "…"
}
```

## 실행 방식 (사람 운영자 절차 — 신원은 프로그램이 증명 못 함)

> 사람 운영자가 직접 수행해야 하는 부트스트랩 절차다. 자동화 정책상 AI 실행을 금지하지만,
> 이번 epoch에서는 행위자가 실제 사람인지 프로그램으로 증명하지 못한다
> (`HUMAN_DECLARED_NOT_CRYPTOGRAPHICALLY_VERIFIED`와 일관).

```text
Phase A (pre-land):
  1. 모든 AI/자동 실행 중단
  2. 사람이 로컬 터미널에서 통합 land gate(1~12) 실행 → 통과 시 baseline commit A 생성
Phase B (post-land):
  3. clean checkout A → build/reconciliation 재확인
  4. 프로그램이 evidence 파일 생성 → 사람이 evidence·hash 직접 확인
  5. 사람이 trust-origin declare 실행 (record가 baselineCommit=A 참조)
  6. declaration commit B 생성:  [trust-origin] bootstrap TO-2026-001 at trust epoch 1
  7. annotated tag trust-origin/TO-2026-001 → B (record 내부엔 declarationCommit 미포함 — 자기참조 방지)
```

명령의 **약한 안전장치**(신원 증명은 아님): 비대화형 실행 기본 거부 · 명시적 bootstrap acknowledgement ·
모든 자동 launcher 비활성 확인 · 기존 epoch atomic-create · record 경로 고정 · record 외 파일 변경 거부.

## 구현 경계

```text
trust-origin declare   (StateApplier transition과 분리된 별도 명령)
  - WORKSTATE/applier-log 수정 안 함
  - 검증 결과·hash만 읽음
  - trust-origin record만 새로 생성
  - 기존 epoch 있으면 기본 거부
```

**`state-transition apply --bootstrap` 금지** — 그러면 부트스트랩이 고위험 상태 변경의 우회 통로가 된다.

## 재선언 규칙

```text
trustEpoch == 0  → BOOTSTRAP_TRUST_ORIGIN 허용 (1회)
trustEpoch >= 1  → 새 trust origin에는 VERIFIED_HUMAN_APPROVAL receipt 필수
```

저장소 전체 폐기 후 새 신뢰 체인은 `REPOSITORY_REINITIALIZATION`(별도 사람 절차)이며, 단순 두 번째
bootstrap으로 처리하지 않는다.

## 완료 후 상태

```text
TRUSTED_BASELINE = true · NORMAL_TRANSITION_READY = true
VERIFIED_HUMAN_APPROVAL_READY / RECOVERY_APPLY_READY / PHASE_CHANGE_READY / REPLAY_READY / AUTOMATED_EXECUTION_READY = false
```

---

## 부록 A — 선행조건 2 정정 (사람 결재, 2026-07-14)

> **위 원문은 사람이 준 설계다. 바꾸지 않았다. 이 부록이 정정분이다.**

### 발견된 순환 (검수자, 06C-2 발사 전)

```
선행조건 2 : "현재 WORKSTATE ↔ applier-log reconciliation exit 0"
현실       : at-rest reconciliation = exit 1  (DI0004-BLOCKED-CODEX)
```

`DI0004-BLOCKED-CODEX`는 **적용이 실패했다고 자기 로그가 말하는 전이가 상태에 남아 있는 것**이다
(`WORKSTATE.applier-log.jsonl:20` = `exitCode:1` / `WORKSTATE.json:384` = `appliedTransitions`에 존재).
**rollback 부재 결함(WP 결함 2)의 실측 흔적**이며, 05H-R1이 되살린 reconciliation이 정확히 이것을 잡는다.

**부트스트랩의 목적이 바로 이 오염을 사람이 1회 인정하고 "여기부터 믿는다"고 선언하는 것이다.
그런데 선행조건 2가 그것을 막는다. 순환이다.**

### 정정 (사람 결재: choi, 2026-07-14 — `HUMAN-INBOX.md` 참조)

**선행조건 2**:

> ~~현재 WORKSTATE ↔ applier-log reconciliation **exit 0**~~
>
> **→ reconciliation이 실행 가능하고, 발생하는 모든 failure가 record의 `knownExceptions[]`에
> 빠짐없이 명시돼 있을 것. 명시되지 않은 failure가 하나라도 있으면 선언하지 않는다.**

**`trust-origin` record에 `knownExceptions[]` 추가:**

```json
"knownExceptions": [
  {
    "code": "state-transition-not-logged",
    "subject": "DI0004-BLOCKED-CODEX",
    "what": "적용이 실패했다고 로그에 기록된 전이가 appliedTransitions에 남아 있다",
    "why": "post-apply 실패 시 rollback이 없던 시절(WP 결함 2)의 흔적. 06C-1-R2가 그 결함을 고쳤다",
    "whyNotReplayed": "새 prepare/apply 경로로 과거 legacy 전이를 재생할 수 있는지 미지수다. 재생 실패 시 상태가 더 나빠진다"
  }
]
```

**핵심 안전장치**: `knownExceptions[]`는 **정확한 집합**이어야 한다.

- reconciliation의 failure 집합 **⊆** `knownExceptions[]`의 subject 집합 → 통과
- **하나라도 명시되지 않은 failure가 있으면 → 선언 거부.**
- **즉 "오염을 인정한다"가 "오염을 안 본다"가 되지 않는다.** 새 오염이 생기면 선언이 막힌다.

**기존 필드 `legacyHistory: "NOT_EXACTLY_REPLAY_VERIFIED"`와 일관된다** — 이 record는 원래
"정확한 replay는 검증되지 않았다"를 **인정하는** 설계다. `knownExceptions[]`는 그것을 **구체화**한다.

### 이것이 바꾸지 않는 것

- **선언은 여전히 사람 게이트다.** 이 정정은 "AI가 선언해도 된다"는 뜻이 **아니다.**
- **`recoveryApplyReady: false` · `automatedExecutionReady: false` 유지.**
- **재선언 규칙(§재선언) 유지** — `trustEpoch >= 1`이면 `VERIFIED_HUMAN_APPROVAL` receipt 필수.
