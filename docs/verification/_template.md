# <DI-ID> 검증 — <제목>

> **모든 DI의 검증 문서는 이 템플릿을 쓴다**(v9 §DI-00-02). 절을 지우지 마라 — 해당 없으면 "없음"이라고 쓴다.

## DI 유형 ※필수 (v9 §0.1)

`implementation | schema | documentation | harness | skill | migration | verification | operations` 중 **하나를 선언한다.**

- **선언한 유형**: <유형>

> 유형을 선언하지 않으면 아래 「유형별 필수 검증」 중 무엇을 해야 하는지 정할 수 없다. **선언은 시작 전에 한다.**

## 주체 (actor) ※필수
- **누가**: <sonnet | 코덱스 | 조율자 | 검수자(Claude) | 사람>, 식별자 <PID/세션/커밋 author>
- **경로**: <헤드리스 발사 | 릴레이 | 스케줄 태스크 | 대화 세션>

> 왜 적는가: 같은 오류가 반복될 때 **어느 주체 탓인지** 추적하기 위함. 주체 기록이 없으면 프록시로 추측하게 되고, 그러면 틀린다(FAIL-2026-012 — 커밋 접두사를 행위주체로 오판해 위반 22건 날조).

## 사용한 하네스 ※필수
| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| build-verify | `dotnet run --project server -c Release -- build-verify` | 0 | | |
| verify-behavior | `... -- verify-behavior` | 0 | | behaviorEqual= |
| measure | `... -- measure dev-pack` | 0 | | violations= |
| gate-clean | `... -- gate-clean server` | 1(실행 직후) / 0(커밋 후) | | |
| hs-scan | `... -- hs-scan` | **1** | | |
| <추가> | | | | |

> **판정은 "모두 0"이 아니라 "명령별 기대 exit == 실제 exit"다.** `hs-scan`은 기대 1이다.
> 왜 적는가: **조율자·검수자가 이 목록을 직접 재실행해 대조한다.** 기록하지 않으면 검사할 수 없다.
> **자기보고는 증거가 아니다** — "PASS라고 썼으니 PASS"가 아니라 "재실행 결과가 PASS여야 PASS"다.

## 유형별 필수 검증 ※필수 (v9 §0.1 — 선언한 유형의 행만 채운다)

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| implementation | 정상·실패·재실행 테스트, 개발 검증 Evidence | |
| schema | 정상·비정상 fixture, unknown field·호환성 검사 | |
| documentation | 링크·필수 항목·안정 section ID lint | |
| harness | **positive·negative·결정성·격리** 테스트 | |
| skill | dry-run, 입력 누락, 중단 조건, 부작용 범위 테스트 | |
| migration | dry-run, 반복 실행, 수량·hash 비교, rollback 계획 | |
| verification | 독립 재현, 결과 manifest, 근거 링크 | |
| operations | 권한, 복구, audit, 재실행 안전성 검사 | |

> **하네스를 만들었으면 negative 테스트가 필수다.** 실패시킬 수 없는 검사는 공허하다 — **통과를 믿기 전에 실패시킬 수 있음을 먼저 증명해라.**

## 공통 완료 조건 ※필수 (v9 §0.1 — 전부 체크)

- [ ] 선언한 DI 유형의 완료 프로필 충족
- [ ] 관련 계약·스키마·문서 갱신
- [ ] 발견된 실패·위험·미확정 사항 기록
- [ ] `WORKSTATE.json` 갱신 — **`state-transition`으로만.** 손으로 쓰지 마라(`docs/handoff/RECOVERY.md`)
- [ ] 변경 범위 준수(allowlist) · 파일 claim 규칙 준수
- [ ] 원본 저장소 무단 변경 없음(commit/push/결재/반입/발사는 사람 게이트)

## 실패 분류와 실패 사례 ※필수 (v9 §0.3)

- **실패 분류**: `expected_rejection | known_failure | new_failure | incident | design_learning | 없음`
- **실패 사례 ID 또는 기존 위키 링크**: <`FAIL-2026-0NN` 또는 `docs/wiki/failures/cases/...` 또는 **"신규 실패 사례 없음"**>

> `expected_rejection`(반증 시험이 의도대로 거부된 것)은 **새 위키 문서를 만들지 않는다.**
> `new_failure`·`incident`·`design_learning`·운영자 개입이 필요한 `known_failure`는 **위키 등록이 기본 동작이다.** 코드만 고치고 끝내지 마라.
> **원인이 미확정이면 `원인 상태: 미확정`으로 남긴다.** 추측을 사실로 적지 마라.

## 참조한 스킬 ※필수
- `skills/common/...`

## 변경 내용
| 파일 | 종류(신규/수정) | 요약 |
| --- | --- | --- |

## 반증 시험 (negative test) ※필수
| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |

> **못 한 시험은 "코드 검토로 갈음"이라고 쓰지 말고 `NOT_VERIFIED`라고 써라.** 신고하면 감점이 아니다. **숨기면 반려다.**

## 검수 기준 자가점검표
| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |

## 게이트 기록
`{"gate":"dev-pack","violations":N,"attempt":1}`
<위반이 남으면 목록을 그대로 적는다 — 숨기지 않는다>

## 잔여 위험 · 미확정 사항 ※필수
- <다음 실행자가 가장 먼저 확인할 항목. 없으면 "없음">

## 직접 경로 사용 사유 (썼다면)

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005 — 필수, 없으면 "없음"이라고 쓴다)

> **지표 통과는 완료가 아니다.** 측정을 통과시키려고 우회한 부분, 형식만 맞춘 부분, 목적(왜 이 작업을 하는가)에 못 미친 부분을 **자진 신고**한다.
> 예: "maxFunctionLength 80줄을 맞추려고 함수를 나눈 게 아니라 빈 줄을 지웠다 — 기능 분리는 안 됐다."
> **이 절이 비어 있거나 근거 없이 '전부 달성'이면 조율자가 반려한다.** 정직한 미달 보고는 감점이 아니다.

- (여기에 적는다. 없으면 "없음")

## 완료 판정

`PASS | FAIL | BLOCKED` — **생산자가 아니라 검수자가 적는다.**
