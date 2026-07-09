# 전역 결재 인박스 검증

## 참조한 스킬

- `skills/common/directive-writing.md`
- `skills/common/verification.md`
- `skills/domains/dev/file-navigation.md`
- `skills/domains/design/design.md`
- `skills/domains/docs/README.md`

## 변경 경로

- `server/Program.cs`
- `server/Notifier.cs`
- `server/appsettings.json`
- `dashboard/index.html`
- `dashboard/app.js`
- `dashboard/style.css`
- `dashboard/data/lang/ko.json`
- `dashboard/data/lang/en.json`
- `docs/verification/inbox.md`
- `docs/incidents/2026-07-09-decision-gate-visibility.md`

## 목적

dev-pack 화면에 있어도 ruined-lab의 사람 결재 대기가 헤더 배지, 프로젝트 선택 목록, 문서 제목, 인박스 드롭다운으로 드러나는지 확인한다. 결정 게이트는 종료된 제안의 죽은 버튼을 보여주지 않고, 대기 없음 상태를 명시해야 한다.

## 실행 기록

| 항목 | 명령 또는 확인 | 결과 | 판정 |
| --- | --- | --- | --- |
| 서버 빌드 | `dotnet build server\LocalFirstWorkflowDashboard.Server.csproj` | 경고 0, 오류 0 | O |
| JS 문법 | `node --check dashboard\app.js` | 문법 오류 없음 | O |
| 한국어 톤 | `rg -n "(?<!필)요\.|(?<!필)요\"|습니다" dashboard\data\lang\ko.json` | 매치 0건 | O |
| 전역 인박스 API | `GET /api/inbox` | `ruined-lab` 결재 대기 1건 반환 | O |
| 프로젝트 선택 병기 | `dev-pack` 상태에서 인박스 응답을 기준으로 `ruined-lab (결재 1)` 표기 로직 확인 | `populateProjectSelect()`가 프로젝트별 대기 수를 병기 | O |
| 문서 제목 | 전역 인박스 수 기준 제목 갱신 로직 확인 | 현재 프로젝트가 아니라 전역 대기 수를 사용 | O |
| 결정 게이트 빈 상태 | `dev-pack`에는 submitted 제안이 없고 다른 프로젝트 대기 1건이 있음 | 빈 상태와 다른 프로젝트 보기 링크를 렌더링 | O |
| 종료 제안 버튼 제거 | submitted가 아닌 제안 경로 확인 | 종료 제안은 접힌 이력으로 이동하고 승인·거절 버튼 없음 | O |
| 브라우저 클릭 검증 | Playwright 설치 여부 확인 | 현재 작업 폴더에 Playwright 없음. API와 정적 로직 검증으로 대체 | △ |
| 관례 게이트 | `dotnet run --project server --no-build -- measure dev-pack` | `violationCount: 0`, `loopState: aligned` | O |

## API 응답 요약

서버를 `http://127.0.0.1:5187`로 실행한 뒤 `/api/inbox`를 호출했다.

- `inboxCount`: 1
- 첫 항목 `projectId`: `ruined-lab`
- 첫 항목 `kind`: `approval`
- 첫 항목 제목: `레버 변경 후 밴드 도달 실패`
- `dev-pack`의 현재 상태: `completed`

## 대기 0 상태 확인

현재 데이터셋에서는 전역 대기 1건이 존재한다. 대기 없음 화면은 `dev-pack` 프로젝트처럼 현재 프로젝트에 submitted 제안이 없을 때 재현되며, 이 경우 결정 게이트는 `결재 대기 없음` 빈 상태와 다른 프로젝트 대기 링크를 표시한다.

## 리마인더 확인

`appsettings.json`에 `Ntfy:ReminderAfterHours` 기본값 24를 추가했다. `/api/inbox` 스캔 중 결재 대기 항목이 24시간을 넘으면 프로세스 메모리 키 기준으로 항목당 1회만 리마인더를 시도한다. `Ntfy.Enabled=false` 기본값에서는 발송 없이 무시된다.

## 관례 게이트

`dotnet run --project server --no-build -- measure dev-pack`를 실행했다.

- `violationCount`: 0
- `proposalLifecycle`: `superseded`
- `currentStage`: `deviationCheck`
- `overallStatus`: `completed`
- 측정값 확인: `skillDomainViolations=0`, `koPoliteEndings=0`, `functionsWithoutComment=0`
