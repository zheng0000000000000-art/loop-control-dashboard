# 06C-1-R1 검증 — StateApplierCli legacy 단일-샷 경로 삭제 + 6결함 수정

## DI 유형 ※필수 (v9 §0.1)

- **선언한 유형**: implementation

## 주체 (actor) ※필수
- **누가**: sonnet(claude-sonnet-4-6), 세션 34648174-55d4-437b-9cad-fe8ea82d9a5f
- **경로**: 대화 세션

## 사용한 하네스 ※필수
| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| build-verify | `dotnet run --project server -c Release -- build-verify` | 0 | 0 | CS0649 경고 1건(FailAfterWriteHook — 의도된 것) |
| build Debug | `dotnet build server -c Debug` | 0 | 0 | 경고 0 |
| measure | `dotnet run --project server -c Release -- measure dev-pack` | 0 | 0 | violations=0 |
| state-transition apply | `dotnet exec server.dll state-transition apply --envelope ...` | 0 | 0 | 픽스처 기반 |
| state-transition-callsite-check | `dotnet run --project server -c Release -- state-transition-callsite-check` | 0 | 0 | legacyCallsiteCount=0, historicalReferenceCount=2, scannedActiveFiles=329 |
| handoff-integrity | `dotnet run --project server -c Release -- handoff-integrity` | 1 | 1 | failures=1 (DI0004-BLOCKED-CODEX 설계된 참양성) |

