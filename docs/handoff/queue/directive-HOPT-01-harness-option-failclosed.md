```context-pack
{
  "diId": "HOPT-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-HOPT-01-harness-option-failclosed.md",
    "docs/verification/cli-option-failopen-survey.md",
    "server/CliOptions.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# HOPT-01 — 하네스 CLI들도 오타 옵션을 거부한다

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 선행: `DICC-04`(같은 조치를 `di-completion-check`에) · 조율자가 `server/` 루트 CLI 6종에 이미 적용.

---

## 0. 문제 (실측 2026-07-26)

**픽스처 플래그가 전부 같은 부류다.** 오타를 조용히 무시하면 **픽스처를 쟀다고 믿는데 실제를 잰다.**

```
di-completion-check --manifestt <픽스처>  →  exit 0 · production을 재고 PASS   (DICC-04로 해소)
measure --fixturee <픽스처>               →  실제 dashboard/data를 쟀다        (조율자가 해소)
```

**남은 하네스들도 같은 모양이다.** 반증 시험은 픽스처를 겨냥하는데, 한 글자가 틀리면
**실제를 재고 그 결과를 픽스처 결과로 읽는다.**

## 0-A. 그리고 아무 일도 하지 않는 플래그가 하나 있다

```
handoff-integrity --projection   →   exit 0 (정상 검사만 수행)
HandoffIntegrityCli에 projection 코드는 없다. 진짜 명령은 CliRouter의 `projection`이다.
```

CLAUDE.md는 *"파일을 다 쓴 뒤 마지막에 `projection` 실행"* 을 요구한다.
**`--projection`을 붙여도 통과하므로 안 한 것을 한 줄 안다.** 조율자가 오늘 여러 번 그렇게 돌렸다.

## 1. 무엇을 하는가

`server/CliOptions.cs`의 **`CliOptions.Validate`를 그대로 쓴다**(신규 작성 금지 — 오늘 같은 종류의
중복을 다섯 번 없앴다). 각 하네스 진입점에서 **모르는 옵션·값 없는 옵션을 exit 2로 거부**한다.

| 하네스 | 값 옵션 | 스위치 |
| --- | --- | --- |
| `handoff-integrity` | `workstate` · `applier-log` · `pending-transition` | `self-test` |
| `gate-clean` | `status-fixture` | `normalized-hash-self-test` |
| `doc-integrity` | `fixture` | — |
| `build-verify` | `fixture` | — |
| `scope-check` | `actor` · `claims` | — |

**표를 그대로 믿지 말고 코드에서 확인하라.** 내가 grep으로 뽑은 것이고 빠진 것이 있을 수 있다.
확인 결과가 다르면 **보고에 적고 코드를 따르라.**

## 1-A. `--projection`은 받지 마라

`handoff-integrity --projection`은 **`unknown-option`으로 거부**하고, 메시지에
*"projection은 별도 명령이다"* 를 넣어라. 조용히 통과하는 지금이 가장 나쁘다.

## 2. 하지 않을 일 (하면 반려)

- **판정 논리 변경.** 각 하네스의 exit code 규칙·검사 내용은 그대로다. **인자 검증만 더한다.**
- `CliOptions.Validate`의 사본을 만드는 것. **한 곳만 쓴다.**
- `server/` 루트 파일 수정 — 영역 밖(이미 처리됨).
- 위 표를 근거로 **코드에 없는 옵션을 새로 만드는 것.**

## 3. 완료 조건

- [ ] 다섯 하네스가 모르는 옵션·값 없는 옵션에 **exit 2**
- [ ] `handoff-integrity --projection` **exit 2**
- [ ] 기존 게이트 회귀 없음: `di-completion-check --gate POST-COMMIT` **0**,
      `--gate WP-STATE-INTEGRITY-LAND` **0**
- [ ] `measure dev-pack` **violations 0** ★ (`DICC-04`에서 이걸 빠뜨려 사고가 났다)
- [ ] `build-verify` **exit 0**

## 4. 반증 시험 (전부 실측)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | 각 하네스에 오타 옵션 1개씩(예: `--fixturee`·`--workstatee`·`--status-fixturee`) | **exit 2 · `unknown-option`** |
| 2 | 값 필요한 옵션을 값 없이 마지막에 | **exit 2 · `missing-option-value`** |
| **3** | **각 하네스의 정상 픽스처 경로** | **이전과 같은 exit·같은 결과** |
| 4 | `handoff-integrity --projection` | **exit 2** |
| 5 | 스위치(`--self-test`·`--normalized-hash-self-test`) | **exit 0 유지** |
| 6 | `GATE-MANIFEST`의 두 게이트 | **exit 0** |
| 7 | `measure dev-pack` | **violations 0** |

**시험 3이 없으면 이 작업은 "막았다"와 "부쉈다"를 구분하지 못한다.**
`DICC-04`에서 시험 8이 그 역할을 했다 — 정상 `--manifest`가 여전히 픽스처를 재는지 봤다.
**여기서도 픽스처가 여전히 픽스처로 동작해야 한다.**

**시험 7을 빼지 마라.** `DICC-04`는 완료 기준에 `measure`가 없어서, 반입 뒤에야
함수 길이 위반이 드러났고 게이트가 빨개졌다. **같은 실수를 반복하지 않기 위해 넣는다.**

## 5. 착륙

`context-pack-integrity`를 돌려 stale해진 pin이 있는지 확인하라.
**소유 지시서를 가정하지 말고 stale 경로를 grep해서 찾아라.**

## 허용 파일 (allowlist)

- server/Harness/**
- docs/qa/gate-witness/**

> 검증 문서는 영역 밖이라 조율자가 쓴다.
