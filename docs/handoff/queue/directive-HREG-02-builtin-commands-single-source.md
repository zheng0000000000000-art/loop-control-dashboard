```context-pack
{
  "diId": "HREG-02",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-HREG-02-builtin-commands-single-source.md",
    "docs/handoff/decisions/ADR-016-gate-runner-authority.md",
    "server/Harness/HarnessRegistry.cs",
    "server/Harness/DiCompletionCheckCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# HREG-02 — 게이트가 아는 명령 목록을 한 곳에 둔다

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 관련: `ADR-016` §7(중복 지적) · §8(권위) · §10(두 러너 모두 동작).

---

## 0. 문제 (실측 2026-07-26)

게이트가 아는 명령 목록이 **두 벌** 있다.

```
server/Harness/DiCompletionCheckCli.cs:18   private static readonly HashSet<string> BuiltInCommands
server/ProgramVerifierCli.cs                같은 목록의 사본 (주석에 "복제했다"고 적혀 있다)
```

두 러너는 이 목록으로 *"이 명령을 게이트가 아는가"*를 판정하고, 모르면 fail-closed한다.
**목록이 갈리면 같은 매니페스트에 두 러너가 다른 답을 낸다.** `ADR-016` §6이 바로 그 사건이었다 —
한쪽은 `unknown command`로 3건을 거부했고 다른 쪽은 그대로 실행해 PASS를 냈으며,
그 PASS가 `TRUSTED_BASELINE` 선언의 근거로 쓰였다.

**지금은 두 벌이 같아서 안 갈린다. 그건 보장이 아니라 우연이다.**

## 1. 무엇을 하는가

목록의 **정의를 `HarnessRegistry`에 하나만 둔다.**

- `HarnessRegistry`가 *"게이트가 아는 명령인가"*를 답하는 표면을 제공한다
  (`RegisteredNames`가 이미 그 자리에 있다. 이름·형태는 실행자가 정한다).
- `DiCompletionCheckCli`는 **자기 사본을 지우고** 그 표면을 쓴다.
- 판정 결과는 **바뀌지 않아야 한다**(§4 시험 4).

`server/ProgramVerifierCli.cs`는 **영역 밖**이다. 조율자가 같은 표면을 쓰도록 별도로 고친다(§6).

## 1-A. 내장 명령을 `Handlers`에 넣지 마라

`measure`·`verify-behavior`는 **게이트가 아는** 명령이지 **레지스트리가 실행하는** 하네스가 아니다
(`CliRouter` 명령이다). `Handlers` 사전에 끼워 넣어 목록을 합치는 방식은 **금지**다.
`HarnessRegistry.TryRun`의 동작이 바뀌고, 그걸 쓰는 `gate-witness-check`의
`CountInternalNegativeCases`가 조용히 다른 답을 내기 시작한다.

**"안다"와 "실행할 수 있다"는 다른 질문이다. 둘을 한 사전으로 합치지 마라.**

## 2. 하지 않을 일 (하면 반려)

- `server/ProgramVerifierCli.cs` 수정 — 영역 밖(§6).
- `HarnessRegistry.Handlers`에 `measure`·`verify-behavior` 추가(§1-A).
- 목록 자체를 늘리거나 줄이는 것. **이번 작업은 위치만 바꾼다.**
- `GATE-MANIFEST.json` 수정.

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `server/Harness/` 안에 그 목록의 **정의가 하나만** 남는다
- [ ] `di-completion-check --gate POST-COMMIT` 판정이 **수정 전과 같다**
- [ ] `build-verify` **exit 0**
- [ ] §4의 5개 시험이 전부 기대값

### 목적 기준 (사람 판정)

**"목록을 한 곳에서 고치면 두 러너가 같이 따라온다."**

지표만 만족시키는 우회로: `DiCompletionCheckCli`가 사본을 지우는 대신 **같은 값을 다시 적는 것**
(상수 두 개를 문자열로 나열). 정의가 하나로 보이지만 갈릴 수 있다. §4 시험 3이 이걸 잡는다.

## 4. 반증 시험 (전부 실측. **코드 검토로 갈음하지 마라**)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | 모르는 명령이 든 매니페스트로 `di-completion-check` | **fail-closed**, 사유가 `unknown command` |
| 2 | `measure`가 든 매니페스트 | **안다고 판정** — 내장이 여전히 인식된다 |
| 3 | **단일 출처에서 내장 하나를 임시로 빼고 `di-completion-check`** | 그 명령이 **모르는 명령**이 된다 |
| 4 | 실제 세 게이트 판정을 수정 전후 대조 | **동일** |
| 5 | `HarnessRegistry.TryRun("measure", …)` | **여전히 미처리**(§1-A) — 레지스트리가 실행하지 않는다 |

**시험 3이 이 지시서의 목적 자체다.** 한 곳을 고쳤을 때 소비자가 따라오는지를 보는 자리다.
빼고 돌린 뒤 **반드시 되돌려라.** 되돌린 상태로 §4 시험 4를 다시 확인해라.

**시험 5가 §1-A를 지키는지 보는 자리다.** 합치는 우회로를 썼다면 여기서 드러난다.

## 5. 착륙

`context-pack-integrity`를 돌려 stale해진 pin이 있는지 확인하라.
**소유 지시서를 가정하지 마라 — stale로 지목된 경로를 grep해서 실제 소유자를 찾아라.**
2026-07-26에 파일명만 grep하고 `requiredInputs`인지 `readOrder`인지 확인하지 않아 한 번 틀렸다.
**행이 잡혔다는 사실은 pin이라는 증거가 아니다.**

## 6. 후속 (이 지시서가 하지 않는다)

조율자가 `server/ProgramVerifierCli.cs`의 사본을 지우고 같은 표면을 쓰게 바꾼 뒤,
두 러너가 같은 매니페스트에 같은 답을 내는지 대조한다.
**이 지시서가 반입되기 전에는 그쪽을 고칠 수 없다** — 참조할 표면이 아직 없다.

## 허용 파일 (allowlist)

- server/Harness/HarnessRegistry.cs
- server/Harness/DiCompletionCheckCli.cs
- docs/qa/gate-witness/**

> 검증 문서는 영역 밖이라 조율자가 쓴다.
