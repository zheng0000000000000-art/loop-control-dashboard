# 06C-1-R2 검증 — "무시"를 "거절"로, 그리고 rollback을 시험 가능하게

## DI 유형 ※필수

- **선언한 유형**: `implementation`

## 주체 (actor) ※필수
- **누가**: CORE_INFRA_EXECUTOR (sonnet 4.6), 대화 세션, ADR-015 한시 예외
- **경로**: 대화 세션 (직접 경로)

## 사용한 하네스 ※필수
| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| build | `dotnet build server -c Release` | 0 | 0 | 오류 0개 |
| state-transition --self-test | `state-transition --self-test` | 0 | 0 | casesRun=4, verdict=PASS |
| state-transition-callsite-check | `state-transition-callsite-check` | 0 | 0 | legacyCallsiteCount=0, historicalReferenceCount=4 |
| handoff-integrity | `handoff-integrity` | 1 | 1 | failureCount=1 (DI0004-BLOCKED-CODEX, at-rest 정상) |
| verify-behavior | `verify-behavior` | 0 | 0 | behaviorEqual=true |
| measure dev-pack | `measure dev-pack` | 0 | 0 | violationCount=0 |

## 유형별 필수 검증 ※필수

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| implementation | 정상·실패·재실행 테스트, 개발 검증 Evidence | 아래 반증 시험 표 및 판정선 15개 전체 수행 |

## 공통 완료 조건 ※필수

- [x] 선언한 DI 유형의 완료 프로필 충족
- [x] 관련 계약·스키마·문서 갱신 (`CALLSITE-HISTORICAL.json` 신규, `StateApplierCli.cs`·`StateTransitionCallsiteCheckCli.cs` 수정)
- [x] 발견된 실패·위험·미확정 사항 기록 (아래 「잔여 위험」 절)
- [ ] `WORKSTATE.json` 갱신 — **조율자/검수자 판정 후 수행. 이번 세션에서는 미수행.**
- [x] 변경 범위 준수(allowlist): 수정한 파일 3종 모두 `06C-1-R2` allowlist 내
- [x] 원본 저장소 무단 변경 없음 — push·결재·발사 없음

## 실패 분류와 실패 사례 ※필수

- **실패 분류**: `expected_rejection` (반증 시험이 의도대로 거부된 것)
- **실패 사례 ID**: 신규 실패 사례 없음 — 기존 결함(R1 FAIL 코덱스 보고)을 수정함

## 참조한 스킬 ※필수
- `skills/common/` 전체 (powershell-encoding, git 관련)

## 변경 내용
| 파일 | 종류(신규/수정) | 요약 |
| --- | --- | --- |
| `server/StateApplierCli.cs` | 수정 | (1) `ValidateOptions` + `RemovedOptions`/`PrepareKnownKeys`/`ApplyKnownKeys` 추가 — 알 수 없는·삭제된 옵션 즉시 exit 2. (2) `CheckExistingTransition`에서 신규 전이도 contract hash 검증. (3) `--self-test` 명령 신설: `RunSelfTest`·`RunSelfTestInDir`·`RunCaseNormalApply`·`RunCaseRollbackAfterWrite`·`RunCaseRollbackRestoresLog`·`RunCaseFatalRestoreFailed`·`RestoreFixture`. (4) `ProjectionOverride`·`FailRestoreForTest` seam 추가. (5) `RunApply` → `ValidateOptions` 선행 + `LoadWorkstateContextFromRoot` + `ApplyEnvelopeCore` 분리. |
| `server/Harness/StateTransitionCallsiteCheckCli.cs` | 수정 | (1) `ActiveExtensions`에 `.txt` 추가. (2) `HistoricalPrefixes` 배열 삭제 → `LoadHistoricalFiles()`로 `docs/handoff/CALLSITE-HISTORICAL.json` 명시 목록 읽기. (3) `IsHistorical()` 시그니처 변경 — 접두사 매칭 제거, 정확한 경로 매칭. |
| `docs/handoff/CALLSITE-HISTORICAL.json` | 신규 | 역사적 증거 파일 명시 목록 (4건): `docs/handoff/queue/directive-06C-1-R1-legacy-removal.md`, `docs/verification/06c1-r1-legacy-removal.md`, `outputs/review/06C-1.codex.md`, `outputs/review/06C-1-R1.codex.md` |

