# TERR-01 반입 검증 — `territory-check` (2026-07-27)

## 주체

- **구현**: 코덱스(HARNESS_EXECUTOR), `LAUNCH-TERR-01`, exit 0, 5분 58초.
- **발사 결정**: 사람. `--manual` 없이는 `automated-execution-not-ready`로 막힌다 —
  실제로 `wouldLaunch: false`를 확인하고 플래그를 붙였다.
- **검증·반입**: 조율 세션(Claude Opus 5), 사람 지시.

## 조율자가 직접 재실행했다 — 자기보고는 증거가 아니다

패치를 **격리 클론에 붙여** 네 조합을 다시 돌렸다.

| 조합 | 코덱스 보고 | 조율자 재실행 |
| --- | --- | --- |
| `territory-check` | exit 0, violations 0 | **exit 0**, territoryPaths 0 |
| `--commit 7feeb44` | exit 1, violations 2 | **exit 1**, violations 2 |
| `--ledger <stale> --commit 7feeb44` | exit 1, stale 1, violations 3 | **exit 1**, stale 1, violations 3 |
| `--committ HEAD` (오타) | exit 2 | **exit 2** `unknown-option: --committ` |

**전부 일치했다.** 목적 기준도 봤다 — `TerritoryCheckCli.cs`에 영토 배열 **재선언이 없다**.
`CodexTerritory.Contains` 하나만 쓴다. 패치는 allowlist 안 4개 파일뿐(297줄 추가).

`verify-behavior`·`gate-witness-check`·`doc-integrity`·`handoff-integrity`·`measure` 전부 exit 0.

**`build-verify`는 조율자 클론에서 exit 0이다.** 코덱스는 격리 사본에서 NuGet 인증서(`NU1301`)로
exit 1이라 보고하고 **대체 빌드로 구분해 공개했다** — 숨기지 않았고, 환경 탓이 맞았다.

## 매니페스트 배선 (POST-COMMIT order 19·20·21)

**짧은 sha가 아니라 전체 sha를 박았다** — 이력이 자라면 짧은 것은 모호해진다.
등재 전에 전체 sha로 다시 재서 세 exit code가 그대로인 것을 확인했다.

## scopeViolations는 무엇이었나

보고에 `dashboard/data/dev-pack/*` 5개가 떴다. 그런데 **`pathsMissingFromPatch`가 같은 5개**다 —
런처가 감지는 하되 **패치에서 제외**했고 `candidate.patch`에는 들어 있지 않다.
지시서가 `measure dev-pack` 실행을 요구하는데 그 명령이 영토 밖 파일을 다시 쓰기 때문이다.
코덱스는 내용을 HEAD blob과 같게 원복했고 index refresh만 못 했다고 신고했다.

## 설계 결함 — 내 것이다

`--commit 7feeb44`가 잡은 위반은 **2건**인데 그 커밋이 바꾼 영토 파일은 **3개**다.

```
docs/qa/gate-witness/nested-counter-output.json   위반 ✓
docs/qa/gate-witness/nested-counter.json          위반 ✓
server/Harness/GateWitnessCheckCli.cs             coveredByOutbox — 면제
```

면제 근거가 **`LAUNCH-GWIT-01-R2`·`LAUNCH-GWIT-06`** — 내가 그 파일을 직접 고친 것과
**아무 상관 없는 예전 발사들**이다. 내가 지시서에 쓴 규칙이 **경로 기준**이라 그렇다:
*"어떤 outbox 패치든 그 경로를 담고 있으면 반입"*.

**결과: 코덱스가 한 번이라도 만진 파일은 그 뒤로 조율자가 영원히 자유롭게 고칠 수 있다.**

코덱스는 지시서대로 구현했고 이 성질을 **자진 신고했다.** 결함은 지시서를 쓴 내 것이다.
`TERR-02`로 후속한다 — 커밋과 발사를 묶는다.

## 참조한 스킬

`skills/common/` 전부. 변경 경로와 맞는 `skills/domains/` 트리거 없음.

## 지표는 만족했으나 목적은 미달인 부분

1. **`coveredByOutbox` 오탐 면제**(위 §). 지금 이 하네스는 **조율자 침범을 부분적으로만 잡는다.**
   오늘 내가 한 세 번의 침범 중 `GateWitnessCheckCli.cs` 건은 **이 하네스로도 안 잡힌다.**
2. **반입 커밋 자신은 `coveredByOutbox` 4/4로 통과한다.** 맞는 판정이지만, 같은 규칙의
   느슨함에 기대고 있다는 점은 같다.
3. **게이트 증거 보고(`docs/handoff/gate-evidence/LAUNCH-TERR-01.gate.json`)는 처분 기록
   커밋에서 잰 것이다.** 반입 시점 판정이 아니다 — `importCommit`을 미리 알 수 없어
   처분을 pending으로 둔 채 먼저 커밋해야 했고, 그 순간 `launch-disposition`이 위반 1을 센다.
   **의도된 동작이지만 증거 보고에 그 한 검사가 실패로 남는다.** 숨기지 않고 여기 적는다.
4. **`GATE-TRUTH-01`(대기 지시서)의 `HarnessRegistry.cs` 핀을 다시 계산했다.** 이번 반입이
   그 파일에 한 줄을 더했기 때문이다. 변경은 `territory-check` 등록 한 줄이라 그 지시서의
   전제(evidence는 기계가 관찰한 것이어야 한다)에 영향이 없다고 판단했다 — **내 판단이고,
   그 지시서를 다시 읽은 것은 머리 부분과 목적 절까지다.**
