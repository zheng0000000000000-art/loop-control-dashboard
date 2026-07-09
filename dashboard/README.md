# Local-First Workflow Dashboard

로컬 파일을 저장소로 쓰는 루프 관제 대시보드 MVP다. 단일 ASP.NET Minimal API 프로세스가 정적 파일 서빙, API, JSON 파일 쓰기, 복원 지점 생성을 맡는다.

## Run

```powershell
dotnet run --project server
```

기본 주소는 `http://localhost:5173`이다. 기존 정적 서버가 같은 포트를 쓰고 있으면 먼저 종료한다.

## Structure

```text
server
  Program.cs
  Engine.cs
  Guardrails.cs
  Storage.cs
dashboard
  index.html
  style.css
  app.js
  engine.js
  guardrails.js
  data
    projects.json
    lang
      ko.json
      en.json
    ruined-lab
      workflow-definition.json
      workflow-state.json
      run-log.json
      patch-proposal.json
      review-report.json
      scenario.json
      blueprint.json
      history
```

새 프로젝트는 기존 프로젝트 폴더를 복사하고 `dashboard/data/projects.json`에 항목을 추가한다.

```json
{ "id": "new-project", "name": "New Project", "path": "./data/new-project/" }
```

## API

모든 API는 `/api/projects/{projectId}/` 아래에 있다.

- `GET state`
- `GET runlog`
- `GET proposal`
- `GET reviews`
- `GET definition`
- `GET blueprint`
- `POST actions/approve`
- `POST actions/reject`
- `POST actions/edit-change`
- `POST actions/acknowledge`

승인, 거절, 편집, 확인은 서버가 파일에 기록한다. 브라우저는 응답으로 받은 최신 상태를 화면에 반영하고, 5초마다 state와 run-log를 다시 읽는다.

## Persistence

상태 변경 액션 직전에 `history/restore-{time}` 폴더가 생성된다. 액션 처리 후에는 `history/loop-{iteration}` 폴더에 결과 세트가 저장된다.

쓰기 순서는 임시 파일 기록 후 교체다. 직접 덮어쓰지 않는다.

서버 시작 시 주요 JSON을 파싱한다. 파싱 실패가 있으면 최신 restore 지점에서 자동 복원하고 run-log에 `system.restored` 이벤트를 남긴다.

`Download JSON` 버튼은 백업용이다. 서버 모드에서는 승인, 거절, 편집 결과가 이미 프로젝트 폴더 JSON에 기록된다.

## Event Contract

`run-log.json`은 `schemaVersion: 3`이며, 각 엔트리는 `loopIteration`을 가진다.

```json
{
  "createdAt": "2026-07-08T10:14:00+09:00",
  "event": "stage.passed",
  "params": { "stage": "schemaValidation" },
  "level": "info",
  "producedBy": { "provider": "schema-checker", "model": null },
  "attempt": 1,
  "loopIteration": 3,
  "cost": {
    "inputTokens": 0,
    "outputTokens": 0,
    "estimatedUSD": 0,
    "subscriptionCalls": 0,
    "role": "runtime"
  }
}
```

서버 액션이 만드는 로그의 `cost.role`은 모두 `runtime`이다. `dev`는 이 시스템을 만드는 데 든 소비를 따로 적을 때만 쓴다.

## reasonCode

오류 응답은 `reasonCode`와 `reason`을 반환한다.

- `schema.*`, `json.*`, `path.*`: 차단형 오류
- `checklist.*`, `review.*`: 판단형 오류
- `system.*`: 인프라 오류

게이트 미충족 승인 요청은 `409`와 `review.gate_blocked`를 반환하며 파일을 바꾸지 않는다.

## Notes

- DB, 인증, 외부 API 호출은 없다.
- 서버는 localhost 바인딩을 기본값으로 쓴다.
- `Engine.cs`는 단계 ID와 상태값만 다룬다.
- 브라우저의 `engine.js`는 표시와 데모 재생용 미리보기로 남겨 둔다.
- 체크포인트 확인은 `acknowledgedCheckpoints`에 `{ checkpointId, loopIteration }`을 기록해 같은 회차 재발동을 막는다.
- 가드레일의 정식 해제는 `workflow-definition.json`의 한도를 수정하는 것이다. `acknowledge`는 `acknowledgedGuardrails`에 `{ type, loopIteration }`을 기록해 해당 회차의 같은 breach만 한정 면제한다.
