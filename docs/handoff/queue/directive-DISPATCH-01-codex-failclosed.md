```context-pack
{
  "diId": "DISPATCH-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/plan/wp/CODEX-HEADLESS-DISPATCH-CLEANUP-plan.md", "sha256": "a426289a54c4f8889512c53603773c8d4598193e750954f74f2556d4c010f944" },
    { "path": "docs/handoff/decisions/ADR-015-harness-actor-substitution.md", "sha256": "df25d0e69be8debc5b4ea6d0b6ca5292179957b66f2154f902d6b33f4d7da0d3" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/plan/wp/CODEX-HEADLESS-DISPATCH-CLEANUP-plan.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-DISPATCH-01-codex-failclosed.md"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

> **초안(draft). 발사 금지.** 작성: 검수자(claude-opus), 2026-07-13.
> **발사 조건: `TRUSTED_BASELINE` 선언 이후.** 지금 발사하면 `WP-STATE-INTEGRITY` 통합 branch에 조각을 얹는 것이다.
> 그때까지의 방어는 코드가 아니라 규칙이다 — **`executor: "codex"`로 dispatch하지 마라.**

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.

---

# DISPATCH-01 — `executor: "codex"` fail-closed (구현되지 않은 실행 통로가 성공을 보고하지 못하게 한다)

- actor: **미정** (`TRUSTED_BASELINE` 이후 사람이 배정. 05H/06H와 달리 코덱스 소유 영역이 아니다 — `server/OutboxManager.cs`는 dispatch 코어다)
- 근거: `CODEX-HEADLESS-DISPATCH-CLEANUP-plan.md` §0.1 · §2.1
- 관련: `ADR-015`(코덱스 헤드리스 진입점 부재) · `CODEX-HARNESS-LAUNCHER-minimal-contract.md`(이 지시서가 land된 뒤에 launcher가 이 통로를 연다)

---

## 0. 무엇이 문제인가 (실측, 2026-07-13 검수자)

`executor: "codex"`는 "아직 안 채운 빈 슬롯"이 아니다. **성공처럼 보이는 결과를 만든다.**

| # | 실체 | 코드 위치 |
| --- | --- | --- |
| 1 | 결정론 규칙에 맞지 않는 지시는 `EXECUTOR_REPORT.md`를 쓰고 **exit 0** | `server/DispatchExecutorCli.cs:51-53` |
| 2 | dispatch는 `!hasChanges`일 때만 `failed`. 그 보고서가 변경으로 잡혀 `hasChanges=true` → **`import_pending`** | `server/OutboxManager.cs:87-88` |
| 3 | tier-2 자동 승인이 켜져 있으면 그 `import_pending`이 자동 승인 경로로 들어간다 | `server/OutboxManager.cs:102` |
| 4 | `SubscriptionCalls`가 codex를 **1**로 센다 → 하지 않은 구독 호출이 비용 meta에 남는다 | `server/OutboxManager.cs:339-345` |

**호출 가능한 Codex 헤드리스 진입점은 없다**(ADR-015 실측: `where codex` 무결과, App Execution Alias 없음, 전역 npm 없음).
없는 통로가 `import_pending`을 만든다 — 이것은 세션 브리프의 "게이트가 거짓말한 다섯 사례"와 같은 종류다.
**검사가 없는 게 아니라, 대상이 없는데 PASS를 준다.**

## 1. 무엇을 하는가

`CodexHarnessLauncher`가 배선되기 전까지, `executor: "codex"` dispatch를 **명시적 실패로 거절한다.**

```text
POST dispatch { executor: "codex" }
  → 400  dispatch.executor_not_implemented
     "codex executor has no callable headless entrypoint (ADR-015). dispatch is a deterministic stub, not an LLM router."
