```context-pack
{
  "diId": "DICC-04-R1",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-DICC-04-R1-split-parseargs.md",
    "docs/handoff/queue/directive-DICC-04-unknown-option-failclosed.md",
    "server/Harness/DiCompletionCheckCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# DICC-04-R1 — `DICC-04`가 넘긴 함수 길이를 되돌린다

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.

---

## 0. 문제 (실측 2026-07-26)

`DICC-04` 반입 후:

```
measure dev-pack → violationCount 1
  maxFunctionLength = 82 · band [0, 80]
  근거: server/Harness/DiCompletionCheckCli.cs:19-100
```

**`DICC-04`의 완료 기준에 `measure`를 넣지 않은 것이 원인이다** — 지시서를 쓴 조율자의 누락이다.
실행자는 적힌 것을 지켰다.

## 1. 무엇을 하는가

`DiCompletionCheckCli.cs:19-100`의 함수를 **band 안(80줄 이하)으로 되돌린다.**
인자 검증 로직을 별도 함수로 빼는 것이 자연스럽다.

## 2. 하지 않을 일 (하면 반려)

- **동작 변경.** `DICC-04`가 만든 판정은 그대로여야 한다 — 오타 옵션 exit 2,
  값 없는 옵션 exit 2, `--emit-doc` 선택적 값, `--emit-cli-contract` 스위치.
- 주석을 지워 줄 수를 줄이는 것. **한국어 기능 주석은 규칙이다**(CLAUDE.md).
  줄 수는 **구조를 나눠서** 줄인다.
- 다른 파일 수정.

## 3. 완료 조건

- [ ] `measure dev-pack` **violations 0**
- [ ] `build-verify` **exit 0**
- [ ] §4의 6개 시험이 `DICC-04` 반입 직후와 **동일한 값**

## 4. 반증 시험 (전부 실측)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | `--gate POST-COMMIT --manifestt <픽스처>` | **exit 2 · `unknown-option`** |
| 2 | `--gate POST-COMMIT --manifest` (값 없이 끝) | **exit 2 · `missing-option-value`** |
| 3 | `--gate POST-COMMIT --manifest <픽스처>` | **exit 1 · checkCount 1** |
| 4 | `--emit-doc` (값 없이) / `--emit-doc <경로>` | **exit 0 / exit 0** |
| 5 | `--emit-cli-contract` | **exit 0** |
| 6 | `measure dev-pack` | **violations 0** ★ |

**시험 3을 빼지 마라.** 리팩터링이 `--manifest`를 망가뜨리면 시험 1만으로는 안 보인다.

## 5. 착륙

`context-pack-integrity`를 돌려 stale해진 pin이 있는지 확인하라.
**소유 지시서를 가정하지 말고 stale 경로를 grep해서 찾아라.**

## 허용 파일 (allowlist)

- server/Harness/DiCompletionCheckCli.cs

> 검증 문서는 영역 밖이라 조율자가 쓴다.
