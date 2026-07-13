# 실패 위키 색인

| ID | 제목 | 상태 | failureClass | 구성요소 |
| --- | --- | --- | --- | --- |
| [FAIL-2026-001](cases/FAIL-2026-001-outbox-stale-import.md) | outbox 반입이 현행 코드를 덮어쓸 수 있음 | 해결됨 | stale_input, design_learning | outbox-import |
| [FAIL-2026-002](cases/FAIL-2026-002-dispatch-token-blocks-remote-approval.md) | 토큰 미설정이 원격 반입/승인을 401로 막음 | 완화됨 | config_side_effect, design_learning | remote-token, dispatch-auth, dashboard-approval |
| [FAIL-2026-003](cases/FAIL-2026-003-approve-button-stuck-on-new-proposal.md) | 승인 뒤 화면에서 새 제안 승인 버튼이 막힌 듯 보임 | 해결됨 | ui_state, known_failure | dashboard-approval, proposal-rendering |
| [FAIL-2026-004](cases/FAIL-2026-004-parallel-executor-worktree-contamination.md) | 병렬 헤드리스 실행자가 작업트리를 오염시킴 | 완화됨 | concurrency, design_learning | executor-orchestration |
| [FAIL-2026-005](cases/FAIL-2026-005-headless-launch-observability.md) | 헤드리스 실행자 미실행을 진행 중으로 오판 | 해결됨 | observability, known_failure | executor-orchestration |
| [FAIL-2026-006](cases/FAIL-2026-006-storage-project-path-prefix-escape.md) | Storage 프로젝트 경로가 sibling prefix 디렉터리를 통과 | 해결됨 | path_escape, design_learning | storage-project-path |
| [FAIL-2026-007](cases/FAIL-2026-007-outbox-read-prefix-escape.md) | outbox 조회가 encoded backslash로 sibling 디렉터리를 읽음 | 해결됨 | path_escape, design_learning | outbox-read |
| [FAIL-2026-008](cases/FAIL-2026-008-dispatch-self-refactor-template-stale.md) | self-refactor dispatch 템플릿이 현행 코드와 달라 빌드 실패 | 확인됨 | stale_template, design_learning | dispatch-executor, executor-orchestration |
| [FAIL-2026-009](cases/FAIL-2026-009-missing-project-api-returns-500.md) | 없는 projectId 조회가 4xx 대신 500을 반환 | 확인됨 | error_handling, design_learning | project-api-read |
| [FAIL-2026-010](cases/FAIL-2026-010-crlf-gate-deadlock.md) | 줄바꿈 표현 차이가 발사 게이트를 영구 잠금 | 해결됨 | unnormalized_gate, config_side_effect, design_learning | executor-orchestration, gate-evaluation |
| FAIL-2026-011 | 문서 잘림/비원자적 쓰기 계열 | 확인됨 | document_integrity, design_learning | handoff-docs |
| [FAIL-2026-012](cases/FAIL-2026-012-proxy-actor-misjudgment.md) | 커밋 메시지 접두사를 행위 주체로 오판해 위반 22건을 날조 | 해결됨 | unnormalized_gate, design_learning | gate-evaluation, executor-orchestration |
| [FAIL-2026-013](cases/FAIL-2026-013-launch-prompt-truncation.md) | 발사 프롬프트가 잘려 실행자가 지시서를 받은 적이 없었음 | 해결됨 | unnormalized_gate, observability, design_learning | executor-orchestration |
| [FAIL-2026-014](cases/FAIL-2026-014-tier2test-verification-cli-crashes.md) | Tier2Approver 검증 CLI 일부 시나리오가 예외로 종료되고 검증 문서 주장과 실측이 불일치 | 확인됨 | verification_gap, harness_runtime_error, design_learning | tier2-approver, verification-harness |
| [FAIL-2026-015](cases/FAIL-2026-015-harness-measures-wrong-thing.md) | 하네스가 다른 것을 잰다 — 초록불이 대상과 무관하다(9건 실측) | 진행 중 | verification_gap | verification-harness |
| [FAIL-2026-016](cases/FAIL-2026-016-contradictory-requirements-force-relaxation.md) | 모순된 요구가 실행자를 규칙 완화로 민다 — 원인의 절반은 검수자 | 부분 해결 | design_learning | executor-orchestration |
| [FAIL-2026-017](cases/FAIL-2026-017-powershell-encoding-triple-trap.md) | PowerShell 5.1 인코딩 3중 함정 — 독립 검수자에게 깨진 입력을 줘 없는 결함을 보고받음 | 해결됨 | config_side_effect | executor-orchestration |
| [FAIL-2026-018](cases/FAIL-2026-018-reviewer-proxy-relapse.md) | 검수자가 프록시로 네 번 오판할 뻔했다(1건은 실제 사고 — wiki 42파일 삭제) | 진행 중 | known_failure | verification-harness |
| [FAIL-2026-019](cases/FAIL-2026-019-empty-args-webserver-deadlock.md) | 빈 인자 dotnet run이 웹서버를 띄워 실행자를 12분 교착 | 미해결 | harness_runtime_error | executor-orchestration |
