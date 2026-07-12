# ADR-013 — canonical `diId`는 **v9 축**이다. 로컬 P0 큐는 별칭이다

- 상태: **승인됨 (사람 choi, 2026-07-12)**
- 일시: 2026-07-12
- 제안: 검수자 세션(Claude). 근거: `docs/verification/phase-gates/CONFORMANCE-P00-DI-00-01~06.md`(적합성 행렬)
- 관련: `ALIGNMENT-v9`(로컬 P0 계획) · `ADR-001`(운영 등급) · `docs/plan/AI-RUNTIME-REFACTOR-MICRO-DIRECTIVES-v9.md`

## 1. 상황 — 좌표계가 둘이었다

우리는 두 개의 DI 축을 동시에 써 왔다.

| 축 | 정의처 | 예 |
| --- | --- | --- |
| **v9 canonical** | `AI-RUNTIME-REFACTOR-MICRO-DIRECTIVES-v9.md` | `DI-00-01` ~ `DI-00-07` |
| **로컬 큐** | `ALIGNMENT-v9 §4`(진짜 공백 6개를 골라 만든 실행 계획) | `P0-01`~`P0-07`, `LEDGER-04`, `STATE-01`, `FIX-06` … |

**그리고 canonical 필드(`WORKSTATE.diId`)에 로컬 별칭을 넣어 왔다**(`phaseId=P0-04`, `diId=LEDGER-04`).
그 결과 **상태가 실체와 계속 어긋났고**, "Phase 0의 DI 6개가 전부 끝났다 → `DI-00-07`만 남았다"는 **거짓 경계 주장**이 인수인계에 실렸다.

**적합성 행렬이 이를 반증했다**: v9 `DI-00-01~06` 중 **PASS는 `DI-00-03` 하나**, 나머지 5개 PARTIAL, **가장 이른 미충족 = `DI-00-01`.**

## 2. 결정

1. **canonical `diId`·`phaseId`·`wpId`는 v9 축만 쓴다.** 로컬 큐 이름(`LEDGER-04`·`STATE-01` 등)은 **`WORKSTATE.notes`의 별칭**으로만 남긴다.
2. **`diId = DI-00-01`로 내린다**(가장 이른 미충족 DI). **이것은 후퇴가 아니라 좌표 정정이다** — 로컬 P0에서 만든 것(하네스 3종·Projection·State Applier·FILE-CLAIMS·ADR 체계)은 **그대로 실재하며**, 행렬의 각 DI 칸에 증거로 매핑되어 있다.
3. **기준(v9 산출물 목록)을 우리 실체에 맞게 고치지 않는다.** 그 안(나)은 **판정이 불편해서 기준을 옮기는 모양**이 되고, `CLAUDE.md` 금지사항 1번이 막는 행동이다.
4. **새 canonical ID 체계를 만들지 않는다**(`DI-00-01a` 같은 안(다)은 기각).
5. **`DI-00-07`(Phase 0 최종 경계 판정)은 `DI-00-01~06`이 전부 PASS가 된 뒤에만 시작한다.** `HS-GATE-P00.md`는 v9대로 **`DI-00-07`이 정확히 한 번** 생성한다.

## 3. 되돌리는 법

이 결정을 뒤집으려면 **v9 산출물 목록 자체를 바꿔야 하고, 그건 기준 변경이다** — `BASELINE-CHANGES.md`에 ①주체 ②근거 ③되돌리는 법을 남기고 **사람이 결재**한다.

## 4. 강제 방법 (프롬프트가 아니라 코드로)

- `StateApplierCli`의 canonical 패턴 검사(`^DI-\d{2}-\d{2,}$`)가 **요청 delta에만** 걸려 있다 → **candidate(정지 상태)에도 걸어야 한다.** 지금 상태에서는 비canonical 값이 **at rest로 유효**하다(검수자 결함 D4).
- **후속 지시서에 포함한다.**

## 5. 한 줄

> **좌표가 틀렸으면 지도를 고치는 게 아니라 좌표를 고친다.**
