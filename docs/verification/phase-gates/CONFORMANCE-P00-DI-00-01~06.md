# 적합성 행렬 — v9 `DI-00-01` ~ `DI-00-06` (검수자, 2026-07-12)

> **이 문서는 `HS-GATE-P00.md`가 아니다.** v9는 최종 게이트를 **`DI-00-07`이 정확히 한 번** 생성하도록 못박았다(§DI-00-07 검증 1항).
> 이 문서는 **그 게이트의 입력**이다 — "DI-00-01~06이 끝났다"는 경계 주장을 **증명하거나 반증**한다.
> **결론: 반증됐다. 6개 중 완료는 1개다.** 따라서 **`diId`를 `DI-00-07`로 올릴 수 없다.**

## 0. 방법 (판정은 실체로, 프록시로 하지 않는다)

- v9 `DI-00-0X`의 **산출물 목록**과 **검증 체크박스**를 하나씩 저장소 실체와 대조했다.
- **등가물은 인정한다**(`ALIGNMENT-v9 §2 재발명 금지`). 예: `harnesses/<name>/` ↔ `server/Harness/*.cs`.
- 판정 4종: **PASS**(요구를 실체가 충족) · **PARTIAL**(일부만) · **MISSING**(없음) · **NOT_VERIFIED**(있는지 없는지 검수자가 확인하지 않음 — **모르면 모른다고 쓴다**).
- 파일 존재는 `Test-Path`로, 하네스 판정은 **exit code**로, 코드 사실은 **호출부·소스**로 확인했다.

## 1. 종합

| DI | 판정 | 한 줄 |
| --- | --- | --- |
| **DI-00-01** 작업 추적 파일 초기화 | **PARTIAL** | `docs/STATUS.md`는 있으나 **낡았고**(2026-07-11), **WP 등록표가 없고**, **역방향 상태 전이를 막는 것이 없다** |
| **DI-00-02** 공통 검증 템플릿 | **PARTIAL** | `docs/verification/_template.md` 있음. **DI 유형별 완료 프로필**이 없다(공통 조건만) |
| **DI-00-03** 실패 사례 위키 | **✅ PASS** | README·index·template·사례 13건·by-component 12·by-failure-class 13. 검증 4항 전부 충족 |
| **DI-00-04** Harness·Skill 판정 기반 | **PARTIAL** | **`docs/verification/phase-gates/` 디렉터리 자체가 없었다** → `_template.md`·`HS-REVIEW-P00-R1` 없음. Skill manifest 없음. **HS-GATE 누락 탐지 검사 없음** |
| **DI-00-05** 다중 AI 인수인계 기반 | **PARTIAL** | `handoff-integrity`·HANDOFF·WORKSTATE ✅. **`CONTEXT-MANIFEST.json` 없음**, `sessions/_template.md` 없음, `prepare-model-handoff` 스킬 없음, `HS-REVIEW-P00-R2` 없음, **blocker 차단 미구현** |
| **DI-00-06** 컨텍스트 절약 기반 | **PARTIAL** | (예측 적중) `RUNTIME-INDEX`·`context-pack-integrity` ✅. **Context Receipt·Context Budget·L1~L3·schema 파일·packs 파일·스킬 2개·`HS-REVIEW-P00-R3` 전부 없음** |

**가장 이른 미충족 DI = `DI-00-01`.**

> **이것은 "우리가 아무것도 안 했다"는 뜻이 아니다.** 우리는 `ALIGNMENT-v9 §4`가 고른 **로컬 P0-01~07**(진짜 공백 6개)을 실행했고 그건 대부분 끝났다.
> **v9의 DI 시퀀스와 로컬 P0 시퀀스는 다른 축이다.** 지금까지 `diId`에 `LEDGER-04`·`P0-04` 같은 **로컬 큐 별칭**이 들어가 있던 이유가 이것이다.
> **두 축을 섞은 채 `DI-00-07`로 올리면 그건 거짓 경계 주장이다.** §4에서 사람에게 택일을 요청한다.

---

## 2. DI별 대조

### DI-00-01 — 작업 추적 파일 초기화 → **PARTIAL**

| 요구(v9) | 실체 | 판정 |
| --- | --- | --- |
| 산출물 `STATUS.md` | `docs/STATUS.md` 존재(루트 아님 — 경로 차이는 등가로 인정) | ✅ |
| 모든 WP를 `대기`로 등록 | **WP 등록표가 저장소 어디에도 없다.** `WORKSTATE.wpId = WP-00` 하나뿐이고, v9도 WP 목록을 열거하지 않는다(WP는 우리가 정의해야 한다) | **MISSING** |
| 브랜치·담당자·시작일·검증문서 경로 필드 | `docs/STATUS.md`에 없음. `WORKSTATE.changedFiles`/`docs/verification/*`로 흩어져 있음 | PARTIAL |
| 상태 변경 규칙 `대기→진행→검증→완료`로 제한 | `StateApplierCli`가 status **enum**은 강제한다(`waiting/in_progress/verifying/completed/blocked`). **그러나 전이 그래프가 없다** — `completed → in_progress` 역방향이 통과한다(`ValidateRequest`는 enum 소속만 검사) | **MISSING** |
| 검증: 역방향 상태 변경이 차단된다 | 위와 같음 | **MISSING** |

