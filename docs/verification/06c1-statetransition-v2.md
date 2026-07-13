# DI-00-04 (06C-1) 검증 — StateTransition v2: reconciliation-먼저 + 결정적 candidate + rollback + high-risk fail-closed

## DI 유형 ※필수 (v9 §0.1)

- **선언한 유형**: implementation

## 주체 (actor) ※필수

- **누가**: CORE_INFRA_EXECUTOR (sonnet, claude-sonnet-4-6), 현재 세션
- **경로**: 대화 세션 (wp/state-integrity 브랜치)

## 사용한 하네스 ※필수

| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| build-verify | `dotnet run --project server -c Release -- build-verify` | 0 | 0 | verdict=PASS, warnings=0, errors=0 |
| measure dev-pack | `dotnet run --project server -c Release -- measure dev-pack` | 0 | 0 | violations=0, attempt=2 (attempt 1: maxFunctionLength=223 위반 → 함수 분할 후 재측정) |
| hs-scan | `dotnet run --project server -c Release -- hs-scan` | **1** | 1 | triggered=true, S4 executor-orchestration 6건 |
| gate-clean | `dotnet run --project server -c Release -- gate-clean server` | 1(커밋 전) | 1 | contentDirtyCount=3 (커밋 후 0 예상) |
| state-transition-callsite-check | `dotnet run --project server -c Release -- state-transition-callsite-check` | 0 | 0 | legacyCallsiteCount=0, scannedActiveFiles=151 |
| handoff-integrity (canonical) | `dotnet run --project server -c Release -- handoff-integrity` | 1 (설계된 TP) | 1 | DI0004-BLOCKED-CODEX 1건 (의도된 real positive — 수정 금지) |

## 유형별 필수 검증 ※필수 (v9 §0.1)

**선언 유형: implementation** — 정상·실패·재실행 테스트, 개발 검증 Evidence

### 판정선 §8 결과 (사본 모드 — `--root <tmpdir>`)

| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| §1 | `dotnet build server -c Release` | 0 | 0 | PASS |
| §2 | NORMAL prepare→apply 왕복 (`T-VERIFY-01`, `pendingExemptionUsed`) | 0 | 0 | PASS |
| §3 | 같은 envelope 재적용 (idempotent) | 0 (idempotent) | 0 | PASS |
| §4 | 손 위조: state에 `FORGED-ID-99`, log 없음 → apply | 1 `state-corrupted-preapply` | 1 | PASS |
| §5 | 같은 transitionId·다른 effectiveAt (v2 collision) | 1 `transition-id-collision` | 1 | PASS |
| §6 | 기존 id가 v1 로그 | 1 `legacy-idempotency-unverifiable` | 1 | PASS (이전 세션 검증, 이번 세션 미재실행) |
| §7 | candidate 1바이트 변조 후 apply | 1 `candidate-tampered` | 1 | PASS |
| §8 | `_ST_SEAM_FAIL_AFTER_WRITE=1` → rollback | 1 `ROLLED_BACK`, hash==preimage | 1 | PASS |
| §9 | FATAL 4분기 (FATAL_STATE_UNKNOWN / STATE_RESTORED_PROJECTION_NOT_VERIFIED / AUDIT_LOG_STATE_UNKNOWN) | 각 exit 2 | NOT_VERIFIED | NOT_VERIFIED — OS 수준 파일 잠금 주입 불가, seam으로 검증 불가 |
| §10 | `transitionKind=PHASE_CHANGE` apply | 1 `trusted-human-receipt-required` | 1 | PASS |
| §11 | `state-transition-callsite-check` | 0, `legacyCallsiteCount=0` | 0 | PASS |
| §12 | `measure dev-pack` | 0 | 0 | PASS |

**canonical handoff-integrity (§13)**: exit 1 (설계된 TP — `DI0004-BLOCKED-CODEX: state-transition-not-logged`). 이것이 의도다 — 수정하지 않는다.

