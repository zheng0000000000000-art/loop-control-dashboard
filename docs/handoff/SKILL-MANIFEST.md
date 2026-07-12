# SKILL-MANIFEST — Skill 선언 계약 (v9 §0.4)

> **이 파일은 계약(형식 정의)이다.** 기존 `skills/common/*.md` 5개에 이 헤더를 적용하는 것은 **코덱스 몫이다** (`skills/`는 ADR-002상 코덱스 배타 영역).
> 이 파일을 읽는 시스템·사람은 모든 Skill 파일이 아래 5필드를 선언하는지 검사한다.

---

## Skill manifest 5필드 (v9 §0.4 — 전부 필수)

모든 `skills/**/*.md` 파일은 **파일 머리(버전·도메인·트리거 다음 줄)에** 아래 5필드를 YAML 인라인 형식으로 선언한다.

```yaml
# manifest
skillType: procedural | assisted | executable
automationLevel: 0~10  # 0=완전 수동, 10=완전 자동
humanApprovalPoints:   # 사람이 반드시 확인·승인해야 하는 단계 목록
  - "<단계 설명>"
sideEffectScope:       # 이 Skill이 변경·생성할 수 있는 경로 목록
  - "<경로 또는 glob>"
requiredCapabilities:  # 이 Skill을 실행하는 주체가 반드시 가져야 하는 능력·권한
  - "<능력 설명>"
```

---

## 각 필드 정의

### `skillType` (v9 §0.4)

```text
procedural | assisted | executable
```

| 유형 | 설명 | 예시 |
| --- | --- | --- |
| `procedural` | 사람·LLM이 단계별로 수행. 완전 자동화 불가 또는 불필요. | `hs-gate`, `root-cause-diagnosis` |
| `assisted` | LLM이 주도하되 사람 승인 포인트 포함. 출력은 LLM이 생성하고 사람이 검토. | `failure-case-wiki`, `executor-launch` |
| `executable` | 스크립트·CLI·템플릿으로 완전 자동화 가능. 사람 개입 없이 실행. | (현재 해당 없음 — 완전 자동화 Skill은 Harness로 승격 검토) |

### `automationLevel` (0~10)

| 값 | 의미 |
| --- | --- |
| 0 | 완전 수동. 모든 단계를 사람이 직접 수행 |
| 1~3 | 주로 수동. 일부 단계에 도구 보조 |
| 4~6 | 혼합. LLM이 초안 생성하고 사람이 검토·수정 |
| 7~9 | 주로 자동. 사람은 승인 포인트만 개입 |
| 10 | 완전 자동. 사람 개입 없이 실행 (→ Harness 승격 검토) |

### `humanApprovalPoints`

이 Skill을 실행하는 중 **반드시 사람이 확인하거나 승인해야 하는 단계**를 구체적으로 나열한다.
- 비어 있으면 사람 개입 없이 실행 가능하다는 의미다 (`executable`에만 허용).
- `procedural`·`assisted` Skill에서 비어 있으면 manifest가 불완전하다.

예:
```yaml
humanApprovalPoints:
  - "판정 결과를 검수자에게 올리기 전 (6단계)"
  - "큐 등재·발사 여부 결정 (7단계)"
```

### `sideEffectScope`

이 Skill이 **쓰거나 생성할 수 있는 파일 경로 또는 glob**을 나열한다.
- 읽기 전용 Skill이면: `[]` (빈 목록)
- `docs/handoff/HS-CANDIDATES.md`처럼 append 전용이면 경로를 기재하고 `# append-only` 주석을 단다.
- `server/**`·`dashboard/**`를 포함하면 추가 사유를 ADR로 남긴다.

예:
```yaml
sideEffectScope:
  - "docs/handoff/HS-CANDIDATES.md  # append-only"
  - "docs/wiki/failures/cases/*.md"
  - "docs/wiki/failures/by-component/*.md  # append-only"
  - "docs/wiki/failures/by-failure-class/*.md  # append-only"
```

### `requiredCapabilities`

이 Skill을 실행하는 **주체(LLM·사람·코덱스)가 반드시 가져야 하는 능력·권한**을 나열한다.

예:
```yaml
requiredCapabilities:
  - "hs-scan CLI 실행 권한 (server/ 접근)"
  - "docs/handoff/HS-CANDIDATES.md append 권한"
  - "v9 §0.4 판정 기준 숙지"
```

---

## 적용 예시 (기존 hs-gate.md에 manifest를 추가한 형태)

```markdown
# 스킬: HS-GATE 승격 심사 (하네스·스킬 후보 점수화)

버전: 1 | 도메인: common | 트리거: hs-scan exit 1 | 대상: 반복된 실패·절차를 하네스/스킬로 승격 심사할 때

# manifest
skillType: assisted
automationLevel: 4
humanApprovalPoints:
  - "즉시제작·기한부 판정 항목을 검수자에게 올리기 전 (7단계)"
sideEffectScope:
  - "docs/handoff/HS-CANDIDATES.md  # append-only"
requiredCapabilities:
  - "hs-scan CLI 실행 또는 실패 위키 수동 확인 권한"
  - "v9 §0.4 판정 기준 숙지"
```

---

## 기존 Skills에 적용하는 절차 (코덱스 몫)

1. `skills/common/*.md` 5개·`skills/domains/**/*.md`를 순서대로 읽는다.
2. 각 파일 머리(버전·도메인·트리거 줄 다음)에 `# manifest` 블록을 삽입한다.
3. 5필드를 실제 Skill 내용에 맞게 채운다. 모르면 `NOT_DEFINED`로 표시하고 메모를 남긴다.
4. 변경 후 `measure dev-pack`을 실행해 위반 0 확인.
5. 작업 보고(`docs/verification/`)에 주체·하네스·결과를 기록한다.

---

## Skill manifest 검사 기준 (향후 harness 구현 시 참조)

향후 `skill-manifest-check` 하네스(또는 기존 `measure`의 확장)가 아래를 검사한다:

- `skillType`이 3종 중 하나인가
- `automationLevel`이 0~10 정수인가
- `humanApprovalPoints`가 `executable`이 아닌데 비어 있지 않은가
- `sideEffectScope`가 존재하는가 (빈 배열 허용)
- `requiredCapabilities`가 존재하는가 (빈 배열 허용)

이 검사는 현재 미구현이다 (DI-00-04 산출물 범위 밖 — 코드를 만들지 않는다).
