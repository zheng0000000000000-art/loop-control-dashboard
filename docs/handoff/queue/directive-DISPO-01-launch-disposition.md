```context-pack
{
  "diId": "DISPO-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-DISPO-01-launch-disposition.md",
    "docs/handoff/decisions/ADR-016-gate-runner-authority.md",
    "server/Harness/GateWitnessCheckCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# DISPO-01 — 실행자 산출물의 처분을 기록으로 남기고, 안 남긴 것을 드러낸다

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 근거: `ADR-016` §13 · `docs/verification/postexecutor-runner-comparison.md`

---

## 0. 문제 (실측 2026-07-26)

`outbox/codex-launch-*/`가 **15개** 있고 그중 14개에 비어 있지 않은 `candidate.patch`가 있다.
**처분(disposition)이 기록된 것은 0개다.**

```
codex-launch-LAUNCH-GWIT-05      patch 있음 · 요청 형식 오류로 버림   ← 기록 없음
codex-launch-LAUNCH-GWIT-05-R2   patch 있음 · 반입됨                 ← 기록 없음
codex-launch-LAUNCH-GWIT-04      patch 빈 것 · 산출 없음             ← 기록 없음
```

**셋이 파일로 구분되지 않는다.** 반입한 것, 버린 것, 아무것도 안 낸 것이 같아 보인다.
다음 사람은 디렉터리 이름과 커밋 메시지로 추측해야 하고, **그것이 프록시다.**

### 이 지시서가 다루지 **않는** 것 — 이미 강제되는 부분

*"실행자 산출 → 빌드 → 게이트"* 중 **빌드 → 게이트는 이미 강제된다.**
소스가 바이너리보다 새로우면 두 러너 모두 **exit 2 · `NOT-MEASURED` · 검사 0개**로 거부하고
"먼저 빌드하라"고 말한다(`ADR-016` §13에서 실측). **그쪽은 건드리지 마라.**

남은 구멍은 **산출 → 게이트 링크**다. 반입해 놓고 게이트를 **안 돌려도 아무도 모른다.**

## 1. 무엇을 하는가

새 하네스 `launch-disposition`(이름은 실행자가 정해도 된다)을 만들고 `HarnessRegistry`에 등재한다.

각 `outbox/codex-launch-<launchId>/`에 **`disposition.json`**이 있어야 한다.

```
{ "launchId": "...", "state": "imported" | "rejected" | "no-output",
  "decidedAt": "...", "actor": "...",
  "importCommit": "<sha>",          // imported일 때 필수
  "gateReport": "outputs/gates/....json",  // imported일 때 필수
  "reason": "..." }                 // rejected일 때 필수
