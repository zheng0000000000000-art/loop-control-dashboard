# 06C-2-R2 검증 — high-risk check and launcher check truth

## DI 유형 ※필수 (v9 §0.1)

- **선언한 유형**: implementation

## 주체 (actor) ※필수
- **누가**: CORE_INFRA_EXECUTOR (sonnet, claude-sonnet-4-6), 세션 ID 미기록 (대화 세션)
- **경로**: 대화 세션 — 사람이 지시서를 전달, sonnet이 직접 수행

## 반려 사유 닫음 — Codex R1 두 결함

### 결함 1: CheckHighRiskClosed — malformed envelope로 exit 2, trusted-human-receipt-required 미도달

**원인**: 기존 코드가 `{"schemaVersion":1,"transitionKind":"PHASE_CHANGE","transitionId":"..."}` 처럼 필수 필드(`expectedPreStateSha256`, `requestPath`, `requestSha256`, `effectiveAt`, `expectedPostStateSha256`, `candidatePath`)가 없는 envelope를 넣었다. `StateApplierCli.ReadEnvelope`가 필수 필드 누락으로 `null` 반환 → exit 2. `ApplyEnvelopeCore`의 high-risk 분기에 도달하지 못했다.

**수정**: `CheckHighRiskClosed`를 `RunHighRiskEnvelopeCheck(kind)` 헬퍼를 호출하는 구조로 교체. 헬퍼는 필수 필드를 모두 채운 full envelope를 생성한다(플레이스홀더 해시·경로 OK: high-risk 분기는 파일 읽기 전에 반환). PHASE_CHANGE, RECOVERY, REPLAY 세 종류를 모두 검사하고 하나라도 exit 1이 아니면 false를 반환한다.

**근거**: `StateApplierCli.ApplyEnvelopeCore` 코드 순서상 high-risk 분기는 `VerifyApplyEvidence`(파일 접근) 전에 위치한다(StateApplierCli.cs:241-244). 따라서 requestPath/candidatePath가 실제 파일을 가리키지 않아도 high-risk 분기에 도달한다.

### 결함 2: CheckAutoLauncherOff — malformed JSON catch 후 skip, 확인 불가가 통과로 둔갑

**원인**: catch 블록이 `/* 파싱 불가 — 이 파일은 skip */` 으로 무시하고 다음 파일로 넘어갔다. 남은 파일에 hooks가 없으면 true를 반환.

**수정**: catch 블록을 `return false;`로 변경. 파일 있음 + malformed JSON → 확인 불가 → fail-closed.

지시서 규칙:
- 파일 없음 → skip 가능 (유지)
- 파일 있음 + valid JSON + hooks 없음 → 통과 (유지)
- 파일 있음 + valid JSON + hooks 있음 → false (유지)
- 파일 있음 + malformed JSON → **false** (신규 수정)

## 사용한 하네스 ※필수
| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| build-verify | `dotnet build server -c Release -nologo` | 0 | 0 | 경고 0, 오류 0 |
| trust-origin self-test | `dotnet run --project server -c Release -- trust-origin --self-test` | 0 | 0 | casesRun=13, verdict=PASS |
| measure | `dotnet run --project server -c Release -- measure dev-pack` | 0 | 0 | violations=0 |

## self-test 출력 (13개 case 전체)

### high-risk 3종 full envelope case

```
{"case":"high-risk-full-envelope-phase-change","kind":"PHASE_CHANGE","exit":1,"pass":true,"note":"full envelope, trusted-human-receipt-required → exit 1"}
{"case":"high-risk-full-envelope-recovery","kind":"RECOVERY","exit":1,"pass":true,"note":"full envelope, trusted-human-receipt-required → exit 1"}
{"case":"high-risk-full-envelope-replay","kind":"REPLAY","exit":1,"pass":true,"note":"full envelope, trusted-human-receipt-required → exit 1"}
```

### malformed launcher settings case

```
{"case":"launcher-settings-malformed","checkAutoLauncherOffResult":false,"pass":true,"note":"malformed settings.json → 확인 불가 → fail-closed(false)"}
```

### R1 회귀 case (9개 유지)

