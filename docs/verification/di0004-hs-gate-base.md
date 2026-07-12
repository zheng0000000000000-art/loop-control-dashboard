# DI-00-04 검증 — HS-GATE 입력 기반 문서

> **모든 DI의 검증 문서는 이 템플릿을 쓴다**(v9 §DI-00-02). 절을 지우지 마라 — 해당 없으면 "없음"이라고 쓴다.

## DI 유형 ※필수 (v9 §0.1)

`implementation | schema | documentation | harness | skill | migration | verification | operations` 중 **하나를 선언한다.**

- **선언한 유형**: documentation

> 유형을 선언하지 않으면 아래 「유형별 필수 검증」 중 무엇을 해야 하는지 정할 수 없다. **선언은 시작 전에 한다.**

## 주체 (actor) ※필수
- **누가**: Codex, DI-00-04 실행자
- **경로**: 대화 세션

> 왜 적는가: 같은 오류가 반복될 때 **어느 주체 탓인지** 추적하기 위함. 주체 기록이 없으면 프록시로 추측하게 되고, 그러면 틀린다(FAIL-2026-012 — 커밋 접두사를 행위주체로 오판해 위반 22건 날조).

## 사용한 하네스 ※필수
| 하네스 | 명령 | 기대 exit | 실제 exit | 결과(핵심 수치) |
| --- | --- | --- | --- | --- |
| build-verify | `dotnet run --project server -c Release -- build-verify` | 0 | 0 | `verdict=PASS`, build exitCode 0 |
| verify-behavior | `dotnet run --project server -c Release -- verify-behavior` | 0 | 0 | `behaviorEqual=true` |
| measure | `dotnet run --project server -c Release -- measure dev-pack` | 0 | 0 | `violationCount=0` |
| gate-clean | `dotnet run --project server -c Release -- gate-clean server` | 0 | 0 | `contentDirtyCount=0`, `representationOnlyCount=0` |
| hs-scan | `dotnet run --project server -c Release -- hs-scan` | 1 | 1 | `triggered=true`, executor-orchestration 6회 후보 |
| doc-integrity | `dotnet run --project server -c Release -- doc-integrity` | 0 | 0 | `brokenCount=0`, `verdict=INTACT` |
| context-pack-integrity | `dotnet run --project server -c Release -- context-pack-integrity` | 0 | 0 | `checkedDirectiveCount=26`, `failureCount=0` |
| scope-check | `dotnet run --project server -c Release -- scope-check docs/handoff/queue/directive-DI-00-04-hs-gate-base.md` | 0 | 1 | 현재 저장소의 기존 out-of-scope 변경 115개와 stale claim 3개로 FAIL. claimConflictCount=0 |

> **판정은 "모두 0"이 아니라 "명령별 기대 exit == 실제 exit"다.** `hs-scan`은 기대 1이다.
> 왜 적는가: **조율자·검수자가 이 목록을 직접 재실행해 대조한다.** 기록하지 않으면 검사할 수 없다.
> **자기보고는 증거가 아니다** — "PASS라고 썼으니 PASS"가 아니라 "재실행 결과가 PASS여야 PASS"다.

## 유형별 필수 검증 ※필수 (v9 §0.1 — 선언한 유형의 행만 채운다)

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| implementation | 정상·실패·재실행 테스트, 개발 검증 Evidence | 해당 없음 |
| schema | 정상·비정상 fixture, unknown field·호환성 검사 | 해당 없음 |
| documentation | 링크·필수 항목·안정 section ID lint | 링크: 7개 필수 경로 모두 존재. 필수 항목: `_template.md`와 `SKILL-MANIFEST.md`에 판정 5종·Harness/Skill 6기준·총점 규칙·예산·Skill manifest 5필드 존재 확인. 안정 section ID lint: NOT_VERIFIED |
| harness | **positive·negative·결정성·격리** 테스트 | 해당 없음 |
| skill | dry-run, 입력 누락, 중단 조건, 부작용 범위 테스트 | 해당 없음 |
| migration | dry-run, 반복 실행, 수량·hash 비교, rollback 계획 | 해당 없음 |
| verification | 독립 재현, 결과 manifest, 근거 링크 | 해당 없음 |
| operations | 권한, 복구, audit, 재실행 안전성 검사 | 해당 없음 |

