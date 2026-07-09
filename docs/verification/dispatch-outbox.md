# dispatch outbox 1단계 검증

## 참조한 스킬
- /skills/common/directive-writing.md
- /skills/common/verification.md
- /skills/domains/dev/file-navigation.md
- /skills/domains/docs/README.md

## 변경 경로
- server/Program.cs
- server/OutboxManager.cs
- server/DispatchExecutorCli.cs
- docs/DECISIONS.md
- README.md
- outbox/task-20260709131551207/
- outbox/task-20260709131628683/
- docs/verification/dispatch-outbox.md

## 실행 기록

### 빌드
- 명령: `dotnet build server`
- 응답 요약: 경고 0개, 오류 0개
- 판정: O

### 서버 기동
- 명령: `ASPNETCORE_URLS=http://127.0.0.1:5199 RemoteActionToken=dispatch-test-token dotnet run --project server`
- 확인: `GET /api/inbox`
- 응답 요약: HTTP 200
- 판정: O

### 지시 게이트
- 명령: `POST /api/projects/dev-pack/actions/dispatch`
- body: `{ "executor": "ollama", "instruction": "README..." }`
- 응답 요약: `status=needs_questions`, 질문 항목 생성
- 판정: O

### README dispatch와 반입
- 명령: `POST /api/projects/dev-pack/actions/dispatch`
- body: `{ "executor": "ollama", "instruction": "README one line update. Completion check: README changed." }`
- 응답 요약: `task-20260709131551207`, `status=import_pending`, `changedFiles=["README.md"]`, `deletedFiles=[]`, `cost.role=runtime`, `subscriptionCalls=0`
- inbox 확인: `GET /api/inbox`에 `kind=import_pending`, `taskId=task-20260709131551207`
- 반입 명령: `POST /api/projects/dev-pack/outbox/task-20260709131551207/approve-import`
- 반입 응답: `status=imported`
- 결과 파일: `README.md`에 `Dispatch verification line.` 추가
- 판정: O

### timeout 실패 경로
- 명령: `POST /api/projects/dev-pack/actions/dispatch`
- body: `{ "executor": "ollama", "instruction": "server timeout test __timeout__. Completion check: timeout failure recorded." }`
- 응답 요약: `task-20260709131628683`, `status=failed`, `timedOut=true`, `executorExitCode=-1`, `cost.role=runtime`
- 판정: O

### 반입 diff와 게이트 결과
- diff 위치: `outbox/task-20260709131551207/diff.patch`
- measure 결과 위치: `outbox/task-20260709131551207/measure-result.json`
- 요약: 격리 사본의 dev-pack 측정은 기존 구조 위반 1건 때문에 `measureExitCode=1`을 기록했다.
- 판정: O

### 커밋 전 dev-pack 게이트
- 명령: `dotnet run --project server -- measure dev-pack`
- 응답 요약: `violationCount=1`, `proposalId=proposal-1783603096320`, `proposalLifecycle=submitted`
- 결과: `maxFunctionLength` 위반 1건 유지. 직전 #3에서 만든 리팩토링 입력 proposal이므로 승인하지 않았다.
- 고정 형식: `{"gate":"dev-pack","violations":1,"attempt":1}`
- 판정: O

### 코어 청결
- 명령: `rg -n "ollama|dispatch|outbox|metricId|completionRate|ruined|dev-pack|game" server/Engine.cs server/Storage.cs server/Guardrails.cs`
- 응답 요약: 이번 변경 문자열은 없음. `Storage.cs`의 기존 `GameDataFile` 상수만 감지됨.
- 판정: O

## 제한 사항
- 이번 1단계에서 대시보드 inbox에는 `import_pending` 항목이 표시된다.
- diff 전문과 반입 승인/거절은 API로 검증했다. 전용 대시보드 diff 화면은 다음 UI 단계에서 확장 대상이다.

## 판정
- 토큰 필수 dispatch, 격리 사본 실행, outbox 산출, inbox 반입 대기, 반입 적용, timeout 실패 보존을 확인했다.
