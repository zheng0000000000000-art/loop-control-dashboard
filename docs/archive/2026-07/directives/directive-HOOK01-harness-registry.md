# HOOK-01 — HarnessRegistry 1회성 훅 (이후 코덱스가 CliRouter를 건드리지 않게)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
유형: refactor(1회성). 근거: COLLAB-STRUCTURE 2026-07-11 개정 — 하네스·스킬 제작이 코덱스로 위임됐다. 하네스마다 `server/Cli/CliRouter.cs`(sonnet 영역)를 고치면 **영역 충돌**이 난다(FAIL-2026-004 계열).

## 목표
하네스를 `server/Harness/`(코덱스 배타 소유)로 격리하고, **CliRouter는 딱 한 번만** 레지스트리를 호출하게 만든다. 이후 코덱스는 CliRouter를 영원히 건드리지 않는다.

## 작업
1. `server/Harness/HarnessRegistry.cs` 신설 — 하네스 이름→핸들러 표 하나와 `TryRun(string[] args)` 하나.
   ```
   internal static int? TryRun(string[] args)  // 이름이 표에 없으면 null 반환
   ```
2. 기존 하네스 5종을 `server/Harness/`로 **이동**(내용 변경 없이): `GateCleanCli.cs`, `HsScanCli.cs`, `ClaimCheckCli.cs`, `DocIntegrityCli.cs`, `E2EUsageCli.cs`. 공용 헬퍼 `GitTools`도 함께.
3. `server/Cli/CliRouter.cs` — 하네스 개별 분기 5개를 **전부 제거**하고, **한 줄**로 대체:
   ```
   var harness = HarnessRegistry.TryRun(args);
   if (harness is not null) return harness;
   ```
   **이것이 CliRouter의 마지막 하네스 관련 수정이다.**
4. 이후 하네스 추가는 `server/Harness/HarnessRegistry.cs`의 표에 한 줄 추가 + 새 파일 — **전부 코덱스 영역 안**.

## 검수 기준
1. 기존 5개 명령이 그대로 동작: `gate-clean server` / `hs-scan` / `claim-check FEAT-02` / `doc-integrity` / `e2e-usage`. exit code·출력 스키마 동일.
2. `dotnet build server -c Release` 0/0, `verify-behavior` behaviorEqual:true, `measure dev-pack` 비악화(현재 3건).
3. `server/Cli/CliRouter.cs`에 하네스 이름 문자열이 **하나도 남지 않는다**(grep 0건).
4. 코어 3파일 무접촉.

## 작업 보고 (CLAUDE.md 신규 관례 — 반드시)
verification 문서에 ①**주체(actor)** ②**사용한 하네스와 결과**(명령·exit code·수치) ③참조한 스킬을 기록한다.

## 허용 파일 (allowlist)

- server/Harness/**
- server/GateCleanCli.cs
- server/HsScanCli.cs
- server/ClaimCheckCli.cs
- server/DocIntegrityCli.cs
- server/E2EUsageCli.cs
- server/Cli/CliRouter.cs
- docs/verification/hook01-harness-registry.md
- docs/handoff/WORKSTATE.json

> 이 목록 밖의 파일을 수정하면 산출물 전체가 반려된다. 필요하면 고치지 말고 중단하고 보고하라.

## 경계
`server/` + `docs/verification/`만. git commit/push 금지.
