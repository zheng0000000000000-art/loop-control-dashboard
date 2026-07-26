```context-pack
{
  "diId": "DICC-04",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-DICC-04-unknown-option-failclosed.md",
    "docs/verification/cli-option-failopen-survey.md",
    "server/Harness/DiCompletionCheckCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# DICC-04 — 오타 옵션이 production을 재고 PASS를 내는 것을 막는다

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 근거: `docs/verification/cli-option-failopen-survey.md` (2026-07-26 실측)

---

## 0. 문제 (실측 2026-07-26)

```
di-completion-check --gate POST-COMMIT --manifestt <픽스처 매니페스트> --task typo-a
  → exit 0 · manifest = docs/handoff/GATE-MANIFEST.json · checkCount 14 · verdict PASS
```

`--manifest`를 `--manifestt`로 한 글자 틀리자 **그 옵션이 통째로 무시되고 production 매니페스트를
쟀다.** 픽스처는 검사 1개에 exit 1이어야 했는데 **14개 검사에 PASS, exit 0**이 나왔다.

**픽스처를 쟀다고 믿는데 production을 쟀고, 성공으로 끝난다.** fail-open이다.

`ParseArgs`가 `else if` 사슬이라 **어디에도 맞지 않는 인자는 조용히 지나간다**
(`DiCompletionCheckCli.cs:341-361`). 미지 옵션을 거부하는 CLI는 저장소에서
`StateApplierCli` 하나뿐이며, 그쪽도 **오늘 사고가 나고서야** 고쳤다.

**이것이 게이트 정본 러너에서 일어난다는 점이 우선순위를 정한다**(`ADR-016` §15).
반증 시험이 픽스처를 겨냥했는데 production을 재고 통과하면, **그 시험은 아무것도 증명하지 않는다.**

## 1. 무엇을 하는가

`di-completion-check`가 **모르는 옵션과 값 없는 옵션을 거부한다. exit 2.**

- 모르는 옵션 → `unknown-option: --x`
- 값이 필요한데 없는 옵션 → `missing-option-value: --x`
- 둘 다 **판정을 하지 않고 즉시 종료**한다. 게이트 결과를 내지 마라 — 못 잰 것이다.

## 1-A. 옵션마다 값의 개수가 다르다. 뭉뚱그리지 마라

| 옵션 | 값 |
| --- | --- |
| `--gate` · `--task` · `--launch` · `--manifest` | **필수 1개** |
| `--emit-doc` | **선택** — 다음 인자가 `--`로 시작하지 않으면 값, 아니면 기본 경로 |
| `--emit-cli-contract` | **없음**(스위치) |

*"모든 `--`는 값이 있어야 한다"* 로 만들면 `--emit-doc`와 `--emit-cli-contract`가 깨진다.
§4 시험 3·4·5가 그것을 잡는다.

## 1-B. 내부 이름을 CLI 옵션으로 받지 마라

`StateApplierCli`에서 **내부 키 이름(`dry-run-flag`)이 CLI 옵션으로 받아들여져**
안전장치를 닮은 이름이 통과했다. 여기서도 **문서화된 옵션 이름만** 받아라.

## 2. 하지 않을 일 (하면 반려)

- **판정 논리 변경.** `gateVerdict`·`failures`·검사 실행·exit code 규칙은 그대로다.
  **이번 작업은 인자 검증만 더한다.**
- `--emit-doc`의 선택적 값 동작 변경(§1-A).
- 모르는 옵션을 **경고로만** 처리하는 것. **판정을 내면 안 된다** — 그게 지금 문제다.
- `GATE-MANIFEST.json`·다른 CLI 수정 — 영역 밖이거나 별건이다.
- 기존 `docs/qa/gate-witness/` 파일 수정. 새 파일만 추가하라.

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `di-completion-check --gate POST-COMMIT` **exit 0** — 회귀 없음
- [ ] `di-completion-check --gate WP-STATE-INTEGRITY-LAND` **exit 0** — 회귀 없음
- [ ] §4의 8개 시험이 전부 기대값
- [ ] `build-verify` **exit 0**

### 목적 기준 (사람 판정)

**"한 글자 틀린 옵션으로는 아무 판정도 나오지 않는다."**

## 4. 반증 시험 (전부 실측. **코드 검토로 갈음하지 마라**)

| # | 시험 | 기대 |
| --- | --- | --- |
| **1** | **`--gate POST-COMMIT --manifestt <픽스처>`** | **exit 2 · `unknown-option`** — 게이트 결과를 내지 않는다 |
| 2 | `--gate POST-COMMIT --manifest` (값 없이 끝) | **exit 2 · `missing-option-value`** |
| 3 | `--emit-doc` (값 없이) | **동작 유지** — 기본 경로 |
| 4 | `--emit-doc <경로>` | **동작 유지** |
| 5 | `--emit-cli-contract` | **동작 유지** |
| 6 | `--gate X --task Y --launch Z --manifest W` 정상 조합 | **동작 유지** |
| 7 | `--gate POST-COMMIT` · `--gate WP-STATE-INTEGRITY-LAND` | **exit 0** (회귀) |
| 8 | 픽스처 매니페스트 정상 지정(`--manifest docs/qa/fixtures/reconciliation/A/GATE-MANIFEST.json`) | **exit 1 · checkCount 1** |

**시험 1이 이 지시서의 전부다.** 지금은 **exit 0 · checkCount 14 · PASS**가 나온다.
**고치기 전 그 값을 먼저 재서 보고에 적어라** — 무엇이 바뀌었는지는 그 대조로만 보인다.

**시험 8을 빼지 마라.** 시험 1이 exit 2가 되는 것만 보면 *"`--manifest`가 아예 안 먹는"*
구현과 구분되지 않는다. **정상 지정이 여전히 픽스처를 재야 한다**(checkCount 1).

**시험 3·4·5가 §1-A를 지킨다.** 값 규칙을 뭉뚱그렸다면 여기서 깨진다.

## 5. 착륙

`context-pack-integrity`를 돌려 stale해진 pin이 있는지 확인하라.
**소유 지시서를 가정하지 마라 — stale로 지목된 경로를 grep해서 실제 소유자를 찾고,
그 경로가 `requiredInputs`인지 `readOrder`인지까지 보아라.**

## 6. 이 지시서가 다루지 않는 것

같은 fail-open이 `trust-origin`(`--gate-report` 오타 → 증거가 `NOT_RUN`으로 격하)과
`codex-launch`(`--manual` 오타 — **측정하지 못했다**)에도 있다. **둘 다 `server/` 루트라
조율자 영역**이고 별도로 처리한다. 여기 섞으면 반증 시험이 흐려진다.

## 허용 파일 (allowlist)

- server/Harness/DiCompletionCheckCli.cs
- docs/qa/gate-witness/**

> 검증 문서는 영역 밖이라 조율자가 쓴다.