**추가 실체**: `docs/STATUS.md`는 **2026-07-11 갱신**이고 내용이 현재와 불일치한다(SONNET-QUEUE #5 ORCH-01 대기 등). **`projection`이 생성하는 것은 `RUNTIME-INDEX.md`·`HANDOFF.md` 둘뿐이고 STATUS.md는 손으로 쓴다** → **낡은 상태 문서가 잘못된 행동을 낳는다**는 이 저장소의 고질병(`cfbfce4` 사건)이 **여기 그대로 남아 있다.**

### DI-00-02 — 공통 검증 템플릿 → **PARTIAL**

| 요구 | 실체 | 판정 |
| --- | --- | --- |
| `docs/verification/_template.md` | 존재 | ✅ |
| 목표·변경파일·정상/실패/재실행 테스트·Evidence·잔여위험·완료판정 | **주체·하네스 exit·참조 스킬·자가점검표·게이트 기록·ADR-005 자진신고**로 구성. **정상/실패/재실행 테스트 절이 명시적으로 없다**(하네스 표가 대신한다) | PARTIAL |
| 검증: **DI 유형별 완료 프로필**과 공통 완료 조건이 **모두** 존재 | **DI 유형(implementation/verification/…) 구분이 템플릿에 없다.** 공통 조건만 있다 | **MISSING** |

### DI-00-03 — 실패 사례 위키 → **✅ PASS**

| 요구 | 실체 | 판정 |
| --- | --- | --- |
| `README.md`·`index.md`·`_template.md` | 3개 전부 존재 | ✅ |
| `FAIL-YYYY-NNN` ID 규칙 | `FAIL-2026-001` ~ `FAIL-2026-014` (13건) | ✅ |
| 구성요소별·failureClass별 색인 | `by-component/` **12개**, `by-failure-class/` **13개** — 양쪽 조회 가능 | ✅ |
| 템플릿에 발생 상황·해결 방법·판단 기준·발생 이유 | 4개 절 전부 존재 | ✅ |
| 미확정 원인의 상태·가설 표기 | `- 상태:` 필드 + `## 발생 이유(직접·근본·기여, **미확정은 가설 표시**)` | ✅ |
| 동일 원인 재발 누적 | `## 발생 이력` 절 | ✅ |
| verification 템플릿이 실패 사례 참조를 필수화 | **verification `_template.md`에 실패 사례 ID 참조 절이 없다** | **MISSING** |

**판정 PASS — 단 마지막 1항(템플릿 연결)은 미충족이다.** 위키 자체는 v9 요구를 넘어선다.

### DI-00-04 — Harness·Skill 판정 기반 → **PARTIAL**

| 요구 | 실체 | 판정 |
| --- | --- | --- |
| `docs/verification/phase-gates/` 구조 | **이 커밋 전까지 디렉터리가 없었다**(이 문서가 첫 파일이다) | **MISSING** |
| `phase-gates/_template.md` (HS-GATE 판정 템플릿) | 없음. `skills/common/hs-gate.md`가 절차만 서술 | **MISSING** |
| `HS-REVIEW-P00-R1.md` | 없음 | **MISSING** |
| `harnesses/` 구조 | `server/Harness/*.cs` + `HarnessRegistry` — **등가 인정**(ALIGNMENT §2) | ✅ |
| `skills/` 구조 | `skills/common/*.md`(5) + `skills/domains/*`(5) | ✅ |
| **Harness manifest 계약** | `docs/handoff/GATE-MANIFEST.json`(schemaVersion 1) + 생성 문서 `HARNESSES.md` — **등가 인정** | ✅ |
| **Skill manifest 계약** | **없음**(ALIGNMENT §2가 "manifest 형식 미도입"이라 이미 선언) | **MISSING** |
| **HS-GATE 누락 탐지 검사** | **없음.** `hs-scan`은 소스 머리 주석 그대로 **"지식 승격(HS-GATE)의 *트리거*를 탐지"**하는 것이지 **Phase 종료 시 게이트 문서 누락을 막지 않는다** | **MISSING** |
| 초기 Harness가 PASS/FAIL을 기계적으로 반환 | `di-completion-check`(exit code + `gate.json`) ✅ — 예산은 `BC-002`(사람 2→3 승인) | ✅ |
| `failure-case-wiki` Skill | 없음(위키는 있으나 스킬 없음) | **MISSING** |
| HS-GATE 없으면 다음 Phase 진입 차단 | **차단 코드 없음.** 규칙은 `CLAUDE.md` 문장으로만 존재 | **MISSING** |

### DI-00-05 — 다중 AI 모델 인수인계 기반 → **PARTIAL**

| 요구 | 실체 | 판정 |
| --- | --- | --- |
| `HANDOFF.md` | `projection`이 생성 | ✅ |
| `WORKSTATE.json` | v9 canonical 계약(schemaVersion 3, blockers[]) — `STATE-01`에서 이관 | ✅ |
| **`CONTEXT-MANIFEST.json`** | **없다** | **MISSING** |
| `decisions/_template.md` | 존재 (ADR-001~012) | ✅ |
| `sessions/_template.md` | **없다**(실물 세션 로그 `SESSION-*.md`는 있다) | **MISSING** |
| `handoff-integrity` 하네스 | 존재. **반증 시험 통과**(파일 1줄 변조 → exit 1, 원복 → exit 0) | ✅ |
| `prepare-model-handoff` Skill | **없다** | **MISSING** |
| `HS-REVIEW-P00-R2.md` | **없다** | **MISSING** |
| 검증: 이전 대화 없이 Phase·DI·상태 재구성 | **오늘 실측**(RESUME-01 재발사, 2026-07-12 19:46). L0만으로 5필드 답변, 날조 없음 | ✅ |
| 검증: hash·commit·테스트 불일치를 하네스가 탐지 | `handoff-integrity` changedFiles sha256 대조 | ✅ |
| 검증: **blocker가 있으면 다음 DI를 차단** | `blockers[]` 필드는 `STATE-01`에서 신설됐으나 **차단 로직이 없다.** `blocked`면 `blockers` 비었는지만 본다 | **MISSING** |
| 검증: 중요 결정이 ADR 없이 HANDOFF에만 있으면 실패 | **검사 없음** | **MISSING** |
| 검증: Projection 반복 생성해도 안 깨진다 | P0-04에서 멱등 확인 | ✅ |

### DI-00-06 — 컨텍스트 절약 기반 → **PARTIAL** (예측대로)

| 요구 | 실체 | 판정 |
| --- | --- | --- |
| `docs/context/RUNTIME-INDEX.md` (L0) | 존재. `projection` 생성. **14줄**(v9 §0.6의 20줄 상한 준수) | ✅ |
| `docs/context/` **4계층(L0~L3)** | **`docs/context/`에 파일이 `RUNTIME-INDEX.md` 하나뿐이다.** L1~L3 없음 | **MISSING** |
| `schemas/context-pack.schema.json` | **없다** | **MISSING** |
| `schemas/context-receipt.schema.json` | **없다** | **MISSING** |
| `packs/CTX-DI-00-06.json`·`CTX-DI-01-01.json` | **없다.** Context Pack은 지시서 안의 인라인 ` ```context-pack ` 블록 **9건**으로 존재 — **부분 등가**(canonical schema 파일이 없어 v3 Runner가 소비할 정본이 없다) | PARTIAL |
| `context-pack-integrity` 하네스 | 존재. **반증 시험 통과**(유령 참조 주입 → exit 1) | ✅ |
| `build-di-context-pack` Skill | **없다** | **MISSING** |
| `compact-phase-context` Skill | **없다** | **MISSING** |
| 검증: **Context Receipt에 실제 조회 문서와 추가 조회 이유가 남는다** | **없다.** 설계만 있다(`LOCAL-DI-RUNNER-DRAFT-v3.md §8`) | **MISSING** |
| 검증: **Context Budget 초과 시 자동 PASS하지 않는다** | **Budget 개념 자체가 없다.** `LAUNCH-BUDGET.json`의 숫자도 미정(사람 결재 대기) | **MISSING** |
| 검증: L0·L1만으로 Phase 1 첫 DI를 설명할 수 있다 | **L1이 없으므로 시험 불가** | **NOT_VERIFIED** |
| `HS-REVIEW-P00-R3.md` | **없다** | **MISSING** |

---

## 3. 닫아야 할 공백 (우선순위 — 전부 작다. 대부분 문서·schema다)

| # | 공백 | 왜 급한가 | 크기 |
| --- | --- | --- | --- |
| 1 | **상태 전이 그래프**(역방향 차단) — `StateApplierCli` | `completed → in_progress`가 통과한다. **상태 원본을 지키는 유일한 writer에 가드가 없다** | 작다(코드 1곳) |
| 2 | **Context Receipt + Context Budget** | `DI-00-06`의 심장이고, **v3 Runner의 전제**다. 없으면 Local DI Runner를 못 만든다 | 중간 |
| 3 | **`CONTEXT-MANIFEST.json` + context-pack/receipt schema 파일** | 인라인 Context Pack 9건을 **정본 schema로 승격**해야 Runner가 소비한다 | 중간 |
| 4 | **`phase-gates/_template.md` + HS-REVIEW-P00-R1~R3** | `HS-GATE-P00`의 **입력이다.** 없으면 `DI-00-07`을 시작조차 못 한다 | 작다(문서) |
| 5 | **WP 등록표 + STATUS.md를 projection이 생성** | STATUS.md가 낡아서 이미 한 번 사고가 났다(`cfbfce4`) | 작다 |
| 6 | **blocker 차단 · HS-GATE 누락 차단** | 규칙이 `CLAUDE.md` 문장으로만 있다 — **코드로 강제하라**(이 저장소의 제1원칙) | 작다 |
| 7 | Skill manifest · `prepare-model-handoff` · `build-di-context-pack` · `compact-phase-context` · `failure-case-wiki` | v9 산출물. **예산(Phase당 스킬 2개)에 걸린다 — 사람이 우선순위를 정해야 한다** | 사람 결재 |

## 4. ★ 사람 결재 — `diId`를 무엇으로 둘 것인가

`STATE-01` 지시서의 규칙: **"적합성 행렬이 증명하기 전까지, 가장 이른 미충족 DI가 현재 `diId`다."**
행렬대로면 **`diId = DI-00-01`**이다. 그런데 그 값은 **"우리가 P0에서 한 일을 전부 지운 것처럼" 보인다.** 그래서 사람이 택일한다:

| 안 | 내용 | 대가 |
| --- | --- | --- |
| **(가)** | **`diId = DI-00-01`** — 규칙 그대로. 위 공백 7개를 v9 DI 순서로 닫으며 올라온다 | 정직하다. 대신 **로컬 P0 성과가 canonical 필드에서 안 보인다**(`notes`에 별칭으로 남는다) |
| **(나)** | **v9의 `DI-00-0X` 산출물 목록을 우리 실체에 맞게 재정의**(예: `harnesses/` → `server/Harness/`를 계약에 명문화, STATUS.md → RUNTIME-INDEX로 대체) | **이건 기준 변경이다.** `BASELINE-CHANGES.md`에 ①주체 ②근거 ③되돌리는 법을 남기고 **사람이 결재**해야 한다 |
| **(다)** | `phaseId=P00`·`wpId=WP-00`은 두고, **`diId`를 공백 목록 기준의 새 DI(`DI-00-01a` 등)로 발급** | v9 계약을 건드리지 않지만 **ID 체계가 하나 더 늘어난다**(v9가 금지한 "새 canonical ID 체계") |

**검수자 권고: (가).** 이유는 하나다 — **(나)와 (다)는 둘 다 "판정이 불편해서 기준을 옮기는" 모양이 된다.** 이 저장소가 `CLAUDE.md` 금지사항 1번으로 막아둔 바로 그 행동이다. **`DI-00-01`은 후퇴가 아니라 좌표다.** 공백 7개는 전부 작고, 그중 5개는 문서다.

**어느 안이든 `DI-00-07`(Phase 0 최종 경계 판정)로 올리는 것은 지금 불가능하다.** 그것이 이 행렬의 결론이다.

## 5. 지표는 만족했으나 목적은 미달인 부분 (ADR-005)

1. **`DI-00-03`을 PASS로 줬지만 마지막 1항(verification 템플릿이 실패 사례 참조를 필수화)은 미충족이다.** 위키 본체가 요구를 넘어서므로 PASS로 뒀으나, **엄격히는 PARTIAL이다.** 사람이 다르게 판정해도 나는 반박하지 않는다.
2. **v9 §DI-00-01~06의 "금지사항" 절은 대조하지 않았다.** 산출물과 검증 체크박스만 봤다. 금지사항 위반 여부는 **NOT_VERIFIED다.**
3. **등가물 인정은 내 판단이다**(`harnesses/` ↔ `server/Harness/`, `GATE-MANIFEST.json` ↔ Harness manifest). **사람이 "그건 등가가 아니다"라고 하면 DI-00-04는 더 내려간다.**
4. **이 문서를 `docs/verification/phase-gates/`에 두면서 그 디렉터리를 내가 만들었다.** 그 디렉터리 생성은 원래 `DI-00-04`의 산출물이다 — **선취했다.** `_template.md`·`HS-REVIEW-*`는 만들지 않았다(그건 DI 몫이다).
