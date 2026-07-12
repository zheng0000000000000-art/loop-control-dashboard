# HS-REVIEW-P00-R1 — Phase 0 Harness·Skill 판정 (1차)

> **이 문서는 `HS-GATE-P00.md`가 아니다.** `HS-GATE-P00.md`는 `DI-00-07`이 정확히 한 번 만든다(v9 §DI-00-07).
> 이 문서는 Phase 0 진행 중 후보를 점수화하고 판정하는 중간 검토 기록이다 (v9 §0.4 1항).
> **템플릿**: `docs/verification/phase-gates/_template.md`
> **점수화 기준**: v9 §0.4 Harness 6기준·Skill 6기준. 각 0~2, 총 0~12.

판정일: 2026-07-12
판정 주체: sonnet (DI-00-04 실행자)
운영 등급: Required Before Multi-model Parallel Work

---

## Phase 0에서 새로 안정된 계약과 반복 절차

| 계약·절차 | 확립 시점 | 근거 |
| --- | --- | --- |
| `WORKSTATE.json` schemaVersion 3 (blockers[]) | DI-00-01 / STATE-01 | CONFORMANCE §DI-00-01 |
| 상태 전이 화이트리스트 (역방향 차단) | DI-00-01 | CONFORMANCE §DI-00-01 |
| verification `_template.md` — DI 유형 8종·6공통 완료 조건 | DI-00-02 | CONFORMANCE §DI-00-02 |
| 실패 사례 위키 구조 (FAIL-YYYY-NNN·by-component·by-failure-class) | DI-00-03 | CONFORMANCE §DI-00-03 |
| `handoff-integrity` 하네스 (changedFiles sha256 대조) | P0-03 | CONFORMANCE §DI-00-05 |
| `context-pack-integrity` 하네스 (requiredInputs 경로·hash 검사) | P0-05 | CONFORMANCE §DI-00-06 |
| Context Pack 형식 (`context-pack` 블록, diId·requiredInputs·readOrder·forbiddenActions) | _header.md 신설 | DI-00-04 directive |
| `gate-clean`·`claim-check`·`hs-scan`·`doc-integrity` 하네스 | Phase 0 중 | GATE-MANIFEST.json |
| WP-REGISTRY.json (WP 목록 정본) | DI-00-01 완료 | CONFORMANCE §DI-00-01 갱신 |

---

## Harness 후보 판정

> 기준: 반복 검증 가치·결정 가능성·장애 주입 가치·격리 가능성·관찰 가능성·유지 비용 (각 0~2)
> 이미 CODEX-GATE-02 큐에 있는 항목은 `기존 항목 확장`으로 판정하고 해당 사실을 기재한다 (중복 제작 금지).

| 후보 | 반복성 | 결정가능성 | 장애주입 | 격리 | 관찰성 | 유지가치 | 총점 | 판정 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| H-1: `cli-contract-check` | 2 | 2 | 2 | 2 | 2 | 2 | **12** | 기존 항목 확장 |
| H-2: `handoff-integrity` 멱등 대조 확장 | 2 | 2 | 2 | 2 | 2 | 2 | **12** | 기존 항목 확장 |
| H-3: `claim-check --untracked` | 2 | 2 | 2 | 2 | 2 | 2 | **12** | 기존 항목 확장 |
| H-4: HS-GATE 누락 탐지 검사 | 2 | 2 | 2 | 2 | 2 | 2 | **12** | 즉시 제작 필수 |

### H-1: `cli-contract-check` (CLI 배선 계약 대조) — 기존 항목 확장

**근거 데이터**: `state-transition` 배선이 통째로 사라졌는데 `di-completion-check`가 5/5 PASS를 줬다 (실측 사고. CONFORMANCE §DI-00-04 "배선이 통째로 사라졌는데 게이트가 **5/5 PASS**를 줬다").

| 기준 | 점수 | 근거 |
| --- | --- | --- |
| 반복 검증 가치 | 2 | DI마다 CLI 라우팅 변경 가능성. state-transition 배선 누락으로 실측 사고 1건 |
| 결정 가능성 | 2 | `CliRouter` 등록 목록 vs 계약 목록 대조 = 완전 기계 판정 |
| 장애 주입 가치 | 2 | `CliRouter.cs`에서 명령 제거 → 재현 자명 |
| 격리 가능성 | 2 | 소스 읽기 전용. 저장소 상태 변경 없음 |
| 관찰 가능성 | 2 | 누락된 명령 이름과 위치를 분해 출력 가능 |
| 유지 비용 | 2 | CLI 라우팅 구조가 안정적. 게이트가 의존 |

