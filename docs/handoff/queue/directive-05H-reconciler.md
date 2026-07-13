# 05H — handoff-integrity: 내부 ReconciliationChecker + v2 log 계약 + blocker 정정 (codex)

- actor: **HARNESS_EXECUTOR (codex)** · WP-STATE-INTEGRITY · 단일 land gate
- 대상/allowlist: `server/Harness/HandoffIntegrityCli.cs`, 신규 내부 checker, fixture 경로, `docs/verification/…`, `docs/directives/…`
- **절대 수정 금지:** `WORKSTATE.json`, `WORKSTATE.applier-log.jsonl`(append-only 증거), `StateApplierCli.cs`

---

## 1. 로그 계약 — v1(legacy) / v2 버전 구분

```text
v1 (legacy) : {"transitionId","result","exitCode","at"}
v2 (신규)   : {"schemaVersion":2,"transitionId","transitionKind","result","exitCode","at",
              "requestSha256","preStateSha256","postStateSha256","effectiveAt",
              "transitionContractSha256"}
              transitionContractSha256 = sha256(canonical{transitionId,transitionKind,
                requestSha256,preStateSha256,postStateSha256,effectiveAt})
```

- `schemaVersion` 없으면 v1로 읽는다. v2는 hash 4필드 필수.
- reconciliation은 두 버전을 모두 읽되, v2의 hash는 06C-1의 idempotency 충돌 판정에 쓰인다(§7).

## 2. 구조 — pending은 내부 API, 공개 CLI 아님

```csharp
HandoffIntegrityCli.Run(string[] args);   // 외부 CLI — pending 면제 없음
HandoffIntegrityChecker.Run(new ReconciliationOptions {
    WorkstatePath, ApplierLogPath, PendingTransitionId /* 0|1 */ });  // in-process 전용
```

- 내부 checker는 `HarnessRegistry`에 **등재하지 않는다**(CLI 표면·pending 우회 방지). StateApplier가 직접 호출.
- 외부 CLI가 `--pending-transition` 받으면 → Failure `pending-not-allowed-on-cli` (exit 1).
- CLI 경로 인자(fixture 격리): `handoff-integrity --workstate <path> --applier-log <path>` (기본 canonical).
- 내부 checker가 pending 면제 적용 시 report에 `pendingExemptionApplied=true`+id.

## 3. 결과 타입

```csharp
ReconciliationResult { Failures, Warnings, HarnessErrors, Metrics }
// HarnessErrors>0 → exit 2 / Failures>0 → exit 1 / 없음 → exit 0
```

## 4. 자료구조

```csharp
List<LogEntry>          successfulLogEntries;   // result=="ok" && exitCode==0
HashSet<string>         successfulLogIdSet;
Dictionary<string,int>  successfulLogCounts;
List<string>            stateIds;
HashSet<string>         stateIdSet;
```

## 5. reconciliation 규칙

1. `successfulLogIdSet ⊆ stateIdSet` 아니면 Failure `log-transition-missing-from-state`. *(핵심 사고 탐지)*
2. `stateIdSet ⊆ successfulLogIdSet` 아니면 Failure `state-transition-not-logged`.
   `PendingTransitionId`는 면제(내부 checker 전용, state에 정확히 1회·log엔 없음, 어기면 면제 미적용).
3. stateIds 중복 → Failure `duplicate-in-state`.
4. 같은 id 성공 로그 2개+:
   - 모두 같은 `transitionContractSha256` → Warning `duplicate-success-in-log` (append-only 재적용, 정상 이력)
   - 서로 다른 binding → Failure `duplicate-success-log-conflict` (단순 중복 아니라 감사 이력 충돌)
   - lookupSuccess는 conflict가 없을 때만 단일 행을 반환. conflict면 06C-1에 FAIL로 전달(모호 상태 불허).

## 6. Metrics

```json
{ "appliedTransitionCount":11, "successfulLogEntryCount":12, "successfulLogIdCount":11,
  "duplicateSuccessLogCount":1, "logSchemaVersions":{"v1":11,"v2":1},
  "pendingExemptionApplied":false, "reconciliation":"PASS" }
```

