# H-3 template-sync-check 하네스 QA

## 주체(actor)

- actor: codex
- 회차: 2026-07-11 21:15 KST
- 대상: `server/Harness/TemplateSyncCheckCli.cs`, `server/Harness/HarnessRegistry.cs`
- 금지 준수: `server/Cli/CliRouter.cs` 미수정, git commit/push 미실행, 결재·반입 액션 미호출

## 참조한 스킬

- `skills/common/root-cause-diagnosis.md`
- `skills/common/hs-gate.md`
- `docs/handoff/CODEX-AUTO-15min-routine.md`
- `docs/handoff/CODEX-QUEUE.md`

## 데이터 존재 확인

- 실체 데이터: `server/dispatch-templates/*.txt`, `server/DispatchExecutorCli.cs`, `FAIL-2026-008`, `docs/verification/fail-2026-008-template-sync.md`
- 판정 방식: temp copy에서 템플릿 적용 후 `dotnet build` exit code
- 판정: 데이터 존재 관문 PASS. 하네스는 문서 주장 대신 템플릿을 실제 적용하고 빌드 결과로 판정한다.

## 변경 내용

- `template-sync-check` 하네스 추가
- 기본 모드: temp copy에 server + dashboard/app.js를 복사, `dispatch-executor` self-refactor 템플릿 적용, temp server Release 빌드
- 장애주입 모드: `--inject-missing-template`로 `ApplyMeasurementResult.txt`를 temp copy에서 삭제해 FAIL 경로 확인
- `HarnessRegistry.cs`에 `["template-sync-check"] = TemplateSyncCheckCli.Run` 1줄 등록

## 사용한 하네스와 결과

| 명령 | exit code | 수치/결과 |
| --- | ---: | --- |
| `dotnet run --project server -c Release -- hs-scan` | 1 | `failureCaseCount=14`, candidate=`executor-orchestration(6)` |
| `dotnet build server -c Release` | 0 | 경고 0, 오류 0 |
| `dotnet run --project server -c Release --no-build -- template-sync-check` | 0 | `dispatchExitCode=0`, `buildExitCode=0`, `verdict=PASS` |
| `dotnet run --project server -c Release --no-build -- template-sync-check --inject-missing-template` | 1 | `dispatchExitCode=1`, `buildExitCode=1`, `verdict=FAIL` |
| `dotnet run --project server -c Release --no-build -- verify-behavior` | 0 | `behaviorEqual=true` |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | 1 | `violationCount=1`, 기존 위반 잔존 |
| `dotnet run --project server -c Release --no-build -- hs-scan` | 1 | 반복 후보 유지 |

## 판정

- H-3 완료: PASS
- 재현/의심/오탐: 재현 1건(템플릿 누락 주입), 의심 0건, 오탐 0건
- 남은 사항: H-4 `path-escape-qa` skill, H-5 기존 하네스 인수 검토
