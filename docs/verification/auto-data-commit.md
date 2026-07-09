# 결재 데이터 자동 커밋 검증

## 참조한 스킬

- `skills/common/directive-writing.md`
- `skills/common/verification.md`
- `skills/domains/dev/file-navigation.md`
- `skills/domains/docs/README.md`

## 변경 경로

- `server/GitDataCommitter.cs`
- `server/Program.cs`
- `server/appsettings.json`
- `docs/DECISIONS.md`
- `docs/incidents/2026-07-09-human-action-data-not-committed.md`
- `docs/verification/auto-data-commit.md`

## 목적

사람 액션이 운영 데이터를 파일에 기록한 직후, 서버가 `dashboard/data/**`만 자동 커밋하는지 확인한다. 코드 파일은 자동 커밋하지 않는다.

## 실행 기록

| 항목 | 명령 또는 확인 | 결과 | 판정 |
| --- | --- | --- | --- |
| 서버 빌드 | `dotnet build server\LocalFirstWorkflowDashboard.Server.csproj` | 경고 0, 오류 0 | O |
| 임시 저장소 승인 검증 | 임시 복사 저장소에서 dev-pack proposal을 결재 대기로 만든 뒤 `POST /api/projects/dev-pack/actions/approve` | 마지막 git commit: `[loop] dev-pack 회차1: approve proposal-auto-commit-test` | O |
| 자동 커밋 범위 | 임시 저장소의 자동 커밋 후 마지막 커밋 메시지와 응답 확인 | proposal은 `decided`, currentStage는 `apply` | O |
| 실제 결재 보호 | 본 저장소의 ruined-lab 결재 대기 proposal은 승인·거절하지 않음 | 실제 운영 결재 대행 없음 | O |
| 코어 청결 | `rg`로 Engine·Storage·Guardrails의 git 자동 커밋 어휘 검색 | 매치 없음 | O |
| 관례 게이트 | `dotnet run --project server --no-build -- measure dev-pack` | `violationCount: 0`, `overallStatus: completed` | O |

## 임시 저장소 검증 결과

본 저장소를 임시 폴더로 복사하고, 그 안에서만 dev-pack 데이터를 승인 가능한 상태로 조작했다. 그 후 서버를 실행하고 승인 POST를 호출했다.

```json
{
  "LastCommit": "[loop] dev-pack 회차1: approve proposal-auto-commit-test",
  "CurrentStage": "apply",
  "ProposalLifecycle": "decided"
}
```

임시 저장소에 생성된 `server-out.log`, `server-err.log`는 검증 실행 로그였고 본 저장소에는 반영하지 않았다.

## 설정

`server/appsettings.json`에 다음 기본값을 추가했다.

```json
{
  "Git": {
    "AutoCommitData": true,
    "AutoPush": false
  }
}
```

`AutoCommitData=false`이면 서버 자동 커밋을 끈다. `AutoPush=true`이면 자동 커밋 후 push를 시도한다. push 실패는 결재 처리를 막지 않고 콘솔 로그로 남긴다.

## E2E 잔여 안내

다음 사람 결재는 레버 확장 안건이다. 후보는 `enemies.hp` 10~30 step 5, `enemies.count` 1~4 step 1이다. 사람이 값을 확정한 뒤에만 definition에 반영한다.

## 관례 게이트

`dotnet run --project server --no-build -- measure dev-pack`를 실행했다.

- `violationCount`: 0
- `proposalLifecycle`: `superseded`
- `currentStage`: `deviationCheck`
- `overallStatus`: `completed`
