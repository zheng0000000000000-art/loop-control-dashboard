# <DI-ID> 검증 — <제목>

## 주체 (actor) ※필수
- **누가**: <sonnet | 코덱스 | 조율자 | 검수자(Claude) | 사람>, 식별자 <PID/세션/커밋 author>
- **경로**: <헤드리스 발사 | 릴레이 | 스케줄 태스크 | 대화 세션>

> 왜 적는가: 같은 오류가 반복될 때 **어느 주체 탓인지** 추적하기 위함. 주체 기록이 없으면 프록시로 추측하게 되고, 그러면 틀린다(FAIL-2026-012 — 커밋 접두사를 행위주체로 오판해 위반 22건 날조).

## 사용한 하네스 ※필수
| 하네스 | 명령 | exit | 결과(핵심 수치) |
| --- | --- | --- | --- |
| gate-clean | `dotnet run --project server -c Release -- gate-clean server` | 0 | contentDirty=0 |
| doc-integrity | `... -- doc-integrity` | 0 | INTACT 0/12 |
| measure | `... -- measure dev-pack` | 1 | violations=3 (비악화) |
| verify-behavior | `... -- verify-behavior` | 0 | behaviorEqual=true |
| <추가> | | | |

> 왜 적는가: **조율자가 이 목록을 직접 재실행해 대조한다.** 기록하지 않으면 검사할 수 없다.
> **자기보고는 신뢰되지 않는다** — "PASS라고 썼으니 PASS"가 아니라 "재실행 결과가 PASS여야 PASS"다.

## 참조한 스킬 ※필수
- `skills/common/...`

## 변경 내용
<신규/수정 파일>

## 검수 기준 자가점검표
| # | 기준 | 결과 | 근거 |
| --- | --- | --- | --- |

## 게이트 기록
`{"gate":"dev-pack","violations":N,"attempt":1}`
<위반이 남으면 목록을 그대로 적는다 — 숨기지 않는다>

## 직접 경로 사용 사유 (썼다면)
