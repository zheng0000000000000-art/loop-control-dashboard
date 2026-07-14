# ADR-015 검증 — Codex 헤드리스 근거 정정

> **모든 DI의 검증 문서는 이 템플릿을 쓴다**(v9 §DI-00-02). 절을 지우지 마라 — 해당 없으면 "없음"이라고 쓴다.

## DI 유형 ※필수 (v9 §0.1)

- **선언한 유형**: documentation

## 주체 (actor) ※필수
- **누가**: 코덱스, 대화 세션
- **경로**: 대화 세션

## 사용한 하네스 ※필수
| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| measure | `dotnet run --project server -- measure dev-pack` | 0 | 0 | violations=0 |

## 유형별 필수 검증 ※필수 (v9 §0.1 — 선언한 유형의 행만 채운다)

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| documentation | 링크·필수 항목·안정 section ID lint | ADR-015, CODEX-HARNESS-LAUNCHER 계약, CODEX-QUEUE의 실행자 근거 문구를 실체 코드와 대조했다. |

## 공통 완료 조건 ※필수 (v9 §0.1 — 전부 체크)

- [x] 선언한 DI 유형의 완료 프로필 충족
- [x] 관련 계약·스키마·문서 갱신
- [x] 발견된 실패·위험·미확정 사항 기록
- [x] `WORKSTATE.json` 갱신 — 해당 없음(상태 전이 아님)
- [x] 변경 범위 준수(allowlist) · 파일 claim 규칙 준수
- [x] 원본 저장소 무단 변경 없음(commit/push/결재/반입/발사는 사람 게이트)

## 실패 분류와 실패 사례 ※필수 (v9 §0.3)

- **실패 분류**: design_learning
- **실패 사례 ID 또는 기존 위키 링크**: 신규 실패 사례 없음

## 참조한 스킬 ※필수
- `skills/common/directive-writing.md`
- `skills/common/executor-launch.md`
- `skills/common/hs-gate.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/verification.md`

## 변경 내용
| 파일 | 종류(신규/수정) | 요약 |
| --- | --- | --- |
| `docs/handoff/decisions/ADR-015-harness-actor-substitution.md` | 수정 | "코덱스 부재" 근거를 "호출 가능한 헤드리스 진입점 부재"와 dispatch 스텁 실체로 정정했다. |
| `docs/plan/wp/CODEX-HARNESS-LAUNCHER-minimal-contract.md` | 수정 | 현 `executor: "codex"`가 LLM 라우팅이 아니라 예약된 외피임을 명시했다. |
| `docs/handoff/CODEX-QUEUE.md` | 수정 | 05H/06H의 코덱스 소유권과 ADR-015상 sonnet 한시 대행 실행자를 분리했다. |
| `docs/verification/adr015-codex-headless-correction.md` | 신규 | 본 정정의 검증 기록. |

## 반증 시험 (negative test) ※필수
| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 1 | `server/DispatchExecutorCli.cs`에서 외부 Codex/Claude 프로세스 호출 여부 확인 | 해당 호출 없음 | 해당 호출 없음 | PASS |
| 2 | `server/OutboxManager.cs`의 executor 허용과 실제 실행 경로 대조 | 허용값과 실행 경로 분리 | `codex` 허용, 실행은 `dispatch-executor` | PASS |

## 검수 기준 자가점검표
| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | "코덱스가 없다"라고 쓰지 않는다 | PASS | ADR-015에 `~/.codex`와 MS Store 앱 존재를 명시했다. |
| 2 | dispatch가 실제 모델 라우팅인 것처럼 쓰지 않는다 | PASS | ADR-015와 launcher 계약에 `DispatchExecutorCli` 스텁 사실을 명시했다. |
| 3 | ADR-015 결론은 유지한다 | PASS | 05H/06H 한시 sonnet 대행 경계와 종료 조건을 유지했다. |

## 게이트 기록
`{"gate":"dev-pack","violations":0,"attempt":1}`

## 잔여 위험 · 미확정 사항 ※필수
- App Execution Alias가 나중에 생기거나 CodexHarnessLauncher가 구현되면 ADR-015 종료 조건을 다시 판정해야 한다.

## 직접 경로 사용 사유 (썼다면)

관례·가이드·handoff 문서 자체 정정이므로 직접 경로 예외를 사용했다. 코드 실행기 구현은 하지 않았다.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005 — 필수, 없으면 "없음"이라고 쓴다)

- 없음

## 완료 판정

`PASS | FAIL | BLOCKED` — **생산자가 아니라 검수자가 적는다.**
