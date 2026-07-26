# TERR-01 작업 보고

## 주체

- HARNESS_EXECUTOR (Codex)
- 직접 경로 사유: 지시서가 허용한 코덱스 배타 영역 `server/Harness/**`,
  `docs/qa/gate-witness/**`에서 하네스와 증인만 작성했다.
- 커밋·push·상태 전이·결재·반입은 수행하지 않았다.

## 변경

- `TerritoryCheckCli`가 지정 커밋의 변경 경로를 `CodexTerritory.Contains`로 판정한다.
- 추적 중인 `outbox/*/candidate.patch`의 파일 헤더, 사유가 있는 커밋 면제, 저장소에 없는
  stale 면제를 각각 검사한다. 영토 루트 목록은 새로 선언하지 않았다.
- `HarnessRegistry`에 `territory-check`를 등록했다.
- 저장소에 없는 sha 하나만 가진 stale ledger 반증 픽스처를 추가했다.
- 기본 ledger는 반입자가 아직 만들기 전인 실행 환경을 위해 파일 부재를 빈 목록으로 취급한다.
  `--ledger`로 명시한 파일이 없으면 입력 오류 exit 2다.

## 사용한 하네스와 결과

필수 세 조합은 설치된 .NET 10 타기팅 팩으로 같은 소스를 컴파일한 산출물을 실제 실행해 측정했다.

```text
territory-check
exit 0
commit: 4852f0315fdd5c9668a1f6b0677ace7dc616f985
territoryPaths: 0
violations: 0

territory-check --commit 7feeb44
exit 1
commit: 7feeb44d22039f3d92f934ca2dc6ba69bc0dd8f5
territoryPaths: 3
coveredByOutbox: 1
violations: 2
violationPaths: docs/qa/gate-witness/nested-counter-output.json,
  docs/qa/gate-witness/nested-counter.json

territory-check --ledger docs/qa/gate-witness/territory-stale-ledger.json --commit 7feeb44
exit 1
territoryPaths: 3
coveredByOutbox: 1
staleExceptions: 1
violations: 3
```

옵션 오타 반증:

```text
territory-check --committ HEAD
exit 2
error: unknown-option: --committ
```

나머지 검사:

```text
dotnet restore server -p:TargetFramework=net10.0 --source server/obj --ignore-failed-sources
exit 0

dotnet build server -p:TargetFramework=net10.0 --no-restore
exit 0
경고 0개, 오류 0개

verify-behavior
exit 0
behaviorEqual: true

scope-check docs/directives/TERR-01-territory-check.md --actor codex
exit 0
changedFileCount: 4
outOfScopeCount: 0
claimConflictCount: 0

gate-witness-check
exit 0
totalUnwitnessed: 0
```

`build-verify`는 exit 1이었다. 격리 사본의 net8 복원에서 NuGet 저장소 서명 정보를 가져올
인증서가 없어 `NU1301`로 중단됐다. 구현 소스는 위 net10 대체 빌드에서 경고·오류 0개였다.

`measure dev-pack`은 net10 대체 타깃으로 실행해 exit 0, `violationCount: 0`,
`overallStatus: completed`를 실측했다. 측정이 쓴 5개 런타임 JSON은 각 파일의
`git hash-object`가 `HEAD:<path>` blob과 같도록 원복했다.

```json
{"gate":"dev-pack","violations":0,"attempt":1}
```

## 참조한 스킬

- `skills/common/directive-authoring.md`
- `skills/common/directive-writing.md`
- `skills/common/executor-launch.md`
- `skills/common/hs-gate.md`
- `skills/common/powershell-encoding.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/verification.md`

변경 경로와 일치하는 `skills/domains/` 트리거가 없어 도메인 스킬은 읽지 않았다.

## 자진 신고

- 지시서의 세 필수 조합은 모두 추정이 아니라 실행 exit code로 기록했다.
- `7feeb44`의 `GateWitnessCheckCli.cs`는 추적 outbox patch가 현재 두 개 있어
  `coveredByOutbox`로 분류됐다. 같은 커밋의 다른 코덱스 영토 경로 2개는 outbox 근거가 없어
  위반으로 검출됐다.
- net8 `build-verify`의 환경 제약을 숨기지 않았고 대체 빌드를 별도로 구분했다.
- 측정 런타임 파일은 내용 blob을 HEAD와 동일하게 원복했지만, 읽기 전용 worktree git index를
  refresh하지 못해 `git status`에는 5개가 수정으로 남아 보일 수 있다. `git diff`에는 이
  파일들의 내용 차이가 없고 각 HEAD/WORK blob hash가 일치한다.

## 지표는 만족했으나 목적은 미달인 부분

없음. 영토 정본은 `CodexTerritory.Contains` 하나만 사용했고, 정상 HEAD는 exit 0이며,
과거 직접 수정 커밋과 stale 면제 픽스처는 각각 exit 1로 검출됐다. 단, net8
`build-verify` 자체는 위 인증서 제약으로 완료되지 않았으며 이는 지표 결과에 별도로 공개했다.
