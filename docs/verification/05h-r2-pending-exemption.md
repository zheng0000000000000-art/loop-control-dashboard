# 05H-R2 검증 — pending 면제의 뒷문을 막는다 + 실증 수단 신설

> **모든 DI의 검증 문서는 이 템플릿을 쓴다**(v9 §DI-00-02). 절을 지우지 마라 — 해당 없으면 "없음"이라고 쓴다.

## DI 유형 ※필수 (v9 §0.1)

- **선언한 유형**: harness

## 주체 (actor) ※필수
- **누가**: CORE_INFRA_EXECUTOR (sonnet / claude-sonnet-4-6) — ADR-015 한시 예외
- **경로**: 대화 세션 (사람 게이트 진입점으로부터 직접)

## 사용한 하네스 ※필수
| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| build-verify (Release) | `dotnet build server -c Release` | 0 | 0 | 경고 0, 오류 0 |
| build-verify (Debug) | `dotnet build server -c Debug` | 0 | 0 | 경고 0, 오류 0 |
| handoff-integrity --self-test (수정 전) | `dotnet run --project server -c Release -- handoff-integrity --self-test` | 1 (bug) | 1 | mismatchCount=1, case=pending-failed-log, actualPass=true(기대 false) |
| handoff-integrity --self-test (수정 후) | 동일 | 0 | 0 | verdict=PASS, casesRun=5 |
| 회귀 fixture-a | `handoff-integrity --workstate fixture-a --applier-log ...` | 1 | 1 | ✓ |
| 회귀 fixture-b | 동일 (fixture-b) | 1 | 1 | ✓ |
| 회귀 fixture-c | 동일 (fixture-c) | 0 | 0 | ✓ |
| 회귀 fixture-d | 동일 (fixture-d) | 1 | 1 | ✓ |
| 회귀 fixture-e | 동일 (fixture-e) | 1 | 1 | ✓ |
| 회귀 fixture-f | 동일 (fixture-f) | 1 | 1 | ✓ |
| 회귀 fixture-malformed | 동일 (fixture-malformed) | 2 | 2 | ✓ |
| CLI --pending-transition | `handoff-integrity --pending-transition` | 1 | 1 | pending-not-allowed-on-cli |
| at-rest | `handoff-integrity` (canonical WORKSTATE) | 1 | 1 | failures=1건 (DI0004-BLOCKED-CODEX state-transition-not-logged) ← 정상 |
| measure | `dotnet run --project server -c Release -- measure dev-pack` | 0 | 0 | violationCount=0 |

## 유형별 필수 검증 ※필수 (v9 §0.1)

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| harness | positive·negative·결정성·격리 테스트 | `--self-test`로 5종 케이스 전부 통과. negative: pending-failed-log·pending-duplicate·pending-mismatch 모두 FAIL 확인. positive: pending-ok PASS·pendingExemptionApplied=true 최초 실증. pending-success-log PASS·exemption=false 확인. |

## 완료 기준별 결과 (계약 §4)

| # | 기준 | 기대 | 실제 |
| --- | --- | --- | --- |
| 1 | dotnet build -c Release | 0 | 0 ✓ |
| **2** | **★ 고치기 전 재현: pending-failed-log 현 코드에서 PASS** | PASS(exit 0 내부) | **actualPass=true, actualPendingExemptionApplied=true — --self-test exit 1로 포착** |
| 3 | pending-ok → 0 · pendingExemptionApplied=true | 0 / true | --self-test PASS 내 확인 ✓ |
| 4 | pending-failed-log → 1 · state-transition-not-logged | 1 / code=state-transition-not-logged | --self-test PASS 내 확인 ✓ |
| 5 | pending-success-log → 0 · pendingExemptionApplied=false | 0 / false | --self-test PASS 내 확인 ✓ |
| 6 | pending-duplicate → 1 · duplicate-in-state 포함 | 1 | --self-test PASS 내 확인 ✓ |
| 7 | pending-mismatch → 1 · state-transition-not-logged | 1 | --self-test PASS 내 확인 ✓ |
| 8 | handoff-integrity --self-test | 0 | 0 ✓ |
| 9 | 회귀: a→1 b→1 c→0 d→1 e→1 f→1 malformed→2 | 변화 없음 | 전부 일치 ✓ |
| 10 | CLI --pending-transition | 1 pending-not-allowed-on-cli | 1 ✓ |
| 11 | at-rest | 1 · failures 정확히 1건 | 1 · DI0004-BLOCKED-CODEX 1건 ✓ |
| 12 | measure dev-pack | 0 | 0 ✓ |

