# 네 조각(05H · 06C-1 · 06C-2 · 06H)의 완료 조건 대조

- **주체(actor)**: 조율 세션(Claude Opus 5). 사람 지시.
- **날짜**: 2026-07-26 · HEAD 기준 · **`WORKSTATE`·기록 미변경**
- **대상**: `docs/archive/2026-07/directives/directive-{05H,06C-1,06C-2,06H}*.md` **13개 개정본의 완료 기준 절**

`WORKSTATE`의 세 번째 차단 조건 — *"05H+06C-1+06C-2+06H를 통합 branch에서 단일 land gate로 넘겨야 한다"* — 을
**"게이트가 통과한다"가 아니라 "각 DI가 스스로 정한 완료 기준을 지금도 만족하는가"** 로 다시 물었다.

## 05H — reconciliation (codex)

| 완료 기준 | 기대 | 실측 |
| --- | --- | --- |
| fixture a / b / c / d / e / f | 1 / 1 / 0 / 1 / 1 / 1 | **1 / 1 / 0 / 1 / 1 / 1** |
| fixture malformed | 2 | **2** |
| at-rest 현 저장소 | 0 | **0** |
| `handoff-integrity --self-test` | 0 | **0** |
| CLI `--pending-transition` | 1 (`pending-not-allowed-on-cli`) | **1** |

**전부 일치.**

## 06C-1 — StateTransition v2 (sonnet)

| 완료 기준 | 기대 | 실측 |
| --- | --- | --- |
| `--human-decision` | 2 `removed-option` | **2** |
| `--root` | 2 `removed-option` | **2** |
| `--bogus-flag` | 2 usage/unknown-option | **2** |
| `state-transition --self-test` | 0 | **0** |
| 손 위조: state에 가짜 id, log 없음 | 거부 | **rejected · exit 1 · `stateWritten:false`** · `reconciliation-failed: FORGED-BY-HAND-0001:state-transition-not-logged` |

**전부 일치.** 마지막 항목은 클론에서 실제로 위조해 쟀다
(`docs/verification/blocker-recheck-2026-07-26.md`).

## 06C-2 — trust-origin (sonnet)

| 완료 기준 | 기대 | 실측 |
| --- | --- | --- |
| `trust-origin --self-test` | 0 | **0** (26 case · 음성 21) |
| 인수 없음 | 2 usage | **2** |
| `verify-behavior` | `behaviorEqual:true` | **0 / true** |
| `measure dev-pack` | violations 0 | **0** |
| high-risk 3종이 exit 1 + reason 정확 매칭 | self-test 안 | **self-test 통과에 포함**(`high-risk-stays-closed`) |

**전부 일치.** 다만 아래 §미달 ①을 함께 읽어라.

## 06H — RECOVERY + fixture (codex)

| 완료 기준 | 기대 | 실측 |
| --- | --- | --- |
| fixture A + 전용 매니페스트 존재 | 존재 | **존재** (`docs/qa/fixtures/reconciliation/A/`) |
| `di-completion-check --gate POST-COMMIT --manifest <A>` | **1** | **1** (`handoff-integrity` exit-mismatch) |
| `doc-integrity` | 0 | **0** |
| `measure dev-pack` | 0 | **0** |
| RECOVERY.md 두 시기 분리 | 현재 / provenance 이후 | **있음** (`Current Judgment` … `Post-provenance Future`) |
| 4종 코드 명시 | 4종 | `transition-id-collision` · `duplicate-success-log-conflict` · `legacy-idempotency-unverifiable` **확인** |
| 현재 시기 L1 비활성 | L1 없음 | **Recovery Classes 표에 L2 / L2+ / L3만 있고 L1 행이 없다** |

**전부 일치.** `L1`은 문자열로도 없으며, 그것이 *"현재 시기 L1 비활성"* 의 실체다.

## 지표는 만족했으나 목적은 미달인 부분

1. **06C-2의 완료 기준 몇 개는 지금 상태로는 확인할 수 없다.** 그 기준들은
   *"canonical 저장소에 `TO-2026-001.json` 생성 없음"* 을 요구하는데 — 그것은 **DI 실행 중에는
   만들지 마라**는 뜻이었고, 이후 **사람이 부트스트랩으로 실제로 만들었다.** 지금 파일이 있다는 사실은
   그 기준의 위반이 아니라 **그 기준이 겨냥한 시점이 지났다**는 뜻이다. 혼동하지 마라.
2. **`--self-test` 통과를 개별 항목의 증거로 썼다.** 예: high-risk 3종·hooks 4종은 self-test
   케이스 안에 있고, 나는 **케이스 이름과 전체 통과**만 확인했지 각 케이스의 출력 필드
   (`reasonMatched=true` 등)를 하나씩 열어보지 않았다. **자기보고에 가까운 부분이다.**
3. **13개 개정본의 완료 기준을 전부 열거하지는 않았다.** 각 DI의 **최신 개정본과 회귀 항목**을
   중심으로 골랐다. 초기 개정본에만 있고 이후 대체된 항목(예: 05H-R1의 *"at-rest → 1이 정상"*)은
   **당시 상태 기준이라 지금 잣대로 재면 오히려 틀린다** — 그래서 뺐고, 뺐다는 사실을 적는다.
4. **완료 판정은 하지 않았다.** 이 문서는 대조이며, `blockers`를 쓴 주체는 **검수자**이고
   `WORKSTATE`를 옮기는 것은 **상태 전이 = 사람 결재**다.

## 곁가지 — 아카이브 지시서에 NUL 바이트가 있다

`docs/archive/2026-07/directives/directive-05H-reconciler.md` 등에 **NUL 바이트가 섞여 있어**
`grep`이 바이너리로 판정하고 내용을 건너뛴다(실측: 8,736바이트 중 NUL 4개, UTF-8로는 정상 디코딩됨).
**아카이브를 `grep`으로 훑는 도구·사람은 이 파일들을 못 본다.** 오늘 이 대조를 파이썬으로 읽어서 했다.
고치지 않았다 — 아카이브 수정은 범위 밖이고, 사실만 남긴다.
