# ADR-003 — 기록 파일은 주체별로 소유자를 하나만 둔다

- 상태: 승인됨 (2026-07-11, 소실 사고 직후)
- 일시: 2026-07-11
- 제안: 검수자 세션(Claude)
- 근거 문서: 계획서 §0.5(atomic replace) · `docs/handoff/BASELINE-CHANGES.md`

## 1. 상황
조율자(5분)와 검수자가 `outputs/review-log.md`·`docs/handoff/HUMAN-INBOX.md`에 **잠금 없이 동시 append**했다. read-modify-write가 겹쳐 **검수자의 기록 3건(기준 변경 근거·하네스 오탐 확정·override 규칙)이 통째로 소실**됐다.

그 결과 조율자는 규칙대로 "근거 없음 → 기준 파일 무단 변경 의심"으로 판단했다. **가드는 정상 작동했고, 기록 매체가 실패했다.** HUMAN-INBOX도 같은 원인으로 하루 3회 손상됐다.

## 2. 선택지
(A) 파일 잠금 도입 — 구현 비용, 교착 위험.
(B) **주체별 파일 분리(단일 기록자)** — 각 파일에 쓰는 주체는 한 명, 나머지는 읽기만.
(C) 모든 기록을 append-only DB로 — 과잉.

## 3. 선택
**(B).**

| 파일 | 쓰기 주체 |
| --- | --- |
| `outputs/review-log.md` | 조율자 |
| `outputs/reviewer-log.md` | 검수자 |
| `docs/handoff/BASELINE-CHANGES.md` | 사람·검수자 |
| `docs/handoff/HUMAN-INBOX.md` | 조율자 |
| `docs/handoff/sessions/` | 코덱스 |
| `docs/handoff/decisions/` (ADR) | 검수자·사람 |

## 4. 판단 기준
동시성 제어 비용 0, 즉시 적용 가능, 유실 원인 제거.

## 5. 결과
조율자 프롬프트에 "기록 파일 소유권" 절 신설. 검수 시 세 파일(review-log·reviewer-log·BASELINE-CHANGES)을 **모두 읽되 남의 파일에 쓰지 않는다.**

## 6. 되돌림 조건
주체가 늘어 파일이 파편화되면 생성기로 병합 Projection을 만든다(P0-04).

## 7. 관련 실패 사례
미등록 — HUMAN-INBOX 동시 쓰기 손상(3회), review-log 기록 소실(1회). **코덱스가 FAIL 위키에 등록할 것.**