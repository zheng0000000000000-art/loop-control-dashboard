# GUARD-03 — 게이트 잠김 해제: `handoff-integrity`가 `blockers[]`를 읽게 한다

```context-pack
{
  "diId": "GUARD-03",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/handoff/decisions/ADR-014-adr002-exception-blockers.md", "sha256": "834d3f9049d65822006b9f8afd60fa57772ad15a9761c059a25e2f116e33eb88" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/decisions/ADR-014-adr002-exception-blockers.md",
    "docs/handoff/queue/directive-GUARD-03-blockers-unlock.md"
  ],
  "forbiddenActions": ["git commit", "git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** — 그래서 **positive·negative·결정성·격리** 테스트가 전부 필수다(v9 §0.1).

## ⚠️ 먼저 — 너는 지금 ADR-002의 예외 구역에 있다

**`server/Harness/**`는 원래 코덱스 배타 영역이다.** 사람이 **`ADR-014`로 1회 예외를 승인**했다. 경계는 다음과 같고, **넘으면 반려다**:

- **`server/Harness/HandoffIntegrityCli.cs` 파일 하나.**
- **`CheckBlockerConsistency` 함수 하나.** 다른 검사(changedFiles 해시·schema·큐 상태)는 **읽기만 하고 건드리지 마라.**
- **멱등 대조·CLI 계약(`CLI-CONTRACT.json`)·`GATE-MANIFEST` 등재·`claim-check --untracked`는 여전히 코덱스 몫이다**(`CODEX-GATE-02`). **손대지 마라.**

## 목적 — 저장소가 잠겨 있다 (실측)

- `HandoffIntegrityCli.cs:232`: `var blocker = ReadString(workstate, "blocker");` — **단수 `blocker`**
- `WORKSTATE.json`의 실제 필드: **`blockers`(배열)** — v9 canonical 계약(STATE-01이 신설)
- **2026-07-12 23:0x, 저장소 역사상 처음으로 `status=blocked`가 되자** 이 코드가 발화 → `blocked status requires a blocker field` → **exit 1**
- `state-transition`은 post-apply로 `handoff-integrity`를 부른다 → **모든 상태 전이가 exit 1로 끝난다.** 조율자도 커밋을 보류한다.

**하네스가 틀렸고 상태는 맞다.** WORKSTATE를 고쳐서 게이트를 통과시키는 것은 **금지다**(`CLAUDE.md` 금지사항 1번).

## 할 일 (하나뿐이다)

`CheckBlockerConsistency`가 **`blockers` 배열**을 읽게 한다.

| 상태 | 판정 |
| --- | --- |
| `status=blocked` **인데 `blockers`가 비었거나 없음** | **failure** (기존 취지 유지) |
| `status=blocked` **이고 `blockers`에 1건 이상** | **통과** |
| `status=completed` **인데 `blockers`가 비어 있지 않음** | **failure** (`stale` — 완료됐는데 blocker가 남아 있다) |

- **단수 `blocker`는 더 이상 읽지 마라. 하위 호환을 만들지 마라.** 그 필드는 **WORKSTATE에 존재한 적이 없다** — STATE-01 검수가 "한 번도 발화한 적 없는 죽은 경로"로 이미 기록했다. 호환 코드를 남기면 **같은 함정이 다시 잠든다.**
- 실패 메시지는 **`blockers`** 기준으로 다시 쓴다(`blocked status requires a non-empty blockers array` 같은 식).

## 하지 않는 것

- ❌ `HandoffIntegrityCli.cs`의 **다른 검사**를 고치는 것(changedFiles 해시·schema·큐).
- ❌ **멱등 대조 추가** — 코덱스 몫이다(`CODEX-GATE-02` §1). **여기서 만들면 중복이고 반려다.**
- ❌ `server/Harness/`의 **다른 파일**.
- ❌ **WORKSTATE를 고쳐서 게이트를 통과시키는 것.** 상태는 맞다.
- ❌ git commit/push · 결재 · 반입 · 발사.

## 필수 반증 시험 (DI 유형 `harness` → **positive·negative·결정성·격리** 전부)

**사본에서 해라.** `state-transition --root <path>`와 `--dry-run`이 있다(GUARD-01). **실 WORKSTATE를 건드리지 마라.**

| # | 시험 | 기대 | 성격 |
| --- | --- | --- | --- |
| 1 | 사본: `blocked` + `blockers` **1건** → `handoff-integrity` | **exit 0** | positive |
| 2 | 사본: `blocked` + `blockers` **빈 배열** → `handoff-integrity` | **exit 1** | negative |
| 3 | 사본: `completed` + `blockers` **1건** → `handoff-integrity` | **exit 1** | negative (stale) |
| 4 | 사본: `blocked` + **`blockers` 필드 자체 없음** | **exit 1** | negative |
| 5 | 같은 입력으로 **2회 연속 실행** | **같은 exit·같은 출력** | 결정성 |
| 6 | 하네스 실행 후 **저장소 파일이 하나도 안 바뀐다** | 실행 전후 `git status` 동일 | 격리(읽기 전용) |
| 7 | **실 저장소**(현재 `blocked` + `blockers` 1건) → `handoff-integrity` | **exit 0** — **게이트가 풀린다** | 목적 |

**통과를 믿기 전에 실패시킬 수 있음을 먼저 증명해라.**

## 검수 기준

1. `build-verify` **exit 0**, warning 0
2. `verify-behavior` **exit 0**
3. `measure dev-pack` **violationCount 0** — ⚠️ **`dotnet run --project server -c Release -- measure dev-pack`으로 실행해라.** exe 직접 호출은 저장소 루트를 **부모 폴더**로 잡아 exit 2가 난다(실측).
4. 반증 시험 7개 — **전부 실측. 코드 검토로 갈음하지 마라.**
5. **`handoff-integrity` 실 저장소에서 exit 0** ← **이 작업의 판정선이다**
6. `di-completion-check --gate POST-EXECUTOR --task GUARD-03` **gateVerdict PASS**
7. 파일을 다 쓴 뒤 마지막에 **`projection`**

## 허용 파일 (allowlist)

- server/Harness/HandoffIntegrityCli.cs
- docs/verification/guard03-blockers-unlock.md
- docs/handoff/queue/directive-GUARD-03-blockers-unlock.md
- docs/handoff/WORKSTATE.json
- docs/context/RUNTIME-INDEX.md
- docs/handoff/HANDOFF.md
- docs/STATUS.md

> `server/Harness/`의 **다른 파일 무접촉**. `server/Cli/**`·`server/Program.cs`·`skills/**`·`dashboard/**` 무접촉.

## 보고

`docs/verification/guard03-blockers-unlock.md` — `docs/verification/_template.md` 형식 그대로.
**DI 유형(`harness`) 선언** · **유형별 필수 검증(positive·negative·결정성·격리)** · 주체 · 하네스별 **기대 exit vs 실제 exit** · 공통 완료 조건 6개 · **실패 분류**(v9 §0.3 — 이번 건은 `new_failure`인지 `design_learning`인지 **네가 판단하고 근거를 대라.** 위키 등록이 필요하면 그렇게 신고하라) · 반증 시험 표 · 잔여 위험 · **`## 지표는 만족했으나 목적은 미달인 부분`**.

- **ADR-002 예외를 썼다는 사실을 「직접 경로 사용 사유」에 명시하라**(`ADR-014` 인용).
- **자기보고는 증거가 아니다.** 검수자가 반증 7개를 직접 재실행한다.
- 못 한 시험은 `NOT_VERIFIED`. **숨기면 반려다.**
