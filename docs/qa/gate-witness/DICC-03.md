# DICC-03 gate witness

- actor: HARNESS_EXECUTOR (codex)
- date: 2026-07-26
- DI 유형: harness
- 직접 경로 사유: 지시서가 `server/Harness/**`를 ADR-002 코덱스 배타 영역으로 지정했다.
- 참조한 스킬: `skills/common/**` 전체. 도메인 스킬은 변경 경로와 일치하는 트리거가 없어 읽지 않았다.

## 변경

`DiCompletionCheckCli`가 검사 실행 전에 `git rev-parse HEAD`와
`git status --porcelain`을 실행한다. 그 결과를 모든 보고의 공통 필드인
`baselineCommit`, `worktreeCleanAtStart`로 기록한다. Git 값을 얻지 못하면 커밋은 빈
문자열, 청결 상태는 `false`로 남겨 알 수 없는 값을 지어내지 않는다.

판정 계산, `failures`, 검사 실행, exit code 로직은 변경하지 않았다.

## 컴파일

호스트에는 .NET 8 런타임만 있고 .NET 8 SDK 타기팅 팩은 없었다. 네트워크 복원은
NuGet TLS 인증서 오류로 차단됐다. 설치된 .NET 10 SDK 타기팅 팩을 사용한 다음 명령으로
동일 소스를 컴파일했다.

```text
dotnet build server -c Release -p:TargetFramework=net10.0 -p:TargetFrameworks=net10.0 -p:RestoreIgnoreFailedSources=true
exit 0
경고 0개
오류 0개
```

## 반증 시험

### 1. 실제 보고를 처분 소비자에 왕복

보고 생성 명령:

```text
.\server\bin\Release\net10.0\LocalFirstWorkflowDashboard.Server.exe di-completion-check --gate POST-COMMIT --launch LAUNCH-PROBE --task LAUNCH-PROBE
exit 1
baselineCommit: d403d7cab010adcc96b8ff2dd724eb0f36ff46aa
worktreeCleanAtStart: false
launchId: LAUNCH-PROBE
checkCount: 14
failureCount: 7
evidencePath: outputs/gates/LAUNCH-PROBE.gate.json
```

상위 보고의 exit 1은 net10 실행 파일 안의 하위 명령이 저장소의 기본 net8
`dotnet run --no-build`를 찾지 못해 생겼다. 이 시험의 대상인 실제 보고 파일은 생성됐다.

`importCommit`을 위 커밋과 같은 실제 커밋으로 두고, `gateReport`를 위 실제 보고로 가리킨
임시 `disposition.json`을 만든 뒤 실행했다. 임시 처분은 실행 후 삭제했다.

```text
.\server\bin\Release\net10.0\LocalFirstWorkflowDashboard.Server.exe launch-disposition docs/qa/gate-witness/dicc03-live-probe
exit 0
launchCount: 1
violations: 0
launchId: LAUNCH-PROBE
violation: false
reason: null
```

### 2. HEAD 문자열 대조

```text
git rev-parse HEAD
d403d7cab010adcc96b8ff2dd724eb0f36ff46aa
```

실제 보고의 `baselineCommit`과 문자열이 같다.

### 3. 더러운 워크트리

소스 변경이 존재하는 상태에서 실행한 실제 보고:

```text
worktreeCleanAtStart: false
baselineCommit: d403d7cab010adcc96b8ff2dd724eb0f36ff46aa
```

### 4. 낡은 바이너리 거부

낡은 호스트 경로로 실행해 생성된 보고:

```text
dotnet server/bin/Release/net10.0/LocalFirstWorkflowDashboard.Server.dll di-completion-check --gate POST-COMMIT --launch LAUNCH-PROBE --task LAUNCH-PROBE
exit 2
verdict: NOT-MEASURED
gateVerdict: NOT-MEASURED
launchId: LAUNCH-PROBE
baselineCommit: d403d7cab010adcc96b8ff2dd724eb0f36ff46aa
worktreeCleanAtStart: false
checkCount: 0
failureCount: 0
reason: binary-stale
```

### 5. `--launch` 없는 실행

```text
.\server\bin\Release\net10.0\LocalFirstWorkflowDashboard.Server.exe di-completion-check --gate POST-COMMIT --task DICC03-NO-LAUNCH
exit 1
gateVerdict: FAIL
failureCount: 7
launchId 필드: 없음
baselineCommit: d403d7cab010adcc96b8ff2dd724eb0f36ff46aa
worktreeCleanAtStart: false
```

같은 환경의 `--launch LAUNCH-PROBE` 실행도 exit 1, `gateVerdict: FAIL`,
`failureCount: 7`이었다. `--launch`는 귀속 필드 외 판정과 exit에 영향을 주지 않았다.

### 6. 기존 `case-01`~`case-20`

`launch-disposition`을 각 디렉터리에 직접 실행했다.

```text
case-01=1 case-02=0 case-03=1 case-04=1 case-05=1
case-06=0 case-07=0 case-08=1 case-09=1 case-10=1
case-11=1 case-12=1 case-13=1 case-14=1 case-15=1
case-16=1 case-17=1 case-18=1 case-19=1 case-20=0
```

대표 사유는 `disposition-missing`, `gate-report-not-found`,
`gate-report-launch-id-mismatch`, `gate-report-predates-import`,
`no-output-has-patch`, `disposition-pending`, `actor-missing`,
`state-invalid`, `gate-report-unreadable`, `gate-report-unparsable`였다.
기존 fixture 파일은 수정하지 않았다.

## 필수 명령과 환경 제약

`context-pack-integrity`는 exit 0, `staleCount: 0`, `failureCount: 0`이었다. stale 경로가
없어 소유자 검색 대상도 없었다.

`build-verify`는 exit 1이었다. 격리 환경에서 임시 사본의 net8 빌드가
`NU1301`(NuGet TLS 인증서 없음)으로 복원 단계에서 중단됐다. 구현 소스 자체는 위 net10
컴파일에서 경고·오류 0개였다.

`measure dev-pack`은 exit 2였다. net10 실행 파일 경로에서 저장소 루트 탐색이
`C:\Users\1\AppData\Local\Temp\dashboard\data\projects.json`을 선택해 측정 본체에
진입하지 못했다.

```json
{"gate":"dev-pack","violations":null,"attempt":1,"exitCode":2,"execution":"not-reached","reason":"projects.json path not found under executable-derived root"}
```

## 남은 제약

- 설치된 .NET 8 SDK 타기팅 팩과 정상 NuGet 인증서가 있는 환경에서 `build-verify`와
  `measure dev-pack`을 다시 실행해야 한다.
- 같은 환경에서 `di-completion-check POST-COMMIT`의 하위 net8 검사 14개를 다시 실행해야 한다.
- 깨끗한 반입 커밋에서 `worktreeCleanAtStart: true`를 확인해야 한다. 현재 하네스 실행자는
  구현 소스가 수정된 작업 트리만 소유하므로 거짓으로 깨끗하다고 만들지 않았다.
