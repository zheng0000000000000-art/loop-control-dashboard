```context-pack
{
  "diId": "CPX-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-CPX-01-overlap-fails.md",
    "server/Harness/ContextPackIntegrityCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → positive·negative 시험 필수.

---

# CPX-01 — `requiredInputs`가 allowlist와 겹치면 **경고가 아니라 실패**다

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 발견: 조율 세션 실측, 2026-07-26.

---

## 0. 문제 (실측)

`_header.md`는 이렇게 못박는다:

> **allowlist(쓰기 대상)와 겹치지 않는다.** 읽기 참조 = `requiredInputs`, 쓰기 대상 = `allowlist`.
> 작업 중 바뀌는 파일에 해시를 걸면 게이트가 **자기 작업에 걸려 넘어진다.**

그런데 `context-pack-integrity`는 이 겹침을 **`warning`으로만** 센다(`failureCount`에 들어가지 않는다).
경고는 exit code를 바꾸지 않으므로 아무도 멈추지 않는다.

**실제로 벌어진 일**: `GATE-CP-01`이 자기 allowlist 파일 두 개
(`server/Harness/ContextPackIntegrityCli.cs`, `outputs/launch/run-executor.ps1`)를 `requiredInputs`에
넣고 있었다. 그 지시서를 수행하는 순간 두 pin이 stale이 되어 **그 지시서가 고치려는 하네스가
그 지시서를 막는다.** 조율 세션이 손으로 발견하기 전까지 경고로만 남아 있었다.

**경고로 남겨 두면 다음에도 같은 일이 난다.** 규칙이 명문인데 판정이 무르다.

## 1. 무엇을 하는가

`server/Harness/ContextPackIntegrityCli.cs`에서 `required-input-overlaps-allowlist`를
**warning이 아니라 failure로 집계**한다. 그 결과 겹침이 있으면 **exit 1**이다.

- 겹침 판정 로직 자체는 이미 있다. **집계 위치만 바꾼다** — 새 검사를 만들지 마라.
- `failureCount`·`warningCount`·`verdict` 계산이 서로 어긋나지 않게 하라.
- **다른 warning 종류는 건드리지 마라.** 이 지시서는 겹침 하나만 다룬다.

## 2. 하지 않을 일 (하면 반려)

- 다른 검사의 심각도 조정.
- 겹침을 피하려고 지시서 파일(`docs/handoff/queue/**`)을 고치는 것 — **이 지시서의 범위 밖이다.**
- 하네스를 통과시키려고 판정 기준을 무르게 하는 것.
- `outputs/**`·`server/` 루트 등 코덱스 영역 밖 파일 수정.

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `build-verify` **exit 0**
- [ ] 겹침이 있는 지시서가 하나라도 있으면 `context-pack-integrity` **exit 1**
- [ ] 겹침이 하나도 없으면 **exit 0** (현재 저장소 상태가 그렇다 — 회귀하면 안 된다)
- [ ] 출력 JSON에서 겹침이 `failureCount`에 반영된다

### 목적 기준 (사람 판정)

**"명문 규칙을 어긴 지시서가 발사 전에 멈춘다."**
지표만 만족시키는 우회로가 있다 — 예: 겹침을 아예 탐지하지 않게 만들어 경고도 실패도 없게 하는 것.
**그것은 목적 미달이다.**

## 4. 반증 시험 (없으면 반려)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | 겹침이 있는 지시서 fixture로 실행 | **exit 1**, 해당 항목이 failure로 집계 |
| 2 | 겹침이 없는 현재 저장소로 실행 | **exit 0** (회귀 없음) |
| 3 | 겹침 항목을 제거한 fixture | **exit 0** — 겹침 때문에 실패했음이 증명된다 |

**시험 1·2의 실제 exit code와 출력 JSON을 실행 보고에 붙여라.** "실패로 바꿨다"는 자기보고는 증거가 아니다.

## 허용 파일 (allowlist)

- server/Harness/ContextPackIntegrityCli.cs

> **이 allowlist는 코덱스 배타 영역 안에서만 닫힌다**(`ADR-002`). `docs/verification/cpx01-overlap-fails.md`는
> 의도적으로 넣지 않았다 — 코덱스 영역 밖이므로 검증 문서는 조율자가 쓴다. 실행자는 실행 보고만 낸다.
> `requiredInputs`에 이 파일을 넣지 않은 것도 같은 이유다(§0이 지적한 바로 그 결함을 이 지시서가 반복하지 않는다).
