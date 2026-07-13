# 06C-1 — StateTransition v2 core: reconciliation-먼저 + 결정적 candidate + rollback + high-risk fail-closed (sonnet)

- actor: **CORE_INFRA_EXECUTOR (sonnet)** · WP-STATE-INTEGRITY · 단일 land gate
- 대상: `server/StateApplierCli.cs`, `state-transition` 모든 활성 호출자 정렬
- 선행: 05H가 `HandoffIntegrityChecker.Run(ReconciliationOptions)` + `lookupSuccess`(transitionContract 포함) 제공.
- 짝: 06C-2(trust-origin bootstrap)와 **같은 actor·같은 integration branch·하나의 land gate**. 지시서 분할 ≠ land 분할.

---

## 0. 실측된 결함 4개

```csharp
// Step 1 sha256 → Step 2 IsAlreadyApplied면 즉시 ReportIdempotent(exit 0) → 그 다음 ApplyAndVerify
```

- 결함 1(순서): idempotency가 pre-apply reconciliation보다 먼저 → 손 위조 가짜 id가 idempotent 성공.
- 결함 2(롤백 없음): Step 9/10 실패 시 `File.Move`된 WORKSTATE 원복 없음.
- 결함 3(비결정성): `BuildCandidate`가 `updatedAt`/`appliedAt`에 `UtcNow` 직접 호출.
- 결함 4(ID 결속 없음): id만 같으면 다른 request라도 idempotent 성공.

## 1. 실행 순서 — pre-state hash는 "미적용 ID일 때만" (★ 정정)

> pre-state hash를 idempotency보다 먼저 검사하면 정상 재시도가 죽는다: 최초 전이 성공 후 현재 state는
> 이미 P1(=expectedPost)이라, 같은 envelope 재적용 시 `expectedPre=P0 ≠ 현재=P1`로 sha256 거부되어
> idempotent 판정에 도달하지 못한다. 따라서 순서는:

```text
1. envelope/request 파싱
2. 현재 state·log 로드
3. reconciliation (내부 checker, pending 없음)
     FAIL → 일반 전이 거부 exit 1 state-corrupted-preapply  (가짜 id 공격은 여기서 막힘)
4. existing-transition 판정 (§2)
     - transitionId가 이미 state+성공로그에 있음 → §2로 idempotent/collision 결정 (여기서 종료)
     - state에만 있고 성공로그 없음 → 이미 3의 reconciliation이 FAIL
     - 미적용 ID → 5로
5. (미적용 ID에 한해) 현재 state hash == envelope.expectedPreStateSha256   아니면 거부(안 씀)
6. request/candidate 검증 · 적용 (§4)
```

reconciliation이 가짜 id를 막고, pre-state 검사는 미적용 ID에만 적용되어 재시도가 산다.

## 2. idempotency를 계약 hash에 결속 (결함 4 · v1 fail-closed)

05H가 각 transitionId에 대해 `lookupSuccess(id)`를 반환한다:
`{ exists, schemaVersion, transitionContractSha256? }`.

envelope로부터 같은 방식으로 계약 hash를 계산한다:

```text
transitionContractSha256 = sha256(canonical{
  transitionId, transitionKind, requestSha256, preStateSha256(=expectedPre),
  postStateSha256(=expectedPost), effectiveAt })
```

판정:

```text
기존 ID + v2 로그 + contract hash 일치   → idempotent exit 0
기존 ID + v2 로그 + contract hash 불일치 → exit 1  transition-id-collision
기존 ID + v1 로그(내용 결속 불가)        → exit 1  legacy-idempotency-unverifiable
```

과거 ID의 재적용을 자동 성공으로 인정하지 않는다.

## 3. 결정적 BuildCandidate (결함 3)

```text
Candidate = BuildCandidate(preState, request, transitionId, effectiveAt)   // 순수 함수
  updatedAt ← effectiveAt(날짜) · appliedTransitions[].appliedAt ← effectiveAt · UtcNow 호출 없음
```

직렬화 계약 고정: UTF-8 no BOM · 고정 `JsonSerializerOptions`(`ProjectionCli.WriteOptions` 규약) ·
고정 들여쓰기 · 고정 newline · effectiveAt은 UTC RFC3339 · hash는 **실제로 쓰는 정확한 bytes**에 계산.
동일 입력 → 동일 bytes → 동일 SHA-256.

effectiveAt 발급(외부 조작 금지): **NORMAL은 prepare가 현재 UTC를 직접 발급, 외부 `--effective-at` 금지.**
REPLAY/RECOVERY는 이번 WP fail-closed(§6).

## 4. envelope + prepare/apply + 재계산-write (결함 2 · TOCTOU)

envelope:

```json
{ "schemaVersion":1, "transitionKind":"NORMAL",
  "transitionId":"…", "expectedPreStateSha256":"…",
  "requestPath":"…", "requestSha256":"…", "effectiveAt":"…",
  "expectedPostStateSha256":"…", "candidatePath":"outputs/state-transition/…candidate.json" }
```

