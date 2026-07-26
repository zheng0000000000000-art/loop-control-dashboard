```context-pack
{
  "diId": "TERR-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/handoff/decisions/ADR-002-harness-ownership-split.md", "sha256": "073dfc389a03d18592690c25d2f54a1ad73a3c9f82e0fc2cd7aea63df2f175ad" },
    { "path": "docs/verification/negative-case-count-measured.md", "sha256": "363d47ee69d308dfccf9a966c4b1212fa59932253829581ec2bb3d7bce5a188a" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/directives/TERR-01-territory-check.md",
    "docs/handoff/decisions/ADR-002-harness-ownership-split.md"
  ],
  "forbiddenActions": ["git commit", "git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

# TERR-01 — `territory-check`: 조율자가 코덱스 영토에 직접 쓴 것을 잡는다

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.

## 1. 왜 (실측 근거)

**영토가 한 방향으로만 강제된다.**

- 코덱스 → 밖: `CodexHarnessLauncherCli`가 **쏘기 전에** `allowedPaths`를 검사하고
  영토 밖이면 `allowed-paths-outside-codex-territory`로 요청 자체를 거절한다. **코드가 막는다.**
- 조율자 → 영토: **아무것도 막지 않는다.**

**2026-07-27 실측**: 조율자가 `server/Harness/GateWitnessCheckCli.cs`를 세 번 직접 고쳤고
(`b7bc023`, `7feeb44` 및 그 전) 게이트는 전부 초록이었다. ADR-002는 **규칙일 뿐 코드가 아니다.**

지금은 코덱스가 안 돌아 충돌이 없다. **병행을 켜는 순간 이 구멍이 첫 사고 지점이 된다** —
코덱스의 `candidate.patch`는 baseline 커밋 기준이라, 그 사이 조율자가 같은 파일을 고치면 안 붙는다.

## 2. 무엇을 만드나

`territory-check` 하네스. **HEAD 커밋 하나만 본다.**

```
territory-check [--commit <ish>] [--ledger <path>]
```

기본값: `--commit HEAD`, `--ledger docs/handoff/TERRITORY-EXCEPTIONS.json`.

### 2-1. 판정

그 커밋이 바꾼 파일(`git diff-tree --no-commit-id --name-only -r <commit>`) 중 **코덱스 영토**에
속한 경로마다:

1. 추적되는 `outbox/*/candidate.patch` 중 그 경로를 담은 것이 있으면 → **반입**이므로 통과.
2. 없으면 ledger에 그 **커밋 sha**가 사유와 함께 등재돼 있어야 통과.
3. 둘 다 아니면 **위반**.

추가로 **ledger에 저장소에 없는 sha가 있으면 그 자체로 실패**다(`stale-exception`).
`CALLSITE-HISTORICAL.json`과 같은 규칙이다 — 낡은 면제를 남겨두면 나중에 조용히 면제된다.

### 2-2. 영토 목록은 새로 쓰지 마라

**`server/CodexTerritory.Roots`가 정본이다.** 같은 어셈블리라 `internal`로 바로 쓸 수 있고,
경로 판정은 `CodexTerritory.Contains(path)`가 한다. **새로 배열을 선언하지 마라** —
목록이 두 벌이면 한쪽만 고쳐질 때 쏘는 문(`codex-launch`)과 잡는 문(`territory-check`)이
다른 답을 낸다. `RequiredGateCommands`·`SelfTestGateCounts`에서 이미 두 번 겪었다.

이 파일은 **조율자 영역(server/ 루트)에 일부러 뒀다** — 경계를 긋는 것은 경계를 지키는 쪽의
일이 아니다. `server/Harness/`에 두면 코덱스가 자기 영토를 스스로 넓힐 수 있다. 읽기만 하라.

### 2-3. HEAD만 보는 이유 — 범위 전체를 훑지 마라

`main..HEAD` 전체를 훑으면 **2026-07-27의 직접 수정 커밋들 때문에 게이트가 영구 적색**이 된다.
**영구히 빨간 게이트는 무시된다**(FAIL-2026-010). POST-COMMIT은 커밋 직후에 도니까,
HEAD만 봐도 새 위반은 **그 다음 게이트에서 즉시** 잡힌다.

### 2-4. 옵션 검증

`CliOptions.Validate`를 쓴다. 오타 옵션이 조용히 무시되면 안 된다 —
`--manifestt`가 fixture 대신 production을 재고 exit 0을 낸 전례가 있다.

### 2-5. 출력

```json
{"harness":"territory-check","commit":"<sha>","territoryPaths":[],"coveredByOutbox":[],
 "exempted":[],"staleExceptions":[],"violations":0,"violationPaths":[]}
```

exit **0**=위반 없음, **1**=위반 또는 stale 면제, **2**=입력 오류.

## 3. 반증 시험 — 물리지 않은 픽스처는 시험이 아니다

`jsonlines-state-15.json`이 낡은 값 `20`을 달고도 아무 일 없었던 이유가 **어느 게이트에도
물려 있지 않아서**였다(2026-07-27). 그래서 아래 세 조합은 반드시 POST-COMMIT에 등재된다 —
**등재는 조율자가 반입 때 한다**(매니페스트가 영토 밖이라 네가 못 쓴다, §5).
**네 몫은 기대값을 실측해 보고에 남기는 것이다.** 조율자는 네가 적은 숫자를 그대로 옮긴다.
안 재고 적으면 검증되지 않은 기대값이 게이트에 박힌다.

| args | expectedExit | 무엇을 반증하나 |
| --- | --- | --- |
| (없음) | 0 | 정상 HEAD는 통과한다 |
| `--commit 7feeb44` | 1 | 조율자 직접 수정 커밋을 잡는다(outbox 근거 없음) |
| `--ledger docs/qa/gate-witness/territory-stale-ledger.json --commit 7feeb44` | 1 | 저장소에 없는 sha가 면제 목록에 있으면 실패한다 |

`7feeb44`는 **영구히 존재하는 커밋**이라 기대값이 흔들리지 않는다.
세 번째 픽스처는 **없는 sha 하나**만 담으면 된다.

**`--commit 7feeb44`가 정말 1을 내는지 먼저 실측하고 등재하라.** 기대값을 추정으로 적지 마라.

## 4. ledger 초기 상태

`docs/handoff/TERRITORY-EXCEPTIONS.json`을 **빈 목록**으로 만든다.

```json
{
  "_comment": "조율자가 코덱스 영토에 직접 쓴 커밋의 사람 승인 목록. sha + reason. 저장소에 없는 sha가 남으면 territory-check가 실패한다.",
  "exceptions": []
}
```

2026-07-27 이전의 직접 수정은 여기 넣지 마라 — `BASELINE-CHANGES.md`와 verification 문서에
이미 기록돼 있고, HEAD만 보므로 판정 대상이 아니다.

## 5. 허용 파일 (allowlist)

- server/Harness/TerritoryCheckCli.cs
- server/Harness/HarnessRegistry.cs
- docs/qa/gate-witness/territory-stale-ledger.json
- docs/qa/gate-witness/TERR-01.md

**전부 코덱스 영토 안이다.** 이건 우연이 아니라 제약이다 — `codex-launch`가 영토 밖 경로가
들어간 요청을 **쏘기 전에** `allowed-paths-outside-codex-territory`로 거절한다.

**조율자가 반입 시점에 하는 것**(네가 만들지 마라):

- `docs/handoff/GATE-MANIFEST.json` 배선 — §3의 검사 3개 등재
- `docs/handoff/TERRITORY-EXCEPTIONS.json` 초기 생성 (§4)
- `docs/verification/terr-01.md`

**대신 `docs/qa/gate-witness/TERR-01.md`에 네 작업 보고를 남겨라** — 주체·하네스 결과·참조
스킬·자진 신고. 조율자가 그것을 근거로 verification 문서를 만든다.
**매니페스트에 못 넣는다고 기대값을 안 재도 된다는 뜻이 아니다.** §3의 세 조합을 직접 돌려
exit code를 보고에 적어라. 조율자는 그 숫자를 그대로 등재한다.

## 6. 검수 기준

지표 기준(기계 판정). `_header.md`의 공통 항목에 아래를 더한다.

- [ ] `build-verify` exit 0
- [ ] `verify-behavior` → `behaviorEqual: true`
- [ ] `measure dev-pack` **violations 0** (제출 전 실행. 함수 길이·한국어 주석 누락이 자주 걸린다)
- [ ] `scope-check` — 변경 파일이 위 allowlist 안
- [ ] `territory-check` (인자 없음) exit **0**
- [ ] `territory-check --commit 7feeb44` exit **1**
- [ ] stale ledger 픽스처로 exit **1**
- [ ] §3 세 조합의 exit code를 실제로 재서 보고에 적었다 (매니페스트 등재는 조율자 몫)
- [ ] `gate-witness-check` exit 0
- [ ] `docs/qa/gate-witness/TERR-01.md`에 주체·하네스 결과·참조 스킬·자진 신고 기록

### 목적 기준 (사람 판정)

- **영토 목록이 한 벌인가.** 두 곳에 상수가 있으면 미달이다.
- **위반을 잡는 것을 실제로 보였는가.** `--commit 7feeb44`가 1을 낸 실측 출력이 verification에 있어야 한다.
- **영구 적색이 아닌가.** 정상 HEAD에서 0이어야 하고, 과거 커밋 때문에 막히면 안 된다.

## 7. 작업 보고 (`docs/qa/gate-witness/TERR-01.md`)에 반드시 적을 것

1. **주체** 2. **사용한 하네스와 결과**(명령·exit code·핵심 수치) 3. **참조한 스킬**
4. **`## 지표는 만족했으나 목적은 미달인 부분`** — 없으면 "없음"과 근거.

게이트 결과는 JSON 한 줄로: `{"gate":"dev-pack","violations":0,"attempt":1}`
