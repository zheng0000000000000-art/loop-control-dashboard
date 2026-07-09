# 소작업 묶음 검증

## 참조한 스킬
- `skills/common/directive-writing.md`
- `skills/common/verification.md`
- `skills/domains/dev/file-navigation.md`
- `skills/domains/design/design.md`
- `skills/domains/docs/README.md`
- `skills/domains/game/balance-tuning.md`
- `browser:control-in-app-browser`

## 변경 경로
- `server/Program.cs`
- `server/OllamaExecutor.cs`
- `dashboard/index.html`
- `dashboard/style.css`
- `dashboard/app.js`
- `dashboard/data/lang/ko.json`
- `dashboard/data/lang/en.json`
- `dashboard/data/ruined-lab/workflow-definition.json`
- `dashboard/data/ruined-lab/patch-proposal.json`
- `AGENTS.md`
- `CLAUDE.md`
- `docs/verification/dashboard-small-batch.md`

## 검증 결과
- O `dotnet build server\LocalFirstWorkflowDashboard.Server.csproj`
  - 결과: 경고 0, 오류 0.
- O JSON 파싱 확인
  - 대상: `ko.json`, `en.json`, `workflow-definition.json`, `patch-proposal.json`.
  - 결과: 모두 `JSON.parse` 통과.
- O `GET /api/projects/ruined-lab/cycle-summary`
  - 결과: 200.
  - 응답 요약: `loopIteration=3`, `measurementMs=0`, `generationMs=21745`, `reviewMs=32841`, `humanWaitingMs>0`.
- O 브라우저 표시 확인
  - 위치: 헤더 아래 `#cycleSummary`.
  - 결과: 숨김 해제, 회차 시간 분해 텍스트 표시.
- O proposal `assumptions` 표시
  - 현재 `ruined-lab` proposal의 `assumptions`는 빈 배열이다.
  - 결과: 결재 패널에서 빈 섹션은 렌더링되지 않는다.
- O 코어 청결 확인
  - 명령: `rg -n "ollama|dev-pack|completionRate|room|heal|enemy|patchApproval|unityExport|ruined-lab|metricId" server\Engine.cs server\Storage.cs server\Guardrails.cs`
  - 결과: 매치 없음.
- O dev-pack 게이트
  - 첫 시도: `functionsWithoutComment=2` 감지, `dashboard/app.js`의 새 함수 주석 2개 보강.
  - 문서 작성 후 최종 재실행 결과:
```json
{"gate":"dev-pack","violations":0,"attempt":3}
```

## 체크리스트 v2
- `ruined-lab` definition에 튜닝 시대용 체크리스트 3개를 추가했다.
- 추가 항목:
  - `predicted-band-or-residual-declared`
  - `lever-range-within-definition`
  - `note-purpose-effect`
- 기존 항목은 제거하지 않았다. 제거는 사람 결재 사항이다.
- 제거 제안 후보:
  - `note-direction-match`
  - `risk-note-present`
  - `no-unrelated-change`

## 남은 대기
- 현재 사람 결재 대기 proposal은 승인·거절하지 않았다.
- `dev-pack` 측정 결과 파일은 게이트 실행으로 최신 상태가 되었다.
