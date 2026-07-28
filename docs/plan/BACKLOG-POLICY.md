# BACKLOG-POLICY — 우선순위는 의견이 아니라 계산이다

`docs/plan/BACKLOG.json`의 `priority` 숫자가 어디서 나오는지를 정한다.
**숫자를 손으로 정하지 않는다.** 항목마다 사실(`tier`·`factors`)을 적고, 숫자는 그 사실에서 계산한다.
`scripts/backlog-policy-check.ps1`이 다시 계산해서 어긋나면 커밋을 막는다.

왜 이렇게 하는가: 산문으로 쓴 근거는 다음 세션이 안 읽는다. 읽어도 다르게 해석한다.
**프롬프트로 시키지 말고 코드로 강제하라** — 이 저장소의 규칙이다.

## 1. 계산

```
priority = tierBase + Σ factorWeight
```

### tierBase — 로드맵 층 (`INTENT-DIGEST §2`)

| tier | 값 | 무엇 |
| --- | ---: | --- |
| `P0` | 20 | Runtime / Context Engineering |
| `P1` | 14 | Knowledge (Wiki·Bundle·Skill·Harness·Promotion) + Resource Ledger |
| `P2` | 8 | Simulation · Society · Archive **제품 기능** |
| `P3` | 2 | Registry · Marketplace · Enterprise |

**주의 — 자주 틀리는 곳**: 시뮬레이션이라고 다 `P2`가 아니다.
`ADR-011`이 `HS-GATE-P00`의 통과 조건을 *"시뮬레이션까지 돌려서 충분하다 싶어야"* 로 정의했다.
그러므로 **"시뮬레이션 게이트가 실제로 가르는가"를 다루는 일은 `P0`다.** 게이트가 아니라
경매장의 *기능*을 늘리는 일이 `P2`다. 판단이 갈리면 이렇게 묻는다 —
**"이게 없으면 `HS-GATE-P00`을 통과했다고 말할 수 있는가?"** 없으면 `P0`다.

### factors — 가중치

**값의 정본은 `scripts/backlog-policy-check.ps1`이다.** 아래 표는 사람을 위한 설명이고,
판정은 그 스크립트가 한다. 표와 스크립트가 어긋나면 스크립트가 맞다.

| factor | 값 | 언제 참인가 |
| --- | ---: | --- |
| `measuredFailure` | +6 | **지금 실패가 실측돼 있다.** 문서에 "실패 중"·"무의미"로 표시됐거나 재현 절차가 있다. 추측은 해당 없음 |
| `blocksOthers` | +4 | 이게 안 정해지면 다른 항목의 판정이 흔들린다 |
| `removesManualToil` | +4 | 사람이 반복하던 손질을 없앤다. **횟수를 근거로 댈 수 있어야 한다** |
| `unverifiedRisk` | +2 | 필요한지 아직 안 재봤다("미확인"). 0이 아닌 이유 — 모르는 것도 비용이다 |
| `speculative` | −4 | 근거가 관찰이 아니라 추측이다 |

**동점이면 `id` 오름차순으로 가른다.** 정렬이 흔들리면 같은 상태에서 다른 것이 뽑혀
재현이 안 된다.

## 2. 사람만 정하는 것

계산이 대신할 수 없는 것이 있다. **비워 두는 것이 지어내는 것보다 낫다.**

- **`tier` 배정 자체** — 무엇이 Runtime이고 무엇이 제품 기능인지. 위 "자주 틀리는 곳"이 지침이지만
  경계 사례는 사람이 정한다.
- **가중치 값** — `measuredFailure`가 왜 6이고 `blocksOthers`가 왜 4인가. 지금 값은
  "실패가 실측된 것이 아직 안 재본 것보다 세 배 급하다"는 판단이다. 바꾸려면 `BASELINE-CHANGES`에 남긴다.
- **새 항목을 넣는 것.** 프로그램은 꺼내 쓸 뿐 만들지 않는다(`ADR-024`).

## 3. 지금 값 (계산 결과)

| id | tier | factors | = priority |
| --- | --- | --- | ---: |
| `bl-sim-03-sample-size` | P0 | measuredFailure, blocksOthers | 30 |
| `bl-sim-02-reproducible` | P0 | blocksOthers, unverifiedRisk | 26 |
| `bl-sim-05-search-does-work` | P0 | measuredFailure | 26 |
| `bl-teamloop-worktree-seeds-data` | P0 | removesManualToil | 24 |
| `bl-sim-04-sensitivity-table` | P0 | unverifiedRisk | 22 |
| `bl-sim-06-input-hash` | P1 | unverifiedRisk | 16 |

근거 몇 가지를 남긴다 — 나중에 "왜 저게 먼저였나"를 묻게 된다.

- **표본 수가 1등인 이유**: `runs=40`은 위반, `runs=400`은 통과인데 여유가 0.07이다(실측).
  표본 수가 안 정해지면 §4·§5의 판정이 전부 흔들린다. 실패가 실측됐고 다른 것을 막는다.
- **재현성이 탐색과 동점인 이유**: 재현이 안 되면 나머지 측정이 전부 무의미해진다(`blocksOthers`).
  다만 아직 안 깨졌으므로 `measuredFailure`는 아니다.
- **워크트리 씨딩이 24인 이유**: 회로 차단기까지 갔던 실측이 있지만 지금은
  `scripts/teamloop-isolate.ps1`이 우회하고 있어 **지금 실패 중은 아니다.**
  대신 조율자가 매번 손으로 씨딩하고 있다 — `removesManualToil`.
