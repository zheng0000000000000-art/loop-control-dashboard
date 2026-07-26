```context-pack
{
  "diId": "GWIT-02",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-GWIT-02-fixture-modes.md",
    "docs/handoff/queue/directive-GWIT-01-failure-witness.md",
    "server/Harness/HandoffIntegrityCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# GWIT-02 — `gate-clean`·`doc-integrity`에 픽스처 모드를 넣는다 (반증 witness를 만들 수 있게)

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 선행: `GWIT-01` 착륙(`c987783`). 후속: 조율자가 witness를 매니페스트에 등재한다.

---

## 0. 문제 (실측 2026-07-26)

`POST-COMMIT`에 `requireFailureWitness`를 켜고 반증 witness 3건을 붙여 반증 없는 검사를
**4 → 2**로 줄였다. 남은 둘은 **선언으로 만들 수 없다.**

```
gate-clean      기대값 0 = "트리가 깨끗하다"
                반대는 "더러운 트리"인데 매니페스트로 표현할 수 없다
doc-integrity   같은 이유
```

`gate-witness-check`는 지금 `POST-COMMIT`에서 **exit 1**이고, 이 둘 때문에 0이 되지 않는다.
그래서 **`gate-witness-check` 자신을 게이트에 등재할 수 없다** — 등재하면 영구히 빨개진다
(`FAIL-2026-010`).

## 1. 무엇을 하는가 — **이미 있는 방식을 따른다**

`handoff-integrity`에는 **픽스처 격리 모드**가 이미 있다:

```
handoff-integrity --workstate <경로> --applier-log <경로>
  → 그 파일들만 읽어 판정한다. production 상태를 건드리지 않는다.
```

오늘 그 모드로 반증 witness를 만들었다(fixture-a → exit 1, malformed → exit 2).
**같은 모양을 두 하네스에 넣는다. 새 방식을 만들지 마라.**

### 1-A. `gate-clean --status-fixture <파일>`

`git status --porcelain` 출력을 담은 파일을 받아, **git을 실행하지 않고** 그 내용으로 판정한다.

- 파일이 비어 있으면 clean → **exit 0**
- 내용이 있으면 dirty → **exit 1** (지금 더러운 트리에서 내는 것과 같은 코드)
- 파일이 없거나 못 읽으면 **exit 2** (fail-closed — 모르는 것을 clean으로 적지 않는다)
- **production 판정 경로를 바꾸지 마라.** 인자가 없으면 지금과 똑같이 동작해야 한다.

### 1-B. `doc-integrity --fixture <디렉터리>`

생성 문서와 그 원본이 어긋난 상태를 담은 디렉터리를 받아 그 안에서만 판정한다.

- 어긋나면 **exit 1** · 일치하면 **exit 0** · 읽을 수 없으면 **exit 2**
- 무엇을 픽스처로 볼지는 실행자가 정하되, **현재 production 판정이 무엇을 비교하는지와 같아야
  한다.** 픽스처만 통과시키는 별도 로직을 만들면 그 witness는 아무것도 증명하지 못한다.

### 1-C. 픽스처 파일을 `docs/qa/gate-witness/`에 만든다

```
gate-clean-dirty.status      내용이 있는 status 출력 (witness, exit 1 기대)
gate-clean-clean.status      빈 파일 (positive, exit 0 기대)
doc-integrity-mismatch/      어긋난 상태 (witness, exit 1 기대)
```

각 픽스처 파일에 **"이 파일이 통과하기 시작하면 그 검사가 죽은 것이다. 고치지 마라"**를 적어라.
`docs/qa/gate-witness/stale-pin-directive.md`가 그 형식의 선례다.

## 2. 하지 않을 일 (하면 반려)

- **production 판정 경로 변경.** 인자 없이 부르면 지금과 같아야 한다. 이 지시서는 **입구를
  하나 더 여는 것**이지 판정을 바꾸는 것이 아니다.
- `GATE-MANIFEST.json` 수정 — **영역 밖.** 조율자 후속이다.
- 픽스처만 통과시키는 별도 판정 로직. **그러면 witness가 거짓말한다.**
- `git status`를 실제로 부르는 fixture 모드. 그러면 결정적이지 않다.

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `build-verify` **exit 0**
- [ ] `gate-clean --status-fixture docs/qa/gate-witness/gate-clean-dirty.status` → **exit 1**
- [ ] `gate-clean --status-fixture docs/qa/gate-witness/gate-clean-clean.status` → **exit 0**
- [ ] `gate-clean --status-fixture <없는 파일>` → **exit 2**
- [ ] `doc-integrity --fixture docs/qa/gate-witness/doc-integrity-mismatch` → **exit 1**
- [ ] 인자 없이 `gate-clean`·`doc-integrity` → **지금과 같은 결과** (회귀 없음)

### 목적 기준 (사람 판정)

**"이 검사가 실패를 실패로 보고한다는 것을 매니페스트로 증명할 수 있다."**

지표만 만족시키는 우회로: 픽스처 모드가 **입력을 보지 않고** 파일 이름으로 exit를 정하는 것.
`dirty`가 이름에 있으면 1을 내는 식이면 지표는 전부 통과하고 **아무것도 증명하지 못한다.**
그래서 §4 시험 3을 둔다.

## 4. 반증 시험 (전부 실측. **코드 검토로 갈음하지 마라**)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | dirty 픽스처 | exit 1 |
| 2 | clean(빈) 픽스처 | exit 0 |
| 3 | **dirty 픽스처의 내용을 비우고 이름은 그대로** | **exit 0** — 내용을 본다는 증명 |
| 4 | 없는 픽스처 경로 | exit 2 (fail-closed) |
| 5 | 인자 없이 실행 | production 동작과 동일 |
| 6 | `doc-integrity --fixture` 어긋난/일치 두 경우 | 1 / 0 |

**시험 3이 이 지시서의 목적 자체다.** 출력 원문을 실행 보고에 붙여라.

## 5. 후속 (이 지시서가 하지 않는다)

조율자가 ①`POST-COMMIT`에 두 witness를 등재하고 ②`gate-witness-check`가 그 게이트에서 **exit 0**이
되는지 확인한 뒤 ③비로소 `gate-witness-check`를 게이트에 등재한다.
**②가 0이 되기 전에 ③을 하지 마라** — 그 순간 영구히 빨개진다.

## 착륙 (두 걸음 — `skills/common/directive-authoring.md` §7)

착륙 전 `crossDirectivePinCollisions`를 확인하라. **소유 지시서를 가정하지 말고 stale 경로를
직접 grep해서 찾아라** — 2026-07-26에 가정했다가 한 번 틀렸다.

## 허용 파일 (allowlist)

- server/Harness/GateCleanCli.cs
- server/Harness/DocIntegrityCli.cs
- docs/qa/gate-witness/

> 검증 문서는 영역 밖이라 조율자가 쓴다.
