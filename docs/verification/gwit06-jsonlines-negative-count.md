# GWIT-06 반입 검증 — `gate-witness-check`가 여러 줄 JSON을 읽는다

- **주체(actor)**: 산출은 **코덱스**(`CodexHarnessLauncher`, `LAUNCH-GWIT-06`).
  검증·반입 집행은 **조율 세션(Claude Opus 5)**, 결재는 **사람**(git user `Jaehyuk`).
- **날짜**: 2026-07-26

## 사용한 하네스 (명령 · exit code · 수치)

| 명령 | exit | 수치 |
| --- | --- | --- |
| `codex-launch validate` | 0 | ACCEPTED |
| `codex-launch launch --manual` | **0** | 변경 8, `scopeViolations` 0, `pathsMissingFromPatch` 0 |
| `build-verify` (패치 후) | 0 | 오류 0 |
| `gate-witness-check` (**LAND 플래그 ON**) | **0** | `totalUnwitnessed` **0** (13/13, 12/12, 18/18) |
| `context-pack-integrity` | 0 | 착륙 한 걸음 (§아래) |
| `measure dev-pack` | 0 | `{"gate":"dev-pack","violations":0,"attempt":1}` |

## 반증 시험 (지시서 §4 — 전부 실측, 사유까지 대조)

| # | 픽스처 | 주장 | 기대 | 실측 exit | 미반증 사유 |
| --- | --- | --- | --- | --- | --- |
| 1 | `jsonlines-state-15` | 15(참) | 반증 있음 | **0** | — |
| 2 | `jsonlines-state-999` | 999 | 반증 없음 | **1** | `state-transition-selftest` |
| 3 | `jsonlines-state-16` | **16** | 반증 없음 | **1** | `state-transition-selftest` |
| 5 | `jsonlines-truncated` | — | 0건 | **1** | `gate-witness-check` |
| 6 | `jsonlines-non-json` | — | 0건 | **1** | `gate-witness-check` |
| 4 | 현재 매니페스트 (객체 1개짜리 둘 포함) | — | 회귀 없음 | **0** | — |
| — | 기존 `require-failure-witness.json` | — | 1 | **1** | — |

**시험 3이 이 지시서의 목적이다.** 46개 객체의 카운터를 **합산**하는 구현이면 실제 15보다
훨씬 큰 수가 나와 16 주장이 통과한다. 실측은 차단이고, 패치도 `Math.Max`를 쓴다.
**999만 막아 보는 시험 2로는 합산 구현을 배제하지 못한다** — 그래서 3을 짝으로 뒀다.

시험 5·6은 fail-closed가 살아 있음을 보인다. 부분 파싱을 성공으로 세지 않는다(§1-B).

## 결과 — LAND의 0이 **검증한 0**이 됐다

`WP-STATE-INTEGRITY-LAND`의 `requireFailureWitness`를 **켰다**. 이제 세 게이트 모두
`internalNegativeCases` 주장을 **실제 실행해 확인한다.**

```
POST-EXECUTOR             13 / 13   미반증 0
POST-COMMIT               12 / 12   미반증 0
WP-STATE-INTEGRITY-LAND   18 / 18   미반증 0   ← 이전엔 "믿은 0"이었다
```

세션 시작 `totalUnwitnessed` **17 → 0**, 그리고 **셋 다 검증된 0**이다.

## ★ 지시서 §5가 틀렸다 — 착륙은 한 걸음이었다

§5는 `GWIT-04`가 대상 파일을 **requiredInputs로 pin**한다고 적고 *"추측이 아니다"*라고
단언했다. 실제로는 **`readOrder`**에 있고 sha가 없어 stale해지지 않는다.
반입 후 `context-pack-integrity`는 **exit 0**이었다.

**원인은 내 읽기다.** 파일명으로 grep해 행 번호만 보고 어느 블록인지 확인하지 않았다.
같은 세션에서 "소유 지시서를 가정하지 말고 grep해서 찾아라"라고 써 놓고 **grep 결과를
다시 프록시로 읽었다.** 행이 잡혔다는 사실은 pin이라는 증거가 아니다.
지시서 원문은 지우지 않고 §5 정정을 append했다.

## 참조한 스킬

`skills/common/directive-authoring.md` §7.

## 지표는 만족했으나 목적은 미달인 부분

1. **`program-verify`로는 게이트 전체를 여전히 못 돌린다**(`ADR-016` §9 — 검사마다 자기
   프로젝트를 다시 빌드하다 자기 자신과 충돌). 이번에도 12개를 직접 순차 실행해 대조했다.
   **게이트 러너가 게이트를 못 돌리는 상태**가 남아 있고, 사람 결재 대기다.
2. **`LAND` 게이트 전체를 실행해 보지 않았다.** 켠 것은 `requireFailureWitness` 플래그이고,
   `gate-witness-check`가 그 플래그를 존중한다는 것까지 확인했다. 18개 검사 전체를 돌린
   최근 실측은 이 커밋에 없다.