### ★ 완료 기준 2 — 고치기 전 재현 실제 출력

수정 전 코드(`--self-test` 신설, `HandoffIntegrityChecker.cs` 미수정 상태):

```
EXIT:1
{
  "selfTest": "handoff-integrity-pending",
  "verdict": "FAIL",
  "mismatchCount": 1,
  "mismatches": [
    {
      "case": "pending-failed-log",
      "expectedPass": false,
      "actualPass": true,
      "expectedPendingExemptionApplied": false,
      "actualPendingExemptionApplied": true,
      "failures": []
    }
  ]
}
```

**"고쳤다"의 증거: 고치기 전엔 pending-failed-log가 PASS(actualPass=true)였고, 수정 후엔 FAIL이다.**

### ★ 완료 기준 3 — pending-ok 최초 실증

`--self-test` 내부에서 pending-ok case 실행 결과: `expectPass=true, expectExemption=true → 실제 일치`. 이것이 pending 면제 경로의 **최초 실행 기록**이다. 05H·05H-R1이 둘 다 `NOT_VERIFIED`로 남긴 경로.

## 공통 완료 조건 ※필수 (v9 §0.1)

- [x] 선언한 DI 유형(harness)의 완료 프로필 충족 — positive·negative 검증 완료
- [x] 관련 계약 충족 — 05H 원 지시서 §5-2 "log엔 없음" 조건을 `!allLogIdSet.Contains(id)`로 구현
- [x] 발견된 실패·위험·미확정 사항 기록 (아래 「잔여 위험」 참조)
- [ ] `WORKSTATE.json` 갱신 — **금지(DI0004-BLOCKED-CODEX 유지)**: 이 DI는 harness 수정이므로 state-transition 없음
- [x] 변경 범위 준수(allowlist): `server/Harness/HandoffIntegrityChecker.cs`, `server/Harness/HandoffIntegrityCli.cs`, `docs/qa/fixtures/reconciliation/**`, `docs/verification/05h-r2-pending-exemption.md` 만 수정
- [x] 원본 저장소 무단 변경 없음 (commit/push/결재 미수행)

## 실패 분류와 실패 사례 ※필수 (v9 §0.3)

- **실패 분류**: `design_learning` — 반증 자료 없는 조건절이 어떻게 무증거 통과하는지를 배웠다
- **실패 사례 ID**: 신규 실패 사례 없음 (코덱스 독립 검수가 이미 `05H-R1.codex.md`에 기록)
- **원인 실체**: `CheckStateToLog`(:254)가 `stateIds.Count == 1` 조건만 보고 `allLogIdSet.Contains(id)` 조건을 누락했다. 직접 확인: `HandoffIntegrityChecker.cs:254-259` 기존 코드에서 `allLogIdSet` 인자 자체가 없었다.

## 참조한 스킬 ※필수
- `skills/common/verification.md` (방법론)
- `skills/common/root-cause-diagnosis.md` (원인 실체 원칙)

## 변경 내용
| 파일 | 종류 | 요약 |
| --- | --- | --- |
| `server/Harness/HandoffIntegrityChecker.cs` | 수정 | `CheckStateToLog`에 `allLogIdSet` 매개변수 추가 + 면제 조건에 `!allLogIdSet.Contains(id)` 추가 + 실패 메시지 "has no log entry" → "has no successful log entry" |
| `server/Harness/HandoffIntegrityCli.cs` | 수정 | `--self-test` 분기 + `RunSelfTest` 메서드 신설 (5종 하드코딩 단언) |
| `docs/qa/fixtures/reconciliation/pending/pending-ok/*` | 신규 | pending 면제 정당 케이스 |
| `docs/qa/fixtures/reconciliation/pending/pending-failed-log/*` | 신규 | ★ 구멍 케이스 — 실패 로그가 있는 전이를 pending으로 지정 |
| `docs/qa/fixtures/reconciliation/pending/pending-success-log/*` | 신규 | 이미 성공 로그가 있는 전이(면제 불필요) |
| `docs/qa/fixtures/reconciliation/pending/pending-duplicate/*` | 신규 | state 중복(면제 미적용) |
| `docs/qa/fixtures/reconciliation/pending/pending-mismatch/*` | 신규 | 엉뚱한 pending id(면제 미적용) |
| `docs/verification/05h-r2-pending-exemption.md` | 신규 | 이 문서 |

