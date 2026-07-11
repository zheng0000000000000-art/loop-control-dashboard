# H-6 claim-check 오탐 수정 QA

## 주체(actor)

- actor: codex
- 회차: 2026-07-11 20:15 KST
- 대상: `server/Harness/ClaimCheckCli.cs`
- 금지 준수: `server/Cli/CliRouter.cs` 미수정, git commit/push 미실행, 결재·반입 액션 미호출

## 참조한 스킬

- `skills/common/root-cause-diagnosis.md`
- `skills/common/hs-gate.md`
- `docs/handoff/CODEX-AUTO-15min-routine.md`
- `docs/handoff/CODEX-QUEUE.md`

## 데이터 존재 확인

- 실체 데이터: `docs/verification/actor01-actor-provenance.md`의 빌드 명령 `server/LocalFirstWorkflowDashboard.Server.csproj`
- 수정 전 실측: `claim-check ACTOR-01`이 위 문자열을 `server/LocalFirstWorkflowDashboard.Server.cs` 파일 존재 주장으로 오인
- 수정 전 결과: exit 1, `claimCount=13`, `mismatchCount=1`, `verdict=MISMATCH`

## 변경 내용

- `ExtractClaimedFiles` 정규식에 `.cs` 뒤 종단 경계 `(?![A-Za-z0-9])` 추가
- 목적: `.csproj`, `.csv`처럼 `.cs` 다음에 영문/숫자가 이어지는 확장자를 `.cs` 파일 주장으로 추출하지 않기

## 사용한 하네스와 결과

| 명령 | exit code | 수치/결과 |
| --- | ---: | --- |
| `dotnet run --project server -c Release -- hs-scan` | 1 | `failureCaseCount=14`, candidate=`executor-orchestration(6)` |
| `dotnet run --project server -c Release --no-build -- claim-check ACTOR-01` (수정 전) | 1 | `claimCount=13`, `mismatchCount=1`, `.csproj` 오탐 재현 |
| `dotnet build server -c Release` | 0 | 경고 0, 오류 0 |
| `dotnet run --project server -c Release --no-build -- claim-check ACTOR-01` (수정 후) | 0 | `claimCount=12`, `mismatchCount=0`, `verdict=MATCH` |
| PowerShell regex probe | 0 | `server/Foo.cs`, `server/Bar.cs`, `server/Baz.cs`만 매치. `server/Foo.csproj`, `server/Foo.csv` 미매치 |
| `dotnet run --project server -c Release --no-build -- verify-behavior` | 0 | `behaviorEqual=true` |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | 1 | `violationCount=1`, 기존 위반 잔존 |
| `dotnet run --project server -c Release --no-build -- hs-scan` | 1 | `failureCaseCount=14`, candidate=`executor-orchestration(6)` |

## 판정

- H-6 완료: PASS
- 재현/의심/오탐: 재현 1건(`.csproj` 오탐), 의심 0건, 오탐 0건
- 남은 사항: `hs-scan`은 계속 exit 1이므로 다음 회차에서 H-1/H-7/H-2 후보를 이어서 처리
