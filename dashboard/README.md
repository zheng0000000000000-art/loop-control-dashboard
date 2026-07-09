# Local-First Workflow Dashboard

로컬 파일을 저장소로 쓰는 루프 관제 대시보드 MVP다. 단일 ASP.NET Minimal API 프로세스가 정적 파일 서빙, API, JSON 파일 쓰기, 복원 지점 생성을 맡는다.

## Run

```powershell
dotnet run --project server
```

기본 주소는 `http://localhost:5173`이다. 기존 정적 서버가 같은 포트를 쓰고 있으면 먼저 종료한다.

## Ollama Setup

1층 로컬 AI 검토를 쓰려면 Ollama를 준비한다.

```powershell
./scripts/setup-ollama.ps1
```

스크립트는 `ollama --version`을 확인하고, 없으면 `winget install Ollama.Ollama`로 설치한다. 이후 `http://localhost:11434/api/tags` 응답을 확인하고, definition의 `reviewerPolicy.tier1`에서 쓰는 모델을 받을 수 있게 기본 모델 `qwen2.5:14b-instruct`, `llama3.1:8b`를 준비한다.

수동 대안:

```powershell
winget install --id Ollama.Ollama --exact
ollama serve
ollama pull qwen2.5:14b-instruct
ollama pull llama3.1:8b
ollama list
```

모델명, endpoint, timeout, retry 횟수는 코드가 아니라 각 프로젝트의 `workflow-definition.json`에 둔다.

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
- `GET measurement`
- `POST actions/measure`
- `POST actions/approve`
- `POST actions/reject`
- `POST actions/edit-change`
- `POST actions/acknowledge`

승인, 거절, 편집, 확인은 서버가 파일에 기록한다. 브라우저는 응답으로 받은 최신 상태를 화면에 반영하고, 5초마다 state와 run-log를 다시 읽는다.

`appsettings.json`의 `RemoteActionToken`이 설정돼 있으면 모든 `POST actions/*`는 `X-Action-Token` 헤더가 일치해야 한다(불일치·누락 시 `401`). 비어 있으면(기본값) 토큰 검사가 없다. `GET`은 토큰과 무관하게 항상 열려 있다.

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

## 원격 접근 (선택)

기본값은 여전히 `localhost` 전용이다. 이 절은 **Tailscale 사설망 안에서만** 폰으로 대시보드를 열람·결재하고 싶을 때만 따른다.

> **경고**: 이 구성은 Tailscale 사설망 접근을 전제로 한다. 공유기 포트포워딩이나 다른 방식으로 이 서버를 공용 인터넷에 직접 노출하지 않는다. `RemoteActionToken`은 가벼운 2차 방벽일 뿐, 정식 인증 시스템이 아니다.

### 1. Tailscale로 폰에서 PC 대시보드 열기

1. PC에 Tailscale 설치: `winget install Tailscale.Tailscale` (또는 https://tailscale.com/download/windows ). 설치 후 트레이 아이콘에서 로그인한다.
2. 폰에 Tailscale 앱 설치(App Store/Play Store) 후 **PC와 같은 계정**으로 로그인한다.
3. PC의 Tailscale IP를 확인한다: `tailscale ip -4` (예: `100.x.x.x`).
4. `server/appsettings.json`의 `BindUrls`를 `"http://0.0.0.0:5173"`으로 바꾼다(모든 네트워크 인터페이스에서 수신 — Tailscale이 그 중 하나다).
5. 같은 파일의 `RemoteActionToken`에 임의의 긴 문자열을 넣는다(예: `openssl rand -hex 16`으로 생성). 승인·거절·측정 등 쓰기 액션에 이 값이 필요해진다. 열람(GET)은 토큰 없이 항상 된다.
6. `dotnet run --project server`로 서버를 다시 시작한다.
7. 폰 브라우저에서 `http://100.x.x.x:5173`으로 접속한다. 승인/거절처럼 쓰기 액션을 처음 누르면 토큰 입력 창이 뜬다 — 5번에서 정한 값을 넣는다(브라우저 메모리에만 보관되며 저장되지 않는다. 새로고침하면 다시 물어본다).

### 2. ntfy로 폰 푸시 받기

결재 대기 도착, 가드레일 정지, 체크포인트 확인 요청, 데이터 자동 복원 — 사람이 반드시 필요한 순간에만 무료 푸시 알림을 보낸다. 측정 성공, 정렬 도달 같은 통과성 이벤트는 보내지 않는다.

1. 폰에 ntfy 앱 설치(App Store/Play Store).
2. 앱에서 임의의 긴 topic 이름을 만들어 구독한다(예: `my-loop-alerts-8f3a2c`). **topic 이름이 사실상 비밀번호다** — 짧거나 흔한 이름은 다른 사람도 구독할 수 있다.
3. `server/appsettings.json`의 `Ntfy`를 채운다:
   ```json
   "Ntfy": { "Enabled": true, "Server": "https://ntfy.sh", "Topic": "my-loop-alerts-8f3a2c" }
   ```
4. 서버를 다시 시작한다. 이후 결재 대기가 발생하면 폰에 푸시가 온다.

### 알림 실패는 무해하다

ntfy 서버에 접속할 수 없거나 발송이 실패해도 측정·승인·거절 루프는 멈추지 않는다. 실패는 서버 콘솔에만 남고 `run-log.json`은 오염하지 않는다.

## Notes

- 기본값은 DB 없음, 인증 없음, 외부 API 호출 없음이다. 원격 접근 절을 따랐을 때만 `RemoteActionToken`(경량 액션 토큰)과 ntfy(선택적 외부 푸시 호출)가 켜진다.
- 서버는 localhost 바인딩을 기본값으로 쓴다. `appsettings.json`의 `BindUrls`로 바꿀 수 있다.
- `Engine.cs`는 단계 ID와 상태값만 다룬다.
- 브라우저의 `engine.js`는 표시와 데모 재생용 미리보기로 남겨 둔다.
- 체크포인트 확인은 `acknowledgedCheckpoints`에 `{ checkpointId, loopIteration }`을 기록해 같은 회차 재발동을 막는다.
- 가드레일의 정식 해제는 `workflow-definition.json`의 한도를 수정하는 것이다. `acknowledge`는 `acknowledgedGuardrails`에 `{ type, loopIteration }`을 기록해 해당 회차의 같은 breach만 한정 면제한다.
