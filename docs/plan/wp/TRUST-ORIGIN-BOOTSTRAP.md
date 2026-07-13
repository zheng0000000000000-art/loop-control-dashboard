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
