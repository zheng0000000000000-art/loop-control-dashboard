# 06C-2 검증 — trust-origin declare: 부트스트랩 command

> **모든 DI의 검증 문서는 이 템플릿을 쓴다**(v9 §DI-00-02). 절을 지우지 마라 — 해당 없으면 "없음"이라고 쓴다.

## DI 유형 ※필수 (v9 §0.1)

- **선언한 유형**: `implementation`

신규 CLI command(`trust-origin declare`) 구현 + `CliRouter.cs` 배선 수정.

## 주체 (actor) ※필수

- **누가**: CORE_INFRA_EXECUTOR (claude-sonnet-4-6), 대화 세션
- **경로**: 대화 세션 (수동 dispatch — 자동 스케줄러 중단 중)

## 사용한 하네스 ※필수

| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| build-verify | `dotnet run --project server -c Release -- build-verify` | 0 | 0 | PASS, 경고 0, 오류 0 |
| verify-behavior | `dotnet run --project server -c Release -- verify-behavior` | 0 | 0 | behaviorEqual=true |
| measure dev-pack | `dotnet run --project server -c Release -- measure dev-pack` | 0 | 0 | violations=0, attempt=1 |
| gate-clean (pre-commit) | `dotnet run --project server -c Release -- gate-clean server` | 1 | 1 | contentDirtyCount=2 (CliRouter.cs 수정, TrustOriginCli.cs 신규) |
| hs-scan | `dotnet run --project server -c Release -- hs-scan` | 1(템플릿 기본) | 0 | HS-GATE 수행 의무 메시지 출력 — 주석 참조 |
| trust-origin --self-test | `dotnet run --project server -c Release -- trust-origin --self-test` | 0 | 0 | 5/5 PASS |
| handoff-integrity (at-rest) | `dotnet run --project server -c Release -- handoff-integrity` | 1 | 1 | FAIL, failures=[DI0004-BLOCKED-CODEX] — 정상(부트스트랩 이전 예상 상태) |

> **hs-scan**: 템플릿은 기대 exit 1로 표시되어 있으나 실제 exit 0이었다. hs-scan은 후보 목록이 있을 때 exit 0으로 "수행 의무" 메시지를 출력하는 방식으로 동작하는 것으로 보인다. 06C-2 범위 밖이므로 NOT_VERIFIED로 남긴다.

## 유형별 필수 검증 ※필수 (v9 §0.1 — 선언한 유형의 행만 채운다)

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| implementation | 정상·실패·재실행 테스트, 개발 검증 Evidence | 완료 — `--self-test` 5개 case: declare-ok/no-self-reference/unlisted-failure/redeclare/record-path-fixed 전부 PASS. 반증(판정선 3·4·8) 실증 완료. 아래 「반증 시험」 참조. |

## 공통 완료 조건 ※필수 (v9 §0.1 — 전부 체크)

- [x] 선언한 DI 유형의 완료 프로필 충족 (`--self-test` 5개 케이스 + measure violations=0)
- [x] 관련 계약·스키마·문서 갱신 (`TRUST-ORIGIN-BOOTSTRAP.md` 부록 A 사람 결재 반영, `knownExceptions[]` 스키마 구현)
- [x] 발견된 실패·위험·미확정 사항 기록 (아래 「잔여 위험」 참조)
- [ ] `WORKSTATE.json` 갱신 — 미수행. state-transition은 사람 게이트 후 수행. 현재 epoch=0 (부트스트랩 이전)
- [x] 변경 범위 준수(allowlist) · 파일 claim 규칙 준수 — allowlist: `server/TrustOriginCli.cs`, `server/Cli/CliRouter.cs`, `docs/verification/06c2-trust-origin.md`만 변경
- [x] 원본 저장소 무단 변경 없음 — commit/push/결재 없음. `docs/handoff/trust-origin/TO-2026-001.json` canonical record 미생성.

## 실패 분류와 실패 사례 ※필수 (v9 §0.3)