## 유형별 필수 검증 ※필수 (v9 §0.1)

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| implementation | 정상·실패·재실행 테스트, 개발 검증 Evidence | 판정선 16개 중 15개 실측, 1개 NOT_VERIFIED(#9) |

## 공통 완료 조건 ※필수 (v9 §0.1)

- [x] 선언한 DI 유형의 완료 프로필 충족
- [x] 관련 계약·스키마·문서 갱신 (`docs/verification/06c1-r1-legacy-removal.md` 신규)
- [x] 발견된 실패·위험·미확정 사항 기록 (RECOVERY.md 파손, #9 NOT_VERIFIED)
- [ ] `WORKSTATE.json` 갱신 — 이 작업은 통합 브랜치 land gate의 일부이며, WORKSTATE 전이는 gate 통과 후 사람이 한다
- [x] 변경 범위 준수(allowlist) · 파일 claim 규칙 준수
- [x] 원본 저장소 무단 변경 없음(commit/push/결재/반입/발사 미수행)

## 실패 분류와 실패 사례 ※필수 (v9 §0.3)

- **실패 분류**: expected_rejection (반증 시험 #5, #6, #7, #8, #11, #12, #13이 의도대로 거부됨)
- **실패 사례 ID**: 신규 실패 사례 없음

## 참조한 스킬 ※필수
- `skills/common/` (CLAUDE.md 규칙)

## 변경 내용
| 파일 | 종류(신규/수정) | 요약 |
| --- | --- | --- |
| `server/StateApplierCli.cs` | 수정 | legacy 단일-샷 경로 전체 삭제 + 결함 5건 수정 (상세 아래) |
| `server/Harness/StateTransitionCallsiteCheckCli.cs` | 수정 | 스캔 범위 수정: outputs/launch/ 활성 복구, .md 추가, .github/ 추가 |
| `docs/verification/06c1-r1-legacy-removal.md` | 신규 | 본 문서 |

### 삭제된 legacy 코드 범위 (StateApplierCli.cs)

| 삭제 대상 | 종류 | 비고 |
| --- | --- | --- |
| `LegacyOptions` record | 레코드 타입 | `--transition-id / --expected-workstate-sha256 / --request / --human-decision` 파싱용 |
| `RunLegacy()` | 메서드 | legacy 진입점, `--human-decision` 경로 포함 |
| `ApplyLegacy()` | 메서드 | legacy 단일-샷 apply, rollback 없음 |
| `ApplyLegacyDryRun()` | 메서드 | legacy dry-run 경로 |
| `RunLegacyPostApply()` | 메서드 | legacy post-apply 검사 |
| `IsAlreadyAppliedWithLog()` | 메서드 | v1 log 기반 멱등 판정(contract binding 불가) |
| `ValidateHumanDecision()` | 메서드 | ★ AI가 임의 JSON을 써서 자기 승인 위조 가능했던 구멍 |
| `ValidateRequest()` | 메서드 | legacy request 검증 |
| `ValidateCandidate()` | 메서드 | legacy candidate 검증 |
| `LegacyPostApplyError()` | 메서드 | legacy 오류 출력 |
| `ParseLegacyArgs()` | 메서드 | legacy 인자 파싱 |
| `Root` 필드 (PrepareOptions, ApplyOptions) | 레코드 필드 | `--root` 플래그 지원용, `canonicalMode` 분기의 원인 |

### 추가된/수정된 코드 (StateApplierCli.cs)

| 항목 | 변경 내용 |
| --- | --- |
| `ValidTransitionKinds` | 허용 종류 명시적 집합: NORMAL, PHASE_CHANGE, RECOVERY, REPLAY |
| `FailAfterWriteHook` | `internal static Func<string?>?` — in-process seam. 환경변수·CLI에서 켤 수 없음 |
| `CheckExistingTransition()` | contract hash를 envelope 필드로부터 재계산 → log hash 비교(collision), 그 뒤 self-reported 비교(mismatch) |
| `LoadWorkstateContext()` | RunApply() 81줄 위반 해소용 헬퍼 |
| `Run()` | prepare/apply 외 인자 조합 → exit 2 usage |
| `RunApply()` | unknown kind fail-closed 먼저, canonicalMode 제거, projection 항상 실행 |
| `RunApplyCommitPhase()` | 환경변수 읽기 제거, FailAfterWriteHook in-process 훅으로 교체 |
| `Rollback()` | canonicalMode 제거, projection 항상 실행 |

## 반증 시험 (negative test) ※필수

### ★ 완료 기준 5·6·10·13 — 뚫렸다 → 막았다

**테스트 #5: transition-id-collision**

뚫렸다(06C-1 구식 코드 동작):
- `envelope.transitionContractSha256`을 log의 hash와 직접 비교. 공격: 성공 로그에서 contractHash를 복사해 다른 effectiveAt을 가진 동일 transitionId 재요청에 심으면, 서로 다른 내용의 요청이 `idempotent exit 0`으로 통과됨.

막았다(현재 코드):
```
envelope(T-TEST-001, effectiveAt="2099-01-01T00:00:00Z", contractSha=<original>) 적용 시도
→ Exit: 1
STDERR: {"ok":false,"transitionId":"T-TEST-001","reason":"transition-id-collision",
  "detail":"id 'T-TEST-001'는 다른 contract hash로 이미 적용됨"}
```
필드에서 재계산: sha256(effectiveAt=2099...|postHash|preHash|reqHash|T-TEST-001|NORMAL) ≠ log에 저장된 hash → collision

---

**테스트 #6: envelope-contract-mismatch**

뚫렸다(06C-1 구식 코드 동작):
- 동일 fields, self-reported hash를 `"aaaa..."` 로 위조. 구식 코드는 self-reported를 log와 비교만 했으므로 envelope 내부 self-consistency 검사가 없었음. 새 전이(state 미포함)에서 위조된 hash가 통과할 수 있는 경로 존재.

막았다(현재 코드):
```
envelope(T-TEST-001, 원본 fields, transitionContractSha256="aaaa...") 적용 시도
→ Exit: 1
STDERR: {"ok":false,"transitionId":"T-TEST-001","reason":"envelope-contract-mismatch",
  "detail":"envelope.transitionContractSha256이 계산값과 다릅니다 — 위조 또는 수정됨"}
```
computed hash from fields == log hash (collision 없음) → 이후 self-reported `"aaaa..."` ≠ computed → mismatch

---

**테스트 #10: 환경변수 무효 확인**

뚫렸다(06C-1 구식 코드 동작):
- `_ST_SEAM_FAIL_AFTER_WRITE=1` 환경변수를 읽어 atomic write 직후 강제 rollback을 일으켰음. 환경변수는 자식 프로세스로 상속되므로 production 진입점(환경변수)에서 실패를 주입할 수 있었음.

막았다(현재 코드):
```
_ST_SEAM_FAIL_AFTER_WRITE=1 설정 후 정상 apply 실행
→ Exit: 0
STDOUT: {"ok":true,"transitionId":"T-D01","previousStatus":"in_progress","newStatus":"verifying",...}
```
코드가 환경변수를 읽지 않음. `FailAfterWriteHook`은 `internal static`이며 외부(CLI·환경변수·설정파일)에서 접근 불가.

---

**테스트 #13: callsite-check 범위 확인**

뚫렸다(06C-1 구식 코드 동작):
- `outputs/launch/`를 historical allowlist에 포함시켜 활성 발사 프롬프트를 스캔하지 않았음 → legacy 호출이 있어도 `legacyCallsiteCount=0`으로 거짓 통과.

막았다(현재 코드):
```
outputs/launch/test-legacy-call.ps1 생성 (내용: "state-transition --transition-id TEST") 후:
→ Exit: 1, legacyCallsiteCount=1
파일 삭제 후:
→ Exit: 0, legacyCallsiteCount=0, scannedActiveFiles=329
```

---

### 전체 판정선

| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 1 | build -c Release | 0 | 0 | PASS |
| 2 | legacy 인자 (--transition-id) | 2 | 2 | PASS |
| 2 | legacy 인자 (--human-decision) | 2 | 2 | PASS |
| 3 | NORMAL prepare→apply 왕복 (픽스처, cwd=픽스처) | 0 | 0 | PASS |
| 4 | 같은 envelope 재적용 (idempotent) | 0 | 0 | PASS |
| 5 ★ | 다른 effectiveAt으로 같은 transitionId 재요청 | 1 | 1 | PASS (transition-id-collision) |
| 6 ★ | envelope.transitionContractSha256 위조 | 1 | 1 | PASS (envelope-contract-mismatch) |
| 7 | state에 가짜 id, log 없음 → reconciliation 차단 | 1 | 1 | PASS (state-corrupted-preapply) |
| 8 | candidate 1바이트 변조 | 1 | 1 | PASS (candidate-tampered) |
| 9 | in-process 훅으로 atomic write 직후 실패 주입 | 1 | — | NOT_VERIFIED |
| 10 ★ | 환경변수 _ST_SEAM_FAIL_AFTER_WRITE=1 설정 후 정상 apply | 0 | 0 | PASS (환경변수 무효) |
| 11 | transitionKind="EVIL" | 1 | 1 | PASS (unknown-transition-kind) |
| 12 | transitionKind="PHASE_CHANGE" | 1 | 1 | PASS (trusted-human-receipt-required) |
| 13 ★ | outputs/launch/ 에 legacy 호출 심고 callsite-check | 1 | 1 | PASS (legacyCallsiteCount=1) |
| 14 | 심은 파일 제거 후 callsite-check | 0 | 0 | PASS |
| 15 | at-rest handoff-integrity | 1 | 1 | PASS (failures=1 설계된 참양성) |
| 16 | measure dev-pack (-c Release) | 0 | 0 | PASS |

#### 테스트 #9 NOT_VERIFIED 사유

`FailAfterWriteHook`은 `internal static Func<string?>?`이며, 같은 프로세스의 자기 시험 코드만 설정할 수 있다. `dotnet exec`는 매번 새 프로세스를 생성하므로 외부에서 훅을 설정할 수 없다. `state-transition self-test` 부속 명령을 추가하면 검증 가능하지만, 이번 지시서에 해당 명령이 없으므로 NOT_VERIFIED로 남긴다.

## 검수 기준 자가점검표
| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | legacy 단일-샷 경로 완전 삭제 | PASS | Run(), 인자 파싱 모두 삭제됨 — 테스트 #2 실증 |
| 2 | contract hash 재계산 (envelope 자기신고 불신) | PASS | ComputeContractHash() 필드 재계산 — 테스트 #5, #6 실증 |
| 3 | test seam 환경변수 제거 | PASS | FailAfterWriteHook in-process 전용 — 테스트 #10 실증 |
| 4 | --root / canonicalMode 제거 | PASS | PrepareOptions·ApplyOptions에 Root 없음, projection 항상 실행 |
| 5 | unknown transitionKind fail-closed | PASS | ValidTransitionKinds 집합 — 테스트 #11 실증 |
| 6 | callsite-check 범위 수정 | PASS | outputs/launch/ 활성화, .md 추가 — 테스트 #13, #14 실증 |
| 7 | RECOVERY.md 무접촉 | PASS | 파일 수정 없음 (파손 사실만 보고) |

## RECOVERY.md 파손 보고 (06H가 수정해야 한다)

**파손 위치**: `docs/handoff/RECOVERY.md` §3 "누락 전이 재적용" (줄 49–55)

**파손 내용**: 아래 명령 형식이 더 이상 존재하지 않음.

```bash
dotnet run --project server -c Release -- state-transition \
  --transition-id <누락된-ID> \
  --expected-workstate-sha256 <현재-sha256> \
  --request <request-파일.json>
```

**06H가 해야 할 것**:
1. §3 "누락 전이 재적용" 명령을 아래 두 단계로 교체한다:
   ```bash
   # 1단계: prepare (request 파일을 반드시 보존하고 있어야 함)
   dotnet run --project server -c Release -- state-transition prepare \
     --transition-id <누락된-ID> \
     --request <request-파일.json>

   # 2단계: apply
   dotnet run --project server -c Release -- state-transition apply \
     --envelope outputs/state-transition/<누락된-ID>.envelope.json
   ```
2. 주의사항 추가: 복구 시나리오에서 log에 있는 ID가 state에 없으면 reconciliation이 차단함(log-to-state 규칙). 이 경우 자동 복구가 불가능하며 HUMAN-INBOX에 올려야 한다.
3. `--expected-workstate-sha256` 인자에 대한 언급을 모두 제거한다.

## 게이트 기록

`{"gate":"dev-pack","violations":0,"attempt":2}`

- attempt 1: maxFunctionLength=81 위반(RunApply 81줄). LoadWorkstateContext() 헬퍼 추출 후 해소.
- attempt 2: violations=0

## 잔여 위험 · 미확정 사항 ※필수

- **테스트 #9 NOT_VERIFIED**: `FailAfterWriteHook`의 rollback 동작(exit 1 ROLLED_BACK, hash==preimage)은 코드 리뷰로만 확인됨. in-process 시험이 필요하다면 `state-transition self-test` 부속 명령을 06H에서 추가할 수 있다.
- **RECOVERY.md 파손**: §3 "누락 전이 재적용"의 복구 명령이 new API와 맞지 않는다. 06H 수정 필요.
- **복구 시나리오의 reconciliation 차단**: 이전 운영 복구 절차에서 log에 있는 ID가 state에 없으면 자동 재적용이 불가능하다. 운영 복구 전 reconciliation 상태를 먼저 확인해야 한다.

## 직접 경로 사용 사유

`server/StateApplierCli.cs`와 `server/Harness/StateTransitionCallsiteCheckCli.cs`를 직접 수정했다. 지시서(`directive-06C-1-R1-legacy-removal.md`)에 "직접 경로"가 명시되어 있으며, 통합 브랜치 작업이다.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005 — 필수)

- **테스트 #9 NOT_VERIFIED**: `FailAfterWriteHook`이 존재하고 코드상 in-process 전용임을 확인했으나, 실제 rollback-with-hash-check 동작은 외부 실증이 없다. 코드 리뷰로 갈음한 것이 아니라 NOT_VERIFIED로 신고한다.
- **RECOVERY.md**: 지시서에 따라 접촉하지 않았으나, §3의 복구 절차가 깨진 상태로 남아 있다. 다음 실행자(06H)가 고쳐야 목적이 완성된다.
- **그 외 없음**: measure violations=0, 판정선 15/16 실측, legacy 경로 완전 삭제, contract hash 재계산, env var seam 제거, callsite-check 범위 수정 모두 목적 수준에서 달성됨.

## 완료 판정

`PASS | FAIL | BLOCKED` — **생산자가 아니라 검수자가 적는다.**
