# GWIT-03 검증 — `GateCleanCli.Run` 길이 회귀 해소

- **주체(actor)**: 산출은 **코덱스**(`LAUNCH-GWIT-03`, 1파일·scope 위반 0).
  검증·반입 집행은 **조율 세션**, 결재는 **사람**.
- **날짜**: 2026-07-26 · **출처**: `GWIT-02` 반입(`fb086f3`)이 만든 회귀

## 지표 (실측)

| 항목 | 이전 | 이후 |
| --- | --- | --- |
| `measure dev-pack` | **violationCount 1** | **0** ✅ |
| `maxFunctionLength` | **85** (`GateCleanCli.cs:19-103`) | **80** (`dashboard/app.js:535-614`) ✅ |
| `functionsWithoutComment` | 0 | **0** ✅ |
| `verify-behavior` | — | **exit 0** (동작 보존) ✅ |

한도가 `[0, 80]`이므로 80은 통과다. 최장 함수가 `GateCleanCli.cs`에서 `app.js`로 옮겨간 것은
**이 파일이 더 이상 최장이 아니라는 뜻**이다.

## 반증 시험 (지시서 §4)

| # | 시험 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1 | dirty 픽스처 | 1 | **1** ✅ |
| 2 | **이름 그대로, 내용만 비움** | **0** | **0** ✅ (되돌리니 다시 **1**) |
| 3 | 없는 픽스처 경로 | 2 | **2** ✅ |
| 5 | 더러운 트리에서 인자 없이 | 1 | **1** ✅ |

**시험 2가 이 지시서의 회귀 감시 핵심이었다.** `GWIT-02`의 목적은 *"이름이 아니라 내용을 본다"*였고,
길이를 줄이려 분해하다 그 성질을 잃으면 **숫자만 맞고 witness가 거짓이 된다.** 유지됐다.

시험 4(깨끗한 트리에서 인자 없이 → 0)는 **하지 않았다.** 반입 직후라 트리가 더러웠다.
`GWIT-02` 검증에서도 같은 이유로 미룬 항목이며, **여전히 미실시로 남는다.**

## 다른 게이트

`context-pack-integrity` 0(stale 없음) · `gate-witness-check` 0 · `handoff-integrity` 0 ·
`doc-integrity` 0 · `build-verify` 오류 0.

착륙은 **한 걸음으로 끝났다** — 이 파일을 pin한 지시서가 없어 ②단계가 필요 없었다.
(가정하지 않고 `context-pack-integrity`로 확인했다.)

## 지표는 만족했으나 목적은 미달인 부분

1. **분해가 실제로 읽기 쉬워졌는지는 사람 판정이다.** 지시서 목적 기준이 *"이름이 그 함수가
   하는 일을 말해야 한다"*였는데, 조율자는 **지표(80 이하)와 동작 보존까지만 확인했고
   이름의 적절성은 판정하지 않았다.** 코드 검토는 사람 몫으로 남는다.
2. **시험 4 미실시.** 깨끗한 트리에서의 회귀 확인은 두 지시서 연속으로 미뤄졌다.
3. 이 회귀는 `GWIT-02` 반입 때 **측정을 먼저 돌렸으면 반입 전에 잡혔다.** 조율자가 측정과
   커밋을 한 명령에 묶어 결과를 보기 전에 커밋한 것이 원인이며 `9159d3f`에 정정했다.

---

## 정정 (같은 세션) — 시험 4를 실측했다

위에 *"시험 4는 하지 않았다"*고 적었으나, 반입을 커밋해 트리가 깨끗해진 직후 실측했다.
**두 지시서 연속으로 미뤘던 항목이 닫혔다.**

```
git status --porcelain      0줄 (clean)
gate-clean                  exit 0   ✅ 기대 0
gate-clean server           exit 0   ✅ POST-COMMIT 형태
```

**production 판정에 회귀가 없다**는 것이 이로써 확인됐다. `--status-fixture` 분기를 얹고
함수를 분해했는데도 인자 없는 경로가 그대로다.

### POST-COMMIT 게이트 전체 (깨끗한 트리)

```
di-completion-check --gate POST-COMMIT
  verdict: PASS   checks: 12   failures: []   exit 0
```

**반증 witness 5건이 포함된 게이트가 전부 통과한다.** 이 게이트는 이제
`requireFailureWitness: true`이고 반증 없는 검사가 0건이며, `gate-witness-check` 자신도
등재돼 자기 witness를 갖는다.

이 세션에서 POST-COMMIT이 지나온 길:

| 시점 | 검사 | 반증 없음 |
| --- | --- | --- |
| 세션 시작 | 5 | 4 |
| `handoff-integrity` witness 2건 + 플래그 켬 | 7 | 3 |
| `context-pack-integrity` witness | 8 | 2 |
| `GWIT-02` witness 2건 (`gate-clean`·`doc-integrity`) | 10 | **0** |
| `gate-witness-check` + 자기 witness 등재 | **12** | **0** |
