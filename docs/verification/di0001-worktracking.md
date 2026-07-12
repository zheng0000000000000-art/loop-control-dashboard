# DI-00-01 검증 — 상태 원본이 거짓말을 못 하게 만든다

## 주체 (actor) ※필수
- **누가**: claude-sonnet-4-6, 대화 세션
- **경로**: 대화 세션 (릴레이 없음, 헤드리스 발사 없음)

> 왜 적는가: 같은 오류가 반복될 때 **어느 주체 탓인지** 추적하기 위함. 주체 기록이 없으면 프록시로 추측하게 되고, 그러면 틀린다(FAIL-2026-012).

## 사용한 하네스 ※필수
| 하네스 | 명령 | exit | 결과(핵심 수치) |
| --- | --- | --- | --- |
| build-verify | `dotnet run --project server -c Release -- build-verify` | 0 | buildVerifyVerdict=PASS, warnings=0, errors=0 |
| verify-behavior | `dotnet run --project server -c Release -- verify-behavior` | 0 | behaviorEqual=true |
| measure dev-pack | `dotnet run --project server -c Release -- measure dev-pack` | 0 | violations=0, attempt=2 (첫 시도 maxFunctionLength=84줄→함수 분리 후 재측정) |
| handoff-integrity | `dotnet run --project server -c Release -- handoff-integrity` | 0 | PASS |
| di-completion-check | `dotnet run --project server -c Release -- di-completion-check --gate POST-EXECUTOR --task DI-00-01` | 0 | gateVerdict=PASS |
| projection | `dotnet run --project server -c Release -- projection` | 0 | STATUS.md 생성 포함 |
| gate-clean server | `dotnet run --project server -c Release -- gate-clean server` | 1 | 미커밋 WIP 파일 존재(DI-00-01 작업물) — 예상된 결과 |

## 반증 시험 (9개)
| # | 설명 | 기대 exit | 실제 exit | 결과 |
| --- | --- | --- | --- | --- |
| TEST-DI0001-1 | completed → in_progress (human-decision 없음) | 1 | 1 | PASS |
| TEST-DI0001-2 | completed → in_progress (human-decision approved=true) | 0 | 0 | PASS |
| TEST-DI0001-3 | waiting → completed (verifying 단계 건너뜀) | 1 | 1 | PASS |
| TEST-DI0001-4 | waiting → waiting (nextActions만 갱신) | 0 | 0 | PASS |
| TEST-DI0001-5 | wpId=WP-99 (WP-REGISTRY 미등록) | 1 | 1 | PASS |
| TEST-DI0001-6 | WORKSTATE diId=LEDGER-04 (비canonical 패턴) | 1 | 1 | PASS |
| TEST-DI0001-7 | projection 두 번 연속 → STATUS.md sha256 동일(멱등) | sha 동일 | sha 동일 | PASS |
| TEST-DI0001-8 | STATUS.md 손편집 후 projection → 덮어써짐 | 손편집 제거됨 | 손편집 제거됨 | PASS |
| TEST-DI0001-9 | waiting → in_progress 정상 전이 (exit 0, 3파일 상태 일치) | 0, 일치 | 0, 일치 | PASS |

## 참조한 스킬 ※필수
- `docs/handoff/queue/directive-DI-00-01-worktracking.md` (지시서)
- `docs/directives/_header.md` (불변 제약 + Context Pack 형식)
- `docs/handoff/RULES-RATIONALE.md` (규칙 근거)

## 변경 내용
| 파일 | 유형 | 내용 |
| --- | --- | --- |
| `docs/handoff/WP-REGISTRY.json` | 신규 | WP 목록 단일 원본. WP-00 항목 1개. |
| `server/StateApplierCli.cs` | 수정 | AllowedTransitions 허용 전이 whitelist 추가. completed 터미널 강제(human-decision 없이 전이 불가). ValidateCandidate에 canonical 패턴 검사 + WP-REGISTRY wpId 검증 추가. |
| `server/ProjectionCli.cs` | 수정 | GenerateStatusMd + AppendStatusWpTable 추가. SelfExcluded에 STATUS.md 포함. projection 실행 시 docs/STATUS.md 자동 생성. |