- **prepare**(canonical 미수정): hash 계산 → effectiveAt 발급(NORMAL) → candidate 계산 →
  expectedPostStateSha256 = sha256(직렬화) → envelope+candidate를 `outputs/state-transition/`에 기록.
- **apply**(canonical 수정):

```text
1. prepared candidate 파일 hash 확인 (evidence 무결성)
2. 현재 preState + request + envelope로 candidate를 메모리에서 재계산
3. 재계산 bytes hash == envelope.expectedPostStateSha256   아니면 거부(안 씀)
4. §1 순서: reconciliation → existing-transition → (미적용 시) pre-state hash → 검증
5. preimage = ReadAllBytes(WORKSTATE)
6. 재계산 bytes를 tmp에 쓰고 ProjectionCli.WriteAtomically 패턴으로 atomic replace
   ★ prepared candidate 파일은 evidence일 뿐 canonical write source 아님 (TOCTOU 차단)
7. 적용후: ProjectionCli.Run(["projection"]) · 내부 checker(PendingTransitionId=id) · v2 log append
```

## 5. rollback + FATAL taxonomy (결함 2)

```text
성공                                                          → exit 0
적용후 검사 실패 + 복원 성공 + 복원 hash==preimage + projection 성공  → exit 1 / ROLLED_BACK
preimage 복원 실패  또는  복원 hash != preimage                 → exit 2 / FATAL_STATE_UNKNOWN (모든 자동작업 중단)
복원 성공 + projection 재생성 실패                              → exit 2 / STATE_RESTORED_PROJECTION_NOT_VERIFIED (HUMAN-INBOX)
log append 성공 여부 불명                                       → exit 2 / AUDIT_LOG_STATE_UNKNOWN (다음 전이 금지)
```

복원 직후 `sha256(WORKSTATE)==sha256(preimage)` 재검증 필수. v2 ok 로그는 성공 경로에서만 append.
**실패 주입은 결정적 test seam으로**(production 노출 플래그 금지) — 테스트 전용 DI/fake로 "atomic replace 직후 실패".

## 6. transitionKind + high-risk fail-closed

envelope의 `transitionKind` ∈ {NORMAL, PHASE_CHANGE, RECOVERY, REPLAY}.

```text
NORMAL → §1~5 그대로.
PHASE_CHANGE / RECOVERY / REPLAY → apply는 --human-receipt-id 요구, 신뢰 receipt ledger 조회·검증.
    이번 WP는 ledger 부재 → 항상 exit 1  trusted-human-receipt-required
```

임의 경로 `--human-decision some-file.json`은 폐기. receipt 검증 계약의 골격만 남기고 활성화는
`WP-HUMAN-DECISION-PROVENANCE`로. 보고서에서 `CLAIMED_ACTOR`와 `VERIFIED_HUMAN_APPROVAL`을 섞지 않는다.

## 7. 활성 호출자 정렬 + callsite gate

`state-transition` 단일-샷을 쓰던 **활성** 경로를 prepare/apply로 정렬. 단, 옛 옵션은 기존 RECOVERY.md·
최초 StateApplier 지시서·실행 로그 등 **역사적 증거**에도 남아 있어 grep 결과를 전부 수정 대상으로 보면 안 된다.
신규 검사 `state-transition-callsite-check`:

```text
범위: server/**/*.cs · scripts/** · outputs/launch/** · .claude/** · *.ps1 *.sh *.cmd *.bat ·
      운영 docs/prompts·templates · CI·manifest·JSON/YAML
판정: 활성 경로의 옛 단일-shot 호출 → 0 ; history/log/incident fixture의 옛 호출 → 명시 allowlist
evidence: { "legacyCallsiteCount":0, "historicalReferenceCount":N, "classifiedPaths":[…] }
```

di-completion-check는 `state-transition`을 직접 호출하지 않으므로 영향 없음. RECOVERY.md 수동 절차는 06H가 갱신.

## 8. 완료 기준 (exit code)

```text
1. dotnet build server -c Release                                  → 0
2. NORMAL prepare→apply 왕복                                        → 0   ★결함1
3. 같은 envelope 재적용 (최초 성공 후)                              → 0 idempotent  ★재시도 생존(순서 정정)
4. 손 위조: state에 가짜 id만, log엔 없음 → 전이                     → 1 state-corrupted-preapply  ★
5. 같은 id·다른 request(v2)                                        → 1 transition-id-collision  ★결함4
6. 기존 id가 v1 로그                                               → 1 legacy-idempotency-unverifiable
7. prepare 후 candidate 1바이트 변조 → apply                        → 거부(재계산 bytes로만 write) ★TOCTOU
8. test seam 적용후 실패 → 복원                                     → 1 ROLLED_BACK, WORKSTATE hash==preimage ★결함2
9. FATAL 4분기 각각 도달
10. transitionKind ∈ {PHASE_CHANGE,RECOVERY,REPLAY}                → 1 trusted-human-receipt-required
11. state-transition-callsite-check                               → legacyCallsiteCount 0
12. measure dev-pack                                              → 0
```

## 9. 보고 / 스킬

actor(sonnet)·명령과 exit·참조 스킬·`## 지표는 만족했으나 목적은 미달인 부분`(ADR-005).
