# 06C-2-R1 검증 — trust-origin production truth

> **모든 DI의 검증 문서는 이 템플릿을 쓴다**(v9 §DI-00-02). 절을 지우지 마라 — 해당 없으면 "없음"이라고 쓴다.

## DI 유형 ※필수 (v9 §0.1)

- **선언한 유형**: `implementation`

06C-2 FAIL 판정 3개 반려 조건을 server/TrustOriginCli.cs 수정으로 닫는다.

## 주체 (actor) ※필수
- **누가**: CORE_INFRA_EXECUTOR (claude-sonnet-4-6), 대화 세션
- **경로**: 대화 세션 (수동 dispatch — 자동 스케줄러 중단 중)

## 사용한 하네스 ※필수
| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| build | `dotnet build server -c Release -nologo` | 0 | 0 | 경고 0, 오류 0 |
| trust-origin --self-test | `dotnet run --project server -c Release -- trust-origin --self-test` | 0 | 0 | 9/9 PASS |
| verify-behavior | `dotnet run --project server -c Release -- verify-behavior` | 0 | 0 | behaviorEqual=true |
| measure dev-pack | `dotnet run --project server -c Release -- measure dev-pack` | 0 | 0 | violations=0 |

> gate-clean, hs-scan은 커밋 전 단계라 이번 세션에서 실행하지 않았다 — NOT_VERIFIED.

## 유형별 필수 검증 ※필수 (v9 §0.1 — 선언한 유형의 행만 채운다)

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| implementation | 정상·실패·재실행 테스트, 개발 검증 Evidence | self-test 9 case PASS. 반려 조건 3개 각각 반증 case 추가 확인 |

## 06C-2 반려 조건 3개 — 어떻게 닫혔는가

### 반려 조건 1: production 경로 `?? true` 기본값으로 VERIFIED_PASS 위조

**닫는 방법**:
- `CheckAllPreconditions` 내 `precon?.BuildVerified ?? true` → `precon?.BuildVerified ?? CheckBuildProduction(root)` 로 교체.
  - `CheckBuildProduction(root)`: `dotnet build server -c Release -nologo`를 실제 실행, exit 0이면 true.
- `precon?.AutoLauncherOff ?? true` → `precon?.AutoLauncherOff ?? CheckAutoLauncherOff(root)` 로 교체.
  - `CheckAutoLauncherOff(root)`: `.claude/settings*.json` (프로젝트 + 홈 디렉터리)를 읽어 `hooks` 항목 유무 확인. hooks 발견 시 false, 없으면 true.
- `precon?.HighRiskClosed ?? true` → `precon?.HighRiskClosed ?? CheckHighRiskClosed(root)` 로 교체.
  - `CheckHighRiskClosed(root)`: 임시 PHASE_CHANGE 전이 envelope로 `StateApplierCli.Run`을 in-process 호출, exit 1(trusted-human-receipt-required)이면 true.
- `buildVerdict = "VERIFIED_PASS"` 는 build check를 통과한 경우에만 도달하므로 record에 정직하게 기록된다.

**검증**: self-test case `production-preconditions-not-default-true` — TestPrecon=null, tmpRoot에 dotnet 프로젝트 없음 → `CheckBuildProduction(tmpRoot)` 실패 → exit 1, record 미생성. PASS.

**검증**: self-test case `build-verdict-not-forged` — BuildVerified=false → exit 1, record 미생성. PASS.

### 반려 조건 2: `knownExceptions[]` 부분집합 허용 (extra known exception 통과)

**닫는 방법**: `RunReconciliationCheck` 내 부분집합 검사 후 `extraKnown = knownSubjects.Except(failureSubjects)` 를 추가. `unlisted.Count > 0 || extraKnown.Count > 0` 이면 선언 거부. 오류 출력에 `unlistedSubjects`, `extraKnownSubjects` 분리 포함.

