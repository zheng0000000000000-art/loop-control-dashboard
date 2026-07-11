# H-2 call-integrity-check 하네스 QA

## 주체(actor)

- actor: codex
- 회차: 2026-07-11 21:00 KST
- 대상: `server/Harness/CallIntegrityCheckCli.cs`, `server/Harness/HarnessRegistry.cs`
- 금지 준수: `server/Cli/CliRouter.cs` 미수정, git commit/push 미실행, 결재·반입 액션 미호출

## 참조한 스킬

- `skills/common/root-cause-diagnosis.md`
- `skills/common/hs-gate.md`
- `docs/handoff/CODEX-AUTO-15min-routine.md`
- `docs/handoff/CODEX-QUEUE.md`

## 데이터 존재 확인

- 실체 데이터: DI-R-01~04 검증 문서와 현재 C# 파일의 정의/호출부
- 기본 룰: `CliRouter.TryRun`, `InboxBuilder.BuildInboxItems`, `InboxBuilder.AddProjectInboxItems`, `CycleSummaryBuilder.BuildCycleSummary`, `MeasurementService.RunMeasureCore`
- 판정: 데이터 존재 관문 PASS. 하네스는 문서 주장 대신 실제 `.cs` 파일의 정의 위치와 호출부를 읽는다.

## 변경 내용

- `call-integrity-check` 하네스 추가
- 기본 모드: DI-R-01~04 대표 이동 심볼 5건 검사
- 입력 모드: `call-integrity-check <Qualifier.Method> <definitionFile> <staleCallFile>` 단일 룰 검사
- 검사 기준: 예상 파일에 정의 1건, qualified call 최소 수 이상, `server/Program.cs` stale unqualified call 0건
- `HarnessRegistry.cs`에 `["call-integrity-check"] = CallIntegrityCheckCli.Run` 1줄 등록

## 사용한 하네스와 결과

| 명령 | exit code | 수치/결과 |
| --- | ---: | --- |
| `dotnet run --project server -c Release -- hs-scan` | 1 | `failureCaseCount=14`, candidate=`executor-orchestration(6)` |
| `dotnet build server -c Release` | 0 | 경고 0, 오류 0 |
| `dotnet run --project server -c Release --no-build -- call-integrity-check` | 0 | `ruleCount=5`, `failureCount=0`, `verdict=PASS` |
| `dotnet run --project server -c Release --no-build -- call-integrity-check MeasurementService.RunMeasureCore server/Program.cs server/Program.cs` | 1 | 잘못된 definition file 주입, `failureCount=1`, `verdict=FAIL` |
| `dotnet run --project server -c Release --no-build -- verify-behavior` | 0 | `behaviorEqual=true` |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | 1 | `violationCount=1`, 기존 위반 잔존 |
| `dotnet run --project server -c Release --no-build -- hs-scan` | 1 | 반복 후보 유지 |

## 판정

- H-2 완료: PASS
- 재현/의심/오탐: 재현 1건(정의 위치 불일치 주입), 의심 0건, 오탐 0건
- 남은 사항: H-3 `template-sync-check`, H-4 `path-escape-qa` skill, H-5 기존 하네스 인수 검토
