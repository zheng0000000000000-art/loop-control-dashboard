# FIX-01 path validation review

검수 대상: `FIX-01` / `docs/verification/fix01-path-validation.md`
검수자: Codex
검수일: 2026-07-11

## 독립 재실행
- `dotnet build server -c Release`: 경고 0, 오류 0
- `dotnet run --project server -c Release --no-build -- verify-behavior`: `{"behaviorEqual":true,"snapshot":"docs/behavior-snapshot.json"}`
- `dotnet run --project server -c Release --no-build -- measure dev-pack`: exit 1, `{"projectId":"dev-pack","violationCount":3,"proposalId":"proposal-1783746981687","proposalLifecycle":"submitted","createdBy":{"provider":"ollama","model":"qwen3:8b"},"currentStage":"changeReview","overallStatus":"warning"}`

## 핵심 불일치
- 검증 문서와 `WORKSTATE.json`은 `server/Storage.cs`, `server/OutboxManager.cs`에 `IsWithinRoot` separator-bounded 경계 검사가 반영됐다고 주장한다.
- 현재 워킹트리의 두 server 파일에는 해당 코드가 없다.
- 독립 확인 명령:
  - `rg -n "IsWithinRoot|StartsWith\\(.*StringComparison" server/Storage.cs server/OutboxManager.cs`
- 실측:
  - `server/Storage.cs:149`: `fullPath.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase)`
  - `server/Storage.cs:190`: `fullPath.StartsWith(DataRoot, StringComparison.OrdinalIgnoreCase)`
  - `server/OutboxManager.cs:651`: `fullPath.StartsWith(outboxRoot, StringComparison.OrdinalIgnoreCase)`
  - `server/OutboxManager.cs:664`: `fullPath.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase)`
  - `IsWithinRoot` 매치 없음

## 판정
- 판정: FAIL
- 사유: 검증 문서의 핵심 수정 반영 주장과 실제 코드 상태가 불일치한다. build/verify는 통과하지만, FIX-01의 완료 기준인 separator-bounded 경계 검사 코드가 현재 server 파일에 존재하지 않는다.

## 비고
- `docs/handoff/HUMAN-INBOX.md`도 동일한 방향의 조율자 판단을 기록하고 있다: FIX-01 dispatch 반복 실패 및 문서/코드 불일치.
- 코덱스는 server 코드를 수정하지 않았다.
