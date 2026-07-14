# 정리 계획서 검증 — Codex Headless Dispatch Cleanup

> **모든 DI의 검증 문서는 이 템플릿을 쓴다**(v9 §DI-00-02). 절을 지우지 마라 — 해당 없으면 "없음"이라고 쓴다.

## DI 유형 ※필수 (v9 §0.1)

- **선언한 유형**: documentation

## 주체 (actor) ※필수
- **누가**: 코덱스, 대화 세션
- **경로**: 대화 세션

## 사용한 하네스 ※필수
| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| measure | `dotnet run --project server -- measure dev-pack` | 0 | 1 | violations=1 (`maxFunctionLength`) |

## 유형별 필수 검증 ※필수 (v9 §0.1 — 선언한 유형의 행만 채운다)

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| documentation | 링크·필수 항목·안정 section ID lint | `CODEX-HEADLESS-DISPATCH-CLEANUP-plan.md`를 신규 작성했고, ADR-015와 launcher 계약의 현재 결론을 참조했다. |

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
- `skills/common/root-cause-diagnosis.md`
- `skills/common/verification.md`

## 변경 내용
| 파일 | 종류(신규/수정) | 요약 |
| --- | --- | --- |
| `docs/plan/wp/CODEX-HEADLESS-DISPATCH-CLEANUP-plan.md` | 신규 | Codex 헤드리스 진입점 부재와 dispatch 스텁 정리 계획서. |
| `docs/verification/codex-headless-dispatch-cleanup-plan.md` | 신규 | 본 계획서 검증 기록. |

## 반증 시험 (negative test) ※필수
| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 1 | 계획서가 지금 launcher 구현을 지시하는지 확인 | 구현 착수 금지 | 구현 착수 금지 | PASS |
| 2 | 계획서가 dispatch를 실제 codex 라우팅으로 부르는지 확인 | 라우팅 아님 | 예약 슬롯/이름표로 명시 | PASS |

## 검수 기준 자가점검표
| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | 현재 사실과 결론 분리 | PASS | "현재 사실"과 Phase별 계획을 분리했다. |
| 2 | TRUSTED_BASELINE 전 구현 금지 | PASS | 상태와 Phase B/C 조건에 명시했다. |
| 3 | ADR-015 종료 조건 명시 | PASS | Phase E에 종료 조건과 종료 작업을 적었다. |

## 게이트 기록
`{"gate":"dev-pack","violations":1,"attempt":1}`

남은 위반:

- `maxFunctionLength`: `server/Harness/HandoffIntegrityChecker.cs:56-317`, value=262. 본 작업은 문서 계획서 작성이며, 해당 하네스 코드는 기존/동시 변경 범위라 직접 수정하지 않았다.

## 잔여 위험 · 미확정 사항 ※필수
- Codex 앱 또는 CLI 배포 상태가 바뀌면 Phase C 착수 전 실측을 다시 해야 한다.
- dev-pack 게이트에 `maxFunctionLength` 위반 1건이 남아 있다. 계획서 자체의 위반은 아니지만 커밋 전 해소 또는 별도 작업 보고가 필요하다.

## 직접 경로 사용 사유 (썼다면)

계획서와 verification 문서 작성은 문서 직접 경로 예외에 해당한다. 코드 구현은 하지 않았다.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005 — 필수, 없으면 "없음"이라고 쓴다)

- 없음

## 완료 판정

`PASS | FAIL | BLOCKED` — **생산자가 아니라 검수자가 적는다.**