## 반증 시험 (negative test) ※필수

### 완료 기준 2·3·9·10·11 — 「고치기 전」 실제 출력

**기준 2 pre-fix** (prepare + `--human-decision`):
```
# 이전 세션(06C-1-R1 이후 직후 상태)에서 확인
$ state-transition prepare --transition-id T --request nonexistent.json --human-decision fake.json
{"error":"request file not found: nonexistent.json"}
exit=2
# ★ removed-option 언급 없음. 파일 부재로만 실패 — usage 거부가 아니다.
```

**기준 3 pre-fix** (prepare + `--root`):
```
# 같은 상태에서 확인
$ state-transition prepare --transition-id T --request nonexistent.json --root C:\somewhere
{"error":"request file not found: nonexistent.json"}
exit=2
# ★ removed-option 언급 없음. 동일 오류 — --root가 무시됐다.
```

**기준 9 pre-fix** (신규 전이 envelope hash 위조):
```
# 이전 세션: C:\tmp\st-pretest 임시 저장소, 수정 전 코드
$ state-transition apply --envelope T-PRE9.envelope.json  # (transitionContractSha256 = "aaaa")
{"ok":true,"transitionId":"T-PRE9",...}  
exit=0
# ★ 위조 hash가 검사되지 않고 정상 적용됨. CheckExistingTransition이 신규 전이에서 null을 반환.
```

**기준 10 pre-fix** (`.txt` legacy call 미탐지):
```
# outputs/launch/ZZ-bait.txt 생성 후 callsite-check
{"legacyCallsiteCount":0,"scannedActiveFiles":581,...}
exit=0
# ★ .txt가 ActiveExtensions에 없어 ZZ-bait.txt가 스캔되지 않음.
```

**기준 11 pre-fix** (docs/wiki/ 접두사 면제):
```
# docs/wiki/ZZ-bait.md 생성 후 callsite-check
{"legacyCallsiteCount":0,"classifiedPaths":["[historical] docs/wiki/ZZ-bait.md"],...}
exit=0
# ★ HistoricalPrefixes에 "docs/wiki/"가 있어 새 파일도 자동 면제됨.
```

### 판정선 15개 전체 (post-fix)

| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 1 | `dotnet build server -c Release` | 0 | 0 | PASS |
| 2a | `prepare ... --human-decision fake.json` | 2 (removed-option 문구) | 2 | PASS — `{"error":"removed-option: --human-decision (06C-1-R1에서 삭제됨...)"}` |
| 2b | `apply ... --human-decision fake.json` | 2 (removed-option 문구) | 2 | PASS |
| 3a | `prepare ... --root C:\somewhere` | 2 (removed-option 문구) | 2 | PASS — `{"error":"removed-option: --root (06C-1-R1에서 삭제됨...)"}` |
| 3b | `apply ... --root C:\somewhere` | 2 (removed-option 문구) | 2 | PASS |
| 4a | `prepare ... --bogus-flag zzz` | 2 (unknown-option 문구) | 2 | PASS — `{"error":"unknown-option: --bogus-flag"}` |
| 4b | `apply ... --bogus-flag zzz` | 2 (unknown-option 문구) | 2 | PASS |
| 5 | 올바른 인자만 (유효하지 않은 JSON request) | 2 (JSON 파싱 오류) | 2 | PASS — 오류 문구 다름: `{"error":"state-transition prepare 실패: ..."}` |
| 6 | `state-transition --self-test` | 0 | 0 | PASS — `{"selfTest":"state-transition","verdict":"PASS","casesRun":4}` |
| 7 | self-test `rollback-after-write` case | exit=1, ROLLED_BACK, hash==preimage, v2 미기록 | exit=1, rolledBack=true, noOkLog=true, hashRestored=true | PASS |
| 8 | self-test 기대값 틀리게 수정 후 실행 (normal-apply 기대 exit=99) | 1 | 1 | PASS — `{"verdict":"FAIL","mismatchCount":1}` |
| 9 | 신규 전이 `transitionContractSha256` = "aaaa" → apply | 1 (envelope-contract-mismatch) | 1 | PASS — `{"reason":"envelope-contract-mismatch"}` |
| 10 | `outputs/launch/ZZ-c10-bait.txt` legacy 호출 → callsite-check | 1 (legacyCallsiteCount≥1) | 1 | PASS — `{"legacyCallsiteCount":1,"classifiedPaths":["outputs/launch/ZZ-c10-bait.txt",...]}` |
| 11 | `docs/wiki/ZZ-c11-bait.md` legacy 호출 → callsite-check | 1 (historical 면제 안 됨) | 1 | PASS — `{"legacyCallsiteCount":1}` |
| 12 | bait 파일 제거 후 callsite-check | 0 | 0 | PASS — `{"legacyCallsiteCount":0,"historicalReferenceCount":4}` |
| 13a | 손위조 transitionId → state-corrupted-preapply | 1 | 1 | PASS — `{"reason":"state-corrupted-preapply"}` |
| 13b | candidate 변조 → candidate-tampered | 1 | 1 | PASS — `{"reason":"candidate-tampered"}` |
| 13c | `transitionKind="EVIL"` → unknown-transition-kind | 1 | 1 | PASS — `{"reason":"unknown-transition-kind"}` |
| 13d | `transitionKind="PHASE_CHANGE"` (hash 비움) → trusted-human-receipt-required | 1 | 1 | PASS — `{"reason":"trusted-human-receipt-required"}` |
| 14 | at-rest `handoff-integrity` | 1, failures 정확히 1건 | 1, failureCount=1 | PASS — `DI0004-BLOCKED-CODEX: state-transition-not-logged` |
| 15 | `measure dev-pack -c Release` | 0 | 0 | PASS — `{"violationCount":0}` |

