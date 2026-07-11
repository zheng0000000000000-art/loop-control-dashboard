# FAIL-2026-007 — outbox 조회가 encoded backslash로 sibling 디렉터리를 읽음

- 상태: 해결됨
- 최초 발생일: 2026-07-10
- 최근 발생일: 2026-07-10
- 관련 DI: S-02 경로 escape 재현 라운드
- 구성요소: outbox-read
- failureClass: path_escape, design_learning
- 심각도: medium

## 발생 상황

`OutboxManager.ResolveTaskDirectory`는 `/api/outbox/{taskId}` 조회에서 task 디렉터리를 계산한다. `Path.GetFullPath(Path.Combine(outboxRoot, taskId))` 후 `fullPath.StartsWith(outboxRoot)`만 검사하고, separator 경계는 확인하지 않는다.

## 관찰된 증상과 영향

임시 사본 서버에서 `GET /api/outbox/..%5Coutbox-escape%5Cpoc`를 호출하자 outbox root 밖의 sibling 디렉터리 `outbox-escape/poc/meta.json`이 읽혔다. 쓰기 반입은 호출하지 않았지만, 읽기 조회만으로 outbox root 밖 meta를 반환하는 것이 확인됐다.

## 발생 이유(직접·근본·기여, 미확정은 가설 표시)

- 직접 원인: encoded backslash가 route segment 안에서 Windows 경로 구분자로 해석됐다.
- 근본 원인: `ResolveTaskDirectory`가 separator-bounded root 검사를 하지 않고 문자열 접두 비교만 사용한다.
- 기여 요인: sibling 디렉터리 이름 `outbox-escape`가 `outbox` 문자열로 시작해 prefix 검사를 통과했다.
- 미확정: `SafeWorkspacePath`의 쓰기 경로도 같은 패턴이지만, 반입 호출은 금지되어 동적 재현하지 않았다.

## 검토한 해결 방법

- `StartsWith(outboxRoot)`만 유지한다.
- route `taskId`에서 slash/backslash와 `..`를 명시적으로 거부한다.
- full path 검증을 `fullPath == outboxRoot` 또는 `fullPath.StartsWith(outboxRoot + Path.DirectorySeparatorChar)`로 바꾼다.
- `Path.GetRelativePath(outboxRoot, fullPath)`가 `..` 또는 rooted path인지 검사한다.

## 선택한 해결 방법

이번 QA 작업에서는 코드를 수정하지 않았다. 구현 실행자에게 taskId 문자 검증과 separator-bounded root 검사를 적용하도록 이관한다.

## 판단 기준

outbox task 조회는 outbox root 내부 task만 읽어야 한다. sibling prefix 디렉터리의 meta를 읽으면 경계 검증 실패다.

## 검증 결과

임시 사본 서버 `http://localhost:5317`에서 harmless sibling directory `outbox-escape/poc/meta.json`을 만든 뒤 GET만 호출했다.

| URI | status | 결과 |
| --- | ---: | --- |
| `/api/outbox/..%5Coutbox-escape%5Cpoc` | 200 | sibling meta 반환 |
| `/api/outbox/%2e%2e%5Coutbox-escape%5Cpoc` | 200 | sibling meta 반환 |
| `/api/outbox/..%2Foutbox-escape%2Fpoc` | 404 | slash variant는 미재현 |

반환 body에는 `taskId: "poc-outside-outbox"`가 포함됐다.

## 재발 방지

- route path parameter에서 `..`, slash, backslash를 거부한다.
- root 검증은 separator-bounded 검사 또는 `Path.GetRelativePath` 기반 검사로 통일한다.
- encoded backslash PoC를 경로 검증 QA 체크리스트에 추가한다.

## 후속 작업과 잔여 위험

- `SafeWorkspacePath`도 같은 prefix 패턴을 사용하므로 구현 수정 시 함께 점검해야 한다.
- 반입/approve 경로는 금지 때문에 동적 재현하지 않았다.
- 서버가 Windows에서 동작할 때 encoded backslash 취급을 우선적으로 테스트해야 한다.

## 발생 이력

- 2026-07-10: 버그헌트 라운드 1에서 S-02 의심 등록.
- 2026-07-10: 후속 재현 라운드에서 GET 기반 sibling outbox read 재현.
- 2026-07-10: `FAIL-2026-007`로 실패 위키 등록.
