```context-pack
{
  "diId": "DICC-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-DICC-01-no-self-rebuild.md",
    "docs/handoff/decisions/ADR-016-gate-runner-authority.md",
    "server/Harness/DiCompletionCheckCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# DICC-01 — `di-completion-check`가 자기 자신을 다시 빌드하지 않게 한다

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 선행: `HREG-02`(단일 출처). 근거: `ADR-016` §10(설계) · §11(이 결함의 실측).

---

## 0. 문제 (실측 2026-07-26)

`di-completion-check`는 검사마다 자식을 이렇게 띄운다.

```
DiCompletionCheckCli.cs:160-164
  run --project server --   ← "--no-build"가 없다
```

**이 하네스 자신이 곧 `server`의 exe다.** 그래서 자식의 빌드가 부모가 잡은 파일을 덮으려다 막힌다.

```
error MSB3021: ... LocalFirstWorkflowDashboard.Server.exe ...
  because it is being used by another process
```

세 게이트 모두 FAIL이 나왔고 사유는 `exit-mismatch`로 적히지만 **실제로는 검사가 빌드 단계에서
죽은 것**이다. 개별 실행에서 exit 0인 `gate-clean`·`handoff-integrity`·`context-pack-integrity`·
`doc-integrity`가 여기서는 1로 잡혔다.

빌드가 이미 최신이면 통과하므로 **간헐적으로만 실패한다** — 게이트 러너로서 최악의 성질이다.
**`ADR-016` §8이 게이트 권위를 준 러너가 이 상태다.**

`program-verify`는 같은 결함을 `ADR-016` §10에서 고쳤다. **같은 설계를 적용한다.**

## 1. 무엇을 하는가

1. 자식 실행에 **`--no-build`**를 붙인다.
2. 검사를 돌리기 전에 **돌고 있는 바이너리가 소스보다 낡았는지 잰다.**
   낡았으면 **검사를 하나도 돌리지 않고 exit 2**, 어느 파일이 더 새로운지 함께 낸다.
3. 빌드는 **호출자가 미리** 한다. 이 하네스는 빌드하지 않는다.

낡음 판정 규칙(`program-verify`와 같아야 한다):

- `server/**`의 `*.cs`·`*.csproj` 중 **가장 최근 수정 시각**과 **돌고 있는 바이너리**의 시각을 비교
- `bin/`·`obj/`는 **제외** — 빌드 산출물이라 넣으면 언제나 낡았다고 나온다

## 1-A. 낡음 판정을 **한 곳에만** 정의하라

`server/ProgramVerifierCli.cs`에 같은 규칙이 이미 있다. **그대로 복사하지 마라.**
바로 앞 작업(`HREG-02`)이 없앤 것이 정확히 이런 중복이고, 여기서 다시 만들면 **같은 결함을
다른 이름으로 되살리는 것**이다.

**`server/Harness/BinaryFreshness.cs`(신규)에 정의를 하나 두고** `DiCompletionCheckCli`가 그것을 쓴다.
조율자가 반입 후 `ProgramVerifierCli`를 같은 표면으로 바꾼다(§6).

## 1-B. 빌드로 해결하려 하지 마라

"앞에서 한 번만 빌드한다"는 처방은 **이미 실패했다**(`ADR-016` §10, 커밋 `dbbff7c`).
부모가 그 exe라서 **언제 빌드하든 자기 자신을 덮을 수 없다.** 순서 문제가 아니다.
`dotnet build`를 호출하는 구현은 반려한다.

## 2. 하지 않을 일 (하면 반려)

- `server/ProgramVerifierCli.cs` 수정 — 영역 밖(§6).
- `dotnet build` 호출(§1-B).
- `unknown-command` 판정 경로 변경. 그 판정은 자식을 띄우기 **전에** 나므로 이 결함의 영향을
  받지 않았고, 지금 정확히 동작한다(`HREG-02` §4 시험 1·3에서 실측).
- 실패 사유를 `exit-mismatch`로 뭉뚱그리는 현재 동작을 **더 뭉뚱그리는 것**.
  낡아서 못 쟀으면 `exit-mismatch`가 아니라 **잴 수 없음**이다.
- `GATE-MANIFEST.json` 수정.

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `di-completion-check --gate POST-COMMIT` (깨끗한 트리, 최신 빌드) **exit 0**
- [ ] 낡은 바이너리에서 **exit 2**, 검사 실행 **0개**
- [ ] `build-verify` **exit 0**
- [ ] 낡음 판정 정의가 `server/**`에 **하나만** 존재

### 목적 기준 (사람 판정)

**"두 러너가 같은 게이트에 같은 답을 낸다."** 이 대조는 지금까지 한 번도 못 했다 —
한쪽이 늘 빌드 실패로 죽었기 때문이다.

## 4. 반증 시험 (전부 실측. **코드 검토로 갈음하지 마라**)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | 깨끗한 트리·최신 빌드에서 `--gate POST-COMMIT` | **exit 0**, 12개 전부 일치 |
| 2 | `server/Engine.cs`의 mtime을 바이너리보다 새롭게 | **exit 2**, 검사 **0개**, 어느 파일인지 출력 |
| 3 | 다시 빌드 | 시험 1로 복귀 |
| 4 | 모르는 명령 매니페스트 (`docs/qa/gate-witness/hreg-02-unknown-command.json`) | `unknown-command` — **변화 없음** |
| 5 | `measure` 매니페스트 (`hreg-02-measure-command.json`) | **PASS** — 변화 없음 |
| 6 | **같은 게이트를 `program-verify verify`로도 돌려 대조** | **판정 동일** |

**시험 2가 이 수정의 핵심이다.** `--no-build`를 쓰면서도 낡은 바이너리를 재지 않는다는 것을
보이는 자리이며, **검사를 하나도 돌리지 않는다**는 것까지 확인해라(모르는 것을 판정으로 적지 않는다).

**시험 6이 목적 기준이다.** 두 러너의 verdict와 실패 목록을 나란히 붙여라.
`program-verify`는 `--manifest`를 해석하지 않으므로 **실제 게이트 id로만 대조 가능하다**
(2026-07-26에 픽스처로 대조를 시도해 무효한 결과를 얻었다 — `ADR-016` §11 곁가지).

**시험 2를 하기 전에 `mtime`을 미래로 찍지 마라.** 다시 빌드해도 계속 거부되어 시험 3이 실패한다
(2026-07-26에 실제로 그렇게 실패했다). **현재 시각으로 touch해라.**

## 5. 착륙

`context-pack-integrity`를 돌려 stale해진 pin이 있는지 확인하라.
**소유 지시서를 가정하지 마라 — stale로 지목된 경로를 grep해서 실제 소유자를 찾고,
그 경로가 `requiredInputs`인지 `readOrder`인지까지 보아라.** `readOrder`에는 sha가 없어
stale해지지 않는다. 2026-07-26에 이 구분을 안 해서 한 번 틀렸다.
같은 날 `HarnessRegistry.cs`의 pin 소유자는 `GATE-TRUTH-01`이었다 — 이름으로 추측할 수 없다.

## 6. 후속 (이 지시서가 하지 않는다)

조율자가 `server/ProgramVerifierCli.cs`의 낡음 판정을 지우고 `BinaryFreshness`를 쓰게 바꾼 뒤,
`ADR-016` §8의 권위 결정을 다시 볼 수 있는 근거(시험 6 결과)를 정리한다.

## 허용 파일 (allowlist)

- server/Harness/DiCompletionCheckCli.cs
- server/Harness/BinaryFreshness.cs
- docs/qa/gate-witness/**

> 검증 문서는 영역 밖이라 조율자가 쓴다.
