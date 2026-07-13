# WP-STATE-INTEGRITY — NORMAL 전이 안전화 + 신뢰 원점 부트스트랩 (단일 land gate)

> 옛 `DIRECTIVE-05` / `DIRECTIVE-06`은 폐기. 검토 5회를 거쳐 05H/06C-1/06C-2/06H로 분할하고 **단일 land gate로만**
> 반영한다. 이 WP는 **NORMAL 전이만** 안전화한다. PHASE_CHANGE/RECOVERY/REPLAY는 fail-closed(후속 WP).

## 완료 범위 (고정)

```text
reconciliation (idempotency보다 먼저)
+ transition ID를 v2 log hash에 결속
+ deterministic candidate + prepare/apply
+ 메모리 재계산 bytes로 atomic replace
+ preimage rollback + FATAL taxonomy
+ transitionKind 도입 (high-risk는 fail-closed)
+ 신뢰 원점 1회 부트스트랩 (BOOTSTRAP_TRUST_ORIGIN)
```

## 이번 WP가 활성화하지 않는 것 (fail-closed)

```text
PHASE_CHANGE / RECOVERY / REPLAY  → exit 1  trusted-human-receipt-required
```

이 셋은 서버 발행 human-approval receipt를 요구하며, receipt 발행 인프라는 **아직 없다**(실측: ACTOR-01은
자기신고 기록일 뿐 provenance 아님). 별도 `WP-HUMAN-DECISION-PROVENANCE`가 receipt issuer + ledger를
세운 뒤 활성화한다. envelope에 receipt 계약 골격(`transitionKind`, `--human-receipt-id`)은 남기되,
이번 WP의 검증기는 "receipt ledger 부재 → fail-closed".

## 상태 이름

```text
WP-STATE-INTEGRITY 통과            → TRUSTED_BASELINE + NORMAL_TRANSITION_READY
WP-HUMAN-DECISION-PROVENANCE 통과   → VERIFIED_HUMAN_APPROVAL_READY → RECOVERY/PHASE_CHANGE/REPLAY_READY
WP-STATE-LAUNCH-GATE 통과           → AUTOMATED_EXECUTION_READY
```

`BOOTSTRAP_TRUST_ORIGIN`(사람이 손으로 놓는 신뢰 바닥, 1회)은 `VERIFIED_HUMAN_APPROVAL`(서버 receipt)과
구분한다. 부트스트랩은 상태 변경 승인권이 아니라 **신뢰 epoch 생성권**이다(TRUST-ORIGIN-BOOTSTRAP 참조).

## 운영 강제 (후속 WP 완료 전까지)

```text
자동 launcher 실행 금지 · 수동 dispatch/outbox만 허용        (WP-STATE-LAUNCH-GATE 전)
high-risk 전이 fail-closed                                (WP-HUMAN-DECISION-PROVENANCE 전)
```

## 왜 한 WP인가 (분리 land 금지)

06C-1이 전이 **인터페이스**를 바꾼다(결정적 candidate + prepare/apply + v2 log + transitionKind). 네 조각과
모든 호출자가 함께 정렬돼야 한다.

```text
05H  내부 ReconciliationChecker + v2 log 계약 + malformed/blocker (codex)
06C-1 결정적 candidate + prepare/apply + reconciliation-먼저 + 재계산-write + rollback + high-risk fail-closed + 호출자 정렬 (sonnet)
06C-2 trust-origin declare 부트스트랩 command (sonnet, 06C-1과 같은 branch)
06H  RECOVERY.md를 새 계약으로 갱신 + 사고 fixture manifest (codex)
```

- 05H만 먼저 → StateApplier가 내부 checker를 안 부르므로 pending 계약 불성립.
- 06C-1만 먼저 → 내부 checker/v2 계약 부재.
- 06H만 먼저 → 존재하지 않는 prepare/apply를 안내.
- **06C-1/06C-2는 지시서만 분할, land는 하나다**(같은 actor·같은 integration branch·단일 land gate).

→ 통합 branch에서 셋 + 호출자 정렬 → 통합 fixture → clean replay/부트스트랩 → **단일 land gate.**

