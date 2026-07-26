```context-pack
{
  "diId": "DICC-03",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-DICC-03-baseline-attribution.md",
    "docs/handoff/queue/directive-DICC-02-launch-attribution.md",
    "server/Harness/DiCompletionCheckCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# DICC-03 — 보고가 **어느 커밋을** 잰 것인지도 말하게 한다

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 선행: `DICC-02`(발사 귀속). **이 지시서는 그것의 미완성을 메운다.**

---

## 0. 문제 — `DICC-02`는 절반만 고쳤다 (실측 2026-07-26)

`DICC-02`로 `launchId`가 실렸다. 그래서 *"이제 두 러너 모두 처분 증거를 낼 수 있다"*고
적었는데 **거짓이었다.** 실제로 써 보니:

```
launch-disposition outbox → LAUNCH-DICC-02 -> gate-report-baseline-invalid
di-completion-check 보고 키: ... launchId 있음 ... baselineCommit 없음
```

`launch-disposition`은 **두 가지**를 본다(`LaunchDispositionCli.cs:160-165`):

```
baselineCommit이 40자리 hex인가            → 아니면 gate-report-baseline-invalid
merge-base --is-ancestor importCommit baselineCommit  → 아니면 gate-report-predates-import
```

**`DICC-02` 지시서가 앞의 것만 요구했다.** 그 규칙은 `DISPO-01` §1-A에 내가 직접 써 놓은 것인데
후속 지시서에서 빠뜨렸다. 그리고 **`launchId`가 실리는 것만 보고 결론을 냈다** —
끝까지 써 보지 않고 중간 지표로 단정했다.

## 1. 무엇을 하는가

`di-completion-check` 보고에 두 필드를 더한다.

- **`baselineCommit`** — 측정 시작 시점 `HEAD`의 **40자리 전체 커밋 id**.
  짧은 sha는 `IsCommitId`가 거부한다(`LaunchDispositionCli.cs:193`).
- **`worktreeCleanAtStart`** — 측정 시작 시점 트리가 깨끗했는지.

## 1-A. `worktreeCleanAtStart`를 빼지 마라

`baselineCommit`만 적으면 **더러운 트리에서 잰 판정에 깨끗한 커밋 해시만 붙는다.**
그 보고는 *"이 커밋을 쟀다"*고 말하지만 사실이 아니다.
`program-verify`가 같은 이유로 그 필드를 남긴다(`ProgramVerifierCli.cs`의 주석 참조).
**둘은 한 쌍이다.**

## 1-B. 측정 **시작 시점**에 잡아라

검사 중 `measure`가 산출물을 바꾸므로 끝난 뒤에 재면 언제나 더럽다.
낡음 거부(exit 2, `NOT-MEASURED`) 경로에도 실어야 한다 — **못 잰 기록일수록 대상이 적혀야 한다.**

## 2. 하지 않을 일 (하면 반려)

- 판정 논리(`gateVerdict`·`failures`·exit code) 변경. **귀속만 더한다.**
- 짧은 sha·태그·브랜치 이름을 `baselineCommit`에 적는 것.
- 커밋 id를 얻지 못했을 때 **빈 문자열이나 0으로 채우는 것.** 모르면 비워 두고,
  그 보고는 `gateReport`로 쓸 수 없는 것이 맞다. **지어내지 마라.**
- `launch-disposition`·`program-verify`·`GATE-MANIFEST.json` 수정 — 영역 밖.
- `outbox/**` 처분 파일 수정 — **처분은 사람 결재다.**

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `di-completion-check --gate POST-COMMIT --launch L --task L` 보고에
      `baselineCommit`(40 hex) · `worktreeCleanAtStart`가 있다
- [ ] `baselineCommit`이 그 시점 `git rev-parse HEAD`와 **문자열이 같다**
- [ ] §4의 6개 시험이 전부 기대값
- [ ] `build-verify` **exit 0**

### 목적 기준 (사람 판정)

**"`di-completion-check`가 낸 보고를 그대로 `gateReport`로 써서 `launch-disposition`이 통과한다."**

## 4. 반증 시험 (전부 실측)

### ★ 시험 1은 **픽스처로 하지 마라. 실제 러너가 낸 보고로 해라.**

`DICC-02`가 실패한 지점이 정확히 여기다. 시험은 **내가 만든 이상적 픽스처**로 돌았고
**러너가 실제로 낸 보고로는 안 돌았다.** 픽스처는 통과했고 실물은 실패했다.

```
1) di-completion-check --gate POST-COMMIT --launch LAUNCH-PROBE --task LAUNCH-PROBE
2) 그 보고 파일 경로를 gateReport로 적은 처분을 임시 루트에 만든다
   (importCommit은 실제 조상 커밋이어야 한다 — merge-base로 확인된다)
3) launch-disposition <그 임시 루트>  →  exit 0
```

**`outputs/*`는 gitignore되므로 그 보고는 커밋되지 않는다.** 이 시험은 **살아 있는 실행**으로
하고 명령·출력을 보고에 그대로 붙여라. 픽스처로 대체한 것은 통과로 치지 않는다.

| # | 시험 | 기대 |
| --- | --- | --- |
| **1** | **위 3단계 실물 왕복** | **exit 0** |
| 2 | 보고의 `baselineCommit` vs `git rev-parse HEAD` | **문자열 일치** |
| 3 | 더러운 트리에서 측정 | `worktreeCleanAtStart: false`, `baselineCommit`은 그대로 실림 |
| 4 | 낡은 바이너리 | exit 2 · `NOT-MEASURED` · `launchId`·`baselineCommit` 실림 · 검사 0개 |
| 5 | `--launch` 없이 | 판정·exit 이전과 동일, `baselineCommit`은 여전히 실림 |
| 6 | 기존 `case-01`~`case-20` | 이전과 같은 exit·사유 |

**시험 5를 빼지 마라.** `baselineCommit`은 발사와 무관하게 *"무엇을 쟀는가"*이므로
`--launch` 유무와 독립이어야 한다.

새 픽스처가 필요하면 `case-21` 이후에 만들어라. **`case-01`~`case-20`은 건드리지 마라** —
`case-01`은 매니페스트에 등재된 반증 witness다.

## 5. 착륙

`context-pack-integrity`를 돌려 stale해진 pin이 있는지 확인하라.
**소유 지시서를 가정하지 마라 — stale로 지목된 경로를 grep해서 실제 소유자를 찾고,
그 경로가 `requiredInputs`인지 `readOrder`인지까지 보아라.**

## 6. 이 지시서가 다루지 않는 것

**처분이 가리키는 `gateReport` 17건이 전부 커밋되지 않았다**(`.gitignore:20` `outputs/*`).
새로 클론하면 `gate-report-not-found`가 17건 난다. **증거의 보관 위치 문제**이고
`.gitignore`·경로 결정은 **사람 결재**다. `HUMAN-INBOX`에 올렸다.

## 허용 파일 (allowlist)

- server/Harness/DiCompletionCheckCli.cs
- docs/qa/gate-witness/**

> 검증 문서는 영역 밖이라 조율자가 쓴다.

---

## §7 갱신 (2026-07-26, 발사 직전) — §6의 문제는 해소됐다

§6은 *"처분이 가리키는 `gateReport` 17건이 전부 커밋되지 않았다"*고 적었다. **해소됐다.**

- 증거는 이제 **`docs/handoff/gate-evidence/`**(추적됨)에 둔다. `outputs/`는 임시 자리다.
- `outbox/`의 `disposition.json`·`execution-report.json`·`candidate.patch`도 추적된다.
- 깨끗한 클론에서 `POST-COMMIT` **PASS 0/14**, `LAND` **PASS 0/18**을 실측했다.
  (`BASELINE-CHANGES.md` 2026-07-26 두 건, `docs/verification/clone-truth-check.md`)

**§4 시험 1에 영향**: 살아 있는 실행으로 하라는 요구는 그대로다.
다만 시험용 보고는 `docs/handoff/gate-evidence/`에 두지 마라 — **거기는 실제 처분이 의존하는
자리다.** 시험은 임시 경로(`outputs/gates/` 등)를 쓰고 명령·출력을 보고에 붙여라.
그 경로가 추적되지 않는다는 사실이 **시험을 무효로 만들지 않는다** — 시험은 왕복이 되는지를
보는 것이지 파일이 영구히 남는지를 보는 것이 아니다.
