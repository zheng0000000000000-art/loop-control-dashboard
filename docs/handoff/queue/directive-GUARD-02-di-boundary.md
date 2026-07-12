# GUARD-02 — DI 경계 전이 + verdict를 게이트 증거에 결속

```context-pack
{
  "diId": "GUARD-02",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/handoff/RECOVERY.md", "sha256": "f7bcf9eccb5cad4cf37b61fc45f5682d5665c090bd074597f0e10d44d56dd069" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-GUARD-02-di-boundary.md"
  ],
  "forbiddenActions": ["git commit", "git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `implementation`**

## 목적 — 두 구멍. 하나는 내가 만들었고, 하나는 아직 안 닫혔다.

### 1. **다음 DI로 넘어갈 수 없다** (설계 오류 — 검수자가 만들었다)

`DI-00-01`이 `completed`가 된 순간 **다음 DI를 시작할 수 없다.**

```
completed → waiting  ⇒  "completed 상태 전이에는 --human-decision 파일이 필요합니다"  (exit 1, 실측)
```

**"완료를 되돌리는 것"과 "다음 DI를 시작하는 것"이 같은 규칙에 걸린다.**
v9는 **Phase 경계에만** 사람 결재를 요구한다(`§DI-00-07`). **DI 경계마다 결재를 요구하지 않는다.**
사람 결정(2026-07-12): **코드로 고친다.**

### 2. **`--verdict`가 형식적이다** (STATE-01 검수 때 발견, 아직 안 닫힘)

`ValidateVerdict`는 **임의 경로의 아무 JSON이나** `{"verificationPassed":true,"exitCode":0}`이면 통과시킨다.
**실측**: 검수자가 방금 `outputs/verdict-DI-00-01.json`을 **손으로 써서** `completed` 전이를 통과시켰다. **생산자가 자기 완료를 결재할 수 있다는 뜻이다.**
→ **"완료에는 독립 검증이 필요하다"는 규칙이 코드로 강제되지 않는다.**

## 할 일

### 1. `server/StateApplierCli.cs` — DI 경계 전이

**규칙(정확히 이대로):**

| 상황 | 판정 |
| --- | --- |
| 요청의 `diId` ≠ 현재 `diId` (**새 DI 착수**) | 현재 status가 **`completed`일 때만 허용**. 새 status는 **`waiting` 또는 `in_progress`만**. status 전이 그래프는 **적용하지 않는다**(새 DI는 새 사이클이다) |
| 요청의 `diId` ≠ 현재 `diId` **인데 현재 status가 `completed`가 아님** | **exit 1** — 미완 DI를 버리고 넘어갈 수 없다. (`blocked`도 안 된다. 막혔으면 막힌 채로 둔다) |
| 요청의 `diId` == 현재 `diId` | **기존 전이 그래프 그대로.** `completed`에서 나가려면 여전히 `--human-decision`(= 완료 되돌림) |
| `phaseId` 변경 | **기존대로 `--human-decision` 필수**(Phase 경계는 사람 게이트다 — 바꾸지 마라) |

- 새 DI 착수 전이는 **`appliedTransitions`·`applier-log`에 정상 기록**한다.
- **`diId`·`phaseId`·`wpId`의 canonical 검사와 `WP-REGISTRY` 검사는 그대로 유지한다.**

### 2. `server/StateApplierCli.cs` — `--verdict`를 게이트 증거에 결속

`completed` 전이의 `--verdict`는 **`di-completion-check`가 생성한 게이트 증거만** 받는다:

- 경로는 **`outputs/gates/<taskId>.gate.json`** 형태여야 한다(다른 경로 거부).
- 그 JSON이 **`"gateVerdict": "PASS"`**, **`"failureCount": 0`**이어야 한다.
- **`taskId`가 전이 대상 DI와 대응**해야 한다(요청의 `diId` 또는 현재 `diId`와 매칭. **대응 규칙을 코드 주석에 명시하라**).
- **생성 시각이 현재 WORKSTATE보다 오래되지 않았는지** 확인하라(stale 증거 거부). 판정 방법은 네가 정하되 **근거를 verification 문서에 적어라.**
- 위 조건을 하나라도 어기면 **exit 1이고 WORKSTATE를 건드리지 않는다.**

> **왜**: 지금은 `echo '{"verificationPassed":true,"exitCode":0}' > any.json` 한 줄로 완료가 통과한다. **게이트 증거는 프로그램이 만든 것이어야 한다. 사람이나 모델이 손으로 쓴 것은 증거가 아니다.**

### 3. 이행 (기존 전이와의 호환)

- **`verificationPassed`/`exitCode` 형식의 옛 verdict는 더 이상 받지 않는다.** 하위 호환을 만들지 마라 — **그게 구멍이다.**
- `outputs/verdict-DI-00-01.json`(검수자가 손으로 쓴 것)은 **더 이상 통과하지 않아야 한다.** **반증 시험 3번이 이것이다.**

## 하지 않는 것

- ❌ `server/Harness/**` 무접촉(코덱스). **`di-completion-check`를 고치지 마라** — 읽기만 한다.
- ❌ `phaseId` 변경의 사람 결재를 없애는 것. **Phase 경계는 사람 게이트다.**
- ❌ 웹서버 기동 경로 변경. 기준 파일(`behavior-snapshot.json`·`blueprint.json`) 수정.
- ❌ git commit/push · 결재 · 반입 · 발사.

## 필수 반증 시험 (**`--root` 사본에서 해라. 실 WORKSTATE를 건드리지 마라** — `GUARD-01`이 그걸 만들었다)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | `completed` + `diId` 변경(`DI-00-01`→`DI-00-04`) + `status=waiting` | **exit 0** — 새 DI 착수 |
| 2 | `verifying` 상태에서 `diId` 변경 시도 | **exit 1** — 미완 DI를 버릴 수 없다 |
| 3 | 손으로 쓴 verdict(`{"verificationPassed":true,"exitCode":0}`)로 `completed` 전이 | **exit 1** — 더 이상 통과하지 않는다 |
| 4 | `outputs/gates/<task>.gate.json`(gateVerdict=PASS)로 `completed` 전이 | **exit 0** |
| 5 | `gateVerdict`가 PASS가 아닌 gate.json | **exit 1** |
| 6 | 존재하지 않는 gate.json 경로 | **exit 1** |
| 7 | 같은 `diId`에서 `completed → in_progress`(human-decision 없음) | **exit 1** — 완료 되돌림은 여전히 사람 결재 |
| 8 | `phaseId` 변경(human-decision 없음) | **exit 1** — Phase 경계는 그대로 |
| 9 | 새 DI 착수 시 비canonical `diId`(`LEDGER-05`) | **exit 1** — canonical 검사 유지 |

**통과를 믿기 전에 실패시킬 수 있음을 먼저 증명해라.**

## 검수 기준

1. `build-verify` **exit 0**, warning 0
2. `verify-behavior` **exit 0**
3. `measure dev-pack` **violationCount 0** — ⚠️ **`dotnet run --project server`로 실행해라.** exe를 직접 부르면 저장소 루트를 **부모 폴더**로 잡아 exit 2가 난다(검수자가 실측으로 당했다. 이건 별건이며 이번 범위가 아니다)
4. 반증 시험 9개 — **전부 실측. 사본에서.**
5. `di-completion-check --gate POST-EXECUTOR --task GUARD-02` **PASS**
6. 파일을 다 쓴 뒤 마지막에 **`projection`**
7. **목적 기준**: DI 경계를 넘을 수 있는가 · **손으로 쓴 verdict가 더 이상 통과하지 않는가**

## 허용 파일 (allowlist)

- server/StateApplierCli.cs
- docs/verification/guard02-di-boundary.md
- docs/handoff/queue/directive-GUARD-02-di-boundary.md
- docs/handoff/WORKSTATE.json
- docs/context/RUNTIME-INDEX.md
- docs/handoff/HANDOFF.md
- docs/STATUS.md

> `server/Harness/**`·`server/Cli/**`·`server/Program.cs` 무접촉. `dashboard/` 무접촉.

## 보고

`docs/verification/guard02-di-boundary.md` — **새 템플릿(`docs/verification/_template.md`)을 그대로 써라.** DI 유형(`implementation`) 선언 · 유형별 필수 검증(정상·실패·재실행) · 공통 완료 조건 6개 · 실패 분류 · 반증 시험 표 · 잔여 위험 · **`## 지표는 만족했으나 목적은 미달인 부분`**.
**자기보고는 증거가 아니다.** 못 한 시험은 `NOT_VERIFIED`라고 써라 — 신고하면 감점이 아니고, 숨기면 반려다.
