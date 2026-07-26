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

**두 가지다. 서로 다른 규칙이므로 심각도도 다르다.**

### 1-A. 지시서 **안**의 겹침 → **실패**

`required-input-baseline-overlaps-own-allowlist`(현 `required-input-overlaps-allowlist`)를
**warning이 아니라 failure로 집계**한다. 겹침이 있으면 **exit 1**이다.

- 겹침 판정 로직 자체는 이미 있다. **집계 위치만 바꾼다** — 새 검사를 만들지 마라.
- `failureCount`·`warningCount`·`verdict` 계산이 서로 어긋나지 않게 하라.
- **다른 warning 종류는 건드리지 마라.**

### 1-B. 지시서 **사이**의 충돌 → **탐지해서 목록으로 낸다** (실패로 올리지 마라)

지시서 A의 allowlist 파일이 지시서 B의 `requiredInputs`에 있으면, **A가 착륙하는 순간 B의 pin이
stale이 된다.** 양쪽 다 규칙을 어기지 않았는데 함께 두면 교착이다.

새 코드 `cross-directive-pin-collision`을 추가하고, 큐의 지시서 전체를 훑어 아래를 낸다:

```json
{ "code": "cross-directive-pin-collision",
  "path": "<파일>", "writtenBy": "<A의 diId>", "pinnedBy": "<B의 diId>" }
```

- **실패로 올리지 마라.** 지금 저장소에 이미 존재하므로(§0 참조) 실패로 만들면 게이트가 즉시
  영구히 빨개진다. `FAIL-2026-010`이 그것을 금지한다.
- 최상위에 `crossDirectivePinCollisionCount`를 낸다. **세어지지 않으면 없는 것과 같다.**
- 이 항목은 `warningCount`와 **따로** 센다 — 종류가 다른 것을 한 통에 넣지 않는다.

## 2. 하지 않을 일 (하면 반려)

- 다른 검사의 심각도 조정.
- 겹침을 피하려고 지시서 파일(`docs/handoff/queue/**`)을 고치는 것 — **이 지시서의 범위 밖이다.**
- 하네스를 통과시키려고 판정 기준을 무르게 하는 것.
- `outputs/**`·`server/` 루트 등 코덱스 영역 밖 파일 수정.

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `build-verify` **exit 0**
- [ ] 1-A 겹침이 있는 지시서가 하나라도 있으면 `context-pack-integrity` **exit 1**
- [ ] 1-A 겹침이 하나도 없으면 **exit 0** — 단, 아래 착륙 절차를 마친 뒤에 잰다
- [ ] 출력 JSON에서 1-A가 `failureCount`에, 1-B가 `crossDirectivePinCollisionCount`에 반영된다

> **⚠ 이 지시서의 착륙에는 pin 갱신이 딸려 있다. 빠뜨리면 완료 판정이 나지 않는다.**
>
> **실측 2026-07-26**: `DLINT-01`이 이 지시서의 유일한 allowlist 파일
> `server/Harness/ContextPackIntegrityCli.cs`를 `requiredInputs`로 고정한다. 이 지시서를 수행하는
> 순간 그 pin이 stale이 되어 `context-pack-integrity`가 **exit 1**이다.
> **후보 패치의 결함이 아니다** — 원본 코드에 개행 하나만 추가해도 같은 실패가 난다(대조 실험으로 확인).
> 이것이 §1-B가 말하는 교착이며, 이 지시서 자신이 그 첫 사례다.
>
> 따라서 착륙은 **두 걸음이다**: ①실행자가 `ContextPackIntegrityCli.cs`를 고친다
> ②반입하는 사람이 `DLINT-01`의 해당 pin을 새 해시로 갱신한다.
> ②는 코덱스 영역 밖(`docs/handoff/queue/**`)이므로 **실행자의 일이 아니다.**
> exit 0 판정은 ② 이후에만 유효하다 — ① 직후에 재고 실패했다고 반려하지 마라.

### 목적 기준 (사람 판정)

**"명문 규칙을 어긴 지시서가 발사 전에 멈추고, 규칙을 어기지 않았는데 생기는 교착은 보인다."**

지표만 만족시키는 우회로가 있다:
- 겹침을 아예 탐지하지 않게 만들어 경고도 실패도 없게 하는 것.
- 1-B를 세기만 하고 어느 지시서 쌍인지 안 남겨 사람이 손으로 다시 찾게 하는 것.
- pin 갱신이 귀찮다고 `DLINT-01`의 참조를 지워 버리는 것 — **읽어야 할 참조를 없애는 것은 해결이 아니다.**

**셋 다 목적 미달이다.**

## 4. 반증 시험 (없으면 반려)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | 1-A 겹침이 있는 fixture | **exit 1**, 해당 항목이 `failureCount`에 집계 |
| 2 | 그 fixture에서 겹침 항목만 제거 | **exit 0** — 겹침 때문에 실패했음이 증명된다 |
| 3 | 1-B 충돌이 있는 fixture 쌍(A가 쓰는 파일을 B가 pin) | **exit 0**, `crossDirectivePinCollisionCount == 1`, 목록에 A·B의 diId가 둘 다 있다 |
| 4 | 1-B 충돌이 없는 fixture 쌍 | `crossDirectivePinCollisionCount == 0` |

**시험 1·2·3의 실제 exit code와 출력 JSON을 실행 보고에 붙여라.** "실패로 바꿨다"는 자기보고는 증거가 아니다.

> 시험 3이 **exit 0**인 것은 실수가 아니다. 1-B는 세고 드러내되 막지 않는다(§1-B의 이유).
> 막는 순간 지금 저장소가 영구히 빨개진다.

## 5. 이 규칙의 승격 (H/S 분류)

이 지시서가 다루는 것은 두 층이고, 둘의 성격이 다르다. 섞어서 한쪽으로 몰지 마라.

| 층 | 성격 | 귀속 |
| --- | --- | --- |
| 겹침·충돌 **탐지** | 파일 목록 대조로 Y/N이 갈린다 | **하네스** — 이 지시서가 만드는 것 |
| 착륙 시 **pin 갱신 절차** | 순서가 있는 사람 절차(누가·무엇을·언제) | **스킬** — `skills/common/directive-authoring.md` |

**하네스가 절차를 대신할 수 없다.** 탐지는 "충돌이 있다"까지고, "그러니 착륙 때 B의 pin을 갱신하라"는
절차다. 이 지시서는 **탐지만** 만든다. 절차 쪽은 별도 항목이며 `skills/`도 코덱스 배타 영역이므로
(`ADR-002`) 같은 실행자에게 갈 수 있으나 **다른 지시서로** 간다 — 한 번에 둘을 하면 하네스와 그
하네스를 설명하는 절차를 같은 세션이 쓰게 되어 `ADR-002`가 경계하는 자기 검증이 된다.

## 허용 파일 (allowlist)

- server/Harness/ContextPackIntegrityCli.cs

> **이 allowlist는 코덱스 배타 영역 안에서만 닫힌다**(`ADR-002`). `docs/verification/cpx01-overlap-fails.md`는
> 의도적으로 넣지 않았다 — 코덱스 영역 밖이므로 검증 문서는 조율자가 쓴다. 실행자는 실행 보고만 낸다.
> `requiredInputs`에 이 파일을 넣지 않은 것도 같은 이유다(§0이 지적한 바로 그 결함을 이 지시서가 반복하지 않는다).