**판정: 기존 항목 확장** — CODEX-GATE-02 큐 C-01 "CLI 계약" 항목으로 이미 등재됨. 중복 제작 금지. 코덱스가 기존 `di-completion-check` 또는 신규 CLI 계약 검사에 확장 형태로 구현.

### H-2: `handoff-integrity` 멱등 대조 확장 — 기존 항목 확장

**근거 데이터**: WORKSTATE 손복구로 멱등이 깨졌는데 `handoff-integrity`가 exit 0을 줬다 (실측 사고. CONFORMANCE §DI-00-04 "WORKSTATE 손복구로 멱등이 깨졌는데 `handoff-integrity`가 **exit 0**을 줬다").

| 기준 | 점수 | 근거 |
| --- | --- | --- |
| 반복 검증 가치 | 2 | 매 Phase·DI 시작 전 WORKSTATE 무결성 확인 필수 |
| 결정 가능성 | 2 | 두 번 실행 결과 비교 = 완전 기계 판정 |
| 장애 주입 가치 | 2 | WORKSTATE 손복구로 이미 재현됨. 인위 재현 자명 |
| 격리 가능성 | 2 | 읽기 전용 + 임시 복사본. 원본 무변경 |
| 관찰 가능성 | 2 | 불일치 필드·값을 분해 출력 |
| 유지 비용 | 2 | `handoff-integrity` 이미 존재. 확장 비용 낮음 |

**판정: 기존 항목 확장** — CODEX-GATE-02 큐 C-01 "멱등 대조" 항목으로 이미 등재됨. 중복 제작 금지. 코덱스가 기존 `handoff-integrity`에 멱등 대조 로직을 추가.

### H-3: `claim-check --untracked` — 기존 항목 확장

**근거 데이터**: untracked 파일을 못 봐 16회 연속 오탐 (조율자 실측. CONFORMANCE §DI-00-04 "untracked 파일을 못 봐 **16회 연속 오탐**").

| 기준 | 점수 | 근거 |
| --- | --- | --- |
| 반복 검증 가치 | 2 | `claim-check` 매 DI 실행. 오탐 16회 = 확실한 반복 패턴 |
| 결정 가능성 | 2 | untracked 목록 vs claim 대조 = 완전 기계 판정 |
| 장애 주입 가치 | 2 | untracked 파일 추가 → 재현 자명 |
| 격리 가능성 | 2 | 읽기 전용 |
| 관찰 가능성 | 2 | 어느 untracked 파일이 누락 주장인지 출력 |
| 유지 비용 | 2 | `claim-check` 이미 존재. `--untracked` 플래그 추가 |

**판정: 기존 항목 확장** — CODEX-GATE-02 큐 C-01 "claim-check" 항목으로 이미 등재됨. 중복 제작 금지. 코덱스가 기존 `claim-check`에 untracked 파일 스캔 기능 추가.

### H-4: HS-GATE 누락 탐지 검사 (신규) — 즉시 제작 필수

**근거 데이터**: `HS-GATE-P00.md` 없음. CLAUDE.md 문장만으로 Phase 경계 차단 불가. "없으면 Phase가 게이트 없이 넘어간다" (directive DI-00-04 §할 일 2항). CONFORMANCE §DI-00-04 "HS-GATE 없으면 다음 Phase 진입 차단 | 차단 코드 없음. 규칙은 CLAUDE.md 문장으로만 존재 | MISSING".

| 기준 | 점수 | 근거 |
| --- | --- | --- |
| 반복 검증 가치 | 2 | 매 Phase 전환 시 필수. `HS-GATE-P00`, `HS-GATE-P01`... Phase마다 반복 |
| 결정 가능성 | 2 | `docs/verification/phase-gates/HS-GATE-PXX.md` 존재 여부 = 완전 기계 판정 |
| 장애 주입 가치 | 2 | HS-GATE 파일 제거 → 재현 자명 |
| 격리 가능성 | 2 | 읽기 전용. 파일 존재 확인만 |
| 관찰 가능성 | 2 | 어느 Phase에 HS-GATE가 없는지 경로와 함께 출력 |
| 유지 비용 | 2 | 간단한 파일 존재 체크. 게이트가 직접 의존 (이것 없으면 Phase 경계가 규칙으로만 존재) |

**판정: 즉시 제작 필수** (gate-critical)
- gate-critical 사유: CLAUDE.md "HS-GATE-P00 PASS 전까지 Phase 1으로 넘어가지 않는다"는 이 저장소의 제1원칙인데, 이를 강제하는 코드가 없다. 코드로 강제하라(CLAUDE.md 철학).
- **예산 주의**: Phase 0 신규 Harness 예산(상한 2개)이 P0-03(`handoff-integrity`)·P0-05(`context-pack-integrity`)로 이미 소진됨. 이 항목은 예산 예외(출시 차단 불변식)로 제안 — **사람 결재 필요**.
- CODEX-GATE-02에 없음. 신규 제작 필요.