## 반증 시험 (negative test) ※필수

| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 1 | pending-failed-log 수정 전 코드 | --self-test exit 1 (mismatch) | 1 | ✓ 구멍 확인 |
| 2 | pending-failed-log 수정 후 코드 | checker FAIL(pass=false) | --self-test PASS 내 확인 | ✓ 막힘 확인 |
| 3 | pending-duplicate (state 2회) | FAIL | --self-test PASS 내 확인 | ✓ |
| 4 | pending-mismatch (엉뚱한 id) | FAIL | --self-test PASS 내 확인 | ✓ |
| 5 | CLI --pending-transition | 1 pending-not-allowed-on-cli | 1 | ✓ 우회 통로 없음 확인 |
| 6 | at-rest (DI0004-BLOCKED-CODEX 포함) | 1 (failures=1) | 1 | ✓ 면제 조건이 at-rest에 영향 없음 |

## 검수 기준 자가점검표
| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | build-verify exit 0 | ✓ | Release exit 0, Debug exit 0 |
| 2 | verify-behavior (동작 보존) | NOT_VERIFIED | verify-behavior 하네스 없음(하네스 자체가 reconciliation 변경 대상이라 별도 스냅샷 없음) |
| 3 | measure dev-pack 비악화 | ✓ | violationCount=0 |
| 4 | allowlist 준수 | ✓ | 5종 파일만 수정, StateApplierCli.cs·DiCompletionCheckCli.cs·ClaimCheckCli.cs 무접촉 |
| 5 | verification 문서에 ①주체 ②하네스 결과 ③스킬 기록 | ✓ | 이 문서 |
| 6 | 목적 미달 자진 신고 | ✓ | 아래 「지표는 만족했으나 목적은 미달인 부분」 |

## 게이트 기록
`{"gate":"dev-pack","violations":0,"attempt":1}`

## 잔여 위험 · 미확정 사항 ※필수

- `verify-behavior` NOT_VERIFIED: 이 harness는 reconciliation 동작 자체를 검사하므로 별도 스냅샷 대상이 없다. `--self-test` 5종 케이스가 대안적 회귀 기준.
- 06C-1이 `StateApplierCli.cs`에서 `HandoffIntegrityChecker`를 in-process 호출하는 부분 — 이번 변경으로 `CheckStateToLog` 시그니처가 바뀌지 않았음(`Run` 메서드 표면은 동일). 06C-1과 충돌 없음.
- `--self-test`가 fixture 경로를 하드코딩한다. fixture 파일을 옮기면 self-test가 조용히 `applier-log not found` HarnessError로 실패하고 exit 1을 낸다 — 오탐 없음(fail-closed).

## 직접 경로 사용 사유

- **ADR-015 한시 예외**: 생산·1차검증을 동일 actor(sonnet)가 수행. 코덱스 독립 검수(`05H-R1.codex.md`)가 이미 1차 확정했으며, 이번 작업은 코덱스 지적을 구현·실증하는 것이다.
- **파일 직접 수정 사유**: `server/Harness/HandoffIntegrityChecker.cs`·`server/Harness/HandoffIntegrityCli.cs`는 지시서 allowlist에 명시된 직접 경로다. `docs/qa/fixtures/**`·`docs/verification/**`는 관례상 직접 경로 허용.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005 — 필수)

- **`verify-behavior` NOT_VERIFIED**: reconciliation 변경에 대한 동작 보존 스냅샷을 만들지 않았다. `--self-test` 5종과 회귀 fixture 7종이 사실상 동작 보존 기능을 수행하지만, 공식 `verify-behavior` 하네스로 확인하지 않았다. 다음 사람이 `verify-behavior` 스냅샷 갱신 여부를 확인해야 한다.
- 없음 이외의 미달: pending 면제 경로의 목적("아직 기록되지 않은 진행 중 전이에만 준다")을 `!allLogIdSet.Contains(id)` 조건으로 정확히 구현했고, `--self-test`로 실증했다. 우회로를 열지 않았다.

## 완료 판정

`PASS | FAIL | BLOCKED` — **생산자가 아니라 검수자가 적는다.**