```
{"case":"declare-ok","exit":0,"recordExists":true,"trustEpoch":1,"commitMatch":true,"noDeclarationCommit":true,"reconciliationVerdict":"VERIFIED_PASS_WITH_KNOWN_EXCEPTIONS","pass":true}
{"case":"no-self-reference","recordExists":true,"hasDeclarationCommit":false,"pass":true}
{"case":"extra-known-exception","exit":1,"recordCreated":false,"pass":true}
{"case":"unlisted-failure","exit":1,"recordCreated":false,"pass":true}
{"case":"redeclare","exit":1,"pass":true}
{"case":"record-path-fixed","exit":0,"newFileCount":1,"onlyCanonicalPath":true,"pass":true}
{"case":"record-ready-flags-complete","recordExists":true,"hasPhaseChangeReady":true,"hasReplayReady":true,"phaseChangeFalse":true,"replayFalse":true,"pass":true}
{"case":"build-verdict-not-forged","exit":1,"recordCreated":false,"pass":true,"note":"BuildVerified=false → 선언 거부 → VERIFIED_PASS record 생성 불가"}
{"case":"production-preconditions-not-default-true","exit":1,"recordCreated":false,"pass":true,"note":"TestPrecon=null: production path에서 build check 실행 — tmpRoot에 dotnet 프로젝트 없어 실패"}
```

## 유형별 필수 검증 ※필수 (v9 §0.1)

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| implementation | 정상·실패·재실행 테스트, 개발 검증 Evidence | self-test 13개 all PASS. 아래 반증 시험 참조 |

## 공통 완료 조건 ※필수

- [x] 선언한 DI 유형의 완료 프로필 충족
- [x] 관련 계약·스키마·문서 갱신 (허용 파일 범위 내)
- [x] 발견된 실패·위험·미확정 사항 기록 (아래 잔여 위험 참조)
- [ ] `WORKSTATE.json` 갱신 — 지시서에 금지됨, 미수행
- [x] 변경 범위 준수(allowlist: server/TrustOriginCli.cs + docs/verification/06c2-r2-*.md)
- [x] 원본 저장소 무단 변경 없음 (commit/push/결재/반입/발사 미수행)

## canonical record 미생성 및 WORKSTATE 무결성 확인

- `docs/handoff/trust-origin/TO-2026-001.json`: 미생성 확인 (`test -f` → 없음 - OK)
- `docs/handoff/WORKSTATE.json`: `git diff --name-only` 결과 변경 없음
- `docs/handoff/WORKSTATE.applier-log.jsonl`: `git diff --name-only` 결과 변경 없음

## 실패 분류와 실패 사례 ※필수 (v9 §0.3)

- **실패 분류**: 없음 (모든 self-test PASS, 위반 0)
- **실패 사례 ID**: 신규 실패 사례 없음

## 참조한 스킬 ※필수
- 없음 (직접 구현)

## 변경 내용
| 파일 | 종류 | 요약 |
| --- | --- | --- |
| server/TrustOriginCli.cs | 수정 | CheckHighRiskClosed: full envelope + 3종 검사 / CheckAutoLauncherOff: malformed → fail-closed / RunHighRiskEnvelopeCheck 신규 / RunCaseHighRiskFullEnvelope 신규 / RunCaseLauncherSettingsMalformed 신규 / RunSelfTestInDir: 13개 case |
| docs/verification/06c2-r2-trust-origin-highrisk-launcher-check.md | 신규 | 이 문서 |

## 반증 시험 (negative test) ※필수

| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 1 | high-risk-full-envelope-phase-change: PHASE_CHANGE full envelope apply | 1 (trusted-human-receipt-required) | 1 | PASS |
| 2 | high-risk-full-envelope-recovery: RECOVERY full envelope apply | 1 | 1 | PASS |
| 3 | high-risk-full-envelope-replay: REPLAY full envelope apply | 1 | 1 | PASS |
| 4 | launcher-settings-malformed: malformed settings.json → CheckAutoLauncherOff | false | false | PASS |
| 5 | extra-known-exception: knownExceptions가 failure보다 많으면 선언 거부 | exit 1, record 미생성 | exit 1, record 미생성 | PASS |
| 6 | unlisted-failure: reconciliation failure 미명시 → 선언 거부 | exit 1, record 미생성 | exit 1, record 미생성 | PASS |
| 7 | redeclare: trustEpoch >= 1 이미 있으면 재선언 거부 | exit 1 | exit 1 | PASS |
| 8 | build-verdict-not-forged: BuildVerified=false → 선언 거부 | exit 1, record 미생성 | exit 1, record 미생성 | PASS |
| 9 | production-preconditions-not-default-true: TestPrecon=null → build 실패 | exit != 0, record 미생성 | exit 1, record 미생성 | PASS |

