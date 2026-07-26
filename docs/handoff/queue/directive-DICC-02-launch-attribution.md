```context-pack
{
  "diId": "DICC-02",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-DICC-02-launch-attribution.md",
    "docs/handoff/decisions/ADR-016-gate-runner-authority.md",
    "server/Harness/DiCompletionCheckCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# DICC-02 — `di-completion-check`의 보고가 어느 발사를 잰 것인지 말하게 한다

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 선행: `DISPO-01`~`DISPO-03`. 근거: `ADR-016` §8(권위).

---

## 0. 먼저 — 앞서 예고한 `--out` 지시서는 쓰지 않는다

`DISPO-03` §6과 `docs/verification/dispo01-launch-disposition.md`는
*"`di-completion-check`에는 `--out`이 없어 같은 자기참조 충돌이 가능하다"*고 적었다.
**전제를 확인해 보니 틀렸다.**

| 확인한 것 | 실측 |
| --- | --- |
| 출력 경로를 정할 수 있는가 | **된다.** `--task dispo-probe` → `outputs/gates/dispo-probe.gate.json` (exit 0) |
| 증거를 언제 쓰는가 | **검사가 끝난 뒤**(`DiCompletionCheckCli.cs:82`, 보고 조립 직후) |
| 그래서 자기참조 충돌이 나는가 | **안 난다.** 실행 중 그 경로를 열어 두지 않는다 |

`program-verify`는 stdout을 셸로 리다이렉트해야 했기 때문에 충돌했다. 그쪽은 처음부터
파일을 자기가 쓴다. **`--out`은 필요 없다.**

## 1. 진짜 결함 — 보고에 `launchId`가 없다

```
di-completion-check 보고의 최상위 키:
  harness, gateId, taskId, manifest, createdAt, gateVerdict,
  checkCount, failureCount, warningCount, verdict, checks, failures
  → launchId 없음
```

`disposition.json`의 `gateReport`는 **그 보고의 `launchId`가 발사와 같은지** 검사받는다
(`launch-disposition`의 `gate-report-launch-id-mismatch`). 그러므로

**`di-completion-check`의 보고는 `gateReport`로 쓸 수 없다.**

`ADR-016` §8은 게이트 권위를 `di-completion-check`에 줬다. 그런데 **권위 있는 러너로 판정하면
그 판정을 처분에 기록할 수 없고**, 기록하려면 권위 없는 쪽(`program-verify`)을 써야 한다.
**규칙과 도구가 반대 방향을 가리킨다.** 사람은 결국 기록되는 쪽을 쓰게 되고, 그러면 §8은
문서에만 남는다 — 이 저장소가 반복해 온 *"기록만 남기고 이행하지 않는다"*이다.

## 2. 무엇을 하는가

`di-completion-check`가 `--launch <launchId>`를 받고 **보고에 그대로 싣는다.**

- 인자가 없으면 **싣지 않거나 명시적으로 null**이다. 어느 쪽이든 좋으나 **"아무 발사에나 맞는 값"이
  되어서는 안 된다**(§3 시험 2).
- **낡음 거부 경로(exit 2, `NOT-MEASURED`)에도 실어야 한다.** 못 잰 보고도 어느 발사에 대한
  것인지 알아야 한다. 안 그러면 "무엇을 못 쟀는지 모르는 기록"이 남는다.
- `--task`와 독립이다. `--task`는 파일 이름, `--launch`는 대상이다. **둘을 합치지 마라.**

## 2-A. 판정 논리는 건드리지 마라

`gateVerdict`·`failures`·exit code 규칙은 그대로다. **이번 작업은 귀속(attribution)만 더한다.**

## 3. 하지 않을 일 (하면 반려)

- `--out` 추가(§0 — 필요 없다).
- `GATE-MANIFEST.json`·`launch-disposition`·`program-verify` 수정 — 영역 밖이거나 무관하다.
- `outbox/**`의 처분 파일 수정 — **처분은 사람 결재다.**
- `launchId`가 없을 때 통과시키는 특례(§4 시험 2).

## 4. 반증 시험 (전부 실측. **코드 검토로 갈음하지 마라**)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | `--gate POST-COMMIT --launch LAUNCH-X --task t1` | 보고에 `launchId: "LAUNCH-X"`, 파일은 `t1.gate.json` |
| 2 | `--launch` **없이** 낸 보고를 `gateReport`로 가리키는 처분 | `launch-disposition`이 **여전히 mismatch로 막는다** |
| 3 | `--launch LAUNCH-Y`인데 처분은 `LAUNCH-X` | **막는다** |
| 4 | `--launch LAUNCH-X`이고 처분도 `LAUNCH-X` (`importCommit` 정합) | **위반 0** |
| 5 | 소스를 바이너리보다 새롭게 하고 `--launch LAUNCH-X` | **exit 2 · `NOT-MEASURED` · 보고에 `launchId`** |
| 6 | `--gate POST-COMMIT` (인자 없음) | **판정·exit가 이전과 동일** |

**시험 2가 이 지시서의 핵심이다.** 귀속을 더하면서 *"없으면 아무거나 맞는다"*로 만들면
`DISPO-01` 시험 4가 막던 거짓(다른 발사의 보고를 갖다 붙이기)이 다시 열린다.
**더하는 쪽이 아니라 막는 쪽을 먼저 확인해라.**

**시험 4는 실제로 통과까지 봐야 한다.** 픽스처 루트를 만들어
`launch-disposition <그 루트>` → **exit 0**을 실측해라. 보고만 만들고 끝내지 마라.

**시험 5를 빼지 마라.** 못 잰 기록일수록 대상이 적혀 있어야 한다.

픽스처는 `docs/qa/gate-witness/launch-disposition/` 아래 **case-18 이후 새 디렉터리**에 만들어라.
**case-01 ~ case-17은 건드리지 마라** — `case-01`은 매니페스트에 등재된 반증 witness다.

## 5. 착륙

`context-pack-integrity`를 돌려 stale해진 pin이 있는지 확인하라.
**소유 지시서를 가정하지 마라 — stale로 지목된 경로를 grep해서 실제 소유자를 찾고,
그 경로가 `requiredInputs`인지 `readOrder`인지까지 보아라.** `readOrder`에는 sha가 없어
stale해지지 않는다. 2026-07-26에 이 구분을 안 해서 한 번 틀렸다.

## 6. 후속 (이 지시서가 하지 않는다)

이것이 끝나면 **두 러너 모두 처분 증거를 낼 수 있다.** 그때 `ADR-016` §8의 정본 결정을
사람이 다시 볼 수 있다 — 지금은 "권위는 A인데 기록은 B만 가능"이라 결정 자체가 실행 불가였다.

## 허용 파일 (allowlist)

- server/Harness/DiCompletionCheckCli.cs
- docs/qa/gate-witness/**

> 검증 문서는 영역 밖이라 조율자가 쓴다.