## 검수 기준 자가점검표
| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | WP-REGISTRY.json 생성 | PASS | `docs/handoff/WP-REGISTRY.json` 신규 파일, WP-00 항목 포함 |
| 2 | 허용 전이 whitelist 구현 | PASS | AllowedTransitions dict, TEST-DI0001-1/3/5 반증 검증 |
| 3 | completed 터미널(human-decision 없이 불가) | PASS | TEST-DI0001-1 exit 1 PASS, TEST-DI0001-2 exit 0 PASS |
| 4 | ValidateCandidate canonical 패턴 검사 | PASS | TEST-DI0001-6 LEDGER-04(비canonical) exit 1 PASS |
| 5 | ValidateCandidate WP-REGISTRY wpId 검증 | PASS | TEST-DI0001-5 WP-99 exit 1 PASS |
| 6 | STATUS.md projection 자동 생성(손편집 불가) | PASS | TEST-DI0001-7(멱등) + TEST-DI0001-8(덮어씀) PASS |
| 7 | measure dev-pack violations=0 | PASS | exit 0, violations=0 |
| 8 | build-verify PASS | PASS | exit 0, behaviorEqual=true |
| 9 | handoff-integrity PASS | PASS | exit 0 |
| 10 | 반증 시험 9개 전부 PASS | PASS | 위 표 참조 |

## 게이트 기록
`{"gate":"dev-pack","violations":0,"attempt":2}`

1차 시도: maxFunctionLength=84줄(GenerateStatusMd 함수, 허용 80줄). AppendStatusWpTable 헬퍼 추출 후 재측정 → violations=0.

## 직접 경로 사용 사유 (썼다면)

**WORKSTATE.json 직접 Write 복구**: 테스트 1~2 사이에 `git checkout docs/handoff/WORKSTATE.json`을 실행해 커밋된 구버전(status=verifying, diId=LEDGER-04, schemaVersion v9-min)으로 복원됨. DI-00-01 작업 상태(status=waiting, diId=DI-00-01, schemaVersion=3)가 소실됨. 세션 메모리에서 내용 재구성 후 Write 툴로 직접 복원. 이후 projection 실행으로 derived files 재생성. 비정상 경로이며 보고 의무 대상.

**STATUS.md**: 이전까지 손편집 파일이었으나 projection이 자동 생성하도록 변경. 이번 projection 실행으로 기존 손편집본을 기계 생성본으로 대체.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005 — 필수)

1. **git checkout 실수로 WORKSTATE 작업본 소실 → Write 툴 직접 복구**: 정상적 state-transition 흐름을 벗어났다. Write 툴로 WORKSTATE를 직접 쓰는 행위는 단일 writer 원칙(STATE-01)에 반한다. 이번은 비상 복구였고 projection으로 derived files를 재생성했으나, 복구 경로 자체는 감사 추적이 끊긴다. 다음번에는 git stash 또는 내용 메모 후 git checkout을 시도해야 한다.

2. **TEST-3 초기 실행이 LEDGER-04 WORKSTATE 상태에서 수행됨**: git checkout 실수 직후 WORKSTATE가 status=verifying/diId=LEDGER-04였던 시점에 TEST-3을 실행했다. verifying → completed는 허용 전이여서 예기치 않게 exit 0이 나왔다. WORKSTATE 복구 후 재실행 결과(exit 1)가 실제 PASS 판정 근거다. 초기 실행 결과는 유효하지 않음을 명시한다.

3. **WP-REGISTRY 기계 생성 아님**: WP-REGISTRY.json은 수동으로 작성했다. 내용은 현재 WORKSTATE와 일치하지만, WORKSTATE에서 자동 파생되지 않으므로 장기적으로 불일치 가능성이 있다. 지시서 범위 내에서의 구현이므로 이 회차에서 해결하지 않는다.
