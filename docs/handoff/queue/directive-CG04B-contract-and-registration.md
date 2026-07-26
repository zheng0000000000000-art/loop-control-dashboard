```context-pack
{
  "diId": "CG04B",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-CODEX-GATE-04-gate-truth.md",
    "docs/handoff/queue/directive-CG04A-harness-truth.md",
    "docs/handoff/queue/directive-CG04B-contract-and-registration.md"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

> **`directive-CODEX-GATE-04-gate-truth.md`를 `requiredInputs`에 넣지 않았다.** 그 파일은
> `CODEX-GATE-04` 자신의 allowlist라 pin하면 교차 충돌이다(`CPX-01` 1-B). 실제로 한 번 넣었다가
> 분할 표시를 추가하는 순간 stale이 됐다 — 하네스가 잡았다. `readOrder`로만 읽는다.

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `infra`** → 판정은 게이트 exit code로 한다.

---

# CG04B — CLI 계약 데이터와 게이트 등재 (`CODEX-GATE-04`의 조율자 영역 절반)

- actor: **조율자** — 대상이 `docs/handoff/**`라 `ADR-002`상 코덱스 영역이 **아니다.**
- 출처: **`CODEX-GATE-04`를 둘로 나눈 것.** 원본 §2의 데이터 절반과 §3을 승계한다.
- **선행: `CG04A` 착륙.** 아래 §0 — 순서를 뒤집으면 둘 다 못 한다.

---

## 0. 선행이 왜 강제인가 (실측 근거)

**둘 다 `CG04A`가 만드는 것에 의존한다.**

1. **`CLI-CONTRACT.json`의 초기값**: 원본 §2가 *"현재 실재 배선에서 열거해 생성한다 —
   **손으로 적지 마라**"*고 못박았다. 열거 수단(`di-completion-check --emit-cli-contract`)은
   `server/Harness/**`에 있고 `CG04A`가 만든다. **그 전에 하면 손으로 적게 된다.**
2. **게이트 등재**: 원본 §3이 *"판정할 수 없는 검사는 넣지 마라 — `scope-check`는 dirty 트리에서
   늘 exit 1이다"*라고 했다. 잡음을 걷어내는 것이 `CG04A` §1-3이다. **그 전에 등재하면
   기대값을 정할 수 없는 검사를 배열에 넣는 것**이고, 그것이 원본이 금지한 바로 그 행위다.

## 1. 무엇을 하는가

### 1-A. `docs/handoff/CLI-CONTRACT.json` 생성 (원본 §2의 데이터 절반)

- `schemaVersion: 1`.
- **초기값은 `di-completion-check --emit-cli-contract`의 출력을 그대로 쓴다.** 손으로 항목을
  더하거나 빼지 마라. 더해야 할 것이 있으면 **왜 열거되지 않았는지**를 먼저 밝혀라 —
  열거가 틀렸다면 그건 `CG04A`로 돌아갈 문제다.
- `critical: true`를 붙이는 대상: `state-transition`·`projection`·`measure`·**하네스 전부**.
  원본이 지정한 목록이며 임의로 줄이지 마라.

### 1-B. `GATE-MANIFEST.json` 등재 (원본 §3)

- `POST-EXECUTOR`·`POST-COMMIT`에 `scope-check`·`claim-check`를 등재한다.
- **기대값을 정할 수 없으면 넣지 말고 `note`에 이유를 적어라.** 원본의 지시이며,
  "일단 넣고 나중에 고친다"는 영구히 빨간 게이트를 만든다(`FAIL-2026-010`).
- 등재 후 `HARNESSES.md`를 **생성 명령으로** 갱신한다:
  `dotnet run --project server -- di-completion-check --emit-doc docs/handoff/HARNESSES.md`
  **손으로 표를 고치지 마라** — 그 파일은 생성물이다.

## 2. 하지 않을 일 (하면 반려)

- `server/Harness/**` 수정 — **코덱스 배타 영역이고 `CG04A`의 일이다.**
- `CLI-CONTRACT.json`의 항목을 손으로 작성하는 것.
- 판정 불가능한 검사를 "일단" 등재하는 것.
- `HARNESSES.md`를 직접 편집하는 것.

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `docs/handoff/CLI-CONTRACT.json`이 존재하고 `--emit-cli-contract` 출력과 **일치**한다
- [ ] 계약에 있는 명령을 배선에서 하나 지우면 게이트가 **exit 1** (§4 시험 1)
- [ ] `GATE-MANIFEST.json`에 등재한 검사에 `expectedExit`이 **전부** 있다
- [ ] `HARNESSES.md`가 `--emit-doc`으로 재생성됐다(손으로 고친 흔적이 없다)
- [ ] `program-verify verify --gate POST-COMMIT` **exit 0** (ADR-016: 러너 이름을 함께 적는다)
- [ ] `doc-integrity` **exit 0**

### 목적 기준 (사람 판정)

**"배선이 사라지면 게이트가 그것을 말한다."**

지표만 만족시키는 우회로: 계약을 만들되 `critical`을 비워 두는 것. 그러면 파일은 있고
**아무것도 막지 않는다.** 원본이 지정한 `critical` 목록을 줄이는 것은 목적 미달이다.

## 4. 반증 시험

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | `CliRouter`에서 `critical: true`인 명령 하나의 배선을 임시로 지운다 | 게이트 **exit 1**, 어느 명령인지 이름이 나온다 |
| 2 | 지운 것을 되돌린다 | **exit 0** — 1번이 그 배선 때문이었음이 증명된다 |
| 3 | 계약에 없는 새 명령을 배선에 추가한다 | **warning**이지 실패가 아니다(새 명령 추가는 정상) |
| 4 | 등재한 검사를 `GATE-MANIFEST`에서 임시로 빼고 게이트 실행 | 검사 수가 줄어드는 것이 **보인다**(조용히 넘어가지 않는다) |

**시험 1·2의 실제 exit code와 출력을 verification 문서에 붙여라.**

## 착륙

이 지시서의 allowlist는 코덱스 영역 밖이므로 `CodexHarnessLauncher`로 쏘지 않는다.
조율자가 직접 수행하며, **직접 경로 사유를 보고에 남긴다**(`CLAUDE.md` 관례).

`GATE-MANIFEST.json`을 `DLINT-01`이 allowlist로 갖고 있다 — 착륙 순서를 먼저 확인하라
(`crossDirectivePinCollisions`).

## 허용 파일 (allowlist)

- docs/handoff/CLI-CONTRACT.json
- docs/handoff/GATE-MANIFEST.json
- docs/handoff/HARNESSES.md
- docs/verification/cg04b-contract-and-registration.md
