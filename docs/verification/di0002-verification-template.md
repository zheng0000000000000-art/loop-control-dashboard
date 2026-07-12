# DI-00-02 검증 — 공통 검증 템플릿 (v9 §DI-00-02)

## DI 유형 ※필수

- **선언한 유형**: `documentation`

## 주체 (actor) ※필수

- **누가**: **검수자(Claude, 대화 세션)**. 실행자 발사 없음.
- **경로**: 대화 세션 — **docs/ 직접 경로**(관례상 허용, 사유는 아래 「직접 경로 사용 사유」)

> ⚠️ **생산자와 1차 판정자가 같다.** 이 문서는 그 사실을 숨기지 않는다. 독립 검증은 **하네스(exit code)와 조율자**가 한다 — 아래 「사용한 하네스」를 조율자가 재실행해 대조하라.

## 사용한 하네스 ※필수

| 하네스 | 명령 | 기대 exit | 실제 exit | 결과 |
| --- | --- | --- | --- | --- |
| context-pack-integrity (수정 **직후**) | `... -- context-pack-integrity` | — | **1** | **stale 10건 검출** — 템플릿 sha가 바뀌자 이를 참조하던 지시서 10개가 stale로 잡혔다 |
| context-pack-integrity (sha 갱신 후) | `... -- context-pack-integrity` | 0 | **0** | ok |
| doc-integrity | `... -- doc-integrity` | 0 | **0** | INTACT |
| handoff-integrity | `... -- handoff-integrity` | 0 | **0** | PASS |
| gate-clean server | `... -- gate-clean server` | 0 | **0** | server 무변경(문서 DI다) |

## 유형별 필수 검증 ※필수 (`documentation`)

| 요구 | 수행 결과 |
| --- | --- |
| **링크** | 템플릿이 참조하는 경로 확인: `docs/handoff/RECOVERY.md`(실재) · `skills/common/`(실재) · `docs/wiki/failures/cases/`(실재) |
| **필수 항목** | v9 §0.1의 **DI 유형 8종** · **공통 완료 조건 6개** · **유형별 필수 검증표 8행** · v9 §0.3의 **실패 분류 6종 + 실패사례 ID** — 전부 템플릿에 존재 |
| **안정 section ID lint** | **NOT_VERIFIED** — 이 저장소에는 stable section ID 체계가 없다(`ALIGNMENT-v9 §1`이 "없음"으로 이미 선언). **DI-00-06의 공백이다.** 여기서 만들지 않았다 |

## 공통 완료 조건 ※필수

- [x] 선언한 DI 유형(`documentation`)의 완료 프로필 충족 — 위 표(1항 NOT_VERIFIED 신고)
- [x] 관련 계약·스키마·문서 갱신 — 템플릿 sha를 물고 있던 **지시서 10개의 `context-pack.requiredInputs` 갱신**
- [x] 발견된 실패·위험·미확정 사항 기록 — 아래 「잔여 위험」
- [x] `WORKSTATE.json` 갱신 — `state-transition`으로만
- [x] 변경 범위 준수 — `docs/` 문서만. `server/` 무접촉(`gate-clean` 0)
- [x] 원본 저장소 무단 변경 없음 — commit은 문서 레인, push 없음

## 실패 분류와 실패 사례 ※필수

- **실패 분류**: `expected_rejection`
- **근거**: `context-pack-integrity`가 템플릿 수정 직후 **exit 1**로 stale 10건을 거부했다. **이것은 계약이 의도대로 작동한 negative test다** → v9 §0.3에 따라 **새 위키 문서를 만들지 않는다.**
- **실패 사례 ID**: **신규 실패 사례 없음**

## 참조한 스킬 ※필수

- `docs/plan/AI-RUNTIME-REFACTOR-MICRO-DIRECTIVES-v9.md` §0.1(DI 유형·완료 프로필) · §0.2(완료 보고 형식) · §0.3(실패 분류)
- `docs/verification/phase-gates/CONFORMANCE-P00-DI-00-01~06.md`(적합성 행렬 — 이 DI가 왜 열렸는지)

## 변경 내용

| 파일 | 종류 | 요약 |
| --- | --- | --- |
| `docs/verification/_template.md` | 수정 | **DI 유형 선언 절** · **유형별 필수 검증표(8종)** · **공통 완료 조건 6개** · **실패 분류·실패사례 ID 절** · **반증 시험 절** · **잔여 위험 절** · **완료 판정 절**(검수자가 적는다) 신설. 하네스 표에 **기대 exit** 열 추가(`hs-scan`은 기대 1 — "모두 0"이 아니다) |
| `docs/handoff/queue/directive-*.md` (7건) · `docs/directives/*.md` (3건) | 수정 | `context-pack.requiredInputs`의 `_template.md` sha256 갱신(stale 해소) |
| `docs/verification/phase-gates/CONFORMANCE-P00-DI-00-01~06.md` | 수정 | DI-00-01·02 **PASS**로 갱신(append 이력). 가장 이른 미충족 = **DI-00-04** |

