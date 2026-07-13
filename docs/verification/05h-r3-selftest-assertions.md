# 05H-R3 검증 — `--self-test`가 실패의 원인을 단언하게 한다

> **모든 DI의 검증 문서는 이 템플릿을 쓴다**(v9 §DI-00-02). 절을 지우지 마라 — 해당 없으면 "없음"이라고 쓴다.

## DI 유형 ※필수 (v9 §0.1)

- **선언한 유형**: harness

## 주체 (actor) ※필수
- **누가**: CORE_INFRA_EXECUTOR (claude-sonnet-4-6, ADR-015 한시 예외), 대화 세션
- **경로**: 대화 세션 (헤드리스 아님)

## 사용한 하네스 ※필수
| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| build-verify | `dotnet build server -c Release` | 0 | 0 | 경고 0·오류 0 |
| handoff-integrity --self-test (수정 전) | `dotnet run --project server -c Release -- handoff-integrity --self-test` | 1(뚫림) | 0 | **"뚫렸다"** — malformed fixture에도 PASS |
| handoff-integrity --self-test (수정 후, fixture 아직 malformed) | 동일 | 1 | 1 | mismatch: unexpected-harness-error(pending-duplicate), unexpected-harness-error(pending-nonok-zero 미존재) |
| handoff-integrity --self-test (최종) | 동일 | 0 | 0 | casesRun=6, verdict=PASS |
| pending-nonok-zero 단독 | `dotnet run ... -- handoff-integrity --workstate .../pending-nonok-zero/workstate.json --applier-log .../applier-log.jsonl` | 1 | 1 | state-transition-not-logged 1건 |
| at-rest handoff-integrity | `dotnet run ... -- handoff-integrity` | 1 | 1 | failures=1 (DI0004-BLOCKED-CODEX/state-transition-not-logged) |
| measure dev-pack (-c Release) | `dotnet run --project server -c Release -- measure dev-pack` | 0 | 0 | violationCount=0 |
| measure dev-pack (Debug) | `dotnet run --project server -- measure dev-pack` | 0 | 0 | violationCount=0 |

## 유형별 필수 검증 ※필수 (v9 §0.1)

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| harness | **positive·negative·결정성·격리** 테스트 | 아래 반증 시험 절 참조 |

## 공통 완료 조건 ※필수 (v9 §0.1)

