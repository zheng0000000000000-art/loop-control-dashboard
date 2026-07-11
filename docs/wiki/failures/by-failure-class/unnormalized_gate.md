# failureClass 색인: unnormalized_gate

> 게이트·판정이 **실체가 아니라 표현/프록시**에 의존해 오판·교착을 일으킨 실패들.
> 공통 교훈: 판정 입력을 **정규화**하고, 프록시가 아닌 **실체(내용 해시·산출물)**를 본다.

| ID | 제목 | 상태 | 프록시(잘못된 판정 근거) | 실체(올바른 판정 근거) |
| --- | --- | --- | --- | --- |
| [FAIL-2026-005](../cases/FAIL-2026-005-headless-launch-observability.md) | 헤드리스 실행자 미실행을 진행 중으로 오판 | 해결됨 | 프로세스 `StartTime`, "launched" 문자열 | 소유 PID·`HasExited`·산출물 존재 |
| [FAIL-2026-010](../cases/FAIL-2026-010-crlf-gate-deadlock.md) | 줄바꿈 표현 차이가 발사 게이트를 영구 잠금 | 해결됨 | raw `git status`(줄끝 포함 바이트 비교) | 정규화된 내용 해시(`gate-clean` 하네스) |

## 승격
2회 반복 확인 → 하네스 `gate-clean`(HARNESS-01) 승격 근거. HS-CANDIDATES.md 참조.