## 재사용 (재발명 금지 — 실측)

- `ProjectionCli.WriteAtomically`(tmp→`File.Move`)가 이미 있다. 06C-1의 atomic replace는 이 패턴 재사용.
- `ProjectionCli.StampHashes`는 실행 시각을 안 쓰고 파일 내용 해시만 → projection은 이미 결정적.
  비결정성은 오직 `BuildCandidate`의 `updatedAt`/`appliedAt`=`UtcNow` 한 곳 → `effectiveAt`로 그것만 고침.
- `HandoffIntegrityChecker`(내부 checker)는 `HarnessRegistry` **미등록** — pending 우회 방지 위해 CLI 미노출,
  StateApplier in-process 호출만. `handoff-integrity` CLI는 기존 Registry 등록 유지.
- `trust-origin declare`는 검증 하네스가 아니라 제한적 write command라 **일반 CLI router에 명시 배선**
  (Registry 대상 아님). 배선이 명시적이므로 launch-check식 죽음 없음.

## 실행 주체

| 조각 | actor | 대상 |
| --- | --- | --- |
| 05H | HARNESS_EXECUTOR (codex) | `server/Harness/HandoffIntegrityCli.cs`, 내부 checker, fixtures |
| 06C-1 | CORE_INFRA_EXECUTOR (sonnet) | `server/StateApplierCli.cs`, 활성 호출자 정렬 |
| 06C-2 | CORE_INFRA_EXECUTOR (sonnet) | `trust-origin declare` command, CLI router 배선 |
| 06H | HARNESS_EXECUTOR (codex) | `docs/handoff/RECOVERY.md`, fixture manifest |

## 통합 Land Gate (통합 branch에서 전부 통과해야 land)

```text
1.  dotnet build server -c Release                                  → exit 0
2.  일반 전이 prepare → apply 왕복 (transitionKind=NORMAL)           → exit 0   ★Step 10 순서 해소
3.  reconciliation (at rest, 현재 repo, pending 없음)               → exit 0   STATE_HISTORY_CONSISTENT
4.  손 위조 idempotent 공격: state에 가짜 id만, log엔 없음 → 전이      → exit 1   (reconciliation이 idempotency보다 먼저) ★핵심
5.  같은 id·다른 request로 재적용                                    → exit 1   transition-id-collision
6.  fixture A(mid-incident)                                        → exit 1   log-transition-missing-from-state
7.  fixture B/D/E → exit 1 ;  fixture C(불변 스냅샷) → exit 0 ;  malformed → exit 2
8.  prepare 후 candidate 1바이트 변조 → apply                       → 거부(재계산 bytes로만 write) ★TOCTOU 차단
9.  적용 후 실패 주입(결정적 test seam) → preimage 복원              → 복원 hash==preimage, state 불변 ; FATAL 4분기 도달 ★
10. high-risk 전이(RECOVERY/PHASE_CHANGE/REPLAY)                    → exit 1   trusted-human-receipt-required
11. measure dev-pack                                              → exit 0
12. clean replay 판정: 아래 A 또는 B
       A. 모든 request/effectiveAt/order 존재 → exact replay → 최종 hash 일치 → LEGACY_REPLAY_VERIFIED
       B. (역사 재생 불가 시) trust-origin declare 부트스트랩 의식 → BOOTSTRAP_TRUST_ORIGIN
          (10개 선행조건 충족, WORKSTATE/log/commit hash 고정, high-risk fail-closed 확인)
```

2·4·8·9가 이번 수정의 핵심 증명이다. 12는 과거 이력이 exact replay 불가여도 WP가 영구히 안 닫히지 않도록
A/B 분기를 둔다(B는 1회성 부트스트랩). 11(measure)과 12(replay/부트스트랩)는 독립 게이트다.

## Land 후

Phase B의 14-A 또는 14-B 성립 → `TRUSTED_BASELINE + NORMAL_TRANSITION_READY` 선언(사람).
high-risk ready 플래그(VERIFIED_HUMAN_APPROVAL/RECOVERY/PHASE_CHANGE/REPLAY/AUTOMATED_EXECUTION)는 전부 false.
