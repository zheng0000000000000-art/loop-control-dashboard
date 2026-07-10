STATUS — 진행 상황 스냅샷 (모바일·새 세션 인수인계용)


갱신 규칙 매 커밋이 아니라 큰 단계 마감 시. 이 파일 + docsdirectives + docsverification 만 읽으면 어떤 세션이든 이어받을 수 있어야 한다.
마지막 갱신 2026-07-10 (Claude)



한 줄 정의

AI가 만들고, AI가 검토하고, AI가 결재를 배우는 — 사람은 기준만 정하는 로컬 자율 런타임. (북극성 개정 2026-07-10 궁극 목표 = AI가 AI를 다루는 런타임, 고정점 = 이양 결정은 항상 사람)

현재 위치


완료·검수 통과 #9(AI 1급 사용자), #11(Context Budget+헤더 참조화), #11-R(재적용+반입 stale 가드)
완료·검수 통과 #10 재작업(제한된 이양안) — outbox 반입 중 게이트 클린 건만 tier-2 AI(qwen3:14b)가 검토·승인,
  proposal 승인/거절은 그대로 사람 전용. `Tier2Approver.Enabled`는 기본값 false로 커밋(사람이 직접 켜야 함).
  감사 로그 docs/audit/tier2-import-approvals.md, 일일 캡 5, 이상 감지 시 자동 halt. 상세 docs/verification/tier2-auto-import-approval.md
완료 모바일 승인 버튼 무반응 버그 — window.prompt()가 401 토큰 재입력에서 막혀(iOS PWA 등에서 미지원/미표시) 렌더러가 멈추는 게 원인.
  페이지 내 모달(promptModal)로 교체. 상세 docs/verification/mobile-approve-button-fix.md
완료(반입 대기) #15(반입 결재 UI) — 커밋 7df4bde로 UI 자체는 이미 머지됨, 검수 통과(docs/verification/import-approval-ui.md)
진행 중(반입 대기, 사람 결정) #12(템플릿 렌더러) — outbox task-20260710070612000 / #7(회고 큐) — outbox task-20260710090000000
  둘 다 서버 코드는 outbox에서 검수까지 끝났고 사람의 approve-import만 남았다. 에이전트는 반입을 대행하지 않는다.
착수 전 #13(실행자 사다리+할당량 원장) — 지시서 없음. #14(Context Pack) — 지시서 없음. #8(규범 스토어) — 지시서 없음.
기준선 숫자 dispatch 사본 = 2.78MB  추정 696k 토큰  131파일 → #14(Context Pack)의 절감 목표


대기열 (4축 재배치)

#12(반입 대기) → #13(실행자 사다리+할당량 원장, 지시서 없음) → #14(Context Pack, 지시서 없음) → #7(반입 대기) → #8(규범 스토어, 지시서 없음)
(#10은 제한된 이양안으로 완료 + 활성화까지 사람이 확정. #15는 이미 반입·검수 완료.)

실행자 규칙 기본 코덱스(CLI 미설치, 수동 채널), 헤드리스는 claude-code(sonnet), 소진 징후 시 강등. 발행·결재·반입 = 항상 사람.

자동화 상태


30분 폴링 새 push 감지 → 자동 검수 → 통과 시 다음 지시서 초안 생성 (데스크톱 Cowork 앱이 켜져 있을 때만 동작)
재귀 실험 Claude가 지시서 작성 → sonnet/codex 헤드리스 수행 → outbox 제출 → 사람 결재. 1호(#12)·2호(#15)·3호(#7) 실측 완료. 2호에서 실행자가 지시 게이트 질문(경로 불일치)을 스스로 제기 — 재지시로 A안 진행. #12·#7은 outbox 반입만 남음(사람 결정)


모바일 일반 채팅에서 이어받는 법

새 채팅에 이렇게 시작 httpsgithub.comzheng0000000000000-artloop-control-dashboard 의 docsSTATUS.md 를 읽고 이어서 협업하자. 원칙은 docsdirectives_header.md 와 docsDECISIONS.md, 대기열은 STATUS 참조.

주의 모바일 채팅은 조언·지시서 작성·검토만 가능(코드 실행·로컬 접근 불가). 실행·결재는 데스크톱대시보드(Tailscale 100.x.x.x5173)에서.