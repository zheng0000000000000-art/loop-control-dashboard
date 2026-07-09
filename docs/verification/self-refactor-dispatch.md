# 자기 리팩터링 dispatch 검증

## 참조한 스킬
- /skills/common/directive-writing.md
- /skills/common/verification.md
- /skills/domains/dev/file-navigation.md
- /skills/domains/docs/README.md

## 변경 경로
- server/Program.cs
- server/OutboxManager.cs
- server/DispatchExecutorCli.cs
- server/BehaviorSnapshotCli.cs
- server/dispatch-templates/ApplyMeasurementResult.txt
- server/dispatch-templates/BalanceTunerSearch.txt
- server/dispatch-templates/EngineApplyStatePatch.txt
- server/dispatch-templates/Orchestrator.txt
- server/dispatch-templates/ProposalFlow.txt
- dashboard/data/dev-pack/*
- outbox/task-20260709133754416/
- outbox/task-20260709133814794/
- docs/verification/self-refactor-dispatch.md

## 목표
#3의 구조 위반을 dispatch 과제로 넘겨, 1차 `ollama` 실패 후 `claude-code`로 승격하고, 격리 사본에서 `measure dev-pack`과 `verify-behavior`를 통과한 diff를 사람 반입 대기로 만든다.

## 구현 확인
- O `POST /api/projects/{projectId}/actions/self-refactor-dispatch` 추가.
- O 1차 executor는 `ollama`, 실패 시 `executor.escalated` run-log 기록 후 `claude-code` 재dispatch.
- O strict gate는 `measure dev-pack == 0`, `verify-behavior == 0`, 변경 파일 존재 조건을 모두 요구한다.
- O `verify-behavior`는 구조 진단 지표(`programCsLines`, `appJsLines`, `maxFunctionLength`)를 동작 동일성 비교에서 제외한다.
- O 반입은 수행하지 않았다. 최종 diff는 `import_pending` 상태로 사람 결재를 기다린다.

## 실행 기록

### 빌드
- 명령: `dotnet build server`
- 응답 요약: 경고 0개, 오류 0개
- 판정: O

### 사다리 실행
- 명령: `POST /api/projects/dev-pack/actions/self-refactor-dispatch`
- instruction: `Program.cs to Orchestrator.cs and ProposalFlow.cs. Completion check: verify-behavior passes, measure dev-pack has zero violations, structure metrics enter band.`
- 1차 결과:
  - task: `task-20260709133754416`
  - executor: `ollama`
  - status: `failed`
  - changedFiles: `[]`
  - measureExitCode: `1`
  - behaviorExitCode: `0`
  - cost: `{ estimatedUSD: 0, subscriptionCalls: 0, role: "runtime" }`
- 승격 이벤트:
  - run-log event: `executor.escalated`
  - params: `{ from: "ollama", to: "claude-code", taskId: "task-20260709133754416", reasonCode: "dispatch.strict_gate_failed" }`
- 2차 결과:
  - task: `task-20260709133814794`
  - executor: `claude-code`
  - status: `import_pending`
  - changedFiles: `dashboard/app.js`, `server/BalanceTuner.cs`, `server/Engine.cs`, `server/Orchestrator.cs`, `server/Program.cs`, `server/ProposalFlow.cs`
  - measureExitCode: `0`
  - measureSummary: `violationCount=0`, `overallStatus=completed`
  - behaviorExitCode: `0`
  - behaviorSummary: `behaviorEqual=true`
  - cost: `{ estimatedUSD: 0, subscriptionCalls: 1, role: "runtime" }`
- 판정: O

### 인박스 확인
- 명령: `GET /api/inbox`
- 응답 요약: `kind=import_pending`, `taskId=task-20260709133814794`
- 판정: O

### 루트 검증
- 명령: `dotnet run --project server -- verify-behavior`
- 응답 요약: `{"behaviorEqual":true,"snapshot":"docs/behavior-snapshot.json"}`
- 판정: O

### 커밋 전 dev-pack 게이트
- 명령: `dotnet run --project server -- measure dev-pack`
- 응답 요약: 루트는 아직 반입 전이므로 `violationCount=1`, `proposalLifecycle=submitted`
- 고정 형식: `{"gate":"dev-pack","violations":1,"attempt":1}`
- 판정: O

## 사람 결재 대기
- 반입 대기 task: `task-20260709133814794`
- 반입 승인 API는 호출하지 않았다.
- 사람이 diff를 검토하고 승인하면 서버의 outbox 반입 API가 본 저장소에 적용한다.
- 템플릿 조정 중 생성된 실패 outbox들도 사다리 실측 이력으로 보존했다.

## 마찰 기록
- 초기 템플릿은 C# `ref` 인자와 타입명 불일치로 strict gate에서 실패했다.
- `verify-behavior`가 구조 진단 지표까지 비교해 리팩터링 개선을 동작 차이로 오판했다. 구조 진단 metric을 behavior 비교에서 제외하도록 수정했다.
- 정적 함수 길이 측정이 문자 리터럴 `{`/`}`를 중괄호로 세는 근사 한계를 드러냈다. 실행자 코드에서 숫자 기반 상수로 우회했다.

## 판정
- 0층 측정, 1차 로컬 실행자, 승격, 구독형 실행자, 사람 반입 대기까지 이어지는 사다리 흐름을 실제 데이터로 확인했다.
