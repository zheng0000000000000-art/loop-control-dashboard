# CPX-01 검증 — `requiredInputs` 겹침을 실패로, 지시서 사이 충돌을 탐지로

- **주체(actor)**: 산출은 **코덱스**(`CodexHarnessLauncher`, `LAUNCH-CPX-01-R2`).
  검증·반입은 **조율 세션(Claude Opus 5)**, 반입 결재는 **사람**(git user `Jaehyuk`).
  CPX-01의 allowlist는 `server/Harness/ContextPackIntegrityCli.cs` 하나뿐이라 이 문서는
  실행자가 쓸 수 없다(코덱스 영역 밖) — 지시서가 그렇게 설계했다.
- **날짜**: 2026-07-26

## 사용한 하네스 (명령 · exit code · 수치)

| 명령 | exit | 수치 |
| --- | --- | --- |
| `codex-launch validate --request …` | 0 | `verdict: ACCEPTED` |
| `codex-launch launch --request … --manual` | 0 | `changedPaths` 1건, `scopeViolations` 0, 패치 188줄 |
| `build-verify` (패치 적용 후) | 0 | 오류 0 |
| `context-pack-integrity` (①직후) | **1** | `failures: 1` (DLINT-01 stale) |
| `context-pack-integrity` (②이후) | **0** | `failures: 0` · `warnings: 0` · `crossDirectivePinCollisionCount: 4` |
| `measure dev-pack` | 0 | `{"gate":"dev-pack","violations":0,"attempt":1}` |
| `doc-integrity` | 0 | — |
| `handoff-integrity` | 0 | — |

## 반증 시험 (지시서 §4)

| # | 시험 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1 | 1-A 겹침 주입(CPX-01에 자기 allowlist 파일을 requiredInputs로) | exit 1, failure 집계 | **FAIL, `failures: 1`, exit 1** ✅ |
| 2 | 그 겹침만 제거 | exit 0 | **PASS, exit 0** ✅ |
| 3 | 1-B 충돌 탐지 | exit 0, 목록에 diId 쌍 | **4건 탐지, 막지 않음** ✅ |
| 4 | 회귀 없음 | 경고 0 | **`warnings: 0`** ✅ |

탐지된 충돌 (전부 `pinnedBy: DLINT-01`):

```
server/Harness/ContextPackIntegrityCli.cs   writtenBy: CODEX-GATE-04
server/Harness/ContextPackIntegrityCli.cs   writtenBy: CPX-01
server/Harness/ContextPackIntegrityCli.cs   writtenBy: GATE-CP-01
skills/common/directive-authoring.md        writtenBy: DAUTH-02
```

## 착륙 (두 걸음 — 지시서 §3의 예고대로였다)

1. **①** 후보 패치 적용 → `context-pack-integrity` **exit 1** (`DLINT-01`의 pin이 stale)
2. **②** `DLINT-01`의 `ContextPackIntegrityCli.cs` pin 갱신
   `ab8de3a12b9a…` → `8ae5412cadca…` (`sha256sum`이 계산, 손으로 적지 않음)
   → **exit 0**

①직후의 빨간 게이트는 산출물 결함이 아니다. **대조 실험으로 갈랐다**: 후보를 적용하지 않고
원본 코드에 개행 하나만 추가해도 동일하게 `DLINT-01` stale로 exit 1이 난다.

## 참조한 스킬

`skills/common/directive-authoring.md` — §3(자기 모순 셀프 검사)·§4(해시는 프로그램이 계산).
착륙 절차 절은 아직 없다. 그것이 `DAUTH-02`이며 이 지시서 착륙 뒤에 수행한다(CPX-01 §5의 순서).

## 지표는 만족했으나 목적은 미달인 부분

1. **1-B는 세기만 하고 막지 않는다.** 지금 4건이 실재하며 착륙 순서 문제는 그대로다 —
   같은 파일을 쓰는 `CODEX-GATE-04`·`GATE-CP-01`이 남아 있고, 어느 쪽이 착륙해도 다시
   `DLINT-01`의 pin을 깨뜨린다. 순서 결정은 사람 몫이며 이 지시서가 해결하지 않았다.
2. **탐지가 붙자마자 4번째 충돌이 나왔다** — 조율자가 30분 전에 쓴 `DAUTH-02`가 만든 것이다.
   즉 이 결함은 과거의 잔재가 아니라 **지금도 계속 생기고 있다.** 하네스가 없었으면 또
   몰랐을 것이고, 실제로 사람이 손으로 찾았을 때는 1건만 보였다.
3. **검증이 self-test에 의존하지 않는 대신 조율자의 수동 대조에 의존한다.** 반증 시험 4종을
   조율자가 손으로 구성해 돌렸다. 이 시험들 자체가 하네스가 되어 있지 않으므로, 다음에
   같은 회귀가 나면 다시 손으로 잡아야 한다.