## 검수 기준 자가점검표
| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | build-verify exit 0 | PASS | `dotnet build server -c Release -nologo` exit 0, 경고 0 |
| 2 | trust-origin --self-test exit 0 | PASS | 13개 case 모두 PASS |
| 3 | high-risk 3종 full envelope case PASS | PASS | PHASE_CHANGE/RECOVERY/REPLAY 각 exit 1 확인 |
| 4 | malformed launcher settings case PASS | PASS | CheckAutoLauncherOff false 반환 확인 |
| 5 | R1 회귀 case 9개 유지 | PASS | 기존 9개 모두 PASS |
| 6 | canonical record 미생성 | PASS | TO-2026-001.json 존재하지 않음 |
| 7 | WORKSTATE/applier-log blob HEAD와 동일 | PASS | git diff 변경 없음 |
| 8 | measure dev-pack violations 0 | PASS | `{"violationCount":0}` |
| 9 | allowlist 준수 | PASS | TrustOriginCli.cs + verification 문서만 수정 |
| 10 | git commit/push/태그 없음 | PASS | 미수행 |

## 게이트 기록
`{"gate":"dev-pack","violations":0,"attempt":2}`

시도 1: violations=1 (maxFunctionLength 117, 원인: 문자열 리터럴 `"{ \"hooks\": "` 내 `{`를 단순 brace 카운터가 여는 중괄호로 오인)
시도 2: violations=0 (malformed 문자열을 `{` 없는 `"malformed-not-json"`으로 교체 후 해소)

## 잔여 위험 · 미확정 사항 ※필수

- `CheckHighRiskClosed(root)` 내부에서 `StateApplierCli.Run`이 `GitTools.FindRepoRoot()`를 호출하므로, WORKSTATE.json이 없는 환경에서 production 선행조건 검사가 실행되면 exit 2를 반환하고 `CheckHighRiskClosed`가 false를 반환한다. 이는 fail-closed이므로 안전하지만, 오류 메시지가 "확인 불가"가 아니라 "high-risk transition fail-closed 확인 필요"로 나온다. 다음 세션에서 오류 메시지를 더 구체화할 수 있다.
- self-test의 `launcher-settings-malformed` case는 `CheckAutoLauncherOff(tmpRoot)`를 직접 호출하며, `GetClaudeSettingsPaths(tmpRoot)`가 yield하는 `~/.claude/settings.json`은 테스트 중 접근하지 않는다(malformed 파일에서 즉시 false 반환). 사용자 홈 디렉토리 설정 파일의 hooks 존재 여부는 이 테스트에서 검증되지 않는다.

## 직접 경로 사용 사유
지시서에 "직접 경로" 명시(`server/TrustOriginCli.cs` allowlist에 포함). verification 문서도 관례상 직접 경로 허용(CLAUDE.md).

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005)

- `production-preconditions-not-default-true` case는 여전히 build check 실패로만 production path 진입을 검증한다. `CheckAutoLauncherOff`와 `CheckHighRiskClosed`의 production path(TestPrecon=null)는 build check보다 늦게 실행되므로 이 case에서 검증되지 않는다. 새로운 `RunCaseHighRiskFullEnvelope`와 `RunCaseLauncherSettingsMalformed` case가 이 두 함수를 직접 검증하는 것으로 보완했다.
- `RunHighRiskEnvelopeCheck`는 `StateApplierCli.Run`을 통해 실제 WORKSTATE.json을 읽는다. 이는 self-test가 실제 저장소 상태에 의존함을 의미한다. WORKSTATE.json이 없거나 손상된 환경에서 self-test가 실패할 수 있다(해당 환경에서는 `LoadWorkstateContextFromRoot`가 exit 2를 반환).

## 완료 판정

`PASS | FAIL | BLOCKED` — **생산자가 아니라 검수자가 적는다.**