---

## Skill 후보 판정

> 기준: 반복성·절차 안정성·입출력 명확성·안전 경계·판단 재사용성·도구화 가능성 (각 0~2)

| 후보 | 반복성 | 절차 안정성 | 입출력 | 안전 경계 | 판단 재사용 | 도구화 | 총점 | 판정 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| S-1: `failure-case-wiki` | 2 | 2 | 2 | 2 | 2 | 1 | **11** | 기한부 제작 |
| S-2: `prepare-model-handoff` | 2 | 2 | 2 | 2 | 2 | 2 | **12** | 즉시 제작 필수 |
| S-3: `build-di-context-pack` | 2 | 1 | 1 | 2 | 1 | 1 | **8** | 보류 |
| S-4: `compact-phase-context` | 2 | 1 | 0 | 2 | 1 | 0 | **6** | 보류 |

### S-1: `failure-case-wiki` Skill — 기한부 제작

**근거 데이터**: 위키는 있으나(FAIL-2026-001~013) 절차를 Skill로 추상화하지 않아 매번 개인 경험에 의존. v9 §DI-00-04 지시 6항.

| 기준 | 점수 | 근거 |
| --- | --- | --- |
| 반복성 | 2 | 매 신규 실패마다 등록 필요. 이미 13건. 매 DI 후 반복 |
| 절차 안정성 | 2 | wiki 템플릿·ID 규칙(FAIL-YYYY-NNN)·색인 구조 완전 고정 |
| 입출력 명확성 | 2 | 입력: 실패 사례 정보. 출력: FAIL-YYYY-NNN 파일 + 색인 2종 갱신 |
| 안전 경계 | 2 | `docs/wiki/` 기록만. 코드·상태 변경 없음 |
| 판단 재사용성 | 2 | failureClass 분류 기준이 by-failure-class 색인으로 일반화됨 |
| 도구화 가능성 | 1 | ID 생성·색인 update는 자동화 가능. 원인 분석·분류는 LLM 필요 |

**판정: 기한부 제작** (non-critical, 11/12)
- non-critical 사유: 위키 자체가 있어 스킬 없이도 손으로 등록 가능. 게이트 차단 조건 아님.
- 목표 Phase: Phase 1 (P1). 담당 WP: WP-01 또는 Phase 1 첫 DI.
- Skill 유형: `assisted` (LLM이 주도, 분류 판단 포함)

### S-2: `prepare-model-handoff` Skill — 즉시 제작 필수

**근거 데이터**: v9 §DI-00-05 산출물. 없음. 현재 등급 "Required Before Multi-model Parallel Work"이며 Phase 1에서 다중 모델 작업 시작 예정.

| 기준 | 점수 | 근거 |
| --- | --- | --- |
| 반복성 | 2 | 매 모델 전환·세션 시작마다 필요. Multi-model 환경에서 매 회차 |
| 절차 안정성 | 2 | HANDOFF.md·WORKSTATE.json·sessions/ 구조 확립. projection CLI 존재 |
| 입출력 명확성 | 2 | 입력: 현재 Phase·DI·WORKSTATE. 출력: handoff 패키지(HANDOFF.md + 검증 통과 증거) |
| 안전 경계 | 2 | 읽기 + `docs/` 문서 작성만. 코드·상태 직접 변경 없음 |
| 판단 재사용성 | 2 | handoff 기준이 `_header.md`·WORKSTATE schema·`handoff-integrity`로 일반화됨 |
| 도구화 가능성 | 2 | `projection` CLI로 HANDOFF.md 생성. `handoff-integrity`로 검증. 절차 완전 스크립트화 가능 |

**판정: 즉시 제작 필수** (gate-critical, 12/12)
- gate-critical 사유: 운영 등급 "Required Before Multi-model Parallel Work". Phase 1 시작 전(= Phase 0 종료 전) 완료 필수.
- Phase 0 Skill 예산: 신규 2개 상한. 현재 0개 사용 → 예산 내.
- Skill 유형: `procedural` (단계별 절차 + LLM 판단 포함. 완전 자동화 아님)

### S-3: `build-di-context-pack` Skill — 보류

**근거 데이터**: v9 §DI-00-06 산출물. DI-00-06 PARTIAL.

