# FAIL-2026-001 — outbox 반입이 현행 코드를 덮어쓸 뻔함

- 상태: 해결됨
- 최초 발생일: 2026-07-10
- 최근 발생일: 2026-07-10
- 관련 DI: 지시서 #11-R(ContextBudget 재적용 + 반입 stale 가드)
- 구성요소: outbox-import
- failureClass: stale_input, design_learning
- 심각도: critical

## 발생 상황

self-refactor outbox task `task-20260709133814794`가 `Program.cs` 등 전체 파일 스냅샷을 담고 있었다. 그 사이 다른 커밋으로 현행 코드가 앞서갔다. 당시 반입은 base 검사 없이 outbox 파일을 작업공간 파일 위에 단순 덮어쓰는 방식이었다.

## 관찰된 증상과 영향

반입을 승인했다면 현행 `Program.cs`의 신규 API와 분리 작업을 포함한 최신 코드가 구버전 스냅샷으로 통째 덮어써질 수 있었다. 사람이 반입 전 사전 발견했고, `reject-import`로 거절해 실제 덮어쓰기는 발생하지 않았다.

## 발생 이유(직접·근본·기여, 미확정은 가설 표시)

- 직접 원인: outbox 반입 로직이 task 파일을 현재 워크스페이스 파일에 그대로 복사했다.
- 근본 원인: outbox meta에 사본 생성 시점의 base 커밋이나 원본 파일 해시가 없었고, 반입 시 현재 파일이 그 base와 같은지 검사하지 않았다.
- 기여 요인: self-refactor task가 작은 diff가 아니라 여러 파일의 전체 스냅샷을 담아 stale 위험의 영향 범위가 컸다.
- 미확정: 추가 병합 전략이 있었어도 충돌을 안전하게 해결했을지는 검증하지 않았다.

## 검토한 해결 방법

- 사람에게 stale 여부를 수동 확인하게 하고 기존 반입을 유지한다.
- outbox meta에 base 커밋만 기록하고, HEAD가 달라지면 차단한다.
- outbox meta에 changedFiles별 원본 SHA-256을 기록하고, 반입 직전 현재 파일 해시와 대조한다.

## 선택한 해결 방법

지시서 #11-R로 `originalFileHashes`를 outbox meta에 기록하고, 반입 전에 changedFiles의 현재 SHA-256과 대조하는 stale 가드를 추가했다. 하나라도 다르면 아무 파일도 덮어쓰지 않고 409 `dispatch.stale_base`를 반환한다. legacy task는 기존 동작을 유지하되 응답에 `staleCheck: "skipped_legacy"`를 남긴다.

## 판단 기준

- stale task가 한 파일이라도 현행 파일과 달라졌으면 부분 반입 없이 전체 차단해야 한다.
- 신규 파일은 사본 생성 시점에 없었으면 `"absent"`로 기록하고, 반입 전 이미 존재하면 stale로 본다.
- 병합은 자동 시도하지 않는다. 차단만 한다.

## 검증 결과

검증 문서 `docs/verification/context-budget-2.md`에 파괴 실험이 기록됐다. changedFiles 중 한 파일을 반입 전 인위로 변경하면 `approve-import`가 409 `dispatch.stale_base`를 반환했고, 파일 해시는 차단 전후 동일했다. 변경을 되돌린 뒤 같은 task는 정상 반입됐다.

## 재발 방지

- 새 outbox task는 `baseCommit`과 `originalFileHashes`를 가진다.
- 반입 전 stale 검사를 수행한다.
- legacy task는 `staleCheck: "skipped_legacy"`로 표시해 보호되지 않는 반입임을 드러낸다.

## 후속 작업과 잔여 위험

- legacy task는 해시 정보가 없어 완전 보호되지 않는다.
- base 커밋은 참고용이고, 실제 차단 기준은 파일 해시다.
- 향후 자동 병합이 필요해도 별도 지시와 검증 없이는 추가하지 않는다.

## 발생 이력

- 2026-07-10: `task-20260709133814794` stale 위험 발견.
- 2026-07-10: 사람이 거절해 실제 덮어쓰기를 방지.
- 2026-07-10: 지시서 #11-R로 stale 가드 구현·검증.

