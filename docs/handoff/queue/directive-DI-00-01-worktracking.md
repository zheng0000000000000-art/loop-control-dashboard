# DI-00-01 — 작업 추적 파일 초기화 (v9 canonical)

```context-pack
{
  "diId": "DI-00-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/verification/phase-gates/CONFORMANCE-P00-DI-00-01~06.md", "sha256": "8a4edd3f0483b010e6c42a10d5db7ebceb33f2775949aa4af18f57082622edce" },
    { "path": "docs/handoff/decisions/ADR-013-canonical-di-coordinate.md", "sha256": "fd40d98d748f497743f0b4d229c20abebc05b8c5f9f86d21830bc56baf76d3fb" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-DI-00-01-worktracking.md",
    "docs/verification/phase-gates/CONFORMANCE-P00-DI-00-01~06.md"
  ],
  "forbiddenActions": ["git commit", "git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.

## 목적 (지표가 아니라 이것이 판정선이다)

**상태 원본이 거짓말을 못 하게 만든다.** 지금 세 구멍이 열려 있다 — 전부 적합성 행렬이 실체로 확인했다:

1. **WP 목록의 정본이 없다.** `WORKSTATE.wpId = WP-00` 하나뿐이고, 저장소에도 v9에도 WP 등록표가 없다. **"누락된 WP가 없다"를 판정할 데이터가 없다.**
2. **역방향 상태 전이가 통과한다.** `StateApplierCli.ValidateRequest`는 status가 **enum에 속하는지만** 본다. `completed → in_progress`가 그대로 적용된다. **완료를 되돌려 없던 일로 만들 수 있다.**
3. **`docs/STATUS.md`가 손편집이라 낡았다**(2026-07-11자). `projection`은 `RUNTIME-INDEX.md`·`HANDOFF.md`만 생성한다. **낡은 상태 문서가 이미 사고를 냈다** — 조율자가 그 문서를 읽고 스스로 스케줄러를 껐다(`cfbfce4`).

여기에 **검수자가 STATE-01 검수에서 찾은 결함 1건**을 합친다:

4. **canonical 패턴 검사가 요청(delta)에만 걸려 있다.** `ValidateCandidate`는 ID 패턴을 보지 않는다. 그래서 **정지 상태(at rest)가 계약을 어겨도 유효**하다(어제까지 `diId=LEDGER-04`가 그랬다).

## 할 일

### 1. `docs/handoff/WP-REGISTRY.json` 신설 — WP 목록의 정본

```json
{
  "schemaVersion": 1,
  "wps": [
    {
      "wpId": "WP-00",
      "phaseId": "P00",
      "title": "Phase 0 — 작업 착수와 공통 기반",
      "status": "진행",
      "branch": "main",
      "owner": "다중 주체(실행자·코덱스·조율자·검수자)",
      "startedAt": "2026-07-11",
      "verificationDoc": "docs/verification/phase-gates/CONFORMANCE-P00-DI-00-01~06.md"
    }
  ]
}
```

- **status enum은 v9 §DI-00-01 그대로**: `대기 → 진행 → 검증 → 완료`. 다른 값 금지.
- **WP를 네가 새로 발명하지 마라.** 지금 실재하는 WP는 `WP-00` 하나다(WORKSTATE가 그렇게 말한다). **WP 추가는 사람 결재다** — 그 규칙을 파일 머리 주석과 아래 §5 문서에 적어라.
- **`WORKSTATE.wpId`가 이 파일에 없는 WP를 가리키면 실패한다** — §2에서 강제한다.

### 2. `server/StateApplierCli.cs` — 전이 그래프 + candidate 불변식

**(a) 허용 전이 화이트리스트** (이 표 밖의 전이는 **전부 거부**):

| from | 허용되는 to |
| --- | --- |
| `waiting` | `in_progress`, `blocked` |
| `in_progress` | `verifying`, `blocked`, `waiting` |
| `verifying` | `completed`, `in_progress`, `blocked` |
| `blocked` | `waiting`, `in_progress`, `verifying` |
| `completed` | **없음 (terminal)** |
| (같은 status로의 전이) | 허용 — nextActions·blockers 갱신은 정상 경로다 |

- **`completed`에서 나가는 전이는 `--human-decision`(`approved:true`)이 있을 때만 허용한다.** 완료를 되돌리는 것은 사람 결재다.
- 거부는 **exit 1**이고 **WORKSTATE를 건드리지 않는다.**

**(b) `ValidateCandidate`에 canonical 패턴 검사 추가** — `phaseId`·`wpId`·`diId`를 **정지 상태에서도** 검사한다. 요청에 없어서 상속된 값이라도 비canonical이면 실패다.

**(c) `wpId`가 `WP-REGISTRY.json`에 등록된 WP인지 검사** — 없으면 exit 1. **이것이 "누락된 WP가 없다"를 코드로 강제하는 지점이다.**

### 3. `server/ProjectionCli.cs` — `STATUS.md`도 생성물로

- `WORKSTATE.json` + `WP-REGISTRY.json`에서 **`docs/STATUS.md`를 생성**한다. 머리에 `<!-- GENERATED ... 직접 편집하지 마라 -->` 주석을 넣는다(`RUNTIME-INDEX.md`와 같은 형식).
- **담아야 하는 것**: WP 등록표(wpId·title·status·branch·owner·startedAt·verificationDoc) · 현재 phaseId/wpId/diId/status · blockers · nextActions · **상태 변경 규칙(허용 전이 표)**.
- **담지 마라**: 서사·회고·큐 이야기. 그건 손으로 쓰는 문서의 몫이고, 그래서 낡는다. **STATUS.md는 이제 기계가 쓴다.**
- 기존 `docs/STATUS.md`의 손편집 내용은 **버린다**(낡았고, 정본은 WORKSTATE다). `doc-integrity`가 이 파일을 검사하므로 markdown이 깨지면 안 된다.

## 하지 않는 것 (범위 밖 — 명시)

- ❌ **`server/Harness/**` 무접촉** — 코덱스 배타 영역(ADR-002).
- ❌ **WORKSTATE를 손으로 고치는 것.** 상태 변경은 `state-transition`으로만 한다(STATE-01이 직접 경로를 폐지했다). `projection`이 changedFiles를 stamp하는 것은 정상이다.
- ❌ **WP를 새로 발명하는 것.** `WP-00` 하나만 등록한다.
- ❌ **새 canonical ID 체계**(ADR-013).
- ❌ git commit/push · 결재 · 반입 · 발사.

## 필수 반증 시험 (전부 exit code로 판정. **원본 WORKSTATE를 망가뜨리지 마라 — 실패 경로는 쓰지 않는다**)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | `completed → in_progress` (사람 결정 없음) | **exit 1**, WORKSTATE sha256 불변 |
| 2 | `completed → in_progress` + `--human-decision approved:true` | exit 0 (되돌림은 사람 결재로만) |
| 3 | `waiting → completed` (verifying 건너뜀) | **exit 1** |
| 4 | `waiting → waiting` (nextActions만 갱신) | exit 0 (같은 status 전이는 정상) |
| 5 | `wpId: "WP-99"` (WP-REGISTRY에 없음) | **exit 1** |
| 6 | candidate가 비canonical (요청에 `diId` 없이, WORKSTATE의 `diId`를 미리 `LEDGER-04`로 만든 **사본**에서) | **exit 1** |
| 7 | `projection` 두 번 연속 | `STATUS.md` **멱등**(sha256 동일) |
| 8 | `STATUS.md`를 손으로 한 줄 고친 뒤 `projection` | **덮어써진다**(손편집이 살아남지 않는다) |
| 9 | 정상 전이 1회 | exit 0 · `STATUS.md`·`RUNTIME-INDEX.md`·`WORKSTATE`가 **같은 상태를 표현한다** |

**통과를 믿기 전에 실패시킬 수 있음을 먼저 증명해라.**

## 검수 기준

1. `build-verify` **exit 0**, warning 0
2. 위 반증 시험 9개 (**8번·9번은 실측이다 — 코드 검토로 갈음하지 마라**)
3. `measure dev-pack` **violationCount 0**
4. `handoff-integrity` **exit 0**
5. `di-completion-check --gate POST-EXECUTOR --task DI-00-01` → **gateVerdict PASS**(기대 exit == 실제 exit. `gate-clean server`는 **기대 1**이다)
6. **파일을 다 쓴 뒤 마지막에 `projection`** 실행
7. **v9 `DI-00-01` 검증 2항이 실제로 충족되는가** — ①누락된 WP가 없다(WP-REGISTRY가 정본) ②**역방향 상태 변경이 차단된다**(문서가 아니라 **코드**로)

## 허용 파일 (allowlist)

- server/StateApplierCli.cs
- server/ProjectionCli.cs
- docs/handoff/WP-REGISTRY.json
- docs/STATUS.md
- docs/handoff/WORKSTATE.json
- docs/context/RUNTIME-INDEX.md
- docs/handoff/HANDOFF.md
- docs/verification/di0001-worktracking.md
- docs/handoff/queue/directive-DI-00-01-worktracking.md

> `server/Harness/**` 무접촉. `dashboard/` 무접촉. `outputs/launch/**` 무접촉.

## 보고

`docs/verification/di0001-worktracking.md`에 `docs/verification/_template.md` 형식으로:
**①주체(actor) ②사용한 하네스와 각각의 exit code(명령 전문·핵심 수치) ③참조한 스킬** + **`## 지표는 만족했으나 목적은 미달인 부분`**(ADR-005, 없으면 "없음"이라고 쓰고 근거를 대라).

- **하네스 결과를 손으로 적되, 검수자가 전부 재실행해 대조한다. 자기보고는 증거가 아니다.**
- **못 한 시험이 있으면 "코드 검토로 갈음"이라고 쓰지 말고 `NOT_VERIFIED`라고 써라.** 신고하면 감점이 아니다. 숨기면 반려다.
- 한도가 임박하면 마지막 세 줄로 `QUOTA_SIGNAL` / `CHANGED:` / `NEXT:`. **부분 작업물은 되돌리지 마라.**
