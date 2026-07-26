```context-pack
{
  "diId": "CG04A",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-CODEX-GATE-04-gate-truth.md",
    "docs/handoff/queue/directive-CG04A-harness-truth.md",
    "docs/handoff/decisions/ADR-016-gate-runner-authority.md"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

> **`directive-CODEX-GATE-04-gate-truth.md`를 `requiredInputs`에 넣지 않았다.** 그 파일은
> `CODEX-GATE-04` 자신의 allowlist라 pin하면 교차 충돌이다(`CPX-01` 1-B). 실제로 한 번 넣었다가
> 분할 표시를 추가하는 순간 stale이 됐다 — 하네스가 잡았다. `readOrder`로만 읽는다.

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → positive·negative·결정성 시험 전부 필수.

---

# CG04A — 게이트가 거짓말하는 지점을 코드에서 고친다 (`CODEX-GATE-04`의 코덱스 영역 절반)

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 출처: **`CODEX-GATE-04`를 둘로 나눈 것**이다. 원본의 실측·근거를 그대로 승계한다.
- 후속: `CG04B`(계약 데이터 생성 + 게이트 등재). **A가 먼저다** — §5.

---

## 0. 왜 나눴는가 (실측 2026-07-26)

`CODEX-GATE-04`의 allowlist가 **코덱스 영역을 걸친다**:

```
server/Harness/DiCompletionCheckCli.cs      ← 코덱스 배타 (ADR-002)
server/Harness/ClaimCheckCli.cs             ← 코덱스 배타
server/Harness/ScopeCheckCli.cs             ← 코덱스 배타
server/Harness/ContextPackIntegrityCli.cs   ← 코덱스 배타
docs/handoff/CLI-CONTRACT.json              ← 영역 밖
docs/handoff/GATE-MANIFEST.json             ← 영역 밖
```

`CodexHarnessLauncher`는 요청을 `allowed-paths-outside-codex-territory`로 **거절한다**(exit 2).
거절을 지나쳐도 **코덱스가 착수를 거부한다** — 2026-07-26 첫 실사격에서 실제로 그랬다:
*"착수할 수 없습니다. 쓰기 범위가 필수 완료 기준과 충돌합니다."*

**그래서 나눈다.** 이 지시서는 코덱스 영역 안에서만 닫힌다.

## 1. 무엇을 하는가 (원본 §1·§4·§5-1·§5-2·§5-4 + §2의 검사기)

원본의 각 절을 그대로 따르되, 대상 파일이 `server/Harness/**`인 것만 한다.

| # | 원본 절 | 대상 | 요지 |
| --- | --- | --- | --- |
| 1 | §1 | `DiCompletionCheckCli.cs` | `--no-build`를 없앤다. **낡은 바이너리를 재는 것이 가장 위험한 거짓말이다**(ADR-016 실측: 같은 게이트에서 거짓 FAIL 3건) |
| 2 | §4 | `ClaimCheckCli.cs` | 심볼 검색에 `--untracked` 추가. **고치기 전 MISMATCH를 먼저 재현해 보여라** |
| 3 | §5-1 | `ScopeCheckCli.cs` | 잡음에 잠긴 것을 걷어낸다. 죽은 게 아니라 안 들린 것이다 |
| 4 | §5-2 | `ClaimCheckCli.cs` | `FILE-CLAIMS` liveness의 PID 프록시를 없앤다. **PID 재사용으로 거짓말한다** |
| 5 | §5-4 | `ContextPackIntegrityCli.cs` | sha256을 **어느 런타임이 계산하는지** 고정한다 |
| 6 | §2의 절반 | `DiCompletionCheckCli.cs` | `CLI-CONTRACT.json`을 **읽어 판정하는 검사기**를 만든다. 계약에 있는데 배선에 없으면 **exit 1**, 반대는 warning. `critical: true`면 무조건 실패 |

**6번에서 `CLI-CONTRACT.json`을 만들지 마라.** 그 파일은 영역 밖이고 `CG04B`가 만든다.
대신 **현재 실재 배선을 열거해 계약 후보를 stdout으로 낼 수 있는 모드**를 함께 만든다
(예: `--emit-cli-contract`). 원본 §2가 *"손으로 적지 마라"*고 했으므로 **생성 수단이 코드 쪽에 있어야
`CG04B`가 손으로 적지 않을 수 있다.** 파일 쓰기는 하지 말고 stdout으로만 낸다.

## 2. 하지 않을 일 (하면 반려)

- `docs/handoff/CLI-CONTRACT.json`·`GATE-MANIFEST.json` 생성·수정 — **영역 밖.** `CG04B`의 일이다.
- 게이트 등재(원본 §3) — 같은 이유.
- `05H`와 겹치는 reconciliation 영역(원본 §경계).
- 판정이 불편하다고 기대값을 옮기는 것. 특히 **`scope-check`를 통과시키려고 검사를 무르게 하지 마라.**

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `build-verify` **exit 0**
- [ ] `di-completion-check`가 **빌드한 바이너리**를 잰다 — 소스를 고친 뒤 빌드 없이 재면 옛 결과가
      나오지 않는다(§4 시험 1)
- [ ] `claim-check`가 untracked 신규 파일을 본다 — 고치기 전 MISMATCH가 사라진다
- [ ] `di-completion-check --emit-cli-contract`가 현재 배선을 열거해 stdout으로 낸다 (파일은 안 쓴다)
- [ ] `program-verify verify --gate POST-COMMIT` **exit 0** (ADR-016: 러너 이름을 함께 적는다)

### 목적 기준 (사람 판정)

**"게이트가 자기가 재지 않은 것을 통과라고 말하지 않는다."**

지표만 만족시키는 우회로: `--no-build`만 지우고 **왜 위험한지**를 검사로 굳히지 않는 것.
빌드 여부를 사람이 기억해야 하면 다음에 또 잊는다.

## 4. 반증 시험 (전부 실측. **코드 검토로 갈음하지 마라**)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | 소스를 고쳐 실패하게 만든 뒤 **빌드 없이** `di-completion-check` 실행 | **실패가 보인다**(옛 통과가 나오지 않는다) |
| 2 | untracked 신규 파일이 포함된 배치로 `claim-check` — 고치기 **전** | **MISMATCH 재현** |
| 3 | 같은 배치로 고친 **뒤** | **MISMATCH 사라짐** |
| 4 | `FILE-CLAIMS`에 죽은 프로세스의 PID를 재사용한 항목을 넣고 liveness 판정 | **살아있다고 말하지 않는다** |
| 5 | `scope-check`를 잡음 있는 트리에서 실행 | 판정이 **읽힌다**(exit 2로 죽지 않는다) |
| 6 | `--emit-cli-contract` 출력과 실재 배선 대조 | 일치. **손으로 적은 항목이 없다** |

**시험 2·3의 실제 출력을 실행 보고에 붙여라.** "오탐이 사라졌다"는 자기보고는 증거가 아니다.

## 5. 순서 — A 다음 B

`CG04B`(계약 데이터 + 등재)는 이 지시서 착륙 뒤에 한다.

- §3의 등재는 **`scope-check`가 판정 가능해진 뒤에만** 가능하다. 원본 §3이 못박았다:
  *"판정할 수 없는 검사는 넣지 마라 — `scope-check`는 dirty 트리에서 늘 exit 1이다."*
  잡음을 걷어내는 것이 이 지시서(§1-3)이므로 **A가 먼저다.**
- `CLI-CONTRACT.json`도 `--emit-cli-contract`가 있어야 손으로 적지 않을 수 있다.

## 착륙 (두 걸음 — `skills/common/directive-authoring.md` §7)

`DLINT-01`이 `ContextPackIntegrityCli.cs`를 `requiredInputs`로 pin한다
(`crossDirectivePinCollisions`에 등재됨). 착륙은 ①실행자가 코드를 고치고
②**반입하는 사람**이 `DLINT-01`의 해당 pin을 갱신하는 두 걸음이다. ②는 영역 밖이다.

## 허용 파일 (allowlist)

- server/Harness/DiCompletionCheckCli.cs
- server/Harness/ClaimCheckCli.cs
- server/Harness/ScopeCheckCli.cs
- server/Harness/ContextPackIntegrityCli.cs

> 검증 문서(`docs/verification/cg04a-harness-truth.md`)는 **의도적으로 뺐다** — 영역 밖이므로
> 조율자가 쓴다. `requiredInputs`에 위 네 파일을 넣지 않은 것도 규칙이다(`_header.md`: 읽기 참조와
> 쓰기 대상은 겹치지 않는다).