| 기준 | 점수 | 근거 |
| --- | --- | --- |
| 반복성 | 2 | 매 DI마다 Context Pack 구성 필요 |
| 절차 안정성 | 1 | 인라인 context-pack 블록 형식은 `_header.md`에 정의. 단 `schemas/context-pack.schema.json` 없어 canonical schema 미확정 |
| 입출력 명확성 | 1 | 출력 형식(context-pack 블록)은 알지만 sha256 계산·schema validation 절차 미확립 |
| 안전 경계 | 2 | 문서 작성만. 금지사항 명확 (`_header.md` 참조) |
| 판단 재사용성 | 1 | _header.md 형식 정의 있으나 schema 미확립으로 재사용 판단 기준 불완전 |
| 도구화 가능성 | 1 | sha256 계산은 `Get-FileHash`로 가능. schema validation 불가 (schema 없음) |

차단 사유: DI-00-06의 `schemas/context-pack.schema.json`이 없어 canonical 형식 미확정. 이 상태에서 Skill을 만들면 schema 완성 후 즉시 Skill을 고쳐야 한다 — 낭비.

**판정: 보류**
- 해소 조건: DI-00-06 완료 (`schemas/context-pack.schema.json` + `schemas/context-receipt.schema.json` 존재·유효)
- 재판정 Phase: Phase 0 (DI-00-06 완료 시점)
- Skill 유형 (예상): `assisted`

### S-4: `compact-phase-context` Skill — 보류

**근거 데이터**: v9 §DI-00-06 산출물. DI-00-06 PARTIAL. L1~L3 context layers 없음.

| 기준 | 점수 | 근거 |
| --- | --- | --- |
| 반복성 | 2 | 매 Phase 종료 시 필요 |
| 절차 안정성 | 1 | L0만 있음. L1~L3 없어 절차 부분 고정 불가 |
| 입출력 명확성 | 0 | L1~L3 schema 없어 입출력 형식 정의 불가 |
| 안전 경계 | 2 | 읽기 + context 문서 작성만 |
| 판단 재사용성 | 1 | L0~L3 구조 미확립 → 재사용 기준 미확정 |
| 도구화 가능성 | 0 | L1~L3 없으면 무엇을 compact할지 모름 → 자동화 불가 |

차단 사유: DI-00-06의 L1~L3 context layers와 schema가 없어 이 Skill의 절차 자체를 정의할 수 없다.

**판정: 보류**
- 해소 조건: DI-00-06 완료 (L1~L3 context layers 구조 + schemas 존재)
- 재판정 Phase: Phase 0 (DI-00-06 완료 시점)
- Skill 유형 (예상): `procedural`

---

## 예산 현황 (Phase 0)

| 유형 | 상한 | 기사용 | 이번 즉시제작 | 잔여 | 비고 |
| --- | --- | --- | --- | --- | --- |
| 신규 Harness | 2 | 2 (P0-03·P0-05) | 1 (H-4) | -1 | H-4는 예산 예외 적용 필요 → **사람 결재** |
| 신규 Skill | 2 | 0 | 1 (S-2) | 1 | S-1은 기한부(Phase 1) → Phase 0 예산 미사용 |

---

## 보류·부적합 항목 요약

| 후보 | 판정 | 차단 사유 | 해소 조건 | 재판정 Phase |
| --- | --- | --- | --- | --- |
| S-3: `build-di-context-pack` | 보류 | DI-00-06 미완료 (schema 없음) | `schemas/context-pack.schema.json` 존재·유효 | Phase 0 (DI-00-06 완료 후) |
| S-4: `compact-phase-context` | 보류 | DI-00-06 미완료 (L1~L3 없음) | L1~L3 구조 + schemas 존재 | Phase 0 (DI-00-06 완료 후) |

---

## 즉시 제작 필수 항목 — DI-00-07 입력

| 항목 | 유형 | 근거 | 예산 |
| --- | --- | --- | --- |
| H-4: HS-GATE 누락 탐지 검사 | 신규 Harness | gate-critical. CLAUDE.md 철학 "코드로 강제하라" | 예산 예외 → 사람 결재 |
| S-2: `prepare-model-handoff` | 신규 Skill | gate-critical. Required Before Multi-model Parallel Work | Phase 0 예산 내 (1/2) |

---

## 실패 위키 연결

- `FAIL-2026-012`: cli-contract-check 필요성의 직접 근거 (커밋 접두사 오판으로 위반 날조)
- CONFORMANCE §DI-00-04 실측 사고들: cli-contract-check(배선 누락)·handoff-integrity 멱등(손복구)·claim-check(16회 오탐)

---

## HS-GATE-P00 판정

**이 문서는 Phase 0 최종 판정 문서가 아니다.** 최종 판정은 `DI-00-07`이 수행한다.
이 문서의 결론: **DI-00-07 착수를 위한 Harness·Skill 후보 판정 완료.**

즉시 제작 필수 2개(H-4·S-2), 기존 확장 3개(H-1·H-2·H-3, CODEX-GATE-02 대기), 기한부 1개(S-1, Phase 1), 보류 2개(S-3·S-4, DI-00-06 완료 대기).