## 반증 시험 (negative test) ※필수

| # | 시험 | 기대 | 실제 | 판정 |
| --- | --- | --- | --- | --- |
| 1 | 템플릿 sha를 바꾸면 그것을 참조하는 지시서가 stale로 잡히는가 | exit 1 | **exit 1, stale 10건** | **PASS** — 하네스가 공허하지 않다 |
| 2 | sha 갱신 후 복구되는가 | exit 0 | **exit 0** | PASS |
| 3 | 템플릿이 markdown으로 깨지지 않았는가 | doc-integrity 0 | **0(INTACT)** | PASS |

## 검수 기준 자가점검표

| # | 기준(v9 §DI-00-02 검증) | 결과 | 근거 |
| --- | --- | --- | --- |
| 1 | **DI 유형별 완료 프로필**이 템플릿에 존재 | **PASS** | 유형 8종 + 유형별 필수 검증표 8행 |
| 2 | **공통 완료 조건**이 템플릿에 존재 | **PASS** | 6개 체크박스(v9 §0.1 그대로) |
| 3 | 모든 WP 검증 문서가 이 템플릿을 쓰도록 규칙 명시 | **PASS** | 템플릿 머리 1줄 + `_header.md`가 이미 이 템플릿을 참조 |
| 4 | 검증 결과를 코드 주석으로만 남기지 않는다(금지사항) | **PASS** | 검증 문서가 정본. 코드 주석은 기능 설명만(CLAUDE.md) |

## 게이트 기록

`{"gate":"dev-pack","violations":0,"attempt":1}` — 코드 무변경(문서 DI). `gate-clean server` **exit 0**.

## 잔여 위험 · 미확정 사항 ※필수

1. **안정 section ID lint는 못 했다(NOT_VERIFIED).** 이 저장소에 stable section ID 체계가 **없다**. `DI-00-06`의 공백이며, 거기서 닫아야 한다. **여기서 만들지 않았다** — 범위를 넘기지 않는다.
2. **기존 검증 문서들은 옛 템플릿으로 작성됐다.** 소급 적용하지 않는다(FAIL-2026-010의 교훈: 과거 때문에 게이트를 영구 잠그지 않는다). **신규 DI부터 필수.**
3. **`_template.md`의 sha를 물고 있는 지시서가 10개**였다. 앞으로 템플릿을 고칠 때마다 같은 갱신이 필요하다 — **자동화 후보**(하네스가 아니라 스크립트).

## 직접 경로 사용 사유

**검수자가 직접 수정했다(실행자 발사 없음).** 이유: 템플릿의 sha256을 **다른 DI의 지시서 10개가 물고 있어서**, 실행자에게 시키면 **남의 지시서(계약 파일)를 만지게 해야 한다.** 그건 더 위험하다. 문서 변경은 `_header.md`상 직접 경로가 허용된다.
**대가**: **생산자와 1차 판정자가 같다.** 이 문서에 그 사실을 명시했고, 독립 검증은 하네스 exit code와 조율자에게 맡긴다.

## 지표는 만족했으나 목적은 미달인 부분 (ADR-005)

1. **템플릿이 좋아졌다고 문서가 좋아지는 것은 아니다.** 이 템플릿은 **"항목이 존재하는가"만 강제한다.** DI-00-01 실행자가 그랬듯 **`## 지표는 만족했으나...` 절에 "없음"이라고만 써도 형식은 green이다.** SIM-0의 `// 이 함수는 필요한 작업을 수행한다`와 같은 구조적 취약점이다. **하네스가 이 절의 내용을 판정할 수는 없다** — 사람·검수자가 읽어야 한다.
2. **"모든 WP 검증 문서가 이 템플릿을 쓴다"는 규칙을 코드가 강제하지 않는다.** 문장으로만 있다. **`doc-integrity` 확장 후보**(필수 절 존재 여부 lint) — 코덱스 영역이므로 여기서 만들지 않았다. **큐에 올려야 한다.**

## 완료 판정

**PASS** — 단 위 두 미달 항목과 잔여 위험 1(section ID)을 안고 간다. **판정 주체: 검수자(생산자와 동일). 조율자가 하네스를 재실행해 대조하라.**
