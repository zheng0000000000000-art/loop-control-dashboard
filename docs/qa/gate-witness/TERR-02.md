# TERR-02 작업 보고

## 주체

- HARNESS_EXECUTOR (Codex)
- 직접 경로 사유: 사용자 지시가 허용한 코덱스 영토
  `server/Harness/TerritoryCheckCli.cs`, `docs/qa/gate-witness/**` 안에서만 산출물을 작성했다.
- 커밋·push·상태 전이·결재·반입은 수행하지 않았다.

## 변경

- 자기 증명은 대상 커밋의 `git diff-tree --name-status`에서 `A`인
  `outbox/*/candidate.patch`만 근거로 사용한다.
- 처분 결속은 `state == imported`이고 `importCommit`이 대상 커밋과 같은 처분의
  `candidate.patch`만 근거로 사용한다.
- 사람 면제와 HEAD 한 커밋 판정 범위는 유지했다.
- `--dispositions` 격리 입력을 추가하고, `rejected` 처분이 같은 커밋과 경로를 주장해도
  면제되지 않는 반증 픽스처를 만들었다.
- 영토 정본은 기존 `CodexTerritory.Contains`만 사용했다.
- 반입자가 갱신할 `GATE-MANIFEST.json` order 20 note의 수치는 `violations 2`에서
  `violations 3`으로 바뀌어야 한다.

## 사용한 하네스와 결과

```text
territory-check --commit 7feeb44d22039f3d92f934ca2dc6ba69bc0dd8f5
exit 1
territoryPaths: 3
coveredByOutbox: 0
violations: 3
violationPaths:
  docs/qa/gate-witness/nested-counter-output.json
  docs/qa/gate-witness/nested-counter.json
  server/Harness/GateWitnessCheckCli.cs

territory-check
exit 0
commit: 9a5f184e44aa25c0f2317fb6ef03c0e69242088e
territoryPaths: 0
violations: 0

territory-check --commit ea52a91441ed32ce7a8ad44ca572d3803b0d441a
exit 0
TERR-01 importCommit: ea52a91441ed32ce7a8ad44ca572d3803b0d441a
territoryPaths: 4
coveredByOutbox: 4
violations: 0

territory-check --commit 774c349a57d659d3eac390613858c6dddb6dfcbe
exit 0
TERR-01 처분 기록 커밋: 774c349a57d659d3eac390613858c6dddb6dfcbe
territoryPaths: 0
violations: 0

territory-check --ledger docs/qa/gate-witness/territory-stale-ledger.json
  --commit 7feeb44d22039f3d92f934ca2dc6ba69bc0dd8f5
exit 1
territoryPaths: 3
staleExceptions: 1
violations: 4

territory-check --dispositions docs/qa/gate-witness/territory-non-imported
  --commit 7feeb44d22039f3d92f934ca2dc6ba69bc0dd8f5
exit 1
fixture state: rejected
territoryPaths: 3
coveredByOutbox: 0
violations: 3

territory-check --committ HEAD
exit 2
error: unknown-option: --committ

verify-behavior
exit 0
behaviorEqual: true

scope-check docs/directives/TERR-02-import-binding.md --actor codex
exit 0
changedFileCount: 2
outOfScopeCount: 0
claimConflictCount: 0

gate-witness-check
exit 0
totalUnwitnessed: 0
```

`build-verify`는 exit 1이었다. 격리 빌드가 NuGet 원본의 TLS 인증서 부재로 `NU1301`에서
중단됐다. 같은 소스를 설치된 net10 타기팅 팩과 로컬 복원 자산으로 빌드한
`dotnet build server -p:TargetFramework=net10.0 --no-restore`는 exit 0,
경고 0개, 오류 0개였다.

필수 원문 명령 `dotnet run --project server -- measure dev-pack`도 같은 `NU1301`로 exit 1이었다.
로컬 복원 자산을 쓰는
`dotnet run --project server -p:TargetFramework=net10.0 --no-restore --no-build -- measure dev-pack`은
exit 0, `violationCount: 0`, `overallStatus: completed`였다. 측정이 쓴 영토 밖 런타임 JSON
5개는 즉시 HEAD blob과 같은 바이트로 되돌렸고 최종 `git diff --stat`에는 남지 않았다.

{"gate":"dev-pack","violations":0,"attempt":1}

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

- `build-verify`와 필수 원문 측정 명령의 환경 실패를 숨기지 않고 대체 실행과 구분했다.
- `--dispositions`는 반증 픽스처를 저장소의 실제 outbox와 격리하기 위한 읽기 입력이다.
  기본 실행은 계속 추적 중인 `outbox/*/disposition.json`만 읽는다.
- 사람 면제 ledger와 판정 대상 HEAD 한 커밋 범위는 바꾸지 않았다.

## 지표는 만족했으나 목적은 미달인 부분

없음. 핵심 반증에서 과거의 무관한 패치는 면제 근거에서 빠졌고, TERR-01 정상 반입 커밋은
그 커밋이 추가한 패치로 covered 4/4를 유지했으며, `rejected` 처분 픽스처는 covered 0으로
남았다. 단, 위에 공개한 NuGet 인증서 환경 때문에 `build-verify`와 원문 측정 명령의 exit 0
지표는 이 격리 사본에서 충족되지 않았다.
