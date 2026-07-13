# 06H — RECOVERY.md 갱신 + 사고 fixture manifest (codex)

- actor: **HARNESS_EXECUTOR (codex)** · WP-STATE-INTEGRITY · 단일 land gate
- 대상/allowlist: `docs/handoff/RECOVERY.md`(갱신), `docs/qa/fixtures/reconciliation/…`, `docs/qa/`
- 선행: 06C-1이 prepare/apply·transitionKind·fail-closed·05H failure code를 확정.
- GATE-MANIFEST 무수정(canonical). PRE-LAUNCH 없음.

---

## 0. 원칙

기존 `docs/handoff/RECOVERY.md`를 **갱신**한다(새 파일 금지). 기존 손 Write 금지·사고 근거(커밋 `302b5c3`)·
HUMAN-INBOX 절 보존.

## 1. RECOVERY.md — 두 시기로 나눈다 (★ 이번 WP는 RECOVERY fail-closed)

옛 §3 재적용 절차는 옛 단일-샷 CLI를 안내한다. 06C-1이 인터페이스를 바꾸고, RECOVERY는 이번 WP에서 항상
차단되므로, **현재 시기 / provenance 이후 시기**로 나눠 쓴다. "일반 NORMAL 정정"은 reconciliation이 깨진
사고의 복구 수단이 **아니다**(그건 RECOVERY다).

```text
[일반 상태 정정 — NORMAL] (현재도 가능)
1) state-transition prepare --transition-id <ID> --request <request.json>   (canonical 미수정)
2) 사람이 envelope(pre/post/request hash, effectiveAt, kind=NORMAL) 확인
3) state-transition apply --envelope <envelope.json>
   → 재계산 bytes hash == expectedPostStateSha256 일치 시에만 canonical 수정. 손 Write 금지.

[상태 손상 복구 — RECOVERY] (시기에 따라 다름)

  ● 현재 — WP-HUMAN-DECISION-PROVENANCE 이전:
    - in-place RECOVERY 불가 (trusted human-approval receipt 없음 → apply가 항상
      exit 1 trusted-human-receipt-required)
    - 모든 자동 작업 중단 · 오염 state를 quarantine
    - 신뢰 snapshot(마지막 TRUSTED_BASELINE / trust-origin) 전체 복원, 또는 HUMAN-INBOX
    - L1 fast-path 및 L1~L4 in-place 정책은 비활성 (receipt 없이는 StateApplier RECOVERY 전이가 없다)

  ● provenance 이후 — receipt 발행 인프라 완성 후:
    - receipt를 가진 StateApplier RECOVERY 전이만 허용
    - 아래 L1~L4 정책 활성화
```

sha256 계산 스니펫은 검증용으로 유지.

## 2. L1~L4 복구 분류 (provenance 이후에만 활성 · 05H code · 06C-1 outcome 결속)

> 아래 표는 **provenance 완료 후** RECOVERY가 가능해진 시기의 정책이다. 현재 시기에는 §1의
> quarantine/전체 복원/HUMAN-INBOX만 쓴다.

| 원인 | 레벨 | 처리 |
| --- | --- | --- |
| `log-transition-missing-from-state` | 기본 **L2** | quarantine → clean worktree replay. L1 fast-path는 아래 3조건 전부 |
| `state-transition-not-logged` | **L2+** | 손 Write 의심 → clean replay |
| `transition-id-collision` | **L3** | 같은 id·다른 내용 → 자동 복구 금지 → HUMAN-INBOX |
| `duplicate-success-log-conflict` | **L3** | 성공 로그 binding 충돌 → HUMAN-INBOX |
| `duplicate-in-state` / hash 충돌 | **L3** | 자동 복구 금지 → HUMAN-INBOX |
| `legacy-idempotency-unverifiable` | **L3** | v1 ID 재적용 불가 판정 → 사람 확인 |
| `duplicate-success-in-log` | **WARNING** | 진행 허용(FAIL 동반 시 그 레벨) |
| 순서·request 불명 | **L4** | 마지막 신뢰 지점으로 hard rollback → DI 재발행 |
| `ROLLED_BACK` | L1~L2 | 06C-1이 원상복구 완료. 원인 조사 후 재발행 |
| `FATAL_STATE_UNKNOWN` | **최상위** | 모든 자동작업 중단, 사람 개입 전 어떤 전이도 금지 |
| `STATE_RESTORED_PROJECTION_NOT_VERIFIED` | **L3** | state 복구, projection 미검증 → HUMAN-INBOX |
| `AUDIT_LOG_STATE_UNKNOWN` | **L3** | 다음 전이 금지 → 로그 확인 후 사람 판단 |

L1 fast-path(provenance 이후·셋 다): ①state 중복 없음 ②누락 전이의 원본 request+expectedPre+expectedPost
존재 ③멱등 재적용으로 expectedPostStateSha256 정확히 재현. 하나라도 불확실 → L2. 복구는 오직 receipt를 가진
StateApplier RECOVERY 전이로.

## 3. 사고 fixture — 전용 manifest까지 (실행 계약 완결)

`di-completion-check`는 manifest의 check args를 그대로 실행한다. canonical POST-COMMIT manifest는
handoff-integrity에 fixture 경로를 안 넘기므로, **fixture 전용 manifest**를 만든다.

`docs/qa/fixtures/reconciliation/A/GATE-MANIFEST.json`:

```json
{ "schemaVersion":1, "gates":[{ "gateId":"POST-COMMIT", "checks":[
  { "order":1, "command":"handoff-integrity",
    "args":["--workstate","docs/qa/fixtures/reconciliation/A/WORKSTATE.json",
            "--applier-log","docs/qa/fixtures/reconciliation/A/WORKSTATE.applier-log.jsonl"],
    "expectedExit":0, "mutatesState":false } ]}]}
```

fixture A는 실제 exit 1 → expectedExit 0과 불일치 → `di-completion-check` 자체가 exit 1. 명령:

```text
di-completion-check --gate POST-COMMIT \
  --manifest docs/qa/fixtures/reconciliation/A/GATE-MANIFEST.json --task recon-postcommit-A   → exit 1
```

이 하네스가 없던 시점엔 POST-COMMIT이 7/7 PASS였다(커밋 `302b5c3`). 그 차이가 사고 차단의 증거다.

## 4. 완료 기준 (exit code)

```text
1. RECOVERY.md가 두 시기(현재 fail-closed / provenance 이후)로 갱신 (옛 단일-샷 제거, 기존 금지·근거 보존)
2. 현재 시기 절차(in-place 불가·quarantine·전체 복원·L1 비활성)와 provenance 이후 L1~L4 표가 분리되어 명시
   (transition-id-collision·duplicate-success-log-conflict·legacy-idempotency-unverifiable·FATAL 4종 포함)
3. fixture A + 전용 manifest가 docs/qa/fixtures/reconciliation/A/ 에 존재
4. di-completion-check --gate POST-COMMIT --manifest <A manifest>          → exit 1
5. dotnet run --project server -- doc-integrity                            → 0
6. measure dev-pack                                                       → 0
```

## 5. 보고 / 스킬

actor(codex)·명령과 exit·참조 스킬·`## 지표는 만족했으나 목적은 미달인 부분`(ADR-005).
스킬: `skills/common/hs-gate.md`, `skills/common/root-cause-diagnosis.md`, `skills/domains/docs/README.md`.
