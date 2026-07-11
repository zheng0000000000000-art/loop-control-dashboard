# 결정 기록

- 2026-07-09: 코덱스 주간 할당량 소진으로 본 수정은 Claude Code로 강등 실행 — "구독 소진 시 강등" 설계 전제의 첫 실증 사례.
- 2026-07-09: 탐색 휴리스틱(shaping 보조항)은 목표(blueprint)와 분리한다 — 목표 변경은 사람 결재, 탐색 내부 채점은 구현 자유.
- 2026-07-09: 변경 0건 proposal은 생성하지 않는다 — 해 없음은 제안이 아니라 보고(no_solution 이벤트)다.
- 2026-07-09: 문턱형 지표(생존율 등)는 진행도 보조 신호 없이는 그리디 탐색이 고원에 갇힌다 — 게임류 도메인 팩의 알려진 함정.
- 2026-07-09: 재지시(검토 지적)도 지시다 — 정보가 부족하면 재생성(추측) 대신 사람으로 올린다. 지시 게이트의 내부 대칭.
- 2026-07-09: 운영 데이터의 커밋은 서버, 코드의 커밋은 에이전트/사람 — 결재가 기록 누락을 만들지 않게.
- 2026-07-09: 시스템이 실행자를 부르면 비용 role은 runtime으로 기록한다 — 시스템 안의 실행 소비이기 때문이다.
- 2026-07-09: 레버 확장 첫 사례 — 시스템이 "범위 내 해 없음" 보고 → 사람이 울타리 확장 승인.
- 2026-07-10: AI 1급 사용자 작업에서 Program.cs가 programCsLines 밴드(2661)를 23줄 초과했다.
  안전하게 뺄 수 있는 부분(스킬 트리거 매칭)만 SkillRouter.cs로 옮기고, 나머지는 강제로
  압축하지 않고 위반으로 보고한다 — 진짜 해소는 이미 반입 대기 중인 self-refactor-dispatch
  (Orchestrator.cs/ProposalFlow.cs 분리) 몫이다. 같은 지표를 두 번 손대는 대신 기존 트랙에 맡긴다.
