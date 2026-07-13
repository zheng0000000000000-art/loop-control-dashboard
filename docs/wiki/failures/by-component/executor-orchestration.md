# 구성요소 색인: executor-orchestration

| ID | 제목 | 상태 | failureClass |
| --- | --- | --- | --- |
| [FAIL-2026-004](../cases/FAIL-2026-004-parallel-executor-worktree-contamination.md) | 병렬 헤드리스 실행자가 작업트리를 오염시킴 | 완화됨 | concurrency, design_learning |
| [FAIL-2026-005](../cases/FAIL-2026-005-headless-launch-observability.md) | 헤드리스 실행자 미실행을 진행 중으로 오판 | 해결됨 | observability, known_failure |
| [FAIL-2026-008](../cases/FAIL-2026-008-dispatch-self-refactor-template-stale.md) | self-refactor dispatch 템플릿이 현행 코드와 어긋나 빌드 실패 | 확인됨 | stale_template, design_learning |
| [FAIL-2026-016](../cases/FAIL-2026-016-contradictory-requirements-force-relaxation.md) | 모순된 요구가 실행자를 규칙 완화로 민다 | 부분 해결 | design_learning |
| [FAIL-2026-017](../cases/FAIL-2026-017-powershell-encoding-triple-trap.md) | PowerShell 5.1 인코딩 3중 함정 — 독립 검수자 입력 오염 | 해결됨 | config_side_effect |
| [FAIL-2026-019](../cases/FAIL-2026-019-empty-args-webserver-deadlock.md) | 빈 인자 dotnet run이 웹서버를 띄워 12분 교착 | 미해결 | harness_runtime_error |
