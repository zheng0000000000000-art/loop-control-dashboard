# HUMAN-INBOX — 사람 결정 필요 항목

> 오케스트레이터/조율자가 재량으로 처리하지 않는 항목(반입 승인·거절, proposal 승인·거절, 기준 파일 변경, 이양 전환, 방향 전환)을 여기 쌓는다. 중복 방지: 항목 추가 전 기존 목록 확인.

## 결정 필요: outbox 반입 승인 대기 (2건)

- 맥락: `outbox/task-20260710070612000` — 상태 `import_pending`. review-log 2026-07-10 기록: 서버 코드(DirectiveDraftCli.cs) 반입 건, main에 아직 미반영. #13·#14·#10 항목이 이 반입에 막혀 있음.
- 맥락: `outbox/task-20260710090000000` — 상태 `import_pending`. review-log 2026-07-10 10:30 기록: #7(회고 큐) 실행자 제출물.
- 조치: 두 건 모두 사람의 반입 승인(approve-import) 또는 거절(reject-import) 판단 필요. 오케스트레이터/조율자는 대행하지 않음.
- 확인 시각: 2026-07-11 13:5x (오케스트레이터-escalation-loop).

## 참고: 과거 미해결 관찰(맥락용, 결정 시급성 낮음)

- 2026-07-10 10:30 review-log: 커밋 `34f5116`("[loop] dev-pack 회차6: approve proposal-1783645792306")이 무인 모드 규칙(커밋 금지 + approve는 사람 전용 게이트) 위반으로 기록됨 — 대시보드 loop 프로세스의 자동 커밋으로 추정되나 출처 미확정. 재발 여부·loop 자동 커밋 정책 자체를 사람이 검토할 필요.

## 결정 필요: FIX-01 dispatch 반복 실패 (문서·코드 불일치)

- 맥락: SONNET-QUEUE #1 FIX-01(경로검증 separator-bounded)이 13:27:20(PID 29572)부터 약 40분 실행되다 2026-07-11 14:05~14:09 사이 프로세스 없이 소멸. docs/handoff/WORKSTATE.json(미커밋)은 "완료"를 주장(behaviorEqual:true, build 0/0, measureViolations 3→3)하지만 server/Storage.cs·OutboxManager.cs에는 주장된 IsWithinRoot 코드가 실제로 존재하지 않음(git grep 결과 없음, 커밋도 없음). 조율자 review-log 기준 이 dispatch가 완료 산출물 없이 무산되는 패턴이 이번까지 최소 3회차 관측됨(13:2x대·13:36·14:09).
- 조치: 사람 판단 필요 — ① dispatch 메커니즘(헤드리스 sonnet 실행/파일 반영) 자체의 결함 조사, ② FIX-01 재발사 여부·방식 결정, ③ WORKSTATE.json의 허위/불일치 완료 주장 처리 방침 결정. 조율자는 대행하지 않음(코드 없는 완료 주장을 커밋하지 않고 보류 중).
- 확인 시각: 2026-07-11 14:09 (조율자, recursion1-result-check).

## 결정 필요: dev-pack proposal 결재 대기 (1건)

- 맥락: dashboard/data/dev-pack/patch-proposal.json — `proposal-1783744473208`("UI/UX 개선 및 코드 품질 향상", createdBy ollama/qwen3:8b, revisionOf proposal-1783742578303), lifecycle `submitted`. 변경 대상: smallTouchTargets, skillDomainViolations, maxFunctionLength.
- 조치: 사람의 proposal 승인/거절 판단 필요. 조율자/오케스트레이터는 대행하지 않음.
- 확인 시각: 2026-07-11 14:09 (조율자, recursion1-result-check).

## 결정 필요: dev-pack proposal 리비전 갱신 (1건)

- 맥락: 기존 기록된 proposal-1783744473208의 리비전 체인이 이어져, 현재 dashboard/data/dev-pack/patch-proposal.json에 proposal-1783747077098(revisionOf proposal-1783746981687)이 lifecycle:submitted 상태로 대기 중. 제목 "UI/UX 개선 및 코드 품질 향상"(createdBy ollama/qwen3:8b). 변경 3건: smallTouchTargets 1→0, skillDomainViolations 2→0, maxFunctionLength 159→[0,80].
- 조치: 사람의 승인(approve) 또는 거절(reject) 판단 필요. 조율자는 결재를 대행하지 않음.
- 확인 시각: 2026-07-11 14:19 (조율자, recursion1-result-check).
