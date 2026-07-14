```context-pack
{
  "diId": "06C-2-R1",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/plan/wp/TRUST-ORIGIN-BOOTSTRAP.md", "sha256": "fae8ff3b6b649019409c16762b137010e74c8118234ff0bcbae36d610ab41703" },
    { "path": "docs/handoff/queue/directive-06C-2-trust-origin.md", "sha256": "20963f4abce40d1aa69e8501bca4365a8e8763db986f1b236f973faf2b50a4c9" },
    { "path": "docs/verification/06c2-trust-origin.md", "sha256": "4ebd52d8cad7aa9aad85c044b03aa88101cd23d8af70a3fb320a48bbcee103b4" },
    { "path": "outputs/review/06C-2.codex.md", "sha256": "f889264e07a0f8885df49fcefc5535dff39dac83695978fd5f69dfed83c87c42" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "outputs/review/06C-2.codex.md",
    "docs/plan/wp/TRUST-ORIGIN-BOOTSTRAP.md",
    "docs/handoff/queue/directive-06C-2-trust-origin.md",
    "docs/handoff/queue/directive-06C-2-R1-trust-origin-production-truth.md",
    "docs/directives/_header.md"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline", "create-trust-origin-record-in-canonical"]
}
```

# 06C-2-R1 — trust-origin production truth

이 지시서는 docs/directives/_header.md의 불변 제약을 따른다.

- actor: CORE_INFRA_EXECUTOR (sonnet)
- 배경: 06C-2는 구현됐지만 독립 Codex 검수에서 FAIL로 정정됐다.
- 목표: `trust-origin declare`가 **검증하지 않은 것을 VERIFIED라고 기록하지 못하게** 한다.

## 왜 반려됐는가

`outputs/review/06C-2.codex.md`와 `outputs/reviewer-log.md`의 06C-2 정정 판정이 정본이다.

반려 조건:

1. production 경로에서 선행조건 1(build), 9(자동 launcher 비활성), 10(high-risk fail-closed)을 실제로 검사하지 않는다.
   그런데 record에는 `buildVerdict = "VERIFIED_PASS"`를 쓴다.
2. `knownExceptions[]`가 정확한 집합이어야 하는데 현재 구현은 `failureSubjects ⊆ knownSubjects`만 본다.
   가짜 extra exception을 넣어도 통과한다.
3. record에 `phaseChangeReady: false`, `replayReady: false`가 빠져 있다.
4. `--self-test`가 `PreconOverride` 주입 경로만 주로 보므로 production 경로의 거짓 VERIFIED를 잡지 못한다.

## 고칠 것

### A. production 선행조건을 실제로 검사하라

`trust-origin declare` production 경로에서 선행조건 1·9·10을 실제로 확인하라.

- 선행조건 1: clean clone/worktree에서 build exit 0.
  - 최소 구현은 현재 repo root에서 `dotnet build server -c Release -nologo`를 실행하고 exit 0을 확인한다.
  - build를 실행하지 못하면 선언을 거부하거나 record에 `VERIFIED_PASS`를 쓰지 마라.
- 선행조건 9: 자동 launcher 비활성 확인.
  - 확인할 실체가 없으면 `NOT_VERIFIED`가 아니라 **선언 거부**가 기본이다.
  - "production 기본값 true" 금지.
- 선행조건 10: high-risk transition fail-closed 확인.
  - `state-transition`에서 `PHASE_CHANGE`/`RECOVERY`/`REPLAY`가 `trusted-human-receipt-required`로 거부되는지 self-test 또는 직접 fixture로 확인한다.
  - 확인할 수 없으면 선언 거부.

기록 원칙:

- 실제로 확인한 것만 `VERIFIED_PASS`.
- 모르면 `VERIFIED_PASS` 금지.
- 이 명령은 신뢰 원점 record를 만드는 도구다. record가 실체와 다르면 그 자체가 실패다.

### B. `knownExceptions[]`를 정확한 집합으로 검사하라