- **실패 분류**: `expected_rejection`(반증 시험), `design_learning` 1건 (아래)
- **실패 사례 ID 또는 기존 위키 링크**:
  - 신규 실패 사례 없음 (반증 시험은 의도된 거부)
  - `design_learning`: 최초 구현 시 `RunDeclareCore`가 183줄 → maxFunctionLength 위반 → 6개 함수로 분리. 위반 횟수 attempt=1(빌드 오류 0)+1(measure 위반 발견)=measure 1회 재측정. 기존 FAIL 위키 없음; 교훈: 함수 분리는 작성 후 measure 전에 미리 계획해야 한다.

## 참조한 스킬

- `skills/common/` (공통)
- StateApplierCli.RunSelfTest() 패턴 참조 (temp dir·in-process seam)

## 변경 내용

| 파일 | 종류(신규/수정) | 요약 |
| --- | --- | --- |
| `server/TrustOriginCli.cs` | 신규 | trust-origin declare + --self-test CLI. 10개 선행조건 검사, knownExceptions[] subset 검사, atomic-create, declarationCommit 미포함. |
| `server/Cli/CliRouter.cs` | 수정 | `"trust-origin"` → `TrustOriginCli.Run(args)` 배선 추가. `OwnCommandNames`에 등록. HarnessRegistry 미등록(write command). |
| `docs/verification/06c2-trust-origin.md` | 신규 | 이 문서. |

## 반증 시험 (negative test) ※필수

| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 판정선 3 | unlisted-failure: UNLISTED-TRANSITION이 state에 있고 knownExceptions 비어 있을 때 declare | 1 (거부) | 1 | PASS — self-test case `unlisted-failure`, recordCreated=false |
| 판정선 4 | self-falsification: 기대 epoch를 2로 바꿔 --self-test 실행 | 1 (FAIL) | 1 | PASS — mismatches=[{case:declare-ok, expected:epoch=1→2}], mismatchCount=1 |
| 판정선 8 | 비대화형 거부: `echo "" \| dotnet run ... trust-origin declare --ack BOOTSTRAP_TRUST_ORIGIN` | 1 (거부) | 1 | PASS — `non-interactive-rejected` 코드 출력 |
| 판정선 9 | canonical repo에 trust-origin record 없음 | 경로 없음 | `docs/handoff/trust-origin/` 경로 없음 | PASS |
| 판정선 10 | at-rest handoff-integrity: DI0004-BLOCKED-CODEX failure | 1 | 1 | PASS — failures=[DI0004-BLOCKED-CODEX], reconciliation=FAIL |
| 재선언 | trustEpoch=1 기존 record 있을 때 declare | 1 (거부) | 1 | PASS — self-test case `redeclare` |
| record 경로 고정 | declare 후 신규 파일이 canonical path 1개만 생성됨 | exit 0, newFileCount=1 | 0, newFileCount=1 | PASS — self-test case `record-path-fixed`, onlyCanonicalPath=true |
| 자기참조 없음 | declare 결과 record에 declarationCommit 필드 없음 | hasDeclarationCommit=false | false | PASS — self-test case `no-self-reference` |
| 서브명령 없음 | `trust-origin` 인수 없이 실행 | 2 | NOT_VERIFIED | 코드 검토: `Run()` 진입점에서 sub=="" 시 usage 출력 후 return 2. 직접 실행 미확인. |
| HarnessError 시 거부 | reconciliation 자체 실패(파일 없음 등) 시 declare | 2 (거부) | NOT_VERIFIED | 코드 검토: `HarnessErrors.Count > 0` → WriteError + return (null, 2). in-process 시험 미수행. |

## 검수 기준 자가점검표

| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | build exit 0 | PASS | build-verify exit 0, 경고 0 |
| 2 | 선행 10조건 + declare → record 생성, epoch=1 | PASS | self-test declare-ok: exit=0, recordExists, epoch=1, commitMatch |
| 3 | 재선언 거부 | PASS | self-test redeclare: exit=1 |
| 4 | record 외 파일 변경 시도 거부 | PASS | self-test record-path-fixed: newFileCount=1 (canonical only) |
| 5 | declarationCommit 없음·tag로 연결 | PASS(부분) | self-test no-self-reference: hasDeclarationCommit=false. tag 연결은 Phase B 사람 절차 — 이 DI 범위 외 |
| 6 | 비대화형 실행 기본 거부 | PASS | pipe stdin: exit=1, non-interactive-rejected |
| 7 | measure dev-pack → 0 | PASS | violations=0 |
| 8 | knownExceptions[] subset 검사 (부록 A 정정 반영) | PASS | unlisted-failure case: 미명시 failure → exit 1 |
| 9 | HarnessRegistry 미등록 | PASS | CliRouter.cs에 명시 배선, HarnessRegistry 코드 없음 |
| 10 | state-transition apply --bootstrap 없음 | PASS | 코드에 없음; declare는 StateApplier 호출 안 함 |

## 게이트 기록

`{"gate":"dev-pack","violations":0,"attempt":2}`

> attempt=2: 1회차는 `RunDeclareCore` 183줄 위반 발견 → 함수 분리 → 2회차 violations=0 통과.

## 잔여 위험 · 미확정 사항 ※필수

1. **CheckFilesTracked·CheckNoDirectMod는 production 경로에서만 실행됨** — self-test는 `TestPrecon`으로 우회. git 명령이 clean worktree에서 올바르게 동작하는지 Phase B 실제 실행 전까지 실증 없음.
2. **비대화형 거부는 약한 안전장치** — `Console.IsInputRedirected || !Environment.UserInteractive`는 신원 증명이 아님. CI 환경에서도 이 조건이 false일 수 있음 (`HUMAN_DECLARED_NOT_CRYPTOGRAPHICALLY_VERIFIED` 정책과 일관).
3. **Phase B (실제 선언 절차)는 미수행** — `WORKSTATE.json` 갱신, trust-origin record canonical 생성, git commit/tag는 사람이 직접 수행해야 함. 이 DI는 CLI tool 구현만 완료.
4. **DI0004-BLOCKED-CODEX knownExceptions 등록** — 실제 Phase B 선언 시 `knownExceptions-file`에 DI0004-BLOCKED-CODEX를 명시해야 함. 미명시 시 declare 거부. (부록 A 정정 핵심 안전장치)
5. **hs-scan 기대 exit 불일치** — 템플릿 기대 1 vs 실제 0. hs-scan 동작 계약 불분명. NOT_VERIFIED.

## 직접 경로 사용 사유

없음. 모든 파일 변경은 지시서 allowlist 내에서 직접 편집으로 완료. dispatch/outbox 불필요(allowlist 명시 직접 경로).

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005 — 필수)

1. **maxFunctionLength 위반 후 분리**: `RunDeclareCore` 183줄 → 80줄 제약으로 6개 함수로 분리했으나, 분리의 계기가 기능 단위 설계가 아닌 지표 위반 발견이었다. 결과적으로 함수 경계는 의미 있게 나눠졌으나(epoch guard / reconciliation check / precondition / baseline commit / record build), 최초 설계 단계에서 계획됐어야 한다.

2. **CheckFilesTracked·CheckNoDirectMod in-process 미검증**: self-test가 `TestPrecon`으로 git 명령 경로를 우회하므로, 실제 git 명령의 정확성은 production 실행에서만 확인 가능하다. in-process 시험으로는 이 경로의 정확성을 보장하지 못한다.

3. **서브명령 없음·HarnessError 반증 시험 NOT_VERIFIED**: 코드 검토로만 갈음했다. in-process 시험이 없으므로 회귀 위험이 남아 있다.

## 완료 판정

`PASS | FAIL | BLOCKED` — **생산자가 아니라 검수자가 적는다.**