### self-test 케이스별 실제 출력 (기준 6·7)

```json
{
  "selfTest": "state-transition",
  "verdict": "PASS",
  "casesRun": 4,
  "cases": [
    {
      "case": "normal-apply",
      "exit": 0,
      "v2LogWritten": true,
      "wsChanged": true,
      "pass": true
    },
    {
      "case": "rollback-after-write",
      "exit": 1,
      "rolledBack": true,
      "noOkLog": true,
      "hashRestored": true,
      "pass": true
    },
    {
      "case": "rollback-restores-log",
      "noV2OkEntry": true,
      "pass": true
    },
    {
      "case": "fatal-restore-failed",
      "exit": 2,
      "fatalLogged": true,
      "pass": true
    }
  ]
}
```
이것이 rollback 경로의 **최초 실행 기록**이다.

### 코덱스 #1 (새 전이 self-hash) — 실제로 뚫려 있었는가?

**결론: 뚫려 있었다.** 이전 세션에서 `C:\tmp\st-pretest` 임시 저장소에서 실증 확인 (exit=0). 코덱스 보고 정확. 수정 후 exit=1 확인.

### 반증 시험에 쓴 임시 파일 위치

모든 반증 시험은 `C:\tmp\st-c9b`, `C:\tmp\st-c13`, `C:\tmp\st-c13b` 등 `$TEMP` 또는 `C:\tmp\` 아래 임시 디렉토리에서 수행. 테스트 종료 후 `rm -rf` 제거. 저장소 `outputs/state-transition/`에 남긴 파일:
- `T-PRE9*.json` (이전 세션 pre-fix 아티팩트) → 이번 세션에서 **삭제 완료**
- 현재 남은 파일: `T-CANTEST.*`, `T-TEST-001.*` (이전 세션 아티팩트, allowlist 내)

**저장소 무결성**: `git status`로 staging area 및 tracked file 변경 확인 — `server/StateApplierCli.cs`, `server/Harness/StateTransitionCallsiteCheckCli.cs`, `docs/handoff/CALLSITE-HISTORICAL.json` 3파일만 수정/신규.

## 검수 기준 자가점검표

| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | build exit 0 | PASS | 오류 0개 |
| 2 | prepare/apply + --human-decision → exit 2, removed-option 문구 | PASS | `{"error":"removed-option: --human-decision ..."}` |
| 3 | prepare/apply + --root → exit 2, removed-option 문구 | PASS | `{"error":"removed-option: --root ..."}` |
| 4 | prepare/apply + --bogus-flag → exit 2, unknown-option | PASS | `{"error":"unknown-option: --bogus-flag"}` |
| 5 | 올바른 인자 → 정상 동작 (2·3·4와 다른 오류) | PASS | JSON 파싱 오류로 진행 — option 거부와 다른 메시지 |
| 6 | --self-test → exit 0, 4 case PASS | PASS | `{"verdict":"PASS","casesRun":4}` |
| 7 | rollback-after-write: ROLLED_BACK, hash==preimage, v2 미기록 | PASS | `rolledBack=true, hashRestored=true, noOkLog=true` |
| 8 | self-test 기대값 틀리게 → exit 1 | PASS | `{"verdict":"FAIL","mismatchCount":1}` exit=1 |
| 9 | 신규 전이 hash 위조 → exit 1 envelope-contract-mismatch | PASS | pre-fix exit=0 확인 후 수정, post-fix exit=1 |
| 10 | .txt legacy call → callsite-check exit 1 | PASS | `legacyCallsiteCount=1` exit=1 |
| 11 | docs/wiki/ 새 파일 legacy call → callsite-check exit 1 | PASS | `legacyCallsiteCount=1` (historical 면제 안 됨) |
| 12 | bait 제거 후 callsite-check exit 0 | PASS | `legacyCallsiteCount=0` exit=0 |
| 13 | 회귀: state-corrupted/candidate-tampered/unknown-kind/PHASE_CHANGE | PASS | 전부 exit=1, 각각 올바른 reason |
| 14 | at-rest handoff-integrity → exit 1, failures=1 | PASS | `failureCount=1` |
| 15 | measure dev-pack → exit 0 | PASS | `violationCount=0` |

## 게이트 기록

`{"gate":"dev-pack","violations":0,"attempt":3}`

시도 이력:
- attempt 1: `maxFunctionLength=134` 위반 (`RunSelfTestInDir` 134줄)
- attempt 2: 동일 (캐시)
- attempt 3: 4개 case 함수로 분리 후 → violations=0

## 잔여 위험 · 미확정 사항 ※필수

1. **scope-check FAIL**: `changedFileCount=154`, `outOfScopeCount=146` — 이 146건은 이번 세션이 수정하지 않은 pre-existing untracked/modified 파일들이다. 실제 변경 3파일은 모두 allowlist 내. scope-check 오탐 여부는 조율자가 판단.
2. **at-rest handoff-integrity failure 1건 (`DI0004-BLOCKED-CODEX`)**: 이번 WP의 정상 상태. 이번 DI와 무관.
3. **`verify-behavior` 통과**: `{"behaviorEqual":true}` exit=0. 회귀 없음.
4. **`--self-test`의 projection**: `ProjectionOverride = () => 0`으로 전역 seam을 설정한다. 병렬 실행 시 다른 스레드의 projection에 영향 가능. 하지만 single-process, sequential이므로 현재 운영 방식에서는 문제 없음.

## 직접 경로 사용 사유

지시서 §2에 "직접 경로" 명시됨. 변경 파일 3종 모두 allowlist 내.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005)

1. **scope-check 미통과**: `scope-check 06C-1-R2` exit=1 (FAIL). 출력 내 146건 out-of-scope는 이번 세션 이전부터 있던 파일이나, scope-check 도구 기준상 FAIL이다. 실제 수정한 3파일은 allowlist 내이므로 목적(변경 범위 준수)은 달성했으나 지표 숫자는 FAIL.

2. **`functionsWithoutComment`=0 유지는 확인** — 추가한 함수 5개 모두 Korean comment 포함. 그러나 measure가 실제로 새 함수 이름을 체크했는지 독립 확인 못 함 (measure는 measure.json을 덮어쓰고 count만 보고한다).

3. **`--self-test` projection**: `ProjectionOverride = () => 0` (항상 성공 반환)이라 projection 자체의 정확성을 시험하지 않는다. projection이 실패하면 rollback 경로가 달라질 수 있는데 이 case는 self-test에 없다.

## 완료 판정

`PASS | FAIL | BLOCKED` — **생산자가 아니라 검수자가 적는다.**
