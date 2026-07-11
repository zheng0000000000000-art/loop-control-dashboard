# HUMAN-INBOX — 사람 결정 필요 항목

> 오케스트레이터/조율자가 재량으로 처리하지 않는 항목(반입 승인·거절, proposal 승인·거절, 기준 파일 변경, 이양 전환, 방향 전환)을 여기 쌓는다. 중복 방지: 항목 추가 전 기존 목록 확인.

## 결정 필요: outbox 반입 승인 대기 (2건)

- 맥락: `outbox/task-20260710070612000` — 상태 `import_pending`. review-log 2026-07-10 기록: 서버 코드(DirectiveDraftCli.cs) 반입 건, main에 아직 미반영. #13·#14·#10 항목이 이 반입에 막혀 있음.
- 맥락: `outbox/task-20260710090000000` — 상태 `import_pending`. review-log 2026-07-10 10:30 기록: #7(회고 큐) 실행자 제출물.
- 조치: 두 건 모두 사람의 반입 승인(approve-import) 또는 거절(reject-import) 판단 필요. 오케스트레이터/조율자는 대행하지 않음.
- 확인 시각: 2026-07-11 13:5x (오케스트레이터-escalation-loop).

## 참고: 과거 미해결 관찰(맥락용, 결정 시급성 낮음)

- 2026-07-10 10:30 review-log: 커밋 `34f5116`("[loop] dev-pack 회차6: approve proposal-1783645792306")이 무인 모드 규칙(커밋 금지 + approve는 사람 전용 게이트) 위반으로 기록됨 — 대시보드 loop 프로세스의 자동 커밋으로 추정되나 출처 미확정. 재발 여부·loop 자동 커밋 정책 자체를 사람이 검토할 필요.

## [해소됨 2026-07-11 15:0x] FIX-01 dispatch 반복 실패 — 이후 착지·확정

- 결과: FIX-01은 이후 `13f833a`로 실제 착지, `IsWithinRoot`가 server/Storage.cs·OutboxManager.cs에 실재(git grep 확인). 조율자 `db0e836` 재검증 + 코덱스 `ccd4554` PASS 리뷰. 위 14:09 관측 시점의 "코드 미반영" 불일치는 그 뒤 커밋으로 해소됨.
- 남는 사양 입력(결정 불요, 참고): dispatch가 완료 산출물 없이 수 회차 무산된 패턴은 ORCH 프로그램화의 근거 데이터(발사↔완료 task ID 결속 요구). ORCH-01 관측 스캐폴드로 이어감.
- 원문(이력 보존):
  - 맥락: SONNET-QUEUE #1 FIX-01이 13:27:20(PID 29572)부터 ~40분 실행되다 14:05~14:09 사이 소멸, WORKSTATE.json은 완료 주장하나 당시 코드 미반영이었음(3회차 무산 관측).
  - 확인 시각: 2026-07-11 14:09 (조율자).

## 결정 필요: dev-pack proposal 결재 대기 (1건)

- 맥락: dashboard/data/dev-pack/patch-proposal.json — `proposal-1783744473208`("UI/UX 개선 및 코드 품질 향상", createdBy ollama/qwen3:8b, revisionOf proposal-1783742578303), lifecycle `submitted`. 변경 대상: smallTouchTargets, skillDomainViolations, maxFunctionLength.
- 조치: 사람의 proposal 승인/거절 판단 필요. 조율자/오케스트레이터는 대행하지 않음.
- 확인 시각: 2026-07-11 14:09 (조율자, recursion1-result-check).

## 결정 필요: dev-pack proposal 리비전 갱신 (1건)

- 맥락: 기존 기록된 proposal-1783744473208의 리비전 체인이 이어져, 현재 dashboard/data/dev-pack/patch-proposal.json에 proposal-1783747077098(revisionOf proposal-1783746981687)이 lifecycle:submitted 상태로 대기 중. 제목 "UI/UX 개선 및 코드 품질 향상"(createdBy ollama/qwen3:8b). 변경 3건: smallTouchTargets 1→0, skillDomainViolations 2→0, maxFunctionLength 159→[0,80].
- 조치: 사람의 승인(approve) 또는 거절(reject) 판단 필요. 조율자는 결재를 대행하지 않음.
- 확인 시각: 2026-07-11 14:19 (조율자, recursion1-result-check).

