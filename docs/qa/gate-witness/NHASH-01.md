# NHASH-01 gate witness

- actor: HARNESS_EXECUTOR (codex)
- date: 2026-07-26
- direct-path reason: 지시서가 `server/Harness/**`를 ADR-002 배타 영역으로 지정하고 직접 실행을 요구했다.
- referenced skills: `skills/common/**` 전체, 특히 `directive-writing.md`, `powershell-encoding.md`, `root-cause-diagnosis.md`, `verification.md`

## 변경

- `NormalizedContentHash.Compute` 한 곳으로 기존 `GateCleanCli` 정규화 규칙을 이동했다.
- `GateCleanCli`와 `HandoffIntegrityCli`의 `changedFiles` 비교가 공용 정의를 호출한다.
- BOM 제거, CRLF/CR의 LF 변환, 줄 후행 공백·탭 제거, 끝 개행 통일 규칙은 변경하지 않았다.
- `gate-clean --normalized-hash-self-test`에 네 반증 사례를 추가했다.

## 반증 시험 실측

명령:

`dotnet run --project docs/qa/gate-witness/NHashProbe/NHashProbe.csproj`

결과: exit 0, `caseCount=4`, `mismatchCount=0`

| 사례 | expectedEqual | actualEqual |
| --- | ---: | ---: |
| CRLF와 LF | true | true |
| 한 글자 이상 내용 변경 | false | false |
| 줄 끝 공백·탭만 변경 | true | true |
| BOM 유무 | true | true |

프로브는 사본 구현이 아니라 `server/Harness/NormalizedContentHash.cs`를 링크해 실행한다.

## 제품 프로젝트 명령

다음 명령은 모두 exit 1이었다.

- `dotnet build server --no-restore`
- `dotnet run --project server -- build-verify`
- `dotnet run --project server -- gate-clean --normalized-hash-self-test`
- `dotnet run --project server -- gate-clean --status-fixture docs/qa/gate-witness/gate-clean-dirty.status`
- `dotnet run --project server -- gate-clean --status-fixture docs/qa/gate-witness/gate-clean-clean.status`
- `dotnet run --project server -- gate-clean server`
- `dotnet run --project server -- handoff-integrity`
- `dotnet run --project server -- context-pack-integrity`
- `dotnet run --project server -- measure dev-pack`

공통 원인은 컴파일 오류가 아니라 복원 단계의 `NU1301`이다. 격리 환경에서
`https://api.nuget.org/v3-index/repository-signatures/5.0.0/index.json`에 연결할 때
SSL 인증에 사용할 인증서가 없었다. `--no-restore`도 같은 오류로 중단됐다.
따라서 기존 두 status fixture의 exit·판정, `gate-clean server`, `build-verify`,
`handoff-integrity`, context-pack stale pin, dev-pack violations는 본체에 진입하지 못해 실측되지 않았다.

`handoff-integrity`는 쓰는 쪽 `ProjectionCli` 미반영 때문에 정상 실행 환경에서도 기존
원시 바이트 스탬프와 정규화 해시가 어긋날 수 있는 단계다. 이번 실행의 exit 1은 그 예상
불일치가 아니라 그보다 앞선 NuGet 복원 중단이다.

{"gate":"dev-pack","violations":null,"attempt":1,"exitCode":1,"execution":"not-reached","reason":"NU1301 repository-signature SSL certificate unavailable"}

## 정적 범위 확인

- `GateCleanCli` 안의 기존 `NormalizedHash` 정의는 제거됐다.
- `server/Harness/**`의 정규화 정의는 `NormalizedContentHash.Compute` 하나다.
- 영역 밖 `server/OrchestratorObserverCli.cs`에는 기존 사본이 남아 있다. 지시서 §6에 따라
  조율자가 같은 반입 변경에서 공용 정의로 교체해야 `server/**` 전체 정의가 하나가 된다.
- `server/ProjectionCli.cs`, `server/OrchestratorObserverCli.cs`, `WORKSTATE.json`,
  `GATE-MANIFEST.json`은 수정하지 않았다.

## 미실측 및 후속

- 복원이 가능한 환경에서 제품 프로젝트 명령 전부를 다시 실행해야 한다.
- `context-pack-integrity`가 stale 경로를 출력하면 해당 경로를 큐 전체에서 검색해 실제 소유
  지시서와 `requiredInputs`/`readOrder` 위치를 확인해야 한다. 이번에는 검사 본체가 실행되지 않아
  stale 경로가 출력되지 않았고 소유자 검색 입력도 없었다.
- 조율자는 `ProjectionCli` 쓰기 경로와 `OrchestratorObserverCli` 사본을 같은 공용 정의로 바꾸고
  재스탬프한 뒤 깨끗한 클론에서 최종 프로그램 검증을 수행해야 한다.

## 지표는 만족했으나 목적은 미달인 부분

네 정규화 반증 사례는 실제 공용 구현으로 측정했다. 그러나 제품 프로젝트 복원이 중단되어
기존 gate-clean 판정 보존과 handoff 전체 경로는 측정하지 못했다.
