# ADR-014 — ADR-002(코덱스 배타 영역) 1회 예외: `handoff-integrity`의 `blockers[]` 수정

- 상태: **승인됨 (사람 choi, 2026-07-12 23:1x)**
- 일시: 2026-07-12
- 제안: 검수자. **범위: 이번 1회, 파일 1개, 함수 1개.**
- 관련: `ADR-002`(영역 소유권) · `ADR-013`(canonical 좌표) · `docs/handoff/HUMAN-INBOX.md`(23:0x 등재)

## 1. 상황 (실측)

- `server/Harness/HandoffIntegrityCli.cs:232`가 **단수 `blocker`**를 읽는다: `var blocker = ReadString(workstate, "blocker");`
- `WORKSTATE.json`은 **v9 canonical 계약대로 복수 `blockers[]`**다(STATE-01이 신설).
- **2026-07-12 23:0x, 저장소 역사상 처음으로 `status=blocked`가 되자** 그 코드가 발화해 `blocked status requires a blocker field`로 **exit 1**을 냈다.
- `state-transition`은 post-apply로 `handoff-integrity`를 부른다 → **모든 상태 전이가 exit 1로 끝난다.** 조율자도 이 FAIL을 보고 커밋을 보류한다.
- **즉 저장소 전체가 잠겼다.** 원인은 **한 함수**다.

**STATE-01 검수(2026-07-12 19:5x)에서 이미 "이 코드는 한 번도 발화한 적 없는 죽은 경로"라고 기록했다.** 죽은 줄 알았던 코드가 계약을 어긴 채 살아 있었다.

## 2. 결정

**`ADR-002`(`server/Harness/**`는 코덱스 배타)의 1회 예외를 승인한다.** 실행자(sonnet)가 `GUARD-03`으로 고친다.

**예외의 경계 (넘으면 반려):**

- 파일: **`server/Harness/HandoffIntegrityCli.cs` 단 하나.**
- 함수: **`CheckBlockerConsistency` 단 하나.** 다른 검사(changedFiles 해시·schema)는 **건드리지 않는다.**
- **멱등 대조·CLI 계약·GATE-MANIFEST 등재·`claim-check --untracked`는 여전히 코덱스 몫이다**(`CODEX-GATE-02`). **실행자가 손대면 반려.**

## 3. 근거 — 왜 예외가 정당한가

`v9 §0.4`의 예산 예외 사유와 같은 논리다: **"복구 불가능 상태 방지"·"출시 차단 불변식".**

- 게이트가 잠긴 상태에서는 **어떤 DI도 진행할 수 없고, 상태 전이도 기록될 수 없다.**
- 대안 두 개는 더 나쁘다:
  - **코덱스를 기다린다** → 코덱스가 언제 돌지 모른다(19:15 이후 실체 활동 없음, **원인 주체 미상**). 그동안 저장소가 멈춘다.
  - **WORKSTATE를 `blocked`에서 빼서 회피한다** → **상태를 왜곡해 게이트를 통과시키는 것**이고, `CLAUDE.md` 금지사항 1번이 막는 바로 그 행동이다. **하지 않는다.**

**하네스가 틀렸고 상태는 맞다.** 고칠 것은 하네스다.

## 4. 되돌리는 법

이 ADR은 **1회성**이다. 예외를 반복하려면 **새 ADR과 새 사람 결재**가 필요하다.
`ADR-002`의 영역 소유권은 **그대로 유효하다** — 이 결정으로 완화되지 않는다.

## 5. 한 줄

> **예외는 조용히 쓰면 관행이 된다. 그래서 기록한다.**
