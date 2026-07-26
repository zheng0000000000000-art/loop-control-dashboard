```context-pack
{
  "diId": "DISPO-02",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-DISPO-02-pending-state.md",
    "docs/handoff/queue/directive-DISPO-01-launch-disposition.md",
    "server/Harness/LaunchDispositionCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# DISPO-02 — `launch-disposition`이 `pending`을 알게 한다

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 선행: `DISPO-01`(하네스 신설·backfill 완료).
- **후속(조율자 몫)**: `codex-launch`가 발사 시점에 `pending`을 자동으로 남긴다(§6).
  `server/CodexHarnessLauncherCli.cs`는 `server/` 루트라 **코덱스 영역이 아니다**
  (`PermittedWriteRoots = server/Harness/, skills/, docs/qa/`). 그래서 나눴다.

---

## 0. 문제와 순서

`DISPO-01` backfill로 `outbox`는 16/16 기록됐다. 그런데 **다음 발사는 다시 `disposition-missing`으로
시작한다.** 사람이 매번 손으로 채워야 하고, 안 채우면 `DISPO-01` 이전과 같아진다.

발사 시점에 `state: "pending"`을 자동으로 남기면 *"기록이 없다"*가 *"아직 안 정했다"*로 바뀐다.
**그 둘은 다른 상태다.** 전자는 파이프라인의 구멍이고 후자는 사람이 할 일이다.

**하네스가 먼저다.** 지금 `launch-disposition`은 모르는 state를 `state-invalid`로 처리한다
(`LaunchDispositionCli.cs:113`). 런처를 먼저 고치면 새 발사가 전부 `state-invalid`가 된다.

## 1. 무엇을 하는가

`state: "pending"`을 인식한다.

- 필수 필드: `launchId`(디렉터리와 일치), `decidedAt` 또는 그에 준하는 시각, `actor`
- `importCommit`·`gateReport`·`reason`은 **요구하지 않는다** — 아직 안 정했으니까

## 1-A. `pending`은 **통과가 아니다**

`pending`도 **위반으로 센다.** 다만 사유를 `disposition-missing`과 **구분해서**
`disposition-pending`(이름은 실행자가 정해도 된다)으로 낸다.

**통과로 만들면 이 하네스의 존재 이유가 사라진다.** 발사해 놓고 영원히 안 정해도 게이트가
초록이면, 그것이 바로 이 저장소가 반복해 온 *"기록만 남기고 이행하지 않는다"*이다.
`pending`을 통과시키는 구현은 **반려한다.**

## 1-B. `pending`이 다른 판정을 가리면 안 된다

실제로 반입된 산출물에 `pending`이 붙어 있어도 **`pending`이다.** 하네스가 반입 여부를
추정해 `imported`로 승격시키지 마라 — `DISPO-01` §1-B가 금지한 프록시다.
**기록이 진실의 출처다.**

## 2. 하지 않을 일 (하면 반려)

- `server/CodexHarnessLauncherCli.cs` 수정 — **영역 밖**(§0).
- `GATE-MANIFEST.json` 수정 — 영역 밖. 이미 등재돼 있고 기대값은 그대로다.
- 기존 `outbox/codex-launch-*/disposition.json` 수정 — **처분은 사람 결재다.**
- `imported`·`rejected`·`no-output`의 판정 규칙 변경. **이번 작업은 상태 하나를 더할 뿐이다.**
- `pending`을 통과로 만드는 것(§1-A).

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `launch-disposition outbox` **exit 0**, `violations` **0** — 회귀 없음(현재 16건은 pending이 아니다)
- [ ] `launch-disposition docs/qa/gate-witness/launch-disposition/case-01` **exit 1** — 기존 witness 유지
- [ ] §4의 6개 시험이 전부 기대값
- [ ] `build-verify` **exit 0**

### 목적 기준 (사람 판정)

**"기록이 없는 것과 아직 안 정한 것이 출력에서 구분된다. 둘 다 초록은 아니다."**

## 4. 반증 시험 (전부 실측. **코드 검토로 갈음하지 마라**)

픽스처는 `docs/qa/gate-witness/launch-disposition/` 아래 **새 case 디렉터리**로 만들어라.
**기존 case-01 ~ case-08은 건드리지 마라** — case-01은 매니페스트에 등재된 반증 witness다.

| # | 픽스처 | 기대 |
| --- | --- | --- |
| 1 | `pending` 정상 | **위반 1**, 사유가 `disposition-missing`과 **다른 값** |
| 2 | `pending`인데 `actor` 없음 | **위반 1**, #1과 **또 다른 사유** — 필드를 실제로 읽는다는 증거 |
| 3 | `pending`인데 `launchId`가 디렉터리와 다름 | **위반 1**, launchId 불일치 사유 |
| 4 | 알 수 없는 state(예: `"maybe"`) | **위반 1**, `state-invalid` — 변화 없음 |
| 5 | `pending` 1건 + 정상 `imported` 1건이 한 루트에 | **위반 1** (2도 0도 아니다) |
| 6 | 기존 case-01 ~ case-08 | **전부 이전과 같은 exit·사유** |

**시험 1·2가 짝이다.** 사유가 셋 다 달라야 한다 — 같으면 `pending`을 "그냥 위반"으로
뭉뚱그린 것이고, 그러면 §0이 말한 *"기록이 없다 vs 아직 안 정했다"* 구분이 안 된다.

**시험 5가 세는 방식을 잡는다.** `pending`이 있으면 전부 위반으로 처리하거나, 반대로
`pending`이 있으면 다른 것을 안 세는 구현을 배제한다.

**시험 6을 빼지 마라.** 상태 하나를 더하면서 기존 판정을 건드렸는지 보는 자리다.

## 5. 착륙

`context-pack-integrity`를 돌려 stale해진 pin이 있는지 확인하라.
**소유 지시서를 가정하지 마라 — stale로 지목된 경로를 grep해서 실제 소유자를 찾고,
그 경로가 `requiredInputs`인지 `readOrder`인지까지 보아라.** `readOrder`에는 sha가 없어
stale해지지 않는다. 2026-07-26에 이 구분을 안 해서 한 번 틀렸다.

## 6. 후속 (이 지시서가 하지 않는다 — 조율자 몫)

1. `codex-launch`가 발사 성공 시 `outbox/codex-launch-<id>/disposition.json`에
   `state: "pending"`을 쓴다. **이미 파일이 있으면 덮지 않는다** — 사람이 정한 처분을
   런처가 지우면 안 된다.
2. 그 뒤로 **모든 발사는 즉시 `POST-COMMIT`을 빨갛게 만든다.** 사람이 처분을 정할 때까지.
   **이것은 의도된 결과이지 결함이 아니다** — 다만 운영 감각을 바꾸므로 `HUMAN-INBOX`에
   올려 사람이 알고 받아들이게 한다.

## 허용 파일 (allowlist)

- server/Harness/LaunchDispositionCli.cs
- docs/qa/gate-witness/**

> 검증 문서는 영역 밖이라 조율자가 쓴다.
