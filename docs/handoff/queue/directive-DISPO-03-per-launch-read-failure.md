```context-pack
{
  "diId": "DISPO-03",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-DISPO-03-per-launch-read-failure.md",
    "docs/handoff/queue/directive-DISPO-02-pending-state.md",
    "server/Harness/LaunchDispositionCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# DISPO-03 — 파일 하나를 못 읽었다고 17건 전부를 못 재게 하지 마라

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 선행: `DISPO-01`(신설) · `DISPO-02`(`pending`).

---

## 0. 문제 (실측 2026-07-26)

`launch-disposition`은 파일을 못 읽으면 **전체를 exit 2로 중단한다.**

```
launch-disposition ['outbox'] exp 0 got 2
  "launch-disposition failed: The process cannot access the file ... LAUNCH-DISPO-02.gate.json"
```

한 발사의 `gateReport` 하나가 잠겨 있어서 **나머지 16건이 판정되지 않았다.**
게이트 보고에는 `exit 2` 한 줄만 남고, *어느 발사가 문제인지*·*나머지는 괜찮았는지*가 없다.

**`ADR-016` §11이 지적한 것과 같은 모양이다** — 실패 사유가 뭉뚱그려져 실체가 가려진다.
`gate-clean`·`doc-integrity`가 개별로는 exit 0인데 게이트에서는 1로 잡혔던 그 일이다.

## 1. 무엇을 하는가

**읽기·파싱 실패를 그 발사 하나의 위반으로 낮춘다.** 나머지 발사는 계속 판정한다.

사유를 **구분해서** 낸다(이름은 실행자가 정해도 된다):

- `disposition.json`을 못 읽거나 JSON이 아님 → `disposition-unreadable` / `disposition-unparsable`
- `gateReport`가 가리키는 파일을 못 읽거나 JSON이 아님 → `gate-report-unreadable` / `gate-report-unparsable`

기존 `gate-report-not-found`(파일이 아예 없음)와도 **구분한다.** *"없다"* 와 *"있는데 못 읽는다"* 는
다른 사실이고, 후자는 대개 이번처럼 **다른 프로세스가 쓰고 있다**는 뜻이다.

## 1-A. 실패는 위반이지 통과가 아니다

못 읽었으면 **위반이다.** 예외를 삼켜서 그 발사를 정상으로 넘기거나, 목록에서 **빼버리지 마라.**
빼면 `launchCount`가 줄어 *"그런 발사는 없었다"*가 된다 — 기록이 실체와 어긋나는 방향이다.
**`launchCount`는 디렉터리 수 그대로여야 한다.** §4 시험 4가 이걸 잡는다.

## 1-B. 루트 자체를 못 읽는 것은 여전히 exit 2다

`launch-disposition <없는 경로>`처럼 **셀 대상 자체가 없으면** 그건 "위반 0건"이 아니라
**잴 수 없음**이다. 지금 동작을 유지해라. **낮추는 것은 발사 단위 실패뿐이다.**

## 2. 하지 않을 일 (하면 반려)

- `imported`·`rejected`·`no-output`·`pending`의 **판정 규칙 변경.** 이번 작업은 **실패 처리**만 바꾼다.
- 예외를 삼켜 통과로 만드는 것(§1-A).
- 못 읽은 발사를 목록에서 제외하는 것(§1-A).
- 루트 부재를 위반 0으로 바꾸는 것(§1-B).
- `GATE-MANIFEST.json` 수정 — 영역 밖. 기대값은 그대로다(`outbox` exp 0, `case-01` exp 1).
- 기존 `docs/qa/gate-witness/launch-disposition/case-01` ~ `case-13` 수정.
  **`case-01`은 매니페스트에 등재된 반증 witness다.** 새 case 디렉터리를 써라.

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `launch-disposition outbox` **exit 0**, `violations` 0 — 회귀 없음
- [ ] `launch-disposition docs/qa/gate-witness/launch-disposition/case-01` **exit 1** — witness 유지
- [ ] `launch-disposition <없는 경로>` **exit 2** — §1-B 유지
- [ ] §4의 6개 시험이 전부 기대값
- [ ] `build-verify` **exit 0**

### 목적 기준 (사람 판정)

**"한 건이 깨져도 나머지 판정을 볼 수 있고, 깨진 건이 무엇이며 왜인지 출력에 있다."**

## 4. 반증 시험 (전부 실측. **코드 검토로 갈음하지 마라**)

픽스처는 `case-14` 이후 **새 디렉터리**에 만들어라.

| # | 픽스처 | 기대 |
| --- | --- | --- |
| 1 | `imported` · `gateReport`가 **디렉터리 경로**를 가리킴 | **위반 1**, `not-found`와 **다른 사유** |
| 2 | `imported` · `gateReport`가 **JSON이 아닌 파일** | **위반 1**, #1과 또 다른 사유(또는 파싱 전용 사유) |
| 3 | `disposition.json`이 **JSON이 아님** | **위반 1**, `state-invalid`와 **다른 사유** |
| 4 | **깨진 1건 + 정상 `imported` 2건이 한 루트에** | **`launchCount` 3 · `violations` 1**, exit 1 |
| 5 | 루트 경로가 없음 | **exit 2** — 변화 없음 |
| 6 | 기존 `case-01` ~ `case-13` | **전부 이전과 같은 exit·사유** |

**시험 4가 이 지시서의 목적 자체다.** `launchCount`가 3이어야 한다 — 2가 나오면 깨진 건을
목록에서 빼버린 것이고, 그건 §1-A가 금지한 *"그런 발사는 없었다"*이다.
**`violations`가 3이어도 안 된다** — 정상 2건까지 위반으로 물들이는 구현이다.

**시험 1·2·3의 사유가 서로 달라야 한다.** 같으면 뭉뚱그린 것이고, §0이 문제 삼은
*"실패 사유가 뭉뚱그려져 실체가 가려진다"*를 그대로 반복한다.

**디렉터리를 가리키게 하는 것이 시험 1의 방법이다**(파일 잠금은 픽스처로 재현하기 어렵다).
읽기가 실패한다는 성질은 같다.

## 5. 착륙

`context-pack-integrity`를 돌려 stale해진 pin이 있는지 확인하라.
**소유 지시서를 가정하지 마라 — stale로 지목된 경로를 grep해서 실제 소유자를 찾고,
그 경로가 `requiredInputs`인지 `readOrder`인지까지 보아라.** `readOrder`에는 sha가 없어
stale해지지 않는다. 2026-07-26에 이 구분을 안 해서 한 번 틀렸다.

## 6. 이 지시서가 다루지 않는 것

`di-completion-check`에는 `program-verify`의 `--out`에 해당하는 인자가 없다.
지금은 그런 기록이 없지만, `gateReport`가 그쪽 evidence 경로를 가리키면 같은 충돌이 난다.
**별도 지시서다** — 여기 섞으면 반증 시험이 흐려진다.

## 허용 파일 (allowlist)

- server/Harness/LaunchDispositionCli.cs
- docs/qa/gate-witness/**

> 검증 문서는 영역 밖이라 조율자가 쓴다.
