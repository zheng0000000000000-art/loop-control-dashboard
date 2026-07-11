# HARNESSES — 실행 가능한 검사 목록 (누가 언제 부르는가)

> 원칙: **문서·보고·표현은 프록시다. 판정은 실체로, 규칙은 코드로.**
> 하네스는 전부 **읽기 전용·부작용 0**이며, **검출만 하고 결재·되돌리기·발사를 하지 않는다.**
> 전부 `dotnet run --project server -c Release -- <명령>` 으로 실행. exit code에 복종할 것.

## 목록

| 명령 | 무엇을 판정 | exit 0 | exit 1 | 근거 실패 |
| --- | --- | --- | --- | --- |
| `gate-clean [경로...]` | 트리 clean 여부를 **정규화 내용 해시**로. 표현차(CRLF/BOM/공백)와 실내용 변경을 분해 | 실내용 변경 0 | 실내용 변경 존재 | FAIL-2026-010 |
| `gate-audit [--since <c>]` | 사람 전용 게이트(결재·반입·기준변경)를 **비사람 주체**가 수행한 흔적 | 위반 없음 | 위반 검출 | 고정점 위반 22건 |
| `claim-check <diId>` | 검증 문서의 **자기보고**를 코드·커밋의 **실체**와 대조 | 주장=실체 | 불일치 존재 | FAIL-2026-005, FIX-01 3회 |
| `hs-scan` | 승격 심사(HS-GATE) **트리거를 기계 탐지** | 트리거 없음 | 심사 의무 | 승격 파이프라인 미작동 |
| `doc-integrity` | 핵심 상태·큐 문서가 **비원자적 쓰기로 잘렸는지** | 무결 | 잘림 검출 | FAIL-2026-011 |
| `e2e-usage [projectId]` | 실사용 6개 시나리오 인프로세스 검증 | 전부 pass | fail 존재 | FEAT-02 |
| `measure <projectId>` | 품질 게이트(위반 수) | 위반 0 | 위반 존재 | (기존) |
| `verify-behavior` | 동작 스냅샷 동일성 | behaviorEqual | 차이 | (기존) |

## 누가 부르는가 (배선)

**조율자(5분) — 매 회차:**
1. `gate-clean server` → **발사조건① 판정.** raw `git status` 쓰지 말 것(FAIL-2026-010).
2. `doc-integrity` → exit 1이면 잘린 문서를 **커밋하지 말고** 복구 후 재검사.
3. 커밋 전 `claim-check <diId>` → exit 1이면 **커밋 금지**(허위 완료 주장). 지금까지 손으로 하던 `git grep` 대조를 대체한다.
4. `gate-audit --since <last-reviewed>` → exit 1이면 위반을 **HUMAN-INBOX에 등재**(대행·되돌리기 금지).

**코덱스(15분) — 매 회차:**
1. `hs-scan` → exit 1이면 **반드시** `skills/common/hs-gate.md` 절차로 심사(재량 아님).
2. `e2e-usage` → 회귀 확인.

**실행자(sonnet) — 작업 완료 시:**
- `measure`·`verify-behavior`로 자가검증. 단 **자기보고는 신뢰되지 않는다** — 조율자가 `claim-check`로 대조한다.

## 하지 않는 것 (전 하네스 공통)
결재(approve/reject/import)·되돌리기·발사·push·기준 변경. 전부 사람 게이트다. 하네스는 **본 것을 보고할 뿐이다.**
