# ALIGNMENT-v9 — 계획서와 저장소의 정렬 (P0-01)

> TL;DR: 우리는 계획서(`AI-RUNTIME-REFACTOR-MICRO-DIRECTIVES-v9.md`)를 **읽지 않고 필요할 때마다 즉흥으로 만들어 왔다.**
> 그 결과 ①같은 것을 다른 이름으로 재발명했고 ②계획서가 이미 요구하던 안전장치는 비워둔 채, 그 안전장치가 필요한 **최고 운영 등급으로 이미 달리고 있었다.**
> 이 문서는 **요구 ↔ 실재 ↔ 진짜 공백**을 한 장에 놓고, Phase 0을 어디서부터 세울지 정한다.
>
> authoritative: 이 문서는 매핑 선언이다. 상태 원본은 `docs/handoff/WORKSTATE.json`이다.
> 작성: 검수자 세션, 2026-07-11. 근거 결정: `docs/handoff/decisions/ADR-001-operating-grade.md`

---

## 1. 운영 등급 판정 (§0-A.6) — **이미 최고 등급이다**

계획서는 승격 조건 8개 중 **2개 이상**이면 상위 등급으로 올리라고 한다. 실측:

| 승격 조건 | 충족 | 실체 근거 |
| --- | --- | --- |
| 동시 작업 실행자 3명 이상 | ✅ | sonnet(발사식) · 코덱스(15분) · 조율자(5분) · 검수자(대화 세션) |
| 동일 파일 충돌 또는 잘못된 병합 2회 이상 | ✅ | `server/Tier2Approver.cs`, `server/Tier2ApproverTestCli.cs` (둘 다 격리·되돌림) |
| 인수인계 실패 2회 이상 | ✅ | FAIL-2026-013(프롬프트가 인자 경계에서 잘려 지시 미도착) · 조율자가 세션 outputs의 낡은 사본을 읽음 |
| stale 문서 때문에 잘못된 구현·판단 | ✅ | STATUS.md가 "ORCH-01 참조 .cs 준비됨"이라 했으나 실제로는 커밋 `797e7bc`가 삭제한 상태 |
| 같은 복구/검증을 3회 이상 반복 | ✅ | `claim-check`·build 판정을 매 회차 수동 반복 |
| 자동 retry·Scheduler가 실제 실행 중 | ✅ | 조율자 5분 스케줄 · 코덱스 15분 heartbeat |
| Commit 경계가 canonical repository에 실제 영향 | ✅ | 2026-07-11 origin/main push 완료 |
| 같은 검증을 세 번 이상 반복 | ✅ | 위와 동일 |

**8개 중 7개 충족 → 등급 = `Required Before Multi-model Parallel Work`(최고).**

### 그런데 그 등급의 필수 항목이 없다 (§0-A.4)

| 필수 항목 | 실재 | 오늘 이것 때문에 터진 사고 |
| --- | --- | --- |
| `FILE-CLAIMS.json` 또는 동등한 파일 소유권 예약 | **부분** — 지시서 `## 허용 파일(allowlist)` + `scope-check` 하네스 | 고아 코드 109줄(`Tier2Approver.cs`) — 누가 썼는지 끝내 규명 불가 |
| 불변 handoff snapshot | **없음** | 실행자가 한도로 즉사하면 상태가 통째로 증발 |
| Context Receipt strict mode | **없음** | 실행자가 지시서를 **받은 적도 없이** 저장소를 읽고 딴 일을 함(FAIL-013) |
| 외부 실행자 재개 시험 | **없음** | 새 세션이 매번 장문 인수인계에 의존 |
| stale Context Pack 탐지 | **없음** | 삭제된 참조 스캐폴드를 지시서가 계속 가리킴 |
| 공통 Projection 자동 생성 | **없음** | STATUS.md·SONNET-QUEUE가 손으로 갱신돼 낡음 |
| 충돌 시 authoritative source 우선순위 | **부분** | WORKSTATE가 `FEAT-01 verifying`으로 거짓말하는데 아무도 못 잡음 |

### 자동화 등급(§0-A.3) 항목도 비어 있다 — **Scheduler는 이미 돌고 있는데**

| 필수 항목 | 실재 |
| --- | --- |
| 장애 주입 Harness | 부분(하네스 제작 시 주입 회귀 시험은 함) |
| `handoff-integrity --mode full` | **없음** |
| stable section ID | **없음** |
| Context Pack source hash 검증 | **없음** |
| failureClass별 retry matrix | **없음** |
| 실패 위키 색인 | ✅ 있음 |
| Commit·복구 Harness + rollback 검증 | 부분(`gate-clean`·`claim-check` 있음, rollback 검증 없음) |
| Runtime Evidence와 Engineering Verification Record 분리 | **없음**(섞여 있음) |

**결론: 우리는 안전장치를 켜기 전에 자동화와 병렬화를 먼저 켰다.** 오늘의 사고 목록이 그 대가다.

---

## 2. 동등물 선언 — **재발명 금지**

계획서의 이름과 우리 저장소의 이름이 다르다. **이름을 바꾸지 않는다.** 여기서 동등하다고 선언하고, 앞으로는 이 표를 기준으로 읽는다.

