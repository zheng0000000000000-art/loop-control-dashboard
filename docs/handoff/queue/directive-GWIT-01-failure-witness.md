```context-pack
{
  "diId": "GWIT-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-GWIT-01-failure-witness.md",
    "docs/handoff/GATE-MANIFEST.json",
    "server/Harness/HarnessRegistry.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수. **이 지시서 자신도 자기 규칙을 지켜야 한다** — §5.

---

# GWIT-01 — 성공만 확인하는 게이트를 **드러낸다** (`gate-witness-check`)

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 근거: 2026-07-26 조율 세션 실측. 아래 §0.

---

## 0. 문제 (실측 2026-07-26)

이 저장소는 지시서마다 **반증 시험**을 요구한다. 그런데 **게이트 자체에는 그 요구가 없다.**
현재 매니페스트를 세어 보면:

| 게이트 | 검사 | 반증 witness 있는 명령 | **반증 없는 검사** |
| --- | --- | --- | --- |
| `POST-EXECUTOR` | 7 | 1 | **6** |
| `POST-COMMIT` | 5 | 1 | **4** |
| `WP-STATE-INTEGRITY-LAND` | 14 | 1 | **7** |

**세 게이트를 통틀어 반증 witness를 가진 명령은 `handoff-integrity` 하나뿐이다.**
(fixture a/b/d/e → exit 1, c → 0, malformed → 2. 그래서 그 검사만 "실패를 실패로 보고한다"가 증명돼 있다.)

나머지 17개는 **성공만 확인한다.** 그 검사가 고장 나 항상 0을 내도 게이트는 초록이다.

**같은 날 실제로 그런 일이 있었다.** CLI 계약 검사가 위반이 생기면 크래시했는데,
`exit 1`이라 게이트는 "잡았다"처럼 보였다. `failures[]`를 열어야 `harness-error`인 것이 보였다.
**성공만 재는 게이트는 자기가 죽은 것을 모른다.**

## 1. 무엇을 하는가

`server/Harness/GateWitnessCheckCli.cs`(신규)와 `HarnessRegistry` 등재.

`GATE-MANIFEST.json`을 읽어 게이트별로 **반증 witness 없는 검사를 세고 목록으로 낸다.**

### 1-A. 판정 규칙

검사 `C`(`expectedExit == 0`)에 대해, **같은 게이트 안에** 아래 중 하나가 있으면 witness가 있다:

1. 같은 `command`를 쓰고 `expectedExit != 0`인 다른 검사 (현재 `handoff-integrity`가 이 형태)
2. 검사에 `negativeWitness` 필드가 있고 그 값이 같은 게이트의 다른 검사 `order`를 가리킨다
3. 검사에 `internalNegativeCases` 필드가 있고 값이 1 이상이다 —
   자체 self-test가 거부 케이스를 포함하는 경우(예: `state-transition-selftest`의 19 케이스에는
   `reconciliation-fail`·`candidate-toctou` 등 거부가 들어 있다). **이 필드는 주장이므로
   §1-C의 검증을 함께 요구한다.**

### 1-B. 출력 (실패로 올리지 마라)

```json
{
  "harness": "gate-witness-check",
  "gates": [{ "gateId": "...", "checkCount": 14,
              "witnessedCount": 7, "unwitnessedCount": 7,
              "unwitnessed": [{ "order": 1, "command": "build-verify" }] }],
  "totalUnwitnessed": 17
}
```

- **exit 0으로 끝난다.** 지금 17건이 실재하므로 실패로 만들면 게이트가 즉시 영구히 빨개진다
  (`FAIL-2026-010`). **세고 드러내되 막지 않는다** — `CPX-01` 1-B와 같은 방침이다.
- 최상위 `totalUnwitnessed`를 반드시 낸다. **세어지지 않으면 없는 것과 같다.**

### 1-C. 게이트별 opt-in — 여기서만 막는다

게이트 객체에 `"requireFailureWitness": true`가 있으면 **그 게이트에 한해 exit 1**이다.
`internalNegativeCases`를 주장한 검사는 이때 **실제 실행 출력에서 실패 케이스 수를 세어 대조한다** —
주장만으로 통과시키지 마라. 세지 못하면 witness 없음으로 친다.

**이 지시서는 어느 게이트에도 그 플래그를 켜지 않는다.** 켜는 것은 조율자 후속이며 사람 판단이다.

## 2. 하지 않을 일 (하면 반려)

- `GATE-MANIFEST.json`·`HARNESSES.md` 수정 — **영역 밖.** 조율자 후속이다.
- 기존 게이트를 빨갛게 만드는 것. 이 하네스는 기본 exit 0이다.
- 반증 없는 검사를 게이트에서 빼서 숫자를 줄이는 것. **검사를 없애는 것은 해결이 아니다.**
- `internalNegativeCases`를 문면만 보고 믿는 것(§1-C).

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `build-verify` **exit 0**
- [ ] `gate-witness-check` **exit 0**이고 현재 매니페스트에서 `totalUnwitnessed == 17`
- [ ] 게이트별 수치가 6 / 4 / 7 (`POST-EXECUTOR` / `POST-COMMIT` / `WP-STATE-INTEGRITY-LAND`)
- [ ] `requireFailureWitness: true`인 게이트가 있으면 그 게이트에서 **exit 1**
- [ ] `HarnessRegistry.RegisteredNames`에 `gate-witness-check`가 있다

> 위 17·6·4·7은 2026-07-26 실측값이다. **다르게 나오면 매니페스트가 바뀐 것이니
> 판정 규칙을 고치기 전에 무엇이 바뀌었는지 먼저 밝혀라.**

### 목적 기준 (사람 판정)

**"성공만 재는 검사가 몇 개인지 보인다."**

지표만 만족시키는 우회로:
- `internalNegativeCases`를 모든 검사에 붙여 숫자를 0으로 만드는 것 — **주장으로 숫자를 지운다.**
- 반증 없는 검사를 게이트에서 제거해 숫자를 줄이는 것 — **검사를 없애 초록을 만든다.**

**둘 다 목적 미달이다.**

## 4. 반증 시험 (전부 실측)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | 현재 매니페스트로 실행 | exit 0 · `totalUnwitnessed == 17` · 게이트별 6/4/7 |
| 2 | 임시 매니페스트에 `handoff-integrity`의 fixture 검사(비0 기대값)를 전부 제거 | 그 게이트의 `unwitnessedCount`가 **증가한다** |
| 3 | 임시 매니페스트의 한 게이트에 `requireFailureWitness: true` | **exit 1**, 어느 검사가 witness 없는지 목록에 나온다 |
| 4 | 모든 검사에 반증 짝을 갖춘 임시 게이트 | `unwitnessedCount == 0`, `requireFailureWitness`여도 **exit 0** |

**시험 2·3의 출력 원문을 실행 보고에 붙여라.** 특히 시험 2는 이 하네스가 실제로 매니페스트를
읽는지 증명한다 — 상수를 돌려주는 구현이면 시험 2에서 숫자가 안 움직인다.

## 5. ★ 이 지시서는 자기 규칙을 지킨다

`gate-witness-check` 자신도 게이트에 등재되면 **반증 witness가 필요하다.**
시험 3의 임시 매니페스트(플래그 켠 게이트)가 그 witness다 — 등재 시 조율자가
`negativeWitness`로 묶을 수 있도록 **그 fixture를 `docs/qa/`에 남겨라.**

`docs/qa/`는 이 지시서의 allowlist에 포함돼 있다(승인된 fixture 경로).

## 착륙 (두 걸음 — `skills/common/directive-authoring.md` §7)

착륙 전 `crossDirectivePinCollisions`를 확인하라. `HarnessRegistry.cs`를
`GATE-TRUTH-01`이 pin한다(2026-07-26 실측) — ②단계에서 갱신할 대상이다.

## 6. 후속 (이 지시서가 하지 않는다)

조율자가 ①`GATE-MANIFEST`에 `gate-witness-check`를 등재하고 ②어느 게이트에
`requireFailureWitness: true`를 켤지 정한다. **한 게이트씩 켜라** — 한꺼번에 켜면 17건이
동시에 빨개지고, 그러면 무시된다(`FAIL-2026-010`).

## 허용 파일 (allowlist)

- server/Harness/GateWitnessCheckCli.cs
- server/Harness/HarnessRegistry.cs
- docs/qa/gate-witness/

> 검증 문서는 영역 밖이라 조율자가 쓴다.
