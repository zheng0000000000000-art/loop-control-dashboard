# BASELINE-CHANGES — 기준 파일 변경 원장 (append-only)

> **기준 파일** = `dashboard/data/*/blueprint.json`(목표치) · `dashboard/data/*/workflow-definition.json`(가드레일·정책).
> 이 둘의 변경은 **사람 결재 사항**이며, 변경할 때마다 여기에 **반드시** 항목을 남긴다.
>
> **쓰기 주체: 사람 · 검수자 세션만.** 조율자·코덱스·실행자는 **읽기만** 한다(단일 기록자 — 동시 쓰기 손상 방지).
> **조율자는 기준 파일이 변경돼 있으면 이 파일에서 근거를 찾는다.** 근거가 없으면 커밋하지 말고 HUMAN-INBOX에 "기준 파일 무단 변경 의심"으로 올린다.
>
> **왜 별도 파일인가**: 2026-07-11, 검수자가 기준 변경 근거를 `outputs/review-log.md`에 적었는데 **조율자의 read-modify-write와 겹쳐 통째로 소실**됐다. 조율자는 규칙대로 "근거 없음 → 무단 변경 의심"으로 올렸고, 그 판단은 옳았다. **가드는 작동했고 기록 매체가 실패했다.** 그래서 기록자를 한 명으로 줄인다.

---

## BC-001 — `dashboard/data/dev-pack/workflow-definition.json` : `guardrails.maxLoopIterations` 10 → 100

- **일시**: 2026-07-11 19:5x
- **① 주체(누가 승인했는가)**: **사람(choi)이 명시 승인** ("내가 말한것도 수정해줘"). 실행: 검수자 세션(Claude). **대행 아님 — 사람 지시의 이행.**
- **② 근거(실체)**:
  - `server/Guardrails.cs:40` — `if (loopIteration >= maxLoopIterations && !IsGuardrailAcknowledged(state, "loopIteration", loopIteration))` → 한도를 넘으면 **매 회차마다 사람이 acknowledge를 눌러야** 진행된다.
  - 당시 `loopIteration = 13`, 한도 10. `workflow-state.json`에 회차 10·11·12를 각각 개별 승인한 기록이 실재(19:23:49, 19:33:39, 19:33:44 — 사람이 대시보드에서 클릭).
  - 결과: 사람이 approve해도 apply 단계로 넘어가지 못하고, 다음 measure가 같은 위반을 다시 제안하는 **공회전**. 결재 대기가 무한 재생성됐다.
- **③ 되돌리는 법**: `guardrails.maxLoopIterations` 값을 `100` → `10`으로 되돌린다(1줄).
- **바꾸지 않은 것**: `blueprint.json`(목표치) 무수정 · `server/DevPackMeasures.cs`(측정 코드) 무수정 · 비용 가드레일(`maxEstimatedCost: 5.0`, `maxSubscriptionCalls: 5`) **유지**. **게이트를 통과시키려는 변경이 아니다** — 위반 수는 그대로였고, 위반 자체는 FIX-04/FIX-05로 코드를 고쳐서 없앴다.
- **근본 해결**: 위반이 0이 되면 measure가 warning을 내지 않아 proposal이 아예 생성되지 않는다. 한도 상향은 그때까지의 공회전을 줄이는 조치일 뿐이다.

---

## 기록 형식 (새 항목은 이 형식으로)

```
## BC-00N — <파일> : <필드> <이전> → <이후>
- 일시:
- ① 주체(누가 승인했는가):
- ② 근거(실체 — 코드·로그·측정값 인용):
- ③ 되돌리는 법:
- 바꾸지 않은 것:
```

## BC-002 — Phase 0 신규 하네스 예산 2 → 3 (di-completion-check 추가)

- **주체**: 사람(choi) 승인, 2026-07-12. 제안: 검수자 세션.
- **무엇을 바꿨나**: docs/plan/ALIGNMENT-v9.md §4가 정한 "Phase 0 신규 하네스 2개(handoff-integrity·context-pack-integrity)" 상한을 **3개로 늘린다.** 추가분: **`di-completion-check`**.
- **근거(실측)**: v9 **DI-00-04가 이름으로 요구하는 하네스**다.
  > "7. 공통 완료 조건을 검사하는 Harness 후보를 평가하고, 기준을 충족하면 **di-completion-check Harness의 최소 버전을 제작**한다."
  > "3. **Harness manifest와 Skill manifest의 최소 schema 또는 문서 계약**을 정의한다."
  > "4. Phase 종료 시 **HS-GATE 누락을 탐지하는 검사**를 추가한다."
  **우리는 예산 2칸을 v9가 이름으로 지정하지 않은 하네스에 쓰고, v9가 이름으로 지정한 것은 건너뛰었다.**
  즉 이것은 **예산 초과가 아니라 예산 오배분의 정정**에 가깝다. 그래도 상한을 바꾸는 것이므로 **사람 결재로 처리한다.**
- **되돌리는 법**: di-completion-check를 HarnessRegistry에서 제거하고 server/Harness/DiCompletionCheckCli.cs를 삭제하면 원복된다. manifest(docs/handoff/GATE-MANIFEST.json)는 데이터라 남겨도 무해하다.
- **부작용 주의**: 이 하네스는 **다른 하네스를 실행한다.** measure처럼 **부작용이 있는 검사**(run-log·proposal 생성)를 포함하므로, manifest에 mutatesState를 표기하고 **게이트 재실행이 증거를 오염시킨다는 사실을 드러내야 한다.** (근본 해결은 별도 과제 — 검수자가 재실행할 때마다 run-log가 늘어나는 문제가 이미 실측됐다: 1075 → 1076.)
