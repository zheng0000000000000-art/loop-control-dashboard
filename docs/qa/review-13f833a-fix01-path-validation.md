# FIX-01 path validation review

검수 대상: `13f833a` / FIX-01
검수자: Codex
검수일: 2026-07-11

## 독립 재실행
- `dotnet build server -c Release`: 경고 0, 오류 0
- `dotnet run --project server -c Release --no-build -- verify-behavior`: `{"behaviorEqual":true,"snapshot":"docs/behavior-snapshot.json"}`
- `dotnet run --project server -c Release --no-build -- measure dev-pack`: exit 1, `{"projectId":"dev-pack","violationCount":3,"proposalId":"proposal-1783750546584","proposalLifecycle":"submitted","createdBy":{"provider":"ollama","model":"qwen3:8b"},"currentStage":"changeReview","overallStatus":"warning"}`

## 코드 확인
- `server/Storage.cs`
  - `ProjectFilePath`: `StartsWith(projectPath, ...)` 대신 `IsWithinRoot(fullPath, projectPath)` 사용.
  - `ProjectPath`: `StartsWith(DataRoot, ...)` 대신 `IsWithinRoot(fullPath, DataRoot)` 사용.
  - `IsWithinRoot` helper 존재.
- `server/OutboxManager.cs`
  - `ResolveTaskDirectory`: `StartsWith(outboxRoot, ...)` 대신 `IsWithinRoot(fullPath, outboxRoot)` 사용.
  - `SafeWorkspacePath`: `StartsWith(workspaceRoot, ...)` 대신 `IsWithinRoot(fullPath, workspaceRoot)` 사용.
  - `IsWithinRoot` helper 존재.
- 남은 `StartsWith` 매치는 경로 문자열 정규화/상대 경로 필터 또는 `IsWithinRoot` 내부의 separator-bounded 검사다.

## 불변식
- 기준 파일 무수정: Y
  - `git diff --stat 13f833a^..db0e836 -- "**/blueprint.json" "**/workflow-definition.json"`: 빈 결과
- 비밀 미포함: Y
  - `server/appsettings.json` diff 없음
- 코어 파일 도메인 오염: Y
  - `rg -in "path|IsWithinRoot|outbox|Storage" server/Engine.cs server/Guardrails.cs`: 매치 없음
- 영역 격리: Y
  - FIX-01 변경은 `server/Storage.cs`, `server/OutboxManager.cs`, 관련 docs/wiki/handoff 문서로 한정됨.

## 검증문서 주장 vs 실측
- 핵심 주장 일치:
  - build 0/0
  - `verify-behavior:true`
  - `measure dev-pack` 위반 3 비악화
  - Storage/OutboxManager의 sibling-prefix 취약 `StartsWith(root)` 경계 검사가 `IsWithinRoot`로 교체됨
- 비고:
  - 검증 문서의 예시 snippet은 `StringComparison.Ordinal` 및 `normalizedPath.TrimEnd(...)` 형태지만 실제 구현은 `StringComparison.OrdinalIgnoreCase`와 `normPath = Path.GetFullPath(fullPath)` 형태다. Windows 경로 기준으로 완료 기준을 깨는 차이는 아니며, 문서 예시와 실제 코드의 세부 표현 차이로 기록한다.

## 판정
- PASS
