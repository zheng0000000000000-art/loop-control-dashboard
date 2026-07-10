# 실패 사례 색인

| ID | 제목 | 상태 | failureClass | 구성요소 |
| --- | --- | --- | --- | --- |
| [FAIL-2026-001](cases/FAIL-2026-001-outbox-stale-import.md) | outbox 반입이 현행 코드를 덮어쓸 뻔함 | 해결됨 | stale_input, design_learning | outbox-import |
| [FAIL-2026-002](cases/FAIL-2026-002-dispatch-token-blocks-remote-approval.md) | 토큰 미설정 시 원격 반입/승인이 401로 막힘 | 완화됨 | config_side_effect, design_learning | remote-token, dispatch-auth, dashboard-approval |
| [FAIL-2026-003](cases/FAIL-2026-003-approve-button-stuck-on-new-proposal.md) | 승인된 화면에 새 제안이 떠 승인 버튼이 막힌 것처럼 보임 | 해결됨 | ui_state, known_failure | dashboard-approval, proposal-rendering |