```

하네스는 **위반을 세어 드러낸다.** 판정은 exit code로:
**위반 0이면 exit 0, 1건 이상이면 exit 1.**

## 1-A. `imported`는 게이트 보고와 실제로 이어져야 한다

`gateReport`가 가리키는 파일을 **열어서** 확인한다. 이름만 맞으면 안 된다.

- 그 보고의 `launchId`(또는 그에 준하는 식별자)가 이 디렉터리의 것과 같은가
- 그 보고의 `baselineCommit`이 `importCommit`과 같거나 그 **이후**인가
  (반입 **전에** 잰 게이트는 반입물을 재지 않았다)

어긋나면 위반이다. **`ADR-016` §6이 난 자리가 "보고가 있긴 한데 다른 것을 잰 것"이다.**

## 1-B. 프록시로 판정하지 마라

- **디렉터리 이름·커밋 메시지·타임스탬프 상관으로 반입 여부를 추정하지 마라.**
  CLAUDE.md: *"커밋 접두사·타임스탬프 상관·에러 문구·정규식 매치는 증거가 아니다."*
  이 하네스는 **`disposition.json`이 있는가**만 묻는다. 없으면 **"미상"이 아니라 위반**이다.
- `candidate.patch`가 저장소에 이미 적용됐는지 diff로 추정하지 마라. 부분 반입·후속 수정 때문에
  맞지 않는다. **기록이 진실의 출처다.**

## 2. 하지 않을 일 (하면 반려)

- `GATE-MANIFEST.json` 수정 — 영역 밖이고, **지금 등재하면 15건이 즉시 위반이라 영구 적색이 된다**
  (`FAIL-2026-010`). 등재는 backfill 뒤에 조율자가 한다(§6).
- 기존 `outbox/codex-launch-*/`에 `disposition.json`을 **써 넣는 것.**
  처분은 **사람·조율자의 판단**이다. 실행자가 대신 정하지 마라 — `approve/reject/import 대행 금지`.
- `codex-launch`(`server/CodexHarnessLauncherCli.cs`) 수정 — **영역 밖**이다.
- 낡음 판정·게이트 러너 건드리기(§0 후단).

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `HarnessRegistry`에 등재되어 `di-completion-check`가 아는 명령이 된다
- [ ] 현재 저장소에서 돌리면 **위반 15건**(비어 있지 않은 패치 14 + 빈 것 1)을 세고 **exit 1**
- [ ] §4의 7개 시험이 전부 기대값
- [ ] `build-verify` **exit 0**

### 목적 기준 (사람 판정)

**"어떤 산출물이 반입됐고 무엇이 그것을 판정했는지 파일만 보고 알 수 있다."**

지표만 만족시키는 우회로: `disposition.json`이 **있기만 하면** 통과시키는 것.
그러면 `state: "imported"`에 존재하지 않는 `gateReport`를 적어도 통과한다. §4 시험 3·4가 잡는다.

## 4. 반증 시험 (전부 실측. **코드 검토로 갈음하지 마라**)

픽스처는 `docs/qa/gate-witness/` 아래 **새 디렉터리**에 만들어라. 기존 파일은 다른 검사의
witness다 — 건드리지 마라. 하네스가 **픽스처 루트를 인자로 받게** 해야 시험할 수 있다.

| # | 픽스처 | 기대 |
| --- | --- | --- |
| 1 | 패치 있음 · `disposition.json` 없음 | **위반 1**, exit 1 |
| 2 | `state: "rejected"` + `reason` | **위반 0**, exit 0 |
| 3 | `state: "imported"` · `gateReport`가 **없는 파일** | **위반 1** |
| 4 | `state: "imported"` · 보고는 있으나 **launchId가 다름** | **위반 1** |
| 5 | `state: "imported"` · 보고의 `baselineCommit`이 `importCommit`**보다 앞섬** | **위반 1** |
| 6 | `state: "imported"` · 전부 정합 | **위반 0**, exit 0 |
| 7 | `state: "no-output"` · 패치가 **빈 것** | **위반 0** |
| 8 | `state: "no-output"`인데 패치가 **비어 있지 않음** | **위반 1** — 기록이 실체와 다르다 |

**시험 5가 이 지시서의 핵심이다.** 반입 전에 잰 게이트 보고를 갖다 붙이는 것이 가장 그럴듯한
거짓이고, 파일 존재만 보는 구현은 그걸 통과시킨다.

**시험 8도 빼지 마라.** 기록이 실체와 어긋나는 방향은 양쪽 다다.

## 5. 착륙

`context-pack-integrity`를 돌려 stale해진 pin이 있는지 확인하라.
**소유 지시서를 가정하지 마라 — stale로 지목된 경로를 grep해서 실제 소유자를 찾고,
그 경로가 `requiredInputs`인지 `readOrder`인지까지 보아라.** `readOrder`에는 sha가 없어
stale해지지 않는다. 2026-07-26에 이 구분을 안 해서 한 번 틀렸다.

## 6. 후속 (이 지시서가 하지 않는다)

1. **조율자·사람이 기존 15건의 `disposition.json`을 backfill한다.** 반입 여부는 사람이 안다.
2. backfill이 끝난 **뒤에** 조율자가 `GATE-MANIFEST.json`에 등재하고 반증 witness를 짝지운다.
   **순서를 지켜라 — 먼저 등재하면 영구 적색이 되고 그러면 아무도 안 본다(`FAIL-2026-010`).**
3. 그 다음에야 `codex-launch`가 처분 기록을 요구하게 만들지 논의할 수 있다(별도 결재).

## 허용 파일 (allowlist)

- server/Harness/HarnessRegistry.cs
- server/Harness/LaunchDispositionCli.cs
- docs/qa/gate-witness/**

> 검증 문서는 영역 밖이라 조율자가 쓴다.