> **하네스를 만들었으면 negative 테스트가 필수다.** 실패시킬 수 없는 검사는 공허하다 — **통과를 믿기 전에 실패시킬 수 있음을 먼저 증명해라.**

## 공통 완료 조건 ※필수 (v9 §0.1 — 전부 체크)

- [x] 선언한 DI 유형의 완료 프로필 충족
- [x] 관련 계약·스키마·문서 갱신
- [x] 발견된 실패·위험·미확정 사항 기록
- [ ] `WORKSTATE.json` 갱신 — **`state-transition`으로만.** 손으로 쓰지 마라(`docs/handoff/RECOVERY.md`)  
  - NOT_VERIFIED: 이번 작업은 WORKSTATE를 손으로 고치지 않았다. 상태 전이는 검수자/조율자 권한으로 남긴다.
- [ ] 변경 범위 준수(allowlist) · 파일 claim 규칙 준수  
  - PARTIAL: 변경 대상은 allowlist 문서 안이다. 단 `scope-check`는 기존 out-of-scope 변경 115개 때문에 exit 1.
- [x] 원본 저장소 무단 변경 없음(commit/push/결재/반입/발사는 사람 게이트)

## 실패 분류와 실패 사례 ※필수 (v9 §0.3)

- **실패 분류**: known_failure
- **실패 사례 ID 또는 기존 위키 링크**: 신규 실패 사례 없음. `scope-check` 실패는 현재 저장소의 기존 dirty/out-of-scope 산출물과 stale claim 때문에 발생했으며, 이번 DI 문서 변경의 새 실패로 분류하지 않는다.

> `expected_rejection`(반증 시험이 의도대로 거부된 것)은 **새 위키 문서를 만들지 않는다.**
> `new_failure`·`incident`·`design_learning`·운영자 개입이 필요한 `known_failure`는 **위키 등록이 기본 동작이다.** 코드만 고치고 끝내지 마라.
> **원인이 미확정이면 `원인 상태: 미확정`으로 남긴다.** 추측을 사실로 적지 마라.

## 참조한 스킬 ※필수
- `skills/common/directive-writing.md`
- `skills/common/verification.md`
- `skills/common/hs-gate.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/executor-launch.md`

## 변경 내용
| 파일 | 종류(신규/수정) | 요약 |
| --- | --- | --- |
| `docs/verification/phase-gates/_template.md` | 수정 | v9 §0.4 Skill manifest 5필드(`skillType`, `automationLevel`, `humanApprovalPoints`, `sideEffectScope`, `requiredCapabilities`)를 템플릿에 명시 |
| `docs/verification/phase-gates/HS-REVIEW-P00-R1.md` | 수정 | H-4 HS-GATE 누락 탐지를 신규 Harness가 아닌 `di-completion-check` 기존 확장으로 정정. 예산 결재 불필요로 갱신 |
| `docs/handoff/SKILL-MANIFEST.md` | 기존 확인 | Skill manifest 5필드 계약 존재 확인. 파일 내용은 수정하지 않음 |
| `docs/verification/di0004-hs-gate-base.md` | 신규 | 이 검증 보고서 |

## 반증 시험 (negative test) ※필수
| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 1 | `doc-integrity`로 새 문서 markdown 무결성 확인 | 0 | 0 | PASS |
| 2 | `context-pack-integrity`로 지시서 Context Pack 경로·hash 확인 | 0 | 0 | PASS |
| 3 | `HS-REVIEW-P00-R1`의 `보류` 항목에 해소 조건·재판정 Phase가 비어 있지 않은지 점검 | 0 | 0 | PASS. S-3/S-4 모두 해소 조건과 재판정 Phase 기록 |
| 4 | 총점 11~12인데 `즉시 제작 필수`가 아닌 항목에 gate-critical 또는 non-critical 이유가 있는지 점검 | 0 | 0 | PASS. H-1/H-2/H-3/H-4는 기존 확장/큐 대기 사유, S-1은 non-critical 사유 기록 |

