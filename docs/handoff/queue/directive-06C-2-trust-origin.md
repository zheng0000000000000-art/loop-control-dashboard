# 06C-2 — trust-origin declare: 부트스트랩 command (sonnet)

- actor: **CORE_INFRA_EXECUTOR (sonnet)** · WP-STATE-INTEGRITY · 단일 land gate
- 대상: 신규 `trust-origin declare` command (`server/`), 일반 CLI router 배선
- 짝: 06C-1과 **같은 actor·같은 integration branch·하나의 land gate**. 지시서 분할 ≠ land 분할.
- 상세 계약: `TRUST-ORIGIN-BOOTSTRAP.md`.

---

## 0. 무엇인가

신뢰의 바닥을 사람이 1회 놓는 부트스트랩 command. **신뢰 epoch 생성권이지 상태 변경 승인권이 아니다.**
recovery/phase-change/replay/제품코드/자동실행/과거이력 소급승인/검증실패 override — 무엇도 못 한다.

## 1. 배선 (Registry 아님 — 문서 모순 정정)

```text
HandoffIntegrityChecker  → HarnessRegistry 미등록 (StateApplier in-process 호출만)
handoff-integrity CLI     → 기존 HarnessRegistry 등록 유지
trust-origin declare      → 일반 CLI router에 명시 배선. HarnessRegistry 대상 아님.
```

`trust-origin declare`는 검증 하네스가 아니라 trust record를 만드는 **제한적 write command**라 Registry에
넣지 않는다. `state-transition apply --bootstrap` 금지(고위험 상태변경 우회 통로가 된다).

## 2. baselineCommit / declarationCommit 분리 (★ 자기참조 제거)

record에 자신을 담은 commit hash를 넣으면 순환한다(record→commit hash→record 변경→commit hash 변경).
두 지점을 나눈다.

```text
baselineCommit    : 05H/06C-1/06C-2/06H가 land된 실제 코드·상태 commit (신뢰하려는 snapshot)
declarationCommit : trust-origin record만 추가한 commit (baselineCommit 참조)
```

record는 baselineCommit만 담고, declarationCommit은 **record 밖에서 git annotated tag로 연결**:
`trust-origin/TO-2026-001 → declarationCommit`. record 내부에 declarationCommit을 넣지 않는다.

## 3. trust-origin record (자기참조 없음)

```json
{
  "schemaVersion": 1,
  "trustOriginId": "TO-2026-001",
  "trustEpoch": 1,
  "declarationType": "BOOTSTRAP_TRUST_ORIGIN",
  "baselineCommit": "<A>",
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

## 4. command 계약

```text
trust-origin declare
  - WORKSTATE/applier-log 수정 안 함. 검증 결과·hash만 읽음. trust-origin record만 새로 생성.
  - record 생성 이외의 파일 변경 거부. record 경로 고정.
  - 기존 epoch atomic-create 검사: trustEpoch>=1 이미 있으면 거부.
  - 선행 10조건(TRUST-ORIGIN-BOOTSTRAP §선행) 미충족 시 선언 안 함.
```

## 5. "AI 실행 불가"는 정책 표현으로 정정 (★ 정직성)

프로그램은 이 CLI 명령을 사람이 실행했는지 AI가 실행했는지 **구분 못 한다**(provenance 부재).
따라서 "AI 실행 불가"로 단정하지 않고:

> 사람 운영자가 직접 수행해야 하는 부트스트랩 절차다. 자동화 정책상 AI 실행을 금지하지만,
> 이번 epoch에서는 행위자가 실제 사람인지 프로그램으로 증명하지 못한다.

로 표현한다(`HUMAN_DECLARED_NOT_CRYPTOGRAPHICALLY_VERIFIED`와 일관). 명령에 둘 수 있는 **약한 안전장치**
(신원 증명 아님): 비대화형 실행 기본 거부 · 명시적 bootstrap acknowledgement · 모든 자동 launcher 비활성
확인 · 기존 epoch atomic-create · record 경로 고정 · record 외 파일 변경 거부.

## 6. 2단계 land 의식 (Phase A / Phase B)

```text
Phase A — pre-land integration gate
  05H+06C-1+06C-2+06H 통합 검증(WP land gate 1~11) → baseline commit A 생성

Phase B — post-land bootstrap gate
  clean checkout A → 선행 10조건 확인 → trust-origin record(A 참조) 생성 →
  declaration commit B + annotated tag trust-origin/TO-2026-001 → TRUSTED_BASELINE 선언
```

## 7. 재선언 규칙

```text
trustEpoch == 0 → BOOTSTRAP_TRUST_ORIGIN 허용 (1회)
trustEpoch >= 1 → 새 trust origin에는 VERIFIED_HUMAN_APPROVAL receipt 필수
```

저장소 전체 폐기 후 새 체인은 `REPOSITORY_REINITIALIZATION`(별도 사람 절차) — 두 번째 bootstrap으로 처리 안 함.

## 8. 완료 기준 (exit code)

```text
1. dotnet build server -c Release                                  → 0
2. 선행 10조건 충족 상태에서 trust-origin declare                   → record 생성, trustEpoch=1, baselineCommit=A
3. 재선언 (trustEpoch>=1)                                          → 거부
4. record 외 파일 변경 시도                                         → 거부
5. record에 declarationCommit 없음 (자기참조 없음) · tag로 연결됨
6. 비대화형 실행 기본 거부 동작 확인
7. measure dev-pack                                               → 0
```

## 9. 보고 / 스킬

actor(sonnet)·명령과 exit·참조 스킬·`## 지표는 만족했으나 목적은 미달인 부분`(ADR-005).