```

- **거절이지 지원 목록 삭제가 아니다.** `SupportedExecutors`에서 `codex`를 빼면 오류 메시지가
  `dispatch.invalid_executor`("오타를 냈구나")가 되어 **원인을 숨긴다.** 값은 알되 **구현되지 않았다고 말해야 한다.**
- `SubscriptionCalls(codex)`는 **0**으로 고친다. 거절된 dispatch는 outbox 항목·비용 meta를 남기지 않는다.
- **`claude-code`·`ollama` 경로는 건드리지 않는다.** 이 지시서는 codex 값 하나만 다룬다.

## 2. 하지 않을 일 (범위 밖 — 하면 반려)

- `DispatchExecutorCli`를 실제 LLM launcher로 바꾸는 것. **이 지시서는 통로를 여는 게 아니라 닫는다.**
- `CodexHarnessLauncher` 구현. 그건 `TRUSTED_BASELINE` 이후 별도 지시서다(계획서 Phase D).
- Claude API 프록시·Responses 호환 gateway·Codex 앱 우회 실행기.
- tier-2 자동 승인 정책 자체를 손대는 것. (이 지시서는 codex가 그 경로에 **도달하지 못하게** 할 뿐이다.)
- `OutboxManager`의 다른 실행자 정책 확장.

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `build-verify` **exit 0**
- [ ] `verify-behavior` → `behaviorEqual: true`
- [ ] `dotnet run --project server -c Release -- measure dev-pack` **위반 비악화** (`-c Release` 필수 — 없으면 Debug 바이너리를 잰다)
- [ ] `scope-check` — 변경 파일이 아래 allowlist 안
- [ ] **반증 시험 4종이 전부 실패를 실패로 보고한다** (§4)

### 목적 기준 (사람 판정)

**"구현되지 않은 실행 통로가 성공을 보고하지 않는다."**
지표를 만족시키는 우회로가 있다 — 예: 오류만 바꾸고 `import_pending` 생성 경로를 그대로 두는 것,
또는 `SupportedExecutors`에서 값을 지워 원인을 `invalid_executor`로 뭉개는 것. **둘 다 목적 미달이다.**

## 4. 반증 시험 (negative test) — 없으면 반려

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | `executor: "codex"`로 dispatch | **400 `dispatch.executor_not_implemented`**. outbox 항목이 **생기지 않는다** |
| 2 | 시험 1 직후 `outbox/` 조회 | `import_pending` 항목 **0건**. `EXECUTOR_REPORT.md` **미생성** |
| 3 | `executor: "codex"` + tier-2 자동 승인 **켠** 상태 | 자동 승인 경로에 **도달하지 않는다**(승인 0건) |
| 4 | `executor: "claude-code"` / `"ollama"` dispatch | **회귀 없음** — 기존과 동일하게 동작한다 |

**시험 1·2는 반드시 실행 결과(exit code·응답 본문·outbox 디렉터리 목록)를 verification 문서에 붙인다.**
"거절하도록 고쳤다"는 자기보고는 증거가 아니다.

## 허용 파일 (allowlist)

- server/OutboxManager.cs
- docs/verification/dispatch01-codex-failclosed.md
- docs/handoff/WORKSTATE.json

> `server/DispatchExecutorCli.cs`는 **allowlist 밖이다.** 스텁은 그대로 둔다 — 고칠 것은 스텁이 아니라
> **그 스텁을 실행 통로로 받아주는 dispatch의 판정**이다. 스텁을 고치면 `TemplateSyncCheckCli`가 깨진다
> (`server/Harness/TemplateSyncCheckCli.cs:86`이 `DispatchExecutorCli.Run`을 직접 부른다).

## 6. 검수 기준 (공통 항목은 `_header.md` 상속 + 아래)

- [ ] 반증 시험 4종의 **실제 exit code·응답 본문**이 verification 문서에 있다 (문자열 요약 아님)
- [ ] `dispatch.invalid_executor`와 `dispatch.executor_not_implemented`가 **구분**된다 — 오타와 미구현을 같은 오류로 뭉개지 않았다
- [ ] `claude-code`·`ollama` 회귀 없음이 실측으로 확인됐다
- [ ] verification 문서에 **`## 지표는 만족했으나 목적은 미달인 부분`** 자진 신고 (없으면 "없음" — ADR-005)
