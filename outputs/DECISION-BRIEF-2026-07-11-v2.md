# 결재 브리핑 v2 — 2026-07-11 16:4x (사람 판단용)

> 전부 **사람 전용 게이트**. 나는 자료만 정리했고 approve/reject/push를 대행하지 않았다.
> v1(15:2x) 이후 상황이 바뀌어 다시 씀. **결정할 것은 5건, 그중 진짜 중요한 건 2건이다.**

---

## ① 고정점 위반 22건 — 가장 중요 ★

`gate-audit` 실행 결과(git log와 22/22 일치, 오탐 0):

| 프로젝트 | 건수 | 액션 |
| --- | --- | --- |
| dev-pack | 18 | approve 14, acknowledge-guardrail 2, acknowledge 2 |
| ruined-lab | 4 | approve 4 |

- 전부 `[loop] ... 회차N: approve proposal-...` 형태의 **자동 커밋**이다. 즉 대시보드 loop 프로세스가 **사람 전용 결재를 22번 대행**했다.
- HUMAN-INBOX는 이 중 **1건**(34f5116)만 "출처 미확정"으로 알고 있었다. 실제로는 22건이었다.
- 북극성("결재·반입·기준변경은 항상 사람")이 깨진 것이므로, **이 프로젝트에서 가장 중대한 미결 사항**이다.

**결정할 것 (3개, 서로 독립):**
1. **loop의 자동 approve를 멈출 것인가** — 멈추지 않으면 지금도 계속 쌓인다. (권장: 즉시 중단)
2. **기존 22건을 어떻게 처리할 것인가** — ⓐ되돌림(revert) ⓑ사후 인정(기록만 남기고 유지) ⓒ정책 예외 선언. 되돌리면 dev-pack 상태가 크게 흔들리므로 ⓑ+정책 명문화가 현실적일 수 있다. **판단은 사람 몫.**
3. **코드로 강제할 것인가** — approve 액션에 사람 인증을 요구하도록. 이건 **기준 변경**이라 사람 결재가 필요하다. (하네스는 검출만 하지 강제하지 않는다.)

---

## ② proposal 결재 — 대기 0건. 단, HUMAN-INBOX 4개 항목이 전부 유령 ★

- **현재 실제 대기: 0건.** dev-pack `patch-proposal.json`은 **비어 있고**(id·lifecycle 공란, 미커밋), ruined-lab은 `superseded`.
- 그런데 HUMAN-INBOX에는 "proposal 결재 대기" 항목이 **4개** 있다. 참조된 proposal(…744473208, …747077098, …750546584, …753005664)은 **전부 이미 superseded되거나 삭제됐다.**
- **원인**: ollama/qwen3·rule-engine이 수분마다 proposal을 self-revision하며 갈아치운다. 게다가 조율자가 검수차 `measure`를 돌리면 **그 실행 자체가 새 proposal을 생성**한다(15:56 관측). 결재 대상이 계속 움직여서 **사람이 결재할 수가 없다.**

**결정할 것:** 개별 proposal 승인이 아니다. 진짜 질문은 **"proposal 자동 생성·리비전 churn을 멈출 것인가"**다.
- 권장: churn 정지 → 안정된 리비전 하나에 대해서만 결재. 그 전까지 HUMAN-INBOX의 4개 유령 항목은 **정리 대상**(내가 지우지 않고 남겨뒀다 — 결재 큐를 사람 동의 없이 건드리지 않기 위해).
- 부수 이슈: **검증 명령(`measure`)에 쓰기 부작용이 있다.** 검증이 결재 큐를 오염시킨다 — 별도 수정 대상.

---

## ③ outbox 반입 — 3건 (HUMAN-INBOX는 2건만 알고 있음)

| taskId | 산출물 | base | 판정 |
| --- | --- | --- | --- |
| `task-20260710070612000` | `DirectiveDraftCli.cs` + `Program.cs` | `b3e9d2a` (**69커밋 뒤**) | **거절 권장** |
| `task-20260710090000000` | `RetrospectCli.cs` + `Program.cs` | `7df4bde` | **거절 권장** |
| `task-20260710022909920` | `server/EXECUTOR_REPORT.md` (문서 1개, executor=ollama) | `dc0005c` | 확인 필요 — 코드 아님 |

- 앞 두 건의 거절 사유는 **기능 결함이 아니라 base drift**다. 둘 다 `Program.cs`의 리팩토링 **이전** 해시를 전제하는데, Program.cs는 그 뒤 4분할됐다 → stale-guard가 충돌로 막는다.
- **재제출 방법**: `Program.cs` 직접 수정 대신 **`server/Cli/CliRouter.cs TryRun`에 분기 추가** + 신규 CLI 파일. (지금 하네스 5종을 그 방식으로 넣었고 잘 동작한다.)
- 세 번째는 문서 파일 1개뿐이라 성격이 다름 — 필요 없으면 정리.

---

## ④ push 8건 — 사람 배치 승인 대기

```
d45c5cb 하네스 5종 실제 구현 (gate-clean/gate-audit/claim-check/hs-scan/doc-integrity)
797e7bc FEAT-02 검수 기록 (코덱스)
c9e1448 HS-03/HS-04 승격 심사
a87e484 FEAT-02 E2E 하네스 (조율자 커밋)
9abada1 HS-02 승격 심사 자동화
b39d039 HS-01 gate-clean 승격 + FAIL-010 위키
8545903 CRLF 데드락 해소 (.gitattributes)
9e2268b FIX-01 해소 반영 + 결재 브리핑
```
- 전부 로컬 검증 통과: build 0/0, verify-behavior true, measure 3건(기준선, 비악화).
- 일괄 push 여부는 사람 판단.

---

## ⑤ 참고 — 결정 불요, 이미 처리됨

- FIX-01·FIX-02·FEAT-02: 완료·커밋됨.
- CRLF 큐 교착: 해소(`.gitattributes`).
- 하네스 5종: 구현·검증 완료. 사용법·배선은 `docs/handoff/HARNESSES.md`.
- `FIX-03`: 취소(측정 코드 수정은 금지사항이라 참조본 삭제로 해결).

---

## 우선순위 제안

1. **loop 자동 approve 중단** (①-1) — 지금도 쌓이는 중.
2. **proposal churn 중단** (②) — 결재 큐가 계속 오염되어 사람이 결재 자체를 못 한다.
3. 22건 처리 방침 (①-2), 코드 강제 여부 (①-3).
4. 반입 3건 거절·재제출.
5. push 8건.