**검증**: self-test case `extra-known-exception` — LISTED-TRANSITION(실제 failure) + FAKE-NEVER-OBSERVED(가짜 extra)를 knownExceptions에 넣음 → exit 1, `extraKnownSubjects:["FAKE-NEVER-OBSERVED"]` 출력, record 미생성. PASS.

### 반려 조건 3: record에 `phaseChangeReady`, `replayReady` 누락

**닫는 방법**: `BuildAndWriteRecord` 내 record JSON에 `["phaseChangeReady"] = false`, `["replayReady"] = false` 추가.

**검증**: self-test case `record-ready-flags-complete` — record-path-fixed가 생성한 record를 읽어 두 필드 모두 존재하고 false임을 확인. PASS.

## production 경로 검증 근거

| 항목 | 근거 | 위치 |
| --- | --- | --- |
| build가 실제 실행됨 | `CheckBuildProduction(root)`: 프로세스 spawn, exit 0 확인 | TrustOriginCli.cs |
| launcher check가 기본 true가 아님 | `CheckAutoLauncherOff(root)`: settings 파일 실제 검사, hooks 없으면 true | TrustOriginCli.cs |
| high-risk check가 기본 true가 아님 | `CheckHighRiskClosed(root)`: PHASE_CHANGE 전이 in-process 확인, exit 1이면 true | TrustOriginCli.cs |
| production test 실증 | TestPrecon=null + tmpRoot → build 실패 → exit 1 | self-test case 9 |

**현재 프로젝트 상태**: `.claude/settings.local.json`에 hooks 항목 없음 → `CheckAutoLauncherOff` true. WORKSTATE이 정상이면 `CheckHighRiskClosed`도 true (PHASE_CHANGE가 exit 1로 거부).

## canonical record 미생성 확인

실행 결과: `docs/handoff/trust-origin/TO-2026-001.json` 파일 없음 (CANONICAL_RECORD_ABSENT).
self-test 는 `$TEMP/to-selftest-*/docs/handoff/trust-origin/TO-2026-001.json` 에만 쓰고 세션 종료 시 삭제.

## WORKSTATE / applier-log 무결성 확인

`git status -- docs/handoff/WORKSTATE.json docs/handoff/WORKSTATE.applier-log.jsonl` → nothing to commit.
두 파일 모두 HEAD와 동일.

## 공통 완료 조건 ※필수 (v9 §0.1 — 전부 체크)

- [x] 선언한 DI 유형의 완료 프로필 충족 (implementation: 테스트 PASS)
- [ ] 관련 계약·스키마·문서 갱신 — WORKSTATE 갱신 불필요 (이번 작업은 구현 수정)
- [x] 발견된 실패·위험·미확정 사항 기록 (아래 잔여 위험 참조)
- [ ] `WORKSTATE.json` 갱신 — 이번 WP에서 land gate 통과 전 조각 transition 금지. 미수행.
- [x] 변경 범위 준수(allowlist) — server/TrustOriginCli.cs 단독 수정
- [x] 원본 저장소 무단 변경 없음 (commit/push/결재/반입/발사 미수행)

## 실패 분류와 실패 사례 ※필수 (v9 §0.3)

- **실패 분류**: `expected_rejection` (반증 시험이 의도대로 거부됨)
- **실패 사례**: 신규 실패 사례 없음

## 참조한 스킬 ※필수
- `skills/common/` — 이번 작업에서 skills 파일을 참조하지 않았다. NOT_VERIFIED.

## 변경 내용
| 파일 | 종류(신규/수정) | 요약 |
| --- | --- | --- |
| server/TrustOriginCli.cs | 수정 | production precondition 실제 검사, knownExceptions 정확한 집합, phaseChangeReady/replayReady 추가, self-test 4 case 추가 |
| docs/verification/06c2-r1-trust-origin-production-truth.md | 신규 | 이 검증 문서 |