## 결정 필요: dev-pack proposal 리비전 갱신 (2건째, 2026-07-11 15:2x)

- 맥락: dashboard/data/dev-pack/patch-proposal.json이 proposal-1783750546584(revisionOf proposal-1783750066352, 제목 "UI/UX 개선 및 코드 품질 향상", createdBy ollama/qwen3:8b)로 다시 갱신됨. lifecycle `submitted`. 변경 3건: smallTouchTargets 1→0, skillDomainViolations 2→0, maxFunctionLength(before 159)→[0,80]. 위 14:19 기록된 proposal-1783747077098 이후의 후속 리비전.
- 조치: 사람의 승인(approve) 또는 거절(reject) 판단 필요. 조율자는 결재를 대행하지 않음.
- 확인 시각: 2026-07-11 15:27 (조율자, recursion1-result-check).

## 결정 필요: dev-pack proposal 신규 리비전 (3건째, rule-engine 계열, 2026-07-11 15:56)

- 맥락: dashboard/data/dev-pack/patch-proposal.json이 proposal-1783753005664(revisionOf proposal-1783752773893, 제목 "브랜딩 관리 이슈 제안", createdBy rule-engine)로 갱신됨. 위에 기록된 proposal-1783750546584 계열(ollama/qwen3:8b, "UI/UX 개선 및 코드 품질 향상")과는 다른 리비전 체인. lifecycle submitted, overallStatus warning. 변경 5건: functionsWithoutComment 12→0(docs/handoff/queue/GateCleanCli.reference.cs 등), smallTouchTargets 1→0, skillsWithoutVersion 1→0, skillDomainViolations 2→0, maxFunctionLength 159→[0,80].
- 발생 경위: 조율자가 FEAT-02 검수 절차 중 dotnet run -- measure dev-pack을 실행(비악화 확인 목적)했고, 그 실행 자체가 rule-engine 제안을 새로 생성함(조율자의 의도적 결재 행위 아님, measure 실행의 부수 효과).
- 조치: 사람의 승인(approve) 또는 거절(reject) 판단 필요. 조율자는 결재를 대행하지 않음.
- 확인 시각: 2026-07-11 15:56 (조율자, recursion1-result-check).
## 긴급 확인 필요: 무인 승인(loop approve) 위반 재발 규모 확대 — 1건→12건 (2026-07-11 16:05)

- 맥락: 검수자(코덱스) 세션이 docs/handoff/HS-CANDIDATES.md에 HS-04 gate-audit 후보를 12/12점(★최우선)으로 등재. 근거: git log에서 [loop] dev-pack 회차N: approve proposal-... / cknowledge-guardrail ... 형태의 무인 커밋 **12건 실측**(회차5·6·7·8·9·10, 두 계열). 기존 HUMAN-INBOX 항목(아래 "참고: 과거 미해결 관찰")은 34f5116 1건만 "출처 미확정"으로 기록했으나, 이후 6건 이상 추가 재발한 것으로 확인됨.
- 의미: 북극성 원칙("결재·반입·기준변경은 항상 사람")이 문서 규칙으로만 존재하고 코드 강제가 없어, 대시보드 loop 프로세스(추정)가 proposal을 반복적으로 무인 승인·자동 커밋한 것으로 보임.
- 조치: (1) 이 12건 커밋 목록·주체 확인 및 대시보드 loop 자동 승인 코드 경로 사람 감사 필요, (2) HARNESS-04 gate-audit(검출 전용, 되돌리기·결재 미포함) 즉시제작 여부 사람 승인 필요. 조율자는 감사·되돌리기·정책변경을 대행하지 않음.
- 확인 시각: 2026-07-11 16:05 (조율자, recursion1-result-check, docs/handoff/HS-CANDIDATES.md HS-04 절 근거).