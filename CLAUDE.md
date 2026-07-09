# 이 저장소에서 작업하는 에이전트의 규칙

## 품질 게이트 (필수)
- 코드·데이터 수정 후 커밋 전에 반드시 측정을 실행한다:
  `dotnet run --project server -- measure dev-pack` (아래 CLI, 서버 기동 불필요)
- 위반(deviations)이 0이 될 때까지 수정 후 재측정한다.
- 끝내 통과하지 못하면 남은 위반 목록을 작업 보고에 그대로 포함한다 — 숨기지 않는다.

## 금지 사항
- blueprint·workflow-definition·측정 코드를 수정해서 게이트를 통과하는 것.
  기준 변경은 사람 결재 사항이다.
- approve/reject 계열 액션 호출. 결재는 사람 몫이다.
- Engine.cs·Storage.cs·Guardrails.cs에 도메인 지식(게임 용어, metricId, ollama 코드)을 넣는 것.

## 관례
- 주석은 한국어 기능 설명만 (파일 머리 1~2줄 + 함수 위 1줄). 함수 수정 시 주석 갱신.
- 커밋 전 git status로 bin/, obj/, history/ 미포함 확인.
