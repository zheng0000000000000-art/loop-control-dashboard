```context-pack
{
  "diId": "GWIT-03",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-GWIT-03-function-length.md",
    "docs/verification/gwit02-fixture-modes.md",
    "server/Harness/GateCleanCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 동작 보존 확인 필수.

---

# GWIT-03 — `GateCleanCli.Run`이 길이 한도를 넘는다 (`GWIT-02` 반입이 만든 회귀)

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 출처: `GWIT-02` 반입(`fb086f3`) 직후 측정에서 드러났다.

---

## 0. 문제 (실측 2026-07-26)

```
maxFunctionLength = 85     server/Harness/GateCleanCli.cs:19-103
blueprint 한도    = [0, 80]
measure dev-pack  → violationCount: 1
```

`GWIT-02`가 `--status-fixture` 분기를 기존 `Run` 본문 **위에 얹으면서** 85줄이 됐다.

**이 위반은 아직 저장소에 남아 있다.** 조율자는 `server/Harness/`를 고칠 수 없어
`9159d3f`에 사실만 기록했다. (같은 커밋에서 조율자가 측정값을 확인하지 않고 커밋 메시지에
`violations: 0`이라고 적은 것도 함께 정정했다 — 그 경위는 `docs/verification/gwit02-fixture-modes.md`
말미에 있다.)

## 1. 무엇을 하는가

`GateCleanCli.Run`을 **한도 안으로** 줄인다. 방법은 실행자가 정하되 아래를 지킨다.

- **판정 로직을 바꾸지 마라.** 이 지시서는 길이만 다룬다.
- **분해는 실제 이음매를 따라라.** 지금 `Run`은 최소 세 가지를 한다:
  ①인자 해석(`--status-fixture` 유무) ②상태 목록 확보(픽스처 파일 또는 `git status`)
  ③그 목록으로 판정·출력. **셋 중 어디를 잘라도 되지만, 줄 수를 맞추려고 의미 없는 자리를
  자르지 마라.**
- 새 함수에도 **한국어 기능 주석 1줄**을 붙여라(`measure`의 `functionsWithoutComment`가 센다).

## 2. 하지 않을 일 (하면 반려)

- 판정 결과가 달라지는 변경. `--status-fixture` 유무 양쪽 모두 **지금과 같은 exit**를 내야 한다.
- 다른 파일 수정. 이 지시서의 allowlist는 `GateCleanCli.cs` 하나다.
- **blueprint의 `maxFunctionLength` 한도를 고치는 것.** 그건 기준 변경이고 사람 결재다
  (`CLAUDE.md` 금지사항 1번). **판정이 불편하다고 기준을 옮기지 마라.**
- 줄을 이어 붙여 물리적 줄 수만 줄이는 것. 한 줄에 여러 문장을 넣는 식이면 목적 미달이다.

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `build-verify` **exit 0**
- [ ] `measure dev-pack` **violationCount 0** — `maxFunctionLength`가 80 이하
- [ ] `functionsWithoutComment` **0** (새 함수에 주석이 있다)
- [ ] `verify-behavior` **exit 0** (동작 보존)

### 목적 기준 (사람 판정)

**"분해가 읽기 쉬워졌다."**

지표만 만족시키는 우회로: 임의의 지점에서 잘라 `RunPart2` 같은 이름의 함수를 만드는 것.
숫자는 내려가지만 **다음 사람이 더 헤맨다.** 이름이 그 함수가 하는 일을 말해야 한다.

## 4. 반증 시험 (전부 실측)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | `gate-clean --status-fixture docs/qa/gate-witness/gate-clean-dirty.status` | **exit 1** (변경 전과 같다) |
| 2 | 같은 파일의 **내용을 비우고** 실행 | **exit 0** — 여전히 이름이 아니라 내용을 본다 |
| 3 | `gate-clean --status-fixture <없는 파일>` | **exit 2** (fail-closed 유지) |
| 4 | 깨끗한 트리에서 인자 없이 `gate-clean` | **exit 0** |
| 5 | 더러운 트리에서 인자 없이 `gate-clean` | **exit 1** |

**시험 2가 회귀 감시의 핵심이다.** `GWIT-02`의 목적이 그것이었고, 분해하다 그 성질을 잃으면
숫자만 맞고 witness가 거짓이 된다. **출력 원문을 실행 보고에 붙여라.**

## 5. 왜 이 지시서가 따로 있는가

`GWIT-02` 안에서 함께 처리할 수도 있었다. 그러지 않은 이유:

- `GWIT-02`는 이미 반입됐고 그 verification에 위반이 기록돼 있다. **되돌리지 않고 정정을
  덧붙이는 것**이 이 저장소의 방식이다.
- 길이 위반은 `GWIT-02`의 목적(픽스처 모드)과 다른 문제다. 한 지시서에 섞으면 완료 판정이 흐려진다.

## 착륙 (두 걸음 — `skills/common/directive-authoring.md` §7)

착륙 전 `crossDirectivePinCollisions`를 확인하라. **소유 지시서를 가정하지 말고 stale 경로를
직접 grep해서 찾아라** — 2026-07-26에 가정했다가 한 번 틀렸다.

## 허용 파일 (allowlist)

- server/Harness/GateCleanCli.cs

> 검증 문서는 영역 밖이라 조율자가 쓴다.