## 7. transition-id 내용 결속 지원 (06C-1 idempotency가 소비)

reconciliation은 자료 제공까지; idempotency 결정은 06C-1이 한다. checker는 각 stateId에 대해:

```text
lookupSuccess(transitionId) → { exists, schemaVersion, transitionContractSha256? }
  v2 : transitionContractSha256 반환
  v1 : schemaVersion=1, contract hash 없음 (06C-1이 legacy-idempotency-unverifiable로 fail-closed)
```

06C-1은 envelope로부터 같은 계약 hash를 계산해 대조: 일치 → idempotent / 불일치 → transition-id-collision /
v1 → legacy-idempotency-unverifiable.

## 8. malformed 기준 (→ HarnessError, exit 2)

`applier-log-malformed` / `workstate-malformed`:

- `transitionId` 누락 또는 빈 문자열
- `result` 누락/문자열 아님 · `exitCode` 누락/정수 아님
- `at` 누락 또는 시간 형식 오류
- v2인데 `transitionKind` 누락 또는 허용값 아님
- v2인데 3개 hash(`requestSha256`/`preStateSha256`/`postStateSha256`) 및 `transitionContractSha256` 중
  누락 또는 64-hex 아님
- v2인데 `effectiveAt` 누락 또는 UTC RFC3339 아님 (effectiveAt은 hash가 아니다)
- `appliedTransitions` 배열 아님 · 원소 `id` 누락/빈 문자열
- **`appliedTransitions[].appliedAt` 필수** — 누락 또는 유효 UTC/RFC3339 아니면 `workstate-malformed`
  (실측: 현재 WORKSTATE 전 항목에 appliedAt 존재, StateApplier가 항상 생성 → 필수로 확정)

## 9. blocker 계약 정정

`blockers[]` 배열 읽기(현재 `blocker` 단수 = 죽은 검사). `status=="blocked"`&&activeBlockers==0 →
`blockers-missing` / 완료 status&&activeBlockers≥1 → `blockers-stale`.

## 10. fixtures (각자 전용 log)

| fixture | 구성 | 기대 |
| --- | --- | --- |
| A mid-incident | log에 `TEST-DI0001-2`(ok,0) / state엔 없음 | exit 1 `log-transition-missing-from-state` ★ |
| B state 중복 | appliedTransitions에 같은 id 2회 | exit 1 `duplicate-in-state` |
| C **known-good 불변 스냅샷** | 고정 정상 WORKSTATE+log 복사본 | exit 0 |
| D blocked+빈 blockers | status=blocked, blockers=[] | exit 1 `blockers-missing` |
| E completed+active blocker | status=completed, blockers=[{…}] | exit 1 `blockers-stale` |
| malformed | 깨진/필드누락/appliedAt누락 줄 | exit 2 `*-malformed` |

**별도(fixture 아님): at-rest current repo test** — 현재 저장소 실파일로 exit 0.

## 11. 완료 기준 (exit code)

```text
1. dotnet build server -c Release                          → 0
2. at-rest current repo (CLI, pending 없음)                 → 0
3. fixture A → 1 · B/D/E → 1 · C → 0 · malformed → 2
4. CLI에 --pending-transition                               → 1 pending-not-allowed-on-cli
5. 내부 checker 단위: state에 X 1회+log에 X 없음+Pending=X   → PASS ; Pending 없음 → FAIL state-transition-not-logged
6. v2 log fixture: lookupSuccess가 requestSha256/postStateSha256 반환
7. appliedAt 누락 fixture                                   → 2 workstate-malformed
8. measure dev-pack                                        → 0
```

## 12. 보고 / 스킬

actor(codex)·명령과 exit·참조 스킬·`## 지표는 만족했으나 목적은 미달인 부분`(ADR-005).
스킬: `skills/common/hs-gate.md`, `skills/common/root-cause-diagnosis.md`, `skills/common/verification.md`.
