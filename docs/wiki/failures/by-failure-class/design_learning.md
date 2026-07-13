# failureClass 색인: design_learning

| ID | 제목 | 상태 | 구성요소 |
| --- | --- | --- | --- |
| [FAIL-2026-001](../cases/FAIL-2026-001-outbox-stale-import.md) | outbox 반입이 현행 코드를 덮어쓸 뻔함 | 해결됨 | outbox-import |
| [FAIL-2026-002](../cases/FAIL-2026-002-dispatch-token-blocks-remote-approval.md) | 토큰 미설정 시 원격 반입/승인이 401로 막힘 | 완화됨 | remote-token, dispatch-auth, dashboard-approval |
| [FAIL-2026-004](../cases/FAIL-2026-004-parallel-executor-worktree-contamination.md) | 병렬 헤드리스 실행자가 작업트리를 오염시킴 | 완화됨 | executor-orchestration |
| [FAIL-2026-006](../cases/FAIL-2026-006-storage-project-path-prefix-escape.md) | Storage 프로젝트 경로가 sibling prefix 디렉터리를 통과시킴 | 해결됨 | storage-project-path |
| [FAIL-2026-007](../cases/FAIL-2026-007-outbox-read-prefix-escape.md) | outbox 조회가 encoded backslash로 sibling 디렉터리를 읽음 | 해결됨 | outbox-read |
| [FAIL-2026-008](../cases/FAIL-2026-008-dispatch-self-refactor-template-stale.md) | self-refactor dispatch 템플릿이 현행 코드와 어긋나 빌드 실패 | 확인됨 | dispatch-executor, executor-orchestration |
| [FAIL-2026-010](../cases/FAIL-2026-010-crlf-gate-deadlock.md) | 줄바꿈 표현 차이가 발사 게이트를 영구 잠금 | 해결됨 | executor-orchestration, gate-evaluation |
| [FAIL-2026-016](../cases/FAIL-2026-016-contradictory-requirements-force-relaxation.md) | 지시서와 프롬프트가 모순되면 실행자는 가장 약한 제약을 완화한다 | 부분 해결 |
