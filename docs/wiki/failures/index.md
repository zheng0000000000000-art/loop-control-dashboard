# 실패 위키 색인

| ID | 제목 | 상태 | failureClass | 구성요소 |
| --- | --- | --- | --- | --- |
| [FAIL-2026-001](cases/FAIL-2026-001-outbox-stale-import.md) | outbox 반입이 현행 코드를 덮어쓸 수 있음 | 해결됨 | stale_input, design_learning | outbox-import |
| [FAIL-2026-002](cases/FAIL-2026-002-dispatch-token-blocks-remote-approval.md) | 토큰 미설정 시 원격 반입/승인이 401로 막힘 | 완화됨 | config_side_effect, design_learning | remote-token, dispatch-auth, dashboard-approval |
| [FAIL-2026-003](cases/FAIL-2026-003-approve-button-stuck-on-new-proposal.md) | 승인 후 화면에서 새 제안 승인 버튼이 막힘처럼 보임 | 해결됨 | ui_state, known_failure | dashboard-approval, proposal-rendering |
| [FAIL-2026-004](cases/FAIL-2026-004-parallel-executor-worktree-contamination.md) | 병렬 헤드리스 실행자가 작업트리를 오염시킴 | 완화됨 | concurrency, design_learning | executor-orchestration |
| [FAIL-2026-005](cases/FAIL-2026-005-headless-launch-observability.md) | 헤드리스 실행자 미실행을 진행 중으로 오판 | 해결됨 | observability, known_failure | executor-orchestration |
| [FAIL-2026-006](cases/FAIL-2026-006-storage-project-path-prefix-escape.md) | Storage 프로젝트 경로가 sibling prefix 디렉터리를 통과시킴 | 확인됨 | path_escape, design_learning | storage-project-path |
| [FAIL-2026-007](cases/FAIL-2026-007-outbox-read-prefix-escape.md) | outbox 조회가 encoded backslash로 sibling 디렉터리를 읽음 | 확인됨 | path_escape, design_learning | outbox-read |
| [FAIL-2026-008](cases/FAIL-2026-008-dispatch-self-refactor-template-stale.md) | self-refactor dispatch 템플릿이 현행 코드와 어긋나 빌드 실패 | 확인됨 | stale_template, design_learning | dispatch-executor, executor-orchestration |
| [FAIL-2026-009](cases/FAIL-2026-009-missing-project-api-returns-500.md) | 없는 projectId 조회가 4xx 대신 500을 반환 | 확인됨 | error_handling, design_learning | project-api-read |