**pending exemption PASS path 최초 실증**: §2 apply 결과에 `"pendingExemptionUsed": true` 포함. atomic write 후 log append 전 `HandoffIntegrityChecker.Run(ReconciliationOptions(ws, log, "T-VERIFY-01"))` 호출 → PendingTransitionId 면제 적용.

## 공통 완료 조건 ※필수 (v9 §0.1)

- [x] 선언한 DI 유형(implementation)의 완료 프로필 충족 — §8 판정선 11/12개 PASS, 1개 NOT_VERIFIED(§9 FATAL)
- [x] 관련 계약·스키마·문서 갱신 — v2 log 포맷 구현, envelope 스키마 구현
- [x] 발견된 실패·위험·미확정 사항 기록 — 잔여 위험 절 참조
- [ ] `WORKSTATE.json` 갱신 — **state-transition으로만** — 현재 canonical WORKSTATE는 `blocked` (`DI0004-BLOCKED-CODEX`) 상태. 06C-1 완성 후 NORMAL 전이는 reconciliation이 차단. 의도된 잠금.
- [x] 변경 범위 준수(allowlist) — `server/StateApplierCli.cs`, `server/Harness/StateTransitionCallsiteCheckCli.cs`, `server/Harness/HarnessRegistry.cs`, `outputs/state-transition/**`, `docs/verification/06c1-statetransition-v2.md` 만 수정
- [x] 원본 저장소 무단 변경 없음 — 커밋·push·결재 없음

## 실패 분류와 실패 사례 ※필수 (v9 §0.3)

- **실패 분류**: `expected_rejection` (반증 시험이 의도대로 거부된 것 — §4·§5·§6·§7·§10)
- **실패 사례 ID**: 신규 실패 사례 없음

## 참조한 스킬 ※필수

- `skills/common/` (공통)

## 변경 내용

| 파일 | 종류 | 요약 |
| --- | --- | --- |
| `server/StateApplierCli.cs` | 수정 | 결함 1-4 수정. prepare/apply 분리, reconciliation-먼저 순서, 결정적 candidate, rollback+FATAL taxonomy, v2 log. RunApply() 분할(RunApplyValidatePhase, RunApplyCommitPhase, VerifyApplyEvidence). RunPrepare() 분할(WritePrepareOutput). |
| `server/Harness/StateTransitionCallsiteCheckCli.cs` | 신규 | 레거시 단일-샷 호출 탐지 하네스 |
| `server/Harness/HarnessRegistry.cs` | 수정 | `state-transition-callsite-check` 등록 |
| `outputs/state-transition/fixtures/st-test-a/workstate.json` | 신규 | 사본 모드 테스트용 고정 픽스처 |
| `outputs/state-transition/fixtures/st-test-a/applier-log.jsonl` | 신규 | 빈 log 픽스처 |
| `outputs/state-transition/fixtures/st-test-a/request-normal.json` | 신규 | 정상 요청 픽스처 |

## 반증 시험 (negative test) ※필수

| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| N1 | 손 위조: state에 가짜 id, log 없음 | 1 `state-corrupted-preapply` | 1 | PASS |
| N2 | 같은 id·다른 contract (v2 collision) | 1 `transition-id-collision` | 1 | PASS |
| N3 | candidate 파일 1바이트 변조 | 1 `candidate-tampered` | 1 | PASS |
| N4 | `_ST_SEAM_FAIL_AFTER_WRITE=1` 주입 | 1 `ROLLED_BACK` | 1 | PASS |
| N5 | `transitionKind=PHASE_CHANGE` | 1 `trusted-human-receipt-required` | 1 | PASS |
| N6 | FATAL 4분기 (preimage write fail, projection rollback fail, log append fail) | exit 2 | NOT_VERIFIED | OS 수준 파일 잠금 주입 불가 |

## 검수 기준 자가점검표

| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | reconciliation이 idempotency보다 먼저 실행 | PASS | §4: 가짜 id가 reconciliation에서 막힘 |
| 2 | 같은 envelope 재적용이 exit 0 | PASS | §3: idempotent exit 0 |
| 3 | 결정적 candidate (UtcNow 호출 없음) | PASS | BuildCandidate는 effectiveAt 파라미터만 사용 |
| 4 | TOCTOU 방어: candidate file을 write source로 쓰지 않음 | PASS | RunApplyCommitPhase: recomputedBytes만 atomic write |
| 5 | rollback: preimage 복원 + hash 재검증 | PASS | §8: ROLLED_BACK, hash==preimage 확인 |
| 6 | high-risk kind fail-closed | PASS | §10: PHASE_CHANGE → exit 1 |
| 7 | pending exemption PASS path 실증 | PASS | §2: pendingExemptionUsed: true |
| 8 | legacyCallsiteCount == 0 | PASS | §11: exit 0, count=0 |
| 9 | measure dev-pack violations == 0 | PASS | §12: violations=0 |
| 10 | canonical handoff-integrity exit 1 (설계된 TP) | PASS(의도) | DI0004-BLOCKED-CODEX 1건 유지 |

## 게이트 기록

`{"gate":"dev-pack","violations":0,"attempt":2}`

attempt 1: maxFunctionLength=223 위반 (RunApply 223줄). RunApply를 4개 함수(RunApply/VerifyApplyEvidence/RunApplyValidatePhase/RunApplyCommitPhase)로 분할 후 attempt 2에서 통과.

## 잔여 위험 · 미확정 사항 ※필수

- **§9 FATAL 4분기 NOT_VERIFIED**: `FATAL_STATE_UNKNOWN`(preimage 복원 실패), `STATE_RESTORED_PROJECTION_NOT_VERIFIED`(복원 후 projection 실패), `AUDIT_LOG_STATE_UNKNOWN`(log append 실패) — OS 수준 파일 잠금 또는 I/O 오류 주입 없이 검증 불가. 코드 검토로 경로는 확인함.
- **§6 v1 log 재실행 생략**: 이번 세션에서 v1 log 경로(legacy-idempotency-unverifiable) 재실행 안 함. 코드 변경이 v1 분기(`logInfo.SchemaVersion < 2`)에 직접 영향 없음을 코드 검토로 확인.
- **canonical WORKSTATE 잠금**: 06C-1 완성 후 canonical state에 대한 NORMAL 전이는 reconciliation이 DI0004-BLOCKED-CODEX로 전부 차단. 다음 전이를 위해서는 DI0004-BLOCKED-CODEX를 수동 해소해야 한다.
- **test fixture WP-REGISTRY 의존**: 사본 모드 테스트가 `docs/handoff/WP-REGISTRY.json` 복사에 의존. 이 파일은 픽스처 디렉터리에 포함되지 않아 매번 복사 필요.

## 직접 경로 사용 사유

directive-06C-1 allowlist에 명시된 파일만 수정했다. allowlist 자체가 "직접 경로"를 승인하므로 ADR-003 예외 사유 충족.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005 — 필수)

1. **maxFunctionLength 지표 맞춤**: attempt 1에서 RunApply() 223줄로 위반. 함수를 나눠 80줄 이하로 맞췄다. 분할이 기능적으로 의미있는 단위(evidence 검증 / 검증+dry-run / atomic write+post-check)로 이루어졌으나, 기계적 80줄 제약이 분할 기준이 된 면이 있다.
2. **§9 FATAL 검증 없음**: FATAL_STATE_UNKNOWN 등 exit 2 경로는 코드 검토로만 확인. 실제 OS 장애 주입 시나리오 미검증.
3. **§6 v1 log 이번 세션 미재실행**: 이전 세션 결과에 의존. 코드 분석 상 변경 없음을 확인했으나 직접 실행 증거 없음.

## 완료 판정

`PASS | FAIL | BLOCKED` — **생산자가 아니라 검수자가 적는다.**
