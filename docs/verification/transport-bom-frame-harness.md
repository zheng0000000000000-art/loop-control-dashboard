# transport BOM frame harness

## 주체

Codex 지시자/하네스 작성 세션.

## 참조한 스킬

- `skills/common/executor-launch.md`
- `skills/common/powershell-encoding.md`
- `skills/common/verification.md`

## 문제

Claude CLI 자체는 연결되어 있었지만 `run-executor.ps1`의 stream-json stdin 경로가 실패했다.
stderr에는 JSON 줄 앞에 UTF-8 BOM이 붙은 것으로 보였다.

R4 실패 예:

```text
Error parsing streaming input line: ﻿{"type":"user",...}
SyntaxError: JSON Parse error: Unrecognized token '﻿'
```

## 원인 분리

`run-executor.ps1`에 stdin frame evidence를 추가해 확인했다.

- `payloadFramePrefixHex`: `7b2274797065223a`
- `payloadFrameBomPresent`: `false`

즉 프롬프트 파일이나 payload 생성 결과에는 BOM이 없었다. 실패는 `Process.StandardInput` 전달 계층에서 발생했다. `BaseStream.Write`만으로는 충분하지 않았다.

## 변경

- `outputs/launch/run-executor.ps1`
  - 자식 프로세스 시작 전 stdin frame 첫 바이트가 `0x7b`인지 검사한다.
  - frame이 UTF-8 BOM으로 시작하면 발사 전 중단한다.
  - `payloadFrameSha256`, `payloadFrameByteLength`, `payloadFramePrefixHex`, `payloadFrameBomPresent`를 transport evidence에 기록한다.
  - `Process.StandardInput` 대신 BOM 없는 `.stdin.jsonl` 파일을 만들고 `cmd /c ... < file` raw stdin 리다이렉션으로 Claude CLI에 전달한다.
  - `cmd.exe` wrapper 아래의 실제 `claude.exe` PID를 찾아 `executorPid`로 기록한다. `pid`와 `sonnet-active.pid`도 executorPid를 쓴다.
  - 실행 시작 시 이전 `.out/.err/.transport/.stdin` 산출물을 삭제해 stale stderr 오판을 막는다.
- `server/Harness/LaunchCheckCli.cs`
  - 새 frame evidence가 있으면 prefix가 `7b`로 시작하는지, BOM flag가 false인지 검사한다.
  - BOM fixture는 `stdin-frame-prefix`, `stdin-frame-bom` 실패로 잡는다.
  - `cmd-stdin-file-redirection` evidence에서 `wrapperPid`와 `executorPid`가 분리되어 있고 `executorPidDiscovered=true`인지 검사한다.
- `skills/common/executor-launch.md`
  - stdin stream-json preflight, wrapper/executor PID 분리, fallback 기록 규칙을 추가했다.

## 실측

| 항목 | 결과 |
| --- | --- |
| 정상 frame fixture | `launch-check` exit 0, `TRANSPORT_VALID` |
| BOM frame fixture | `launch-check` exit 1, `failureCount=3` |
| 실제 `TRANSPORT-BOM-PROBE` launch | exit 0, `transportValid=true` |
| `payloadFramePrefixHex` | `7b2274797065223a` |
| `payloadFrameBomPresent` | `false` |
| `replayEventCount` | 1 |
| `payloadSha256 == replaySha256` | true |
| `wrapperPid != executorPid` | true |
| `executorProcessName` | `claude.exe` |
| stale err log | 없음 |

## 사용한 명령

| 명령 | 결과 |
| --- | --- |
| `dotnet build server -c Release -nologo` | exit 0 |
| `dotnet run --project server -c Release -- launch-check TRANSPORT-BOM-PROBE outputs/launch/TRANSPORT-BOM-PROBE.transport.json` | exit 0, `TRANSPORT_VALID` |
| executor PID 미발견 fixture | `launch-check` exit 1, `failureCount=2` |
| `dotnet run --project server -c Release -- verify-behavior` | exit 0, `behaviorEqual:true` |
| `dotnet run --project server -c Release -- measure dev-pack` | exit 0, `violationCount=0` |

게이트 기록:

`{"gate":"dev-pack","violations":0,"attempt":1}`

## 위반 발생 및 해소

중간에 `measure dev-pack`이 `maxFunctionLength=232`로 1건 실패했다. 원인은 `LaunchCheckCli.cs`의 오류 메시지 문자열에 포함된 `{` 문자를 측정기가 실제 중괄호로 세어 함수 끝을 파일 끝으로 오판한 것이다. 측정 코드를 수정하지 않고 메시지에서 `{` 문자를 제거해 `violationCount=0`으로 복구했다.

또 한 번 `dotnet build`와 `measure dev-pack`을 병렬 실행해 `server/obj/Release/...dll` 파일 잠금 오류를 만들었다. 코드 결함이 아니라 검증 명령 병렬화 문제였고, 순차 재실행으로 통과했다.

## 남은 위험

- `stdinFramePath`가 outputs 아래 산출물로 남는다. prompt 본문을 담고 있으므로 민감 정보가 포함된 prompt에는 별도 삭제/보존 정책이 필요하다.
