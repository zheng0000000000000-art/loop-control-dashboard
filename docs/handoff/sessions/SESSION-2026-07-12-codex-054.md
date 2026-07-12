# SESSION-2026-07-12-codex-054

## 주체

- actor: codex
- 작업: `launch-check` 하네스를 ACK 기반에서 Transport Receipt 기반으로 교체
- 직접 경로 사유: 기존 하네스 교체이며 쓰기 범위가 코덱스 소유 `server/Harness/`, `docs/qa/`, `docs/handoff/sessions/`로 명시됨

## 변경

- `server/Harness/LaunchCheckCli.cs`에서 ACK stdout 탐지와 quota 문자열 탐지를 제거했다.
- `launch-check <taskId> <transportEvidencePath>`가 `outputs/launch/<TaskId>.transport.json` 계약을 검사하도록 바꿨다.
- `payloadSha256 == replaySha256`와 `replayEventCount == 1`일 때만 `TRANSPORT_VALID` exit 0을 반환한다.
- evidence 누락, replay 없음/중복, JSON 파싱 실패, 64자리 해시 위반, taskId 불일치, command-line fallback 흔적은 모두 fail-closed exit 1이다.
- `docs/handoff/LAUNCH-BUDGET.json`이 없으면 budget 검사는 `skipped`로 두고 exit code에 반영하지 않는다.

## 사용한 하네스와 exit code

| Harness | Command | Exit | Result |
| --- | --- | ---: | --- |
| launch-check pre-change missing evidence | `dotnet run --project server -c Release -- launch-check LEDGER-05 outputs/launch/LEDGER-05.transport.json` | 1 | old ACK harness failed on missing file |
| launch-check missing evidence | `dotnet run --project server -c Release -- launch-check LEDGER-05 outputs/launch/LEDGER-05.transport.json` | 1 | `evidence-missing`, `TRANSPORT_INVALID` |
| launch-check encoding damage fixture | temp evidence | 1 | mismatch detected |
| launch-check UTF-8 direct fixture | temp evidence | 0 | `TRANSPORT_VALID` |
| launch-check multilingual fixture | temp evidence | 0 | `TRANSPORT_VALID` |
| launch-check no replay event | temp evidence | 1 | fail-closed |
| launch-check two replay events | temp evidence | 1 | fail-closed |
| launch-check tampered JSON | temp evidence | 1 | fail-closed |
| launch-check one-character changed | temp evidence | 1 | mismatch detected |
| launch-check long prompt | temp evidence | 0 | command-line length irrelevant to harness input |
| launch-check command-line fallback marker | temp evidence | 1 | fallback detected |
| build | `dotnet build server -c Release` | 0 | warnings 0, errors 0 |
| forbidden phrase scan | searched `server/Harness/LaunchCheckCli.cs` for removed ACK variables, quota helpers, and model-scope verdict terms | 1 | no matches |
| dev-pack gate | `dotnet run --project server -c Release -- measure dev-pack` | 0 | `{"gate":"dev-pack","violations":0,"attempt":1}` |

## 참조한 스킬

- `skills/common/verification.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/directive-writing.md`
- `skills/common/executor-launch.md`
- `skills/common/hs-gate.md`
- `skills/domains/dev/file-navigation.md`
- `skills/domains/dev/path-escape-qa.md`

## 지표는 만족했으나 목적은 미달인 부분

- 발사 래퍼(`outputs/launch/run-executor.ps1`)는 이번 쓰기 영역이 아니라 구현하지 않았다. 따라서 실제 `outputs/launch/<TaskId>.transport.json` 생산은 다음 실행자 레인 작업으로 남는다.
- replay hash는 payload와 CLI replay 사이의 바이트 무결성만 증명한다. 모델의 이해, 주의, 순종은 이 하네스의 판정 범위가 아니다.
- `docs/handoff/LAUNCH-BUDGET.json`이 없어 budget 검사는 `skipped`였다. 정책 숫자는 승인 전이므로 새로 만들지 않았다.
