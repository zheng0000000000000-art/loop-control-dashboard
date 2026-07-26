# GCLEAN-01 작업 보고

## 주체와 범위

- 주체: HARNESS_EXECUTOR (Codex)
- 직접 경로 사용 사유: 지시서가 격리 사본의 allowlist 파일 직접 수정을 명시했다.
- 변경 파일: `server/Harness/GateCleanCli.cs`, 이 보고서
- 변경 내용: 검사 대상 파일·디렉터리와 status fixture 파일의 실재 여부를 판정 전에 확인한다. 누락된 입력은 경로를 JSON에 담고 exit 2를 반환한다.
- 기존 dirty/representation 판정 로직과 기본 경로 `server`는 변경하지 않았다.
- 격리 worktree의 `.git` 포인터 파일도 저장소 루트로 인식하도록 기존 루트 탐색을 보완했다.

## 빌드

명령:

`dotnet build server -v q --nologo --no-restore`

실제 exit: `0`

출력 꼬리:

```text
빌드했습니다.
    경고 0개
    오류 0개

경과 시간: 00:00:03.20
BUILD_EXIT=0
```

## 반증 시험

모든 시험은 위 빌드 직후 `dotnet run --project server --no-build -- gate-clean ...`로 직접 실행했다.

| 명령 | 실제 exit | 관찰 출력 |
| --- | ---: | --- |
| `gate-clean server` | 1 | `contentDirtyCount: 1`; 이 작업의 `server/Harness/GateCleanCli.cs` 변경을 `content-dirty`로 검출 |
| `gate-clean nonexistent-xyz` | 2 | `error: 검사 대상 경로가 없습니다.`; `missingPaths: ["nonexistent-xyz"]` |
| `gate-clean server nonexistent-xyz` | 2 | `missingPaths: ["nonexistent-xyz"]` |
| `gate-clean --status-fixture docs/qa/gate-witness/gate-clean-dirty.status` | 1 | `fixtureMode: true`; `statusLength: 40`; `gate: DIRTY` |
| `gate-clean --status-fixture docs/qa/gate-witness/no-such-fixture.status` | 2 | `error: status fixture 파일이 없습니다.`; 누락 fixture 경로 출력 |

`gate-clean server`의 지시서 기대값 0은 코드 변경 전의 깨끗한 트리를 전제로 한다. 이 격리 사본에서는
검증 대상 하네스 소스 자체가 의도적으로 변경되어 있어 기존 판정 로직이 이를 정확히 dirty로 검출했고,
따라서 실제 exit 1을 추정값 0으로 바꾸어 기록하지 않았다. 반입 후 깨끗한 트리의 exit 0 확인은 별도
프로그램 검수자가 수행해야 한다.

첫 시험 시 격리 worktree의 `.git`이 디렉터리가 아닌 포인터 파일인 점을 기존 루트 탐색이 처리하지
못해 실재 경로도 누락으로 판정했다. 루트 탐색을 보완하고 재빌드한 뒤 위 표 전체를 다시 실측했다.

## 사용한 하네스와 결과

| 명령 | 실제 exit | 결과 |
| --- | ---: | --- |
| `dotnet build server -v q --nologo --no-restore` | 0 | 경고 0개, 오류 0개 |
| `dotnet run --project server --no-build -- verify-behavior` | 0 | `behaviorEqual: true` |
| `dotnet run --project server --no-build -- measure dev-pack` | 0 | `violationCount: 0`, `overallStatus: completed` |
| `dotnet run --project server --no-build -- scope-check docs/directives/GCLEAN-01-path-must-exist.md --actor HARNESS_EXECUTOR` | 0 | `outOfScopeCount: 0`, `claimConflictCount: 0` |

측정이 갱신한 `dashboard/data/dev-pack/` 런타임 파일 5개는 측정 직후 원래 내용과 줄바꿈으로
복원했다. 최종 산출물에는 포함하지 않았다.

`{"gate":"dev-pack","violations":0,"attempt":1}`

## 참조한 스킬

- `skills/common/directive-authoring.md`
- `skills/common/directive-writing.md`
- `skills/common/executor-launch.md`
- `skills/common/hs-gate.md`
- `skills/common/powershell-encoding.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/verification.md`
- `skills/domains/dev/file-navigation.md`
- `skills/domains/dev/path-escape-qa.md`
- `skills/domains/docs/README.md`

## 자진 신고

- 커밋, push, 결재, 반입, 상태 전이, baseline 수정, executor 발사를 하지 않았다.
- 경로 검증은 실재 여부 확인만 추가했으며 저장소 내부 경계 정책을 새로 만들지 않았다.
- `gate-clean server`의 깨끗한 트리 회귀 시험은 현재 작업 파일이 dirty인 격리 사본에서는 exit 0으로
  실측할 수 없었다. 실제 exit 1과 원인을 위에 그대로 기록했다.

## 지표는 만족했으나 목적은 미달인 부분

없음. 누락된 단일·복수 검사 경로와 누락 fixture가 모두 판정 전에 exit 2로 중단되며, 누락 경로가
출력에 포함된다. 기존 내용 dirty 판정 분기는 수정하지 않았다. 다만 깨끗한 반입 후 `server` 회귀
exit 0은 위 자진 신고와 같이 별도 검수에서 확인이 필요하다.