- [x] 선언한 DI 유형의 완료 프로필 충족
- [x] 관련 계약·스키마·문서 갱신 (fixture 추가, CLI.cs 수정)
- [x] 발견된 실패·위험·미확정 사항 기록 (아래 "잔여 위험" 절)
- [ ] `WORKSTATE.json` 갱신 — 사람 게이트. 수정 금지.
- [x] 변경 범위 준수(allowlist): server/Harness/HandoffIntegrityCli.cs, docs/qa/fixtures/reconciliation/**, docs/verification/05h-r3-selftest-assertions.md
- [x] 원본 저장소 무단 변경 없음(commit/push 금지)

## 실패 분류와 실패 사례 ※필수 (v9 §0.3)

- **실패 분류**: `design_learning`
- **내용**: `pending-duplicate/workstate.json`이 malformed 상태로 HEAD에 커밋돼 있었다 (blob: b060b08e94ab1ec15d8eb42482c551a97626307c). 이는 이전 실행자가 반증 시험 중 오염시키고 원복하지 않은 것으로 추정된다. **"git status로 원복을 확인할 수 없다"**는 지시서의 경고가 실증됐다 — 파일이 mtime만 달라도 M으로 보이고, stat 캐시 이슈로 놓칠 수 있다.
- **위키 등록 후보**: 하네스가 자기 입력 fixture를 corrupted 상태로 커밋할 수 있다는 운영 사고 패턴. `docs/wiki/failures/cases/`에 신규 사례 등록 권장 (검수자 판단).

## 참조한 스킬 ※필수
- `skills/common/` (일반 규칙)

## 변경 내용
| 파일 | 종류 | 요약 |
| --- | --- | --- |
| server/Harness/HandoffIntegrityCli.cs | 수정 | `RunSelfTest` 튜플 확장 (expectFailureCodes, expectHarnessErrors 추가), `BuildSelfTestMismatch` 헬퍼 추출, pending-nonok-zero case 추가 |
| docs/qa/fixtures/reconciliation/pending/pending-duplicate/workstate.json | 수정 | malformed → valid JSON (PENDING-DUP 2회, log 없음) |
| docs/qa/fixtures/reconciliation/pending/pending-nonok-zero/workstate.json | 신규 | PENDING-NONOK 1회 state |
| docs/qa/fixtures/reconciliation/pending/pending-nonok-zero/applier-log.jsonl | 신규 | result="error-but-zero", exitCode=0 |

## 반증 시험 (negative test) ※필수

| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 1 | `--self-test` (수정 전, pending-duplicate/workstate.json malformed) | 1 | **0** | **"뚫렸다" — 수정 전 취약점 실증** |
| 2 | `--self-test` (수정 후, pending-duplicate/workstate.json 아직 malformed) | 1 | 1 | PASS — unexpected-harness-error 감지 |
| 3 | `--self-test` (pending-failed-log expectFailureCodes를 "INTENTIONALLY-WRONG-CODE"로 일시 변경) | 1 | 1 | PASS — failure-code-mismatch 감지 |
| 4 | `--pending-transition TEST` | 1 | 1 | PASS — pending-not-allowed-on-cli |
| 5 | at-rest handoff-integrity | 1 | 1 | PASS — DI0004-BLOCKED-CODEX 1건 |
| 6 | 회귀 a/b/d/e/f | 1 | 1 | PASS (변화 없음) |
| 7 | 회귀 c | 0 | 0 | PASS (변화 없음) |
| 8 | 회귀 malformed | 2 | 2 | PASS (변화 없음) |

### 완료 기준 4의 실제 출력 (코드 불일치 감지)

```json
{
  "selfTest": "handoff-integrity-pending",
  "verdict": "FAIL",
  "mismatchCount": 1,
  "mismatches": [
    {
      "case": "pending-failed-log",
      "expectedPendingExemptionApplied": false,
      "actualPendingExemptionApplied": false,
      "expectedFailureCodes": ["INTENTIONALLY-WRONG-CODE"],
      "actualFailureCodes": ["state-transition-not-logged"],
      "expectedHarnessErrors": false,
      "actualHarnessErrors": false,
      "harnessErrors": [],
      "mismatchReason": "failure-code-mismatch"
    }
  ]
}
```
exit: 1

### 완료 기준 5의 실제 출력

**"뚫렸다" (수정 전, pending-duplicate/workstate.json malformed):**
```json
{"selfTest":"handoff-integrity-pending","verdict":"PASS","casesRun":5}
```
exit: 0 — **잘못된 이유로 실패했는데 PASS를 줬다.**

**"막았다" (수정 후, fixture 아직 malformed):**
```json
{
  "verdict": "FAIL",
  "mismatchCount": 2,
  "mismatches": [
    {
      "case": "pending-duplicate",
      "mismatchReason": "unexpected-harness-error",
      "actualHarnessErrors": true,
      "harnessErrors": [{"code":"workstate-malformed","subject":"workstate"}]
    },
    ...
  ]
}
```
exit: 1

### fixture blob 해시 확인 (pending-duplicate/workstate.json)

HEAD(malformed 버전): `b060b08e94ab1ec15d8eb42482c551a97626307c`
작성 후(올바른 버전): `f8114e012a144c3caacaf3625233f202f3b53b48`

해시가 다름 — **예정된 변경**: HEAD 자체가 malformed였으므로 "원복"이 아니라 "신규 올바른 내용 작성"이다.
HEAD 내용(`git show HEAD:<path>`): `{ "schemaVersion": 3, "appliedTransitions": [ BROKEN` (BOM 포함)

## 검수 기준 자가점검표
| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | build-verify exit 0 | PASS | dotnet build -c Release 경고 0 오류 0 |
| 2 | --self-test exit 0 (6 case) | PASS | casesRun=6, verdict=PASS |
| 3 | pending-nonok-zero 단독 exit 1 | PASS | state-transition-not-logged 1건 |
| 4 | 기대 코드 틀리게 → exit 1 | PASS | failure-code-mismatch 감지 |
| 5 | malformed fixture → exit 1 | PASS | unexpected-harness-error 감지 (수정 전 exit 0 실증 포함) |
| 6 | 회귀 fixture 변화 없음 | PASS | a/b/d/e/f→1, c→0, malformed→2 |
| 7 | --pending-transition CLI 금지 유지 | PASS | exit 1, pending-not-allowed-on-cli |
| 8 | at-rest exit 1, failures=1 | PASS | DI0004-BLOCKED-CODEX 1건 |
| 9 | measure dev-pack exit 0 | PASS | violationCount=0 (Release + Debug 둘 다) |
| 10 | HandoffIntegrityChecker.cs 무접촉 | PASS | 수정 없음 |
| 11 | StateApplierCli.cs 무접촉 | PASS | 수정 없음 |

## 게이트 기록
`{"gate":"dev-pack","violations":0,"attempt":1}`

## 잔여 위험 · 미확정 사항 ※필수

1. **pending-duplicate/workstate.json HEAD가 malformed 상태**: 이 커밋(wp/state-integrity branch)에서 올바른 내용으로 교체했으나, 왜 malformed가 커밋됐는지 주체는 미확정. 다음 실행자는 land gate 전에 `git show HEAD:<path>`로 확인 권장.
2. `expectHarnessErrors: true` 케이스 없음: 현재 6 케이스 모두 `expectHarnessErrors: false`다. HarnessError가 기대되는 case(malformed 파일을 fixture로 추가)는 이번 allowlist 밖 범위라 미신설. 다음에 필요하면 추가 가능.

## 직접 경로 사용 사유

ADR-015 한시 예외 — CORE_INFRA_EXECUTOR (sonnet)가 server/Harness 파일 직접 수정. 지시서에 "actor: CORE_INFRA_EXECUTOR (sonnet)" 명시됨.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005)

없음. `--self-test`는 이제 "어쨌든 실패"가 아니라 "기대한 그 이유로 실패"를 단언한다. HarnessErrors와 Failures를 구분해 코드 집합 비교를 수행하므로 목적 기준을 충족한다.

우회로 두 가지 모두 미적용:
- `expectHarnessErrors`를 전 case에 true로 두지 않았다 (전부 false).
- 기대 코드 집합을 부분집합으로 느슨하게 하지 않았다 (`SetEquals` 정확 일치).

## 완료 판정

`PASS | FAIL | BLOCKED` — **생산자가 아니라 검수자가 적는다.**
