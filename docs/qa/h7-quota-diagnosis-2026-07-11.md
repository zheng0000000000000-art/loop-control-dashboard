# H-7 quota 진단 보강 QA

## 주체(actor)

- actor: codex
- 회차: 2026-07-11 20:30 KST
- 대상: `skills/common/root-cause-diagnosis.md`, `server/Harness/LaunchCheckCli.cs`
- 금지 준수: `server/Cli/CliRouter.cs` 미수정, git commit/push 미실행, 결재·반입 액션 미호출

## 참조한 스킬

- `skills/common/root-cause-diagnosis.md`
- `skills/common/hs-gate.md`
- `skills/common/executor-launch.md`
- `docs/handoff/CODEX-AUTO-15min-routine.md`
- `docs/handoff/CODEX-QUEUE.md`

## 데이터 존재 확인

- 실체 데이터: 실행 로그의 quota 문구(`hit your limit`, `rate limit`, `QUOTA_SIGNAL`), CLI exit code, 프로세스 생존 정보
- 큐 근거: `CODEX-QUEUE.md` H-7이 실제 한도 문구 `You've hit your limit · resets 5:40pm`를 기록
- 판정: 데이터 존재 관문 PASS. 로그 문자열은 원인 단정의 프록시가 아니라 실행자가 직접 낸 한도 신호이며, exit code와 함께 분해 가능

## 변경 내용

- `root-cause-diagnosis.md`에 0순위 감별 절 추가: 한도(quota)·인증(auth)·프로세스 생존을 먼저 배제
- `launch-check`에 `quotaSignal` 필드와 `QUOTA` verdict 추가
- 한도 신호가 있으면 ACK가 있어도 exit 1로 처리해 정상 실행 산출물과 분리

## 사용한 하네스와 결과

| 명령 | exit code | 수치/결과 |
| --- | ---: | --- |
| `dotnet run --project server -c Release -- hs-scan` | 1 | `failureCaseCount=14`, candidate=`executor-orchestration(6)` |
| `dotnet build server -c Release` | 0 | 경고 0, 오류 0 |
| `dotnet run --project server -c Release --no-build -- launch-check H7 <pass.log>` | 0 | `quotaSignal=false`, `verdict=PASS` |
| `dotnet run --project server -c Release --no-build -- launch-check H7 <quota.log>` | 1 | `quotaSignal=true`, `verdict=QUOTA`, ACK 없음 |
| `dotnet run --project server -c Release --no-build -- launch-check H7 <quota-ack.log>` | 1 | `quotaSignal=true`, `verdict=QUOTA`, ACK 있음 |
| `dotnet run --project server -c Release --no-build -- verify-behavior` | 0 | `behaviorEqual=true` |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | 1 | `violationCount=1`, 기존 위반 잔존 |
| `dotnet run --project server -c Release --no-build -- hs-scan` | 1 | 반복 후보 유지 |

## 판정

- H-7 완료: PASS
- 재현/의심/오탐: 재현 1건(한도 신호 분해), 의심 0건, 오탐 0건
- 남은 사항: `hs-scan`은 계속 exit 1이므로 다음 회차에서 H-1/H-2/H-3 후보를 이어서 처리
