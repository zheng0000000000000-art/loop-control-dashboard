```context-pack
{
  "diId": "DAUTH-02",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/archive/2026-07/directives/directive-DAUTH-02-landing-procedure.md",
    "docs/archive/2026-07/directives/directive-CPX-01-overlap-fails.md",
    "skills/common/directive-authoring.md"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `skill`** → 절차이므로 exit code로 판정되지 않는다. 완료 판정은 §3의 문면 검사로 한다.

---

# DAUTH-02 — 지시서 **착륙 절차**를 스킬에 적는다 (pin 갱신은 실행자의 일이 아니다)

- actor: **HARNESS_EXECUTOR (codex)** — `skills/`는 `ADR-002` 코덱스 배타 영역이다.
- 선행: `CPX-01`(탐지 하네스). **이 지시서와 같은 세션에서 하지 마라** — 아래 §5.
- 발견: 조율 세션 실측, 2026-07-26.

---

## 0. 문제 (실측)

`CPX-01`을 실제로 발사해 산출물을 받았는데 **완료 판정이 나지 않았다.** 코드는 정확했다.

`DLINT-01`이 `CPX-01`의 유일한 allowlist 파일 `server/Harness/ContextPackIntegrityCli.cs`를
`requiredInputs`로 sha 고정한다. `CPX-01`을 수행하는 순간 그 pin이 stale이 되어
`context-pack-integrity`가 **exit 1**이다.

**양쪽 다 규칙을 어기지 않았다.** `DLINT-01`은 그 파일을 읽기 참조로만 pin했고 allowlist에
넣지 않았다 — `_header.md`가 요구하는 그대로다. `CPX-01`도 자기 allowlist 밖을 pin하지 않았다.
**규칙을 지켰는데 함께 두면 교착이다.**

대조 실험으로 확인했다: **원본 코드에 개행 하나만 추가해도 같은 실패가 난다.** 후보의 결함이 아니다.

그리고 `CPX-01`이 만든 탐지 하네스를 붙여 보니 이 교착이 **3건**이었다 —
`CODEX-GATE-04`·`CPX-01`·`GATE-CP-01`이 전부 같은 파일을 쓰고 `DLINT-01`이 그것을 pin한다.
**사람이 손으로 찾았을 때는 1건만 보였다.**

## 1. 무엇을 하는가

`skills/common/directive-authoring.md`에 **착륙 절차** 절을 추가한다.

기존 §0~§6과 완료 체크리스트는 **건드리지 마라.** `pin`·`착륙`·`stale`은 현재 이 스킬에
한 번도 나오지 않는다(실측). **새 절이며 기존 내용의 재작성이 아니다.**

새 절에 들어가야 하는 것:

1. **발사 전**: `context-pack-integrity`를 돌려 `crossDirectivePinCollisions`에서 **내 allowlist
   파일을 pin한 지시서 목록**을 확인한다. 착륙이 몇 걸음인지는 발사 전에 알 수 있다.
2. **착륙은 두 걸음이다**: ①실행자가 코드를 고친다 ②**반입하는 사람**이 그 파일을 pin한 모든
   지시서의 해시를 새 값으로 갱신한다.
3. **②는 실행자의 일이 아니다.** `docs/handoff/queue/**`는 코덱스 영역 밖이다. 실행자에게
   시키면 범위 위반이고, 실행자가 거절하는 것이 정상이다.
4. **①만 끝난 상태에서 게이트가 빨간 것을 반려 사유로 삼지 마라.** 그것은 절차가 안 끝난
   것이지 산출물이 틀린 것이 아니다. 구분하는 법: **원본 코드에 개행 하나를 넣어 같은 실패가
   나면 산출물 탓이 아니다.**
5. **pin을 지워서 해결하지 마라.** 읽어야 할 참조를 없애는 것은 해결이 아니다. 갱신이 답이다.
6. **해시는 프로그램이 계산한다** — 기존 §4를 참조하라. 손으로 옮겨 적지 마라.
7. **같은 파일을 여러 지시서가 쓰면 착륙 순서를 먼저 정한다.** 하나 착륙 → pin 갱신 → 다음.
   순서를 안 정하면 나중에 착륙하는 쪽이 매번 남의 pin을 깨뜨린다.

## 2. 하지 않을 일 (하면 반려)

- 기존 절의 재작성·재배치. **추가만 한다.**
- 탐지 로직을 스킬에 산문으로 옮겨 적는 것 — 그건 `CPX-01`의 하네스가 한다. **스킬은 절차만.**
- `skills/` 밖 파일 수정.
- 이 절차를 자동화하겠다며 코드를 만드는 것. **이 지시서는 문서만 다룬다.**

## 3. 완료 조건

### 문면 기준 (기계로 셀 수 있는 것만)

- [ ] `skills/common/directive-authoring.md`에 §1의 7개 항목이 **전부** 있다
- [ ] `crossDirectivePinCollisions`라는 실제 출력 키 이름이 언급된다 — 사람이 무엇을 볼지 알아야 한다
- [ ] "①실행자 ②반입하는 사람"의 **주체 구분**이 문면에 있다
- [ ] 기존 §0~§6의 제목이 그대로 남아 있다(추가만 했음을 확인)
- [ ] `doc-integrity` **exit 0**

### 목적 기준 (사람 판정)

**"다음 사람이 같은 자리에서 안 넘어진다."**

지표만 만족시키는 우회로: 7개 항목을 목록으로 나열만 하고 **왜 그런지**를 빼는 것.
`pin을 지우지 마라`만 적고 **`읽어야 할 참조를 없애는 것은 해결이 아니다`**라는 이유를 빼면,
다음 사람은 급할 때 지운다. **이유 없는 규칙은 지켜지지 않는다.**

## 4. 반증 시험 — 절차는 exit code로 못 재므로 **문면 대조**로 한다

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | §1의 7개 항목을 문서에서 찾는다 | 7개 전부 발견 |
| 2 | 기존 §0~§6 제목을 대조한다 | 전부 그대로 |
| 3 | `doc-integrity` 실행 | **exit 0** |

**시험 1·2의 대조 결과를 실행 보고에 붙여라.** "추가했다"는 자기보고는 증거가 아니다.

## 5. 왜 `CPX-01`과 같은 세션에서 하지 않는가

`CPX-01`은 이 절차가 필요한 **이유를 만드는 하네스**다. 같은 실행자가 같은 세션에서 하네스와
그 하네스를 설명하는 절차를 쓰면, **자기가 만든 것에 맞춰 설명을 쓰게 된다** — `ADR-002`가
경계하는 자기 검증이다. 순서는 `CPX-01` 착륙 후 이 지시서다.

## 허용 파일 (allowlist)

- skills/common/directive-authoring.md

> **⚠ 이 지시서는 현재 `CodexHarnessLauncher`로 쏠 수 없다.**
> 런처의 쓰기 허용 범위는 계약 §2-2가 `server/Harness/` + 승인된 fixture 경로로 제한한다
> (`PermittedWriteRoots`). `skills/`는 `ADR-002`상 코덱스 배타 영역이지만 **런처 계약의 범위 밖**이다.
> 계약이 런처를 "하네스·fixture 제작 통로"로 좁혀 놓았기 때문이며(§0), 그것을 조용히 넓히지 않았다.
> **범위를 넓힐지는 사람 결재다.** 그 전까지 이 지시서는 수동 dispatch로만 수행한다.