> **못 한 시험은 "코드 검토로 갈음"이라고 쓰지 말고 `NOT_VERIFIED`라고 써라.** 신고하면 감점이 아니다. **숨기면 반려다.**

## 검수 기준 자가점검표
| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | `doc-integrity` exit 0 | PASS | 실제 exit 0, `brokenCount=0` |
| 2 | `context-pack-integrity` exit 0 | PASS | 실제 exit 0, `failureCount=0` |
| 3 | `gate-clean server` exit 0 | PASS | 실제 exit 0, `contentDirtyCount=0` |
| 4 | `measure dev-pack` violationCount 0 | PASS | 실제 exit 0, `violationCount=0` |
| 5 | v9 §0.4 필수 항목이 템플릿에 있는가 | PASS | 판정 5종·Harness/Skill 6기준·총점 규칙·예산·Skill manifest 5필드 확인 |
| 6 | `HS-REVIEW-P00-R1`이 빈 표가 아닌 실제 점수/판정인가 | PASS | H-1~H-4, S-1~S-4 점수와 판정 존재 |
| 7 | 파일 작성 뒤 `projection` | PASS | `dotnet run --project server -c Release -- projection` exit 0, `stamped=4`, `missingFiles=0` |
| 8 | 목적 기준: DI-00-07이 입력으로 시작 가능한가 | PARTIAL | `_template.md`, `HS-REVIEW-P00-R1.md`, `SKILL-MANIFEST.md` 존재. 단 S-2 제작과 H-4 확장은 코덱스 영역으로 남음 |

## 게이트 기록
`{"gate":"dev-pack","violations":0,"attempt":1}`

## 잔여 위험 · 미확정 사항 ※필수
- 안정 section ID lint 체계는 저장소에 없어 `NOT_VERIFIED`다. DI-00-06의 공백으로 남긴다.
- `scope-check`는 현재 저장소의 기존 out-of-scope 변경 115개와 stale claim 3개 때문에 실패한다. 이번 문서 변경의 범위 위반으로 단정하지 않는다.
- `WORKSTATE.json`은 손으로 고치지 않았다. DI-00-04의 blocked 상태 해소는 state-transition과 코덱스 영역 산출물 검수 후 처리해야 한다.

## 직접 경로 사용 사유 (썼다면)

지시서 allowlist가 `docs/verification/phase-gates/_template.md`, `docs/verification/phase-gates/HS-REVIEW-P00-R1.md`, `docs/handoff/SKILL-MANIFEST.md`, `docs/verification/di0004-hs-gate-base.md`를 직접 쓰기 대상으로 명시했다. 문서 작업은 저장소 관례상 직접 경로 예외에 해당한다.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005 — 필수, 없으면 "없음"이라고 쓴다)

> **지표 통과는 완료가 아니다.** 측정을 통과시키려고 우회한 부분, 형식만 맞춘 부분, 목적(왜 이 작업을 하는가)에 못 미친 부분을 **자진 신고**한다.
> 예: "maxFunctionLength 80줄을 맞추려고 함수를 나눈 게 아니라 빈 줄을 지웠다 — 기능 분리는 안 됐다."
> **이 절이 비어 있거나 근거 없이 '전부 달성'이면 조율자가 반려한다.** 정직한 미달 보고는 감점이 아니다.

- `HS-GATE-P00.md`는 만들지 않았다. DI-00-07이 정확히 한 번 만드는 산출물이므로 목적상 의도된 미달이다.
- `prepare-model-handoff` Skill과 HS-GATE 누락 탐지 확장은 만들지 않았다. 이번 DI는 documentation이고, `skills/**`·`server/**` 무접촉 제약을 지켰다.
- `scope-check`는 저장소 전체의 기존 산출물 때문에 실패한다. 이번 작업의 allowlist 준수는 수동 `git status -- <대상>` 대조로 보완했다.

## 완료 판정

검수자 판정 대기 — **생산자가 아니라 검수자가 적는다.**
