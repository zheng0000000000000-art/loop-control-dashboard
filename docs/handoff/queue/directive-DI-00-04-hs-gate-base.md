# DI-00-04 — Harness·Skill 판정 기반 생성 (v9 canonical)

```context-pack
{
  "diId": "DI-00-04",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/verification/phase-gates/CONFORMANCE-P00-DI-00-01~06.md", "sha256": "8a4edd3f0483b010e6c42a10d5db7ebceb33f2775949aa4af18f57082622edce" },
    { "path": "skills/common/hs-gate.md", "sha256": "7df51f82aea4e153f09c402ccb98e9378000facf8120aa3312eb26a7bf172c7d" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-DI-00-04-hs-gate-base.md",
    "docs/verification/phase-gates/CONFORMANCE-P00-DI-00-01~06.md"
  ],
  "forbiddenActions": ["git commit", "git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `documentation`** (판정 문서와 계약을 만든다. 코드는 만들지 않는다.)

## 목적

**`HS-GATE-P00`(Phase 0 최종 게이트)의 입력이 하나도 없다.** 적합성 행렬이 실측했다:

- `docs/verification/phase-gates/` — **디렉터리 자체가 없었다**(행렬 문서가 첫 파일이다)
- `phase-gates/_template.md`(HS-GATE 판정 템플릿) — **없음**
- `HS-REVIEW-P00-R1.md` — **없음**
- **Skill manifest 계약** — **없음**(`ALIGNMENT-v9 §2`가 "manifest 형식 미도입"이라 이미 선언)

**입력이 없으면 `DI-00-07`을 시작조차 못 한다.** 이 DI는 그 입력을 만든다.

> ⚠️ **v9 §0.4를 읽어라**(`docs/plan/AI-RUNTIME-REFACTOR-MICRO-DIRECTIVES-v9.md:286~380`). 판정 5종·6기준·점수 규칙·예산 상한·Skill manifest 필드가 거기 있다. **네가 발명하지 마라.**

## 할 일

### 1. `docs/verification/phase-gates/_template.md` — HS-GATE 판정 템플릿

v9 §0.4 그대로:

- **판정 5종**: `즉시 제작 필수 | 기한부 제작 | 기존 항목 확장 | 보류 | 부적합`
- **Harness 판정 6기준**(반복 검증 가치·결정 가능성·장애 주입 가치·격리 가능성·관찰 가능성·유지 비용) — 각 `0|1|2`
- **Skill 판정 6기준**(반복성·절차 안정성·입출력 명확성·안전 경계·판단 재사용성·도구화 가능성) — 각 `0|1|2`
- **총점 규칙**: `0~4 부적합` / `5~7 보류·확장 검토` / `8~10 확장 또는 기한부` / `11~12 + gate-critical → 즉시 제작 필수` / `11~12 + non-critical → 두 Phase 내 기한부`
- **점수만으로 결정하지 않는다**: 보안 위험·원본 변경 가능성·정책 미확정은 **점수와 무관한 차단 사유**다 → `보류`/`부적합`
- **`보류`에는 해소 조건과 재판정 Phase가 반드시 있어야 한다.** 이유 없는 보류 금지
- **Harness와 Skill은 독립 판정한다**(하나가 부적합해도 다른 하나는 제작 가능)
- **예산**: Phase당 신규 Harness 2 · 신규 Skill 2가 **상한**(제작 의무 아님). **신규보다 기존 확장을 우선 검토한다.** 예외: 출시 차단 불변식·보안 경계·Commit 안전성·복구 불가능 상태 방지

### 2. `docs/verification/phase-gates/HS-REVIEW-P00-R1.md` — **실제로 판정하라**

**템플릿만 만들고 비워두지 마라.** Phase 0에서 실제로 나온 후보를 **점수화**해서 판정한다. 최소한 아래는 다뤄라(빠뜨린 후보가 있으면 추가하라):

| 후보 | 유형 | 근거 |
| --- | --- | --- |
| `cli-contract-check`(CLI 배선 계약 대조) | Harness | `state-transition` 배선이 통째로 사라졌는데 게이트가 **5/5 PASS**를 줬다(실측 사고) |
| `handoff-integrity` **멱등 대조 확장** | Harness(기존 확장) | WORKSTATE 손복구로 멱등이 깨졌는데 `handoff-integrity`가 **exit 0**을 줬다(실측 사고) |
| `claim-check --untracked` | Harness(기존 확장) | untracked 파일을 못 봐 **16회 연속 오탐**(조율자 실측) |
| **HS-GATE 누락 탐지 검사** | Harness | v9 §DI-00-04 지시 4항. **없으면 Phase가 게이트 없이 넘어간다** |
| `failure-case-wiki` | Skill | v9 §DI-00-04 지시 6항 |
| `prepare-model-handoff` | Skill | v9 §DI-00-05 산출물(현재 없음) |
| `build-di-context-pack` · `compact-phase-context` | Skill | v9 §DI-00-06 산출물(현재 없음) |

- **`즉시 제작 필수`는 gate-critical에만 준다.** 남발하면 예산이 터진다.
- **이미 코덱스 큐에 올라간 것**(`CODEX-GATE-02`: 멱등 대조·CLI 계약·GATE-MANIFEST 등재·claim-check)은 **`기존 항목 확장`으로 판정하고 그 사실을 적어라.** 중복 제작 금지.
- **이건 Phase 종료 게이트가 아니다**(v9 §0.4 1항). `HS-GATE-P00`은 `DI-00-07`이 한 번만 만든다. **여기서 만들지 마라.**

### 3. `docs/handoff/SKILL-MANIFEST.md` — Skill manifest 계약

v9 §0.4 그대로. 모든 Skill이 선언해야 하는 것:

- **`skillType`**: `procedural | assisted | executable` 중 하나
- **`automationLevel`** · **`humanApprovalPoints`** · **`sideEffectScope`** · **`requiredCapabilities`**

**계약(형식)만 정의하라.** 기존 `skills/common/*.md` 5개에 이 헤더를 **적용하는 것은 코덱스 몫이다**(`skills/`는 ADR-002상 코덱스 배타 영역). **`skills/` 아래 파일을 건드리지 마라.**

## 하지 않는 것

- ❌ **`skills/**` 무접촉** · **`server/**` 무접촉**(이번 DI는 코드를 만들지 않는다). 하네스 제작 금지 — **판정만 한다.**
- ❌ **`HS-GATE-P00.md` 생성 금지.** v9는 **`DI-00-07`이 정확히 한 번** 만들라고 못박았다.
- ❌ 적합성 행렬(`CONFORMANCE-P00-*.md`) 수정 — 검수자 소유. **읽기만 해라.**
- ❌ git commit/push · 결재 · 반입 · 발사.

## 필수 검증 (DI 유형 `documentation` — v9 §0.1)

| 요구 | 방법 |
| --- | --- |
| **링크** | 문서가 가리키는 경로가 **전부 실재하는지** 확인하라(없는 파일을 가리키면 `context-pack-integrity`의 존재 이유가 무색해진다) |
| **필수 항목** | v9 §0.4의 판정 5종·6기준·총점 규칙·예산·Skill manifest 5필드가 **전부** 문서에 있는지 대조하라 |
| **안정 section ID lint** | 이 저장소에 체계가 없다 → **`NOT_VERIFIED`라고 써라.** DI-00-06의 공백이다. **여기서 만들지 마라** |

## 반증 시험

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | `doc-integrity` | **exit 0**(INTACT) — 새 문서가 markdown으로 깨지지 않는다 |
| 2 | `context-pack-integrity` | **exit 0** |
| 3 | HS-REVIEW-P00-R1의 판정 중 **`보류`가 있다면** 해소 조건·재판정 Phase가 **비어 있지 않은지** 자기 점검 | 비어 있으면 **네가 반려하라**(v9 §0.4: 이유 없는 보류 금지) |
| 4 | 총점 11~12인데 `즉시 제작 필수`가 아닌 항목이 있으면 **gate-critical이 아닌 이유**를 적었는지 | 근거 없으면 미달 신고 |

## 검수 기준

1. `doc-integrity` **exit 0** · `context-pack-integrity` **exit 0** · `gate-clean server` **exit 0**(코드 무변경)
2. `measure dev-pack` **violationCount 0** — ⚠️ **`dotnet run --project server`로 실행해라.** exe 직접 호출은 저장소 루트를 부모 폴더로 잡아 exit 2가 난다(실측)
3. **v9 §0.4의 필수 항목이 전부 템플릿에 있는가**(위 「필수 검증」 2행)
4. **`HS-REVIEW-P00-R1`이 비어 있지 않은가** — **실제 점수와 판정이 있어야 한다.** 표만 만들고 채우지 않으면 반려다
5. 파일을 다 쓴 뒤 마지막에 **`projection`**
6. **목적 기준**: `DI-00-07`이 이 문서들을 입력으로 **시작할 수 있는가**

## 허용 파일 (allowlist)

- docs/verification/phase-gates/_template.md
- docs/verification/phase-gates/HS-REVIEW-P00-R1.md
- docs/handoff/SKILL-MANIFEST.md
- docs/verification/di0004-hs-gate-base.md
- docs/handoff/queue/directive-DI-00-04-hs-gate-base.md
- docs/handoff/WORKSTATE.json
- docs/context/RUNTIME-INDEX.md
- docs/handoff/HANDOFF.md
- docs/STATUS.md

> `skills/**`·`server/**`·`dashboard/**` **무접촉**. `docs/verification/phase-gates/CONFORMANCE-*.md` **무접촉**(읽기만).

## 보고

`docs/verification/di0004-hs-gate-base.md` — **`docs/verification/_template.md` 형식 그대로.** DI 유형(`documentation`) 선언 · 유형별 필수 검증(링크·필수항목·section ID lint) · 공통 완료 조건 6개 · 실패 분류 · 반증 시험 표 · 잔여 위험 · **`## 지표는 만족했으나 목적은 미달인 부분`**.

- **자기보고는 증거가 아니다.** 검수자가 재실행해 대조한다.
- **못 한 것은 `NOT_VERIFIED`라고 써라.** 신고하면 감점이 아니고, **숨기면 반려다.**
- **WORKSTATE를 손으로 고치지 마라**(`docs/handoff/RECOVERY.md`). 상태 전이는 `state-transition`으로만.
- 한도가 임박하면 마지막 세 줄로 `QUOTA_SIGNAL` / `CHANGED:` / `NEXT:`.
