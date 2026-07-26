```context-pack
{
  "diId": "CG04A-R1",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-CG04A-R1-contract-report.md",
    "docs/verification/cg04b-contract-and-registration.md",
    "server/Harness/DiCompletionCheckCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# CG04A-R1 — CLI 계약 검사가 **위반을 보고하지 못하고 터진다**

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 출처: `CG04A` 반입(`947bafe`) 코드의 결함. `CG04B` 시험 1이 실측으로 잡았다.

---

## 0. 문제 (실측 2026-07-26)

`critical: true`인 `trust-origin`의 배선을 임시로 지우고 게이트를 돌렸다.

```json
// outputs/gates/cli-neg.gate.json
"failures": [{"subject": "", "code": "harness-error", "message": "The node already has a parent."}]
```

**계약 위반을 보고한 것이 아니라 하네스가 터졌다.** `System.Text.Json.Nodes`의 부모 중복 오류다
(같은 `JsonNode` 인스턴스를 두 부모에 붙였다).

- 배선이 온전하면 → `PASS`, exit 0 (정상)
- **계약 위반이 실제로 생기면 → 크래시**

즉 이 검사기는 **보고할 것이 없을 때만 동작한다.** 존재 이유인 경로에서 죽는다.

**`exit 1`이라는 지표는 만족한다.** 게이트는 멈춘다. 그래서 exit code만 보면 통과로 셀 수 있고,
`failures[]`를 열어보지 않으면 "게이트가 잡았다"고 오독한다. 원본 `CODEX-GATE-04` §2가 요구한 것은
멈춤이 아니라 **"배선이 사라지면 게이트가 그것을 말한다"**이다.

## 1. 무엇을 하는가

`server/Harness/DiCompletionCheckCli.cs`의 CLI 계약 검사에서 `JsonNode` 재사용을 없앤다.
같은 노드를 두 곳에 붙이지 말고 필요하면 `DeepClone()`한다.

**보고 내용에 다음이 반드시 들어간다:**

- 사라진 명령의 **이름** (`subject`가 비어 있으면 안 된다)
- `critical` 여부
- 방향 구분: **계약에 있는데 배선에 없음 = 실패** · 배선에 있는데 계약에 없음 = warning

## 2. 하지 않을 일 (하면 반려)

- 크래시를 `try/catch`로 삼켜 통과시키는 것. **터지지 않는 것과 보고하는 것은 다르다.**
- 계약 검사 자체를 끄거나 조건을 무르게 하는 것.
- `docs/handoff/**` 수정 — 영역 밖이다.

## 3. 완료 조건

### 지표 기준

- [ ] `build-verify` **exit 0**
- [ ] 계약에 있는 `critical` 명령의 배선을 지우면 게이트 **exit 1**이고 `failures[]`에
      **그 명령 이름이 나온다** (`code`가 `harness-error`가 아니다)
- [ ] 배선 복구 후 **exit 0**
- [ ] 배선에만 있고 계약에 없는 명령은 **warning**이지 실패가 아니다

### 목적 기준

**"배선이 사라지면 게이트가 무엇이 사라졌는지 말한다."**
지표만 만족시키는 우회로: 크래시를 잡아 `harness-error`를 `contract-violation`으로 이름만 바꾸는 것.
**이름이 아니라 사라진 명령이 나와야 한다.**

## 4. 반증 시험 (전부 실측. **코드 검토로 갈음하지 마라**)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | `critical` 명령 하나의 배선 제거 | exit 1 · `failures[].subject`에 **그 명령 이름** |
| 2 | 배선 복구 | exit 0 |
| 3 | 계약에 없는 새 명령을 배선에 추가 | **warning**, 실패 아님 |
| 4 | 두 개 이상 동시에 제거 | **둘 다** 보고된다(첫 번째에서 멈추지 않는다) |

**시험 1·4의 `failures[]` 원문을 실행 보고에 붙여라.**

## 착륙 (두 걸음 — `skills/common/directive-authoring.md` §7)

`DLINT-01`이 `ContextPackIntegrityCli.cs`를 pin하지만 이 지시서는 그 파일을 쓰지 않는다.
착륙 전 `crossDirectivePinCollisions`를 확인하라.

## 허용 파일 (allowlist)

- server/Harness/DiCompletionCheckCli.cs

> 검증 문서는 영역 밖이라 조율자가 쓴다.
