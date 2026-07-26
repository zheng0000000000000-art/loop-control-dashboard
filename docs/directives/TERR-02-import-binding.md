```context-pack
{
  "diId": "TERR-02",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/terr-01.md", "sha256": "f7f16eaa5e9791a3985410cbcf92db5e9d85a9d6d4c10ce1cc860bd12927ee11" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/directives/TERR-02-import-binding.md",
    "docs/verification/terr-01.md"
  ],
  "forbiddenActions": ["git commit", "git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

# TERR-02 — 면제를 **경로**가 아니라 **그 커밋의 반입**에 묶는다

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.

## 1. 왜 — TERR-01 지시서(내가 쓴 것)의 결함이다

TERR-01의 면제 규칙은 이렇게 썼다:

> 추적되는 `outbox/*/candidate.patch` 중 그 경로를 담은 것이 있으면 → **반입**이므로 통과.

**경로 기준이다. 커밋과 아무 관계가 없다.** 실측(2026-07-27, `--commit 7feeb44`):

```
docs/qa/gate-witness/nested-counter-output.json   위반 ✓
docs/qa/gate-witness/nested-counter.json          위반 ✓
server/Harness/GateWitnessCheckCli.cs             coveredByOutbox — 면제됨
```

면제 근거가 **`LAUNCH-GWIT-01-R2`·`LAUNCH-GWIT-06`** — `7feeb44`와 **아무 상관 없는 예전
발사들**이다. `7feeb44`는 조율자가 직접 고친 커밋인데 잡히지 않았다.

**결과: 코덱스가 한 번이라도 만진 파일은 그 뒤로 조율자가 영원히 자유롭게 고칠 수 있다.**
하네스는 정확히 지시서대로 만들어졌다 — **고칠 것은 규칙이다.**

## 2. 새 규칙 — 그 커밋이 반입한 것만 면제된다

영토 경로 `P`가 커밋 `X`에서 바뀌었을 때, 다음 중 **하나라도** 참이면 면제한다.

**(가) 자기 증명** — `X`가 **추가한** `outbox/*/candidate.patch`가 `P`를 담고 있다.
   `git diff-tree`에서 그 패치 파일이 `A`(added)로 나와야 한다. **이미 있던 패치는 근거가 아니다.**

**(나) 처분 결속** — `state`가 `imported`이고 `importCommit == X`인 `disposition.json`이 있고,
   같은 디렉터리의 `candidate.patch`가 `P`를 담고 있다.

**(다) 사람 승인** — `TERRITORY-EXCEPTIONS.json`에 `X`가 사유와 함께 있다 (TERR-01 그대로).

### 왜 (가)와 (나) 둘 다인가

반입은 **두 커밋에 걸친다.** `importCommit`을 미리 알 수 없어서, 반입 커밋에서는 처분이 아직
`pending`이고 다음 커밋에서 `imported`로 기록된다(TERR-01 실측). 그래서 반입 커밋 자신은
(나)로 증명할 수 없고 **(가)로만 증명된다.** 반대로 패치를 나중에 다시 붙이는 커밋은 (나)로 잡힌다.

**(가)만 두면 재적용 커밋이 막히고, (나)만 두면 반입 커밋 자신이 막힌다.** 둘 다 필요하다.

### 거부된 발사는 근거가 아니다

`state`가 `imported`가 아닌 처분(`pending`·`rejected`·`discarded` 등)의 패치는 **(나)의 근거로
쓰지 마라.** 반려된 산출물이 조율자의 직접 수정을 면제해 주면 안 된다.
**(가)에는 이 조건을 걸지 마라** — 반입 커밋 시점의 처분은 정의상 아직 `pending`이다.

## 3. 반증 시험 — 이 수정이 무엇을 바꾸는지 보여야 한다

**핵심 반증은 이것 하나다.**

```
territory-check --commit 7feeb44d22039f3d92f934ca2dc6ba69bc0dd8f5
```

| | violations | violationPaths |
| --- | --- | --- |
| 수정 전(TERR-01) | **2** | nested-counter*.json |
| 수정 후(기대) | **3** | + `server/Harness/GateWitnessCheckCli.cs` |

exit code는 양쪽 다 1이라 **exit code만으로는 이 수정을 증명할 수 없다.**
`violations` 수와 `violationPaths` 내용을 보고에 그대로 적어라.

**그 외에 반드시 확인할 것** (전부 실측해 exit code와 수치를 보고에 적어라):

1. `territory-check`(인자 없음)가 **HEAD에서 exit 0**. 반입 커밋이 (가)로 통과하는지가 여기서 갈린다.
   **이게 1이면 반입 자체가 막힌다 — 그 상태로 제출하지 마라.**
2. `territory-check --commit <TERR-01 반입 커밋>` → **exit 0**. 실측으로 sha를 찾아 적어라
   (`outbox/codex-launch-LAUNCH-TERR-01/disposition.json`의 `importCommit`).
   이것이 (가) 경로가 살아 있다는 증거다.
3. `territory-check --commit <TERR-01 처분 기록 커밋>` → 그 커밋은 영토를 안 건드리므로 **exit 0**.
4. stale ledger 픽스처 → **exit 1** (TERR-01에서 이미 있다. 회귀 없음 확인).
5. 오타 옵션 → **exit 2**.

**새 반증 픽스처를 하나 더 만들어라**: `state`가 `imported`가 아닌 처분의 패치가 면제 근거로
쓰이지 않는지 보이는 것. 만들 수 없으면 **왜 못 만드는지 보고에 적어라** — 만들었다고 하지 마라.

## 4. 하지 마라

- **`territory-check`의 판정 범위를 넓히지 마라.** HEAD 커밋 하나만 본다(TERR-01 §2-3).
  범위 전체를 훑으면 과거 커밋 때문에 영구 적색이 되고, 영구히 빨간 게이트는 무시된다(FAIL-2026-010).
- **영토 목록을 새로 선언하지 마라.** `CodexTerritory.Contains`가 정본이다.
- **`TERRITORY-EXCEPTIONS.json`에 항목을 넣지 마라.** 면제 등재는 사람 결재다.
- **매니페스트를 고치지 마라.** 영토 밖이라 못 쓴다 — 조율자가 반입 때 한다.
  order 20의 note에 적힌 `violations 2`가 3으로 바뀌어야 하는데, **그 숫자를 보고에 적어라.**

## 5. 허용 파일 (allowlist)

- server/Harness/TerritoryCheckCli.cs
- docs/qa/gate-witness/**

**전부 코덱스 영토 안이다.** `codex-launch`가 영토 밖 경로가 든 요청을 **쏘기 전에**
`allowed-paths-outside-codex-territory`로 거절한다.

**조율자가 반입 시점에 하는 것**(네가 만들지 마라): `GATE-MANIFEST.json` note 갱신,
`docs/verification/terr-02.md`.

작업 보고는 **`docs/qa/gate-witness/TERR-02.md`**에 남겨라.

## 6. 검수 기준

지표 기준(기계 판정). `_header.md`의 공통 항목에 아래를 더한다.

- [ ] `build-verify` exit 0
- [ ] `verify-behavior` → `behaviorEqual: true`
- [ ] `measure dev-pack` **violations 0**
- [ ] `scope-check` — 변경 파일이 위 allowlist 안
- [ ] `territory-check --commit 7feeb44d22039f3d92f934ca2dc6ba69bc0dd8f5` → exit 1이고
      **`violations` 3**, `violationPaths`에 `server/Harness/GateWitnessCheckCli.cs` 포함
- [ ] `territory-check`(인자 없음) HEAD에서 **exit 0**
- [ ] TERR-01 반입 커밋에서 **exit 0** (sha를 실측해 보고에 적었다)
- [ ] stale ledger 픽스처 **exit 1**, 오타 옵션 **exit 2** (회귀 없음)
- [ ] `gate-witness-check` exit 0
- [ ] `docs/qa/gate-witness/TERR-02.md`에 주체·하네스 결과·참조 스킬·자진 신고 기록

### 목적 기준 (사람 판정)

- **면제가 커밋에 묶였는가.** 예전 발사의 패치가 무관한 커밋을 면제하면 미달이다.
- **반입 경로가 살아 있는가.** 정상 반입 커밋이 막히면 이 하네스는 못 쓴다 —
  **막는 것보다 어려운 것은 옳게 통과시키는 것이다.**
- **거부된 발사가 근거로 쓰이지 않는가.**

## 7. 작업 보고 (`docs/qa/gate-witness/TERR-02.md`)에 반드시 적을 것

1. **주체** 2. **사용한 하네스와 결과**(명령·exit code·**violations 수치**) 3. **참조한 스킬**
4. **`## 지표는 만족했으나 목적은 미달인 부분`** — 없으면 "없음"과 근거.

게이트 결과는 JSON 한 줄로: `{"gate":"dev-pack","violations":0,"attempt":1}`