- 2026-07-10: **제한된 이양안(#10 재작업) — outbox 반입 중 게이트 클린 건만 tier-2 AI 결재자에게 위임.**
  사람이 채팅으로 승인한 결정을 구현한다(`server/Tier2Approver.cs`). 범위는 outbox 반입
  1건뿐이다 — proposal 자체의 승인/거절은 여전히 사람 전용(변경 없음). 자동 승인 조건은
  ①게이트 위반 비증가 ②코어 3파일(Engine.cs/Storage.cs/Guardrails.cs) 무수정 ③기준 파일
  (workflow-definition.json/blueprint.json) 무수정, 세 조건 모두 AND. 조건 미달·리뷰어 거절·
  연결 실패는 전부 기존과 동일하게 사람 대기로 남는다. 상위 티어 AI는 로컬에 설치된 가장 큰
  모델 `qwen3:14b`(reviewerPolicy.tier1과 동일 — 더 큰 모델이 없어 "상위 티어"는 역할 분리를
  의미, 모델 크기 서열은 아님)로 정했다.
  **08b648c가 "enabled:true 자가 커밋"으로 revert된 전례**를 반복하지 않기 위해, 이번 커밋은
  장치(게이트·감사 로그·일일 캡·이상 감지 자동정지)를 짓는 것까지만 하고
  `Tier2Approver.Enabled`는 `server/appsettings.json`에 **기본값 false로 커밋**한다 — 켜는
  결정은 별도로 사람이 그 값을 true로 바꿔야 한다. 일일 캡은 이 프로젝트에 outbox 반입
  수준에 대응하는 기존 캡 개념이 없어(guardrails의 maxSubscriptionCalls 등은 프로젝트별
  루프 개념이라 그대로 못 씀) 새로 정했다 — 기본값 5/일. 근거: 반입 빈도가 늘고 있다는
  지시서 #15의 실측(하루 여러 회차)에 비춰, 사람이 감사 로그를 하루 한 번만 훑어봐도
  전량 확인 가능한 크기로 잡았다. 이상 감지(반입 후 재측정에서 위반 증가, 또는 적용 중
  예외)는 즉시 halt 상태로 전환하고 사람이 `docs/audit/tier2-import-approvals-state.json`의
  `halted`를 직접 고쳐야 재개된다(자동 해제 없음). 상세: `docs/verification/tier2-auto-import-approval.md`.
- 2026-07-10: 사람이 채팅으로 `Tier2Approver.Enabled: true` 활성화를 명시적으로 확인했다 —
  `server/appsettings.json`에 반영. 위 결정에서 갈라둔 "장치 구축"과 "활성화"의 두 단계 중
  후자를 사람이 별도로 확정한 사례다.
- 2026-07-10: "승인해도 새로고침하면 다시 승인해야 한다" 버그 수정 — 원인은 dev-pack처럼
  기존 위반이 상시 남아 있는 프로젝트에서, 승인 직후 실제로는 아무것도 안 고쳤는데 재측정만
  실행돼도 `apply` 단계를 무조건 blocked로 되돌리고 새 검토를 강제로 열던 것. 승인 시점의
  위반 집합을 `state.applyBaselineViolations`로 기준선 삼아, 재측정에서 그 집합이 완전히
  같으면(=아직 아무도 안 고침) 단계를 건드리지 않도록 했다. 위반 집합이 조금이라도 달라지면
  기존처럼 새 검토를 연다 — "판정을 완화"하는 게 아니라 "이미 승인된 결정이 재측정만으로
  지워지지 않게" 하는 수정이라 기준(blueprint/measure) 자체는 건드리지 않았다. 이어서 사람이
  실사용 중 "승인 버튼이 막혀 있다"로 직접 신고 — 1차 수정이 단계 전이만 막고 제안(proposal)
  재생성은 막지 않아, 이미 승인된 화면에 새 제안이 뜨고 버튼만 비활성화된 채로 남았던 것.
  같은 조건으로 제안 재생성·회귀 판정도 함께 건너뛰도록 확장했다. 상세:
  `docs/verification/apply-stage-reapproval-bug.md`.
- 2026-07-11: **FEAT-01 — 한정 이양 완성: 게이트 클린 반입 AI 승인(Tier2Approver 확장).**
  Tier2Approver에 3가지를 추가했다. ①**검증 문서 동반 조건** — `changedFiles`에 `docs/verification/` 파일이
  있거나 `meta.hasVerification == true`여야 AI 승인 후보가 된다. 이로써 게이트 클린 조건은
  기존 3개(위반 비증가·코어 무수정·기준파일 무수정)에 stale 방지(기존)와 검증 문서 동반을 더해 5개가 됐다.
  ②**`import.ai` run-log 이벤트** — 승인·이상 시 프로젝트 run-log에 `import.ai` 이벤트를 기록한다
  (taskId·게이트 결과·리뷰어 모델·계층·일일 카운터 포함). 대시보드 run-log 탭에서 감사 가능.
  ③**이상 감지 롤백 요청** — 반입 후 재측정에서 위반 증가 감지 시 `rollback-request.json`을 task
  디렉터리에 생성해 되돌려야 할 파일 목록을 남긴다. 기존 `halt` + 감사 로그에 더해 사람이 실행할
  복구 근거를 직접 제공한다.
  **고정점**: `conditionalDelegation.enabled`(코드에서 `Tier2Approver.Enabled`)는 `false`가 기본이며
  코드가 자동으로 켜는 경로는 없다 — 켜는 결정은 사람만 한다(appsettings.json 직접 수정).
  **되돌림 조건**: 반입 후 위반 증가가 감지되면 자동 halt → 사람이 `docs/audit/tier2-import-approvals-state.json`의
  `halted`를 false로 직접 수정 → rollback-request.json의 `changedFiles`를 참고해 수동 복구.
- 2026-07-10: ruined-lab 스크린샷 재신고("내보내기 진행 중"인데 상세엔 "아직 완료 안 됨")를
  조사해 별개의 실제 데이터 버그를 찾았다 — 적용/내보내기 단계는 배지(`stages.X`)만
  갱신되고 그 설명 텍스트(`stageDetails.X`)는 채우는 코드가 아예 없어 아주 예전에 한 번
  박힌 문구가 그대로 남아 있었다. `SetApplyStageDetails`로 채우되, 처음엔
  `ApplyMeasurementStagePatch` 직후에 불렀다가 회귀·튜닝·기준 추가 제안 분기가 이후에
  또 단계를 바꾸는 걸 발견해(실측 중 자체 버그를 재발견) 호출 위치를 함수 맨 끝으로
  옮겼다. 상세: `docs/verification/apply-stage-detail-desync.md`.
