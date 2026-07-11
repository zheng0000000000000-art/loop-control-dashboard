# H-1 path-guard-check 하네스 QA

## 주체(actor)

- actor: codex
- 회차: 2026-07-11 20:45 KST
- 대상: `server/Harness/PathGuardCheckCli.cs`, `server/Harness/HarnessRegistry.cs`
- 금지 준수: `server/Cli/CliRouter.cs` 미수정, git commit/push 미실행, 결재·반입 액션 미호출

## 참조한 스킬

- `skills/common/root-cause-diagnosis.md`
- `skills/common/hs-gate.md`
- `docs/handoff/CODEX-AUTO-15min-routine.md`
- `docs/handoff/CODEX-QUEUE.md`

## 데이터 존재 확인

- 실체 데이터: `FAIL-2026-006`, `FAIL-2026-007`, `docs/verification/fix01-path-validation.md`
- 판정 데이터: `Path.GetFullPath`로 정규화한 root/candidate full path
- 판정: 데이터 존재 관문 PASS. 하네스는 파일명 문자열 프록시가 아니라 full path equality 또는 root+separator 관계를 검사한다.

## 변경 내용

- `path-guard-check` 하네스 추가
- 기본 회귀 모드: root equality, child, trailing root separator, storage sibling-prefix, outbox sibling-prefix, parent traversal to sibling 6건 검사
- 입력 모드: `path-guard-check <root> <candidate...>`에서 모든 candidate가 root 내부인지 검사
- `HarnessRegistry.cs`에 `["path-guard-check"] = PathGuardCheckCli.Run` 1줄 등록

## 사용한 하네스와 결과

| 명령 | exit code | 수치/결과 |
| --- | ---: | --- |
| `dotnet run --project server -c Release -- hs-scan` | 1 | `failureCaseCount=14`, candidate=`executor-orchestration(6)` |
| `dotnet build server -c Release` | 0 | 경고 0, 오류 0 |
| `dotnet run --project server -c Release --no-build -- path-guard-check` | 0 | `caseCount=6`, `failureCount=0`, `verdict=PASS` |
| `dotnet run --project server -c Release --no-build -- path-guard-check <root> <child>` | 0 | `actualWithinRoot=true`, `verdict=PASS` |
| `dotnet run --project server -c Release --no-build -- path-guard-check <root> <sibling-prefix>` | 1 | `actualWithinRoot=false`, `verdict=FAIL` |
| `dotnet run --project server -c Release --no-build -- verify-behavior` | 0 | `behaviorEqual=true` |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | 1 | `violationCount=1`, 기존 위반 잔존 |
| `dotnet run --project server -c Release --no-build -- hs-scan` | 1 | 반복 후보 유지 |

## 판정

- H-1 완료: PASS
- 재현/의심/오탐: 재현 2건(storage/outbox sibling-prefix 차단), 의심 0건, 오탐 0건
- 남은 사항: `hs-scan`은 계속 exit 1이므로 다음 회차에서 H-2/H-3/H-4 후보를 이어서 처리