## 반증 시험 (negative test) ※필수
| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 1 | extra-known-exception: FAKE-NEVER-OBSERVED extra → 거부 | 1 | 1 | PASS |
| 2 | unlisted-failure: UNLISTED-TRANSITION 미명시 → 거부 | 1 | 1 | PASS |
| 3 | build-verdict-not-forged: BuildVerified=false → 거부 | 1 | 1 | PASS |
| 4 | production-preconditions-not-default-true: TestPrecon=null → build 실패 | 1 | 1 | PASS |
| 5 | redeclare: trustEpoch>=1 재선언 거부 | 1 | 1 | PASS |

## 검수 기준 자가점검표
| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | build exit 0 | PASS | `dotnet build server -c Release -nologo` exit 0 |
| 2 | trust-origin --self-test exit 0 | PASS | 9/9 case PASS |
| 3 | extra-known-exception 반증 | PASS | case 1: exit 1, extraKnownSubjects 출력 |
| 4 | production 경로 VERIFIED_PASS 위조 불가 | PASS | case 4: TestPrecon=null, exit 1 |
| 5 | phaseChangeReady/replayReady 존재 | PASS | case 7: record에 두 필드 false |
| 6 | trust-origin 인수 없음 → exit 2 usage | PASS (코드 검토) | `Run(args)` line 42-44: sub="" → WriteError + return 2 |
| 7 | canonical record 미생성 | PASS | `docs/handoff/trust-origin/TO-2026-001.json` 없음 확인 |
| 8 | WORKSTATE/applier-log 무결 | PASS | git status 확인 — HEAD와 동일 |
| 9 | measure dev-pack violations=0 | PASS | exit 0, violations=0 |

## 게이트 기록
`{"gate":"dev-pack","violations":0,"attempt":1}`

## 잔여 위험 · 미확정 사항 ※필수

1. **`CheckHighRiskClosed` 의존성**: `StateApplierCli.Run`을 in-process로 호출하고 실제 WORKSTATE를 읽는다. WORKSTATE가 malformed이면 exit 2 → `CheckHighRiskClosed` false → 선언 거부(fail-closed). 안전하지만 오해 소지 있음.
2. **`CheckAutoLauncherOff` 범위**: Claude Code hooks만 본다. 시스템 cron/스케줄러는 확인하지 않는다. 이 프로젝트에서 "자동 launcher"는 Claude Code hooks를 의미하므로 적합하다고 판단.
3. **`CheckBuildProduction` 타임아웃**: 2분(120초). 빌드가 느린 환경에서 타임아웃 시 false → 선언 거부. 의도적 fail-closed.
4. **gate-clean, hs-scan**: 커밋 전 단계라 이 세션에서 실행하지 않음 — NOT_VERIFIED. land gate에서 사람이 확인.

## 직접 경로 사용 사유
`server/TrustOriginCli.cs`는 지시서 allowlist에 명시됨. 직접 경로 사용이 지시서 기본 경로임.
검증 문서(`docs/verification/06c2-r1-trust-origin-production-truth.md`)는 관례상 직접 경로 허용.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005 — 필수, 없으면 "없음"이라고 쓴다)

1. **`CheckAutoLauncherOff` 한계**: hooks 항목 유무만 본다. Claude Code CronCreate 기반 scheduled trigger가 `.claude/settings.json`에 기록되는 방식이 아닐 경우 이 검사를 우회할 수 있다. 현재 프로젝트 상태(hooks 없음)에서는 정확하지만, 더 강한 검사(예: 실행 중인 cron 프로세스 확인)는 구현하지 않았다.
2. **production 선행조건 9·10 검증 범위**: self-test case `production-preconditions-not-default-true`는 build check 실패로 exit 1을 확인한다. launcher와 high-risk check가 "기본 true가 아님"을 증명하는 별도 execution path test는 없다 — 코드 구조(각각 `?? CheckXxx(root)`)로 보장한다.
3. **`trust-origin declare` 전 선행조건 9 미충족 시 운영자 안내 부재**: 현재 오류 메시지는 "자동 launcher hooks 발견 또는 확인 불가"만 출력한다. hooks를 어떻게 끄는지 안내하지 않는다.

## 완료 판정

`PASS | FAIL | BLOCKED` — **생산자가 아니라 검수자가 적는다.**