현재 부분집합 검사:

```text
failureSubjects.Except(knownSubjects).Count == 0
```

이것은 부족하다. 아래로 바꿔라.

```text
failureSubjects == knownSubjects
```

거부해야 하는 두 경우:

- failure에는 있는데 knownExceptions에 없는 subject: unlisted failure
- knownExceptions에는 있는데 현재 failure에 없는 subject: extra known exception

오류 출력에는 최소한 `unlistedSubjects`와 `extraKnownSubjects`를 분리해서 담아라.

### C. record ready flag를 완성하라

record에 아래 필드를 명시적으로 넣어라.

```json
"phaseChangeReady": false,
"replayReady": false
```

기존 false flag도 유지한다.

```json
"verifiedHumanApprovalReady": false,
"recoveryApplyReady": false,
"automatedExecutionReady": false
```

### D. self-test가 production 경로의 거짓말을 잡게 하라

`trust-origin --self-test`에 최소 case를 추가하라.

| case | 기대 |
| --- | --- |
| `extra-known-exception` | 현재 failure보다 knownExceptions가 많으면 exit 1 |
| `production-preconditions-not-default-true` | production 경로가 build/launcher/high-risk를 기본 true로 통과시키지 못함 |
| `record-ready-flags-complete` | record에 `phaseChangeReady:false`, `replayReady:false` 존재 |
| `build-verdict-not-forged` | build를 실제 확인하지 않은 경로는 `VERIFIED_PASS` 기록 불가 |

가능하면 self-test 안에서 production과 같은 precondition path를 통과하는 case를 만들어라.
`PreconOverride`를 쓰는 case는 유지해도 되지만, 그것만으로 완료 판정하지 마라.

## 완료 기준

아래는 전부 실행 검증 문서에 명령·기대 exit·실제 exit·핵심 출력을 남긴다.

1. `dotnet build server -c Release -nologo` → exit 0
2. `dotnet run --project server -c Release -- trust-origin --self-test` → exit 0
3. `extra-known-exception` 반증 → 선언 거부, `extraKnownSubjects` 출력
4. production 경로에서 build/launcher/high-risk가 검증되지 않으면 record 생성 거부
5. 정상 self-test record에 `phaseChangeReady:false`, `replayReady:false` 존재
6. `trust-origin` 인수 없음 → exit 2 usage
7. canonical 저장소에 `docs/handoff/trust-origin/TO-2026-001.json` 생성 없음
8. `WORKSTATE.json`·`WORKSTATE.applier-log.jsonl` blob이 HEAD와 동일
9. `dotnet run --project server -c Release -- measure dev-pack` → violations 0

## 허용 파일 (allowlist)

- server/TrustOriginCli.cs
- server/Cli/CliRouter.cs
- docs/verification/06c2-r1-trust-origin-production-truth.md

## 금지

- canonical 저장소에 trust-origin record 생성 금지.
- `WORKSTATE.json`·`WORKSTATE.applier-log.jsonl` 수정 금지.
- `TRUST-ORIGIN-BOOTSTRAP.md` 원문 수정 금지.
- 기준 파일·측정 코드 수정 금지.
- `state-transition apply --bootstrap` 같은 우회 경로 추가 금지.
- git commit/push/tag 금지.
- approve/reject/import 금지.

## 보고

`docs/verification/06c2-r1-trust-origin-production-truth.md`를 `docs/verification/_template.md` 형식으로 작성한다.

반드시 적을 것:

- 06C-2 FAIL 원인 3개가 각각 어떻게 닫혔는지.
- extra known exception 반증의 실제 출력.
- production 경로가 `VERIFIED_PASS`를 위조하지 못한다는 실제 근거.
- canonical record 미생성과 WORKSTATE/applier-log 무결성 확인.
- `## 지표는 만족했으나 목적은 미달인 부분`.

못 한 시험은 `NOT_VERIFIED`라고 적는다. 숨기지 마라.