| 계획서 | 우리 저장소 | 상태 |
| --- | --- | --- |
| `harnesses/<name>/` | **`server/Harness/*.cs`** (CLI 하네스 6종 + `HarnessRegistry`) | 동등 — 이름만 다름. **옮기지 않는다**(CliRouter 배선·코덱스 소유권이 걸려 있다) |
| `skills/<name>/` (manifest) | **`skills/common/*.md`** | 동등(경량) — manifest 형식은 미도입 |
| HS-GATE 판정 | **`HS-CANDIDATES.md`** + `hs-scan` 하네스 + `skills/common/hs-gate.md` | 동등 — 단, **Phase 경계 최종 게이트(`HS-GATE-PXX.md`)는 없음** |
| `docs/handoff/sessions/` | ✅ 동일 | 동등 |
| `docs/wiki/failures/` | ✅ 동일 | 동등 |
| `docs/verification/_template.md` | ✅ 동일 | 동등 |
| ADR(`decisions/`) | **`BASELINE-CHANGES.md`**(기준 파일 전용) | **부분** — 일반 결정 기록은 없음 |
| `WORKSTATE.json` | ✅ 동일 | 동등 — 단, **실제와 어긋나도 검출 못 함** |
| `HANDOFF.md` / `RUNTIME-INDEX.md` (Projection) | `docs/STATUS.md`(수작업) | **부분** — 생성기 없음, 그래서 낡는다 |
| `CONTEXT-MANIFEST.json` / Context Pack | 지시서(`docs/handoff/queue/directive-*.md`) | **부분** — `## 허용 파일`은 있으나 `requiredInputs`+hash·`readOrder` 없음 |
| Context Receipt | 없음 | **없음** |
| `docs/context/` 4계층(L0~L3) | 없음 | **없음** |

---

## 3. 진짜 공백 (만들어야 하는 것)

1. **`handoff-integrity` 하네스** — WORKSTATE가 실체와 일치하는지. *가장 큰 구멍: 상태 원본을 아무도 검증하지 않는다.*
2. **Projection 생성기** — STATUS/HANDOFF/RUNTIME-INDEX를 WORKSTATE에서 **생성**. 손으로 쓰면 낡는다(실증됨).
3. **Context Pack 최소판 + `context-pack-integrity`** — 지시서의 참조 입력에 경로+hash. stale·삭제된 입력을 기계가 검출.
4. **ADR(`decisions/`)** — 결정·근거·되돌림 조건. 오늘 "누가 왜"가 네 번 증발했다.
5. **파일 소유권 claim** — allowlist(사후 검출)를 **실행 중 예약**(사전 차단)으로 확장.
6. **`HS-GATE-P00`** — Phase 경계 최종 판정 1회 + 독립 재개 시험.

---

## 4. Phase 0 실행 계획 (P0)

> 예산(§0.4): Phase당 **신규 하네스 2개·스킬 2개 상한**. 신규는 `handoff-integrity`·`context-pack-integrity` **둘뿐**. 나머지는 기존 확장.

| DI | 내용 | 담당 | 선행 |
| --- | --- | --- | --- |
| **P0-01** | 이 문서 + `ADR-001`(운영 등급 승격) | 검수자 | — |
| **P0-02** | ADR 기반: `docs/handoff/decisions/` + 템플릿 + 오늘의 결정 소급 기록. `BASELINE-CHANGES`를 ADR 체계에 연결 | 검수자 | P0-01 |
| **P0-03** | **`handoff-integrity` 하네스**(신규 1/2) — WORKSTATE schema·changedFiles hash·blocker·큐 상태 일치. exit code 판정 | **코덱스** | P0-02 |
| **P0-04** | **Projection 생성기** — `dotnet run -- projection` 이 WORKSTATE에서 STATUS/HANDOFF/RUNTIME-INDEX 생성. 손편집 금지 | **sonnet** | P0-03 |
| **P0-05** | **Context Pack 최소판** — 지시서 헤더에 `requiredInputs`(경로+sha256)·`readOrder`·`forbiddenActions` 구조화 + **`context-pack-integrity` 하네스**(신규 2/2) | 검수자(형식)·**코덱스**(하네스) | P0-03 |
| **P0-06** | **파일 소유권 claim** — `scope-check` 확장: 발사 시 allowlist를 claim으로 등록, 실행 중 타 주체가 같은 파일을 만지면 검출 | **코덱스**(기존 확장) | P0-05 |
| **P0-07** | **`HS-GATE-P00`** — 최종 판정 1회 + **독립 재개 시험**(새 세션이 L0/L1만으로 상태 재구성·smoke test). PASS해야 Phase 1 | 검수자 + **사람** | P0-01~06 |

**순서의 근거**: 상태 원본을 믿을 수 있게(03) → 파생 문서가 낡지 않게(04) → 실행자 입력이 stale하지 않게(05) → 동시 쓰기를 사전 차단(06) → 경계 판정(07).
오늘 사고의 인과 사슬이 정확히 이 순서다.

## 5. 이 문서를 읽는 주체별 지침

- **조율자**: 커밋 레인·검수 규칙은 그대로. P0 산출물도 문서 레인으로 커밋한다.
- **코덱스**: P0-03·P0-05·P0-06이 네 몫이다. **제작 전 `skills/common/hs-gate.md` 2항(볼 데이터가 실재하는가)을 먼저 통과시켜라.**
- **sonnet**: P0-04. 생성기이지 손편집이 아니다.
- **사람**: P0-07의 PASS 판정과, 등급 승격 승인(ADR-001).
