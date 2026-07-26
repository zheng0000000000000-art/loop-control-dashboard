```context-pack
{
  "diId": "GWIT-04",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-GWIT-04-selftest-negative-count.md",
    "docs/handoff/queue/directive-GWIT-01-failure-witness.md",
    "server/Harness/GateWitnessCheckCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# GWIT-04 — self-test가 **음성 사례 수를 스스로 센다** (`internalNegativeCases`가 쓸 수 있게)

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 선행: `GWIT-01`(rule 3과 §1-C 검증이 이미 구현돼 있다).

---

## 0. 문제 (실측 2026-07-26)

전체 반증 없는 검사가 17 → 8로 줄었고, 남은 8건 중 **3건이 self-test**다.

```
WP-STATE-INTEGRITY-LAND 반증 없음 5
  state-transition-selftest · recovery-selftest · trust-origin-selftest   ← 이 지시서
  build-verify · measure                                                  ← 별도 지시서
```

`GWIT-01` rule 3은 이 경우를 위해 만들었다 — **self-test가 거부 케이스를 품고 있으면
`internalNegativeCases`로 witness를 대신할 수 있다.** §1-C는 그 주장을 **실제 실행 출력에서
세어 대조**하도록 구현돼 있다(`GateWitnessCheckCli.CountInternalNegativeCases`).

**그런데 셀 필드가 없다.** 세 self-test의 출력은 전부 같은 모양이고, 음성 사례를 구분하지 않는다:

```json
{ "selfTest": "...", "verdict": "...", "casesRun": 19, "failed": 0,
  "cases": [ { "case": "reconciliation-fail", "pass": true }, ... ] }
```

`casesRun`은 **전체 수**다. `reconciliation-fail`·`candidate-toctou`·`v1-idempotency-rejected`처럼
**거부를 확인하는 케이스**와 `normal-new-transition`처럼 성공을 확인하는 케이스가 섞여 있고
구분할 방법이 없다.

## 1. 무엇을 하는가

세 self-test의 출력에 **음성 사례 수**를 낸다.

```json
{ "selfTest": "...", "verdict": "...", "casesRun": 19, "failed": 0,
  "negativeCaseCount": 12,
  "cases": [ { "case": "reconciliation-fail", "pass": true, "negative": true }, ... ] }
```

- 최상위 `negativeCaseCount` — `gate-witness-check`가 읽는 이름 셋
  (`internalNegativeCases`·`negativeCaseCount`·`rejectedCaseCount`) 중 하나여야 한다.
  **`negativeCaseCount`로 고정한다.**
- 케이스마다 `negative` 불리언을 함께 낸다. 합계만 있으면 무엇이 음성인지 사람이 볼 수 없다.

### 1-A. ★ 무엇이 "음성 사례"인가 — 이름으로 정하지 마라

**케이스 이름에 `fail`·`reject`가 들어가는지로 판정하지 마라.** 그건 문자열 프록시다.

**판정 기준: 그 케이스가 "거부·실패가 나야 통과"라고 단언하는가.** 즉 기대값이 성공이 아닌
케이스다. 구현은 실행자가 정하되 **케이스 정의부에서 파생**해야 하며, 별도 목록을 손으로
유지하는 방식은 금지한다 — 케이스를 추가하면서 목록 갱신을 잊으면 숫자가 조용히 틀린다.

### 1-B. 대상

```
server/Harness/... 를 통해 등재된 세 self-test 진입점
  state-transition-selftest · recovery-selftest · trust-origin-selftest
```

세 진입점은 `HREG-01`이 `HarnessRegistry`에 등재했다. **실제 케이스 정의는 `server/` 루트의
`StateApplierCli`·`RecoveryCli`·`TrustOriginCli`에 있을 수 있다** — 그 경우 이 지시서의
allowlist 밖이다. **밖이면 손대지 말고 보고에 적어라**(§5).

## 2. 하지 않을 일 (하면 반려)

- 케이스 이름 문자열로 음성 여부를 판정하는 것.
- 손으로 유지하는 음성 케이스 목록.
- 케이스를 추가·삭제·변경하는 것. **이 지시서는 세는 것만 다룬다.**
- `GATE-MANIFEST.json` 수정 — 영역 밖. 조율자 후속이다.
- allowlist 밖 파일 수정. 필요하면 §5대로 보고만 하라.

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `build-verify` **exit 0**
- [ ] 세 self-test가 각각 **exit 0**이고 출력에 `negativeCaseCount >= 1`이 있다
- [ ] 케이스마다 `negative` 불리언이 있다
- [ ] `negativeCaseCount`가 `cases`에서 `negative: true`인 수와 **일치**한다
- [ ] `casesRun`은 그대로다(전체 수 — 의미를 바꾸지 마라)

### 목적 기준 (사람 판정)

**"이 self-test가 거부를 거부로 확인한다는 것이 기계로 읽힌다."**

지표만 만족시키는 우회로: `negativeCaseCount`를 상수로 박는 것. 케이스를 지워도 숫자가 안
움직이면 **주장일 뿐이고 §1-C 검증이 무의미해진다.** 그래서 §4 시험 2를 둔다.

## 4. 반증 시험 (전부 실측)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | 세 self-test 실행 | exit 0 · `negativeCaseCount >= 1` · `negative` 합계와 일치 |
| 2 | **음성 케이스 하나를 임시로 비활성화** | `negativeCaseCount`가 **1 줄어든다** |
| 3 | 양성 케이스 하나를 임시로 비활성화 | `negativeCaseCount`는 **그대로**, `casesRun`만 준다 |
| 4 | 세 self-test 연속 실행 후 `git status --porcelain` | **변경 0** (비파괴 유지) |

**시험 2·3이 이 지시서의 목적 자체다.** 상수 구현이면 둘 다 숫자가 안 움직인다.
**출력 원문을 실행 보고에 붙여라.**

## 5. allowlist 밖이면 보고만 하라

케이스 정의가 `server/` 루트에 있으면 **고치지 말고** 어느 파일의 어느 지점인지 보고에 적어라.
조율자가 그 파일용 지시서를 따로 낸다. **범위를 넘어 고치면 반려한다** — 오늘 첫 실사격에서
코덱스가 범위 충돌을 이유로 착수를 거부한 것이 옳은 판단이었다.

## 6. 후속 (이 지시서가 하지 않는다)

조율자가 `WP-STATE-INTEGRITY-LAND`의 세 self-test 검사에 `internalNegativeCases: N`을 붙이고,
`gate-witness-check`가 §1-C 검증을 통과하는지 확인한다. 그 뒤 LAND의 남은 것은
`build-verify`·`measure` 둘뿐이다.

## 착륙 (두 걸음 — `skills/common/directive-authoring.md` §7)

착륙 전 `crossDirectivePinCollisions`를 확인하라. **소유 지시서를 가정하지 말고 stale 경로를
직접 grep해서 찾아라.**

## 허용 파일 (allowlist)

- server/Harness/HarnessRegistry.cs
- server/Harness/GateWitnessCheckCli.cs

> 검증 문서는 영역 밖이라 조율자가 쓴다.
