# 지시서 공통 불변 제약 (_header)

적용 범위: **지시서 #11까지는 이 내용이 각 지시서에 인라인으로 실렸고, #12부터는 본 파일 참조로 대체된다.** 지시서 본문에는 "이 지시서는 docs/directives/_header.md의 불변 제약을 따른다" 1줄만 쓴다.

## 불변 제약

- 코어 3파일(Engine.cs / Storage.cs / Guardrails.cs)은 도메인 무지를 유지한다. 이번 작업의 도메인 문자열을 넣지 않는다.
- 생성≠검토. 기준 파일(blueprint.json, workflow-definition.json)은 수정하지 않는다 — 기준 변경은 사람만.
- 결재·반입·ack를 대행하지 않는다. approve/reject/approve-import/reject-import 미호출.
- 예측과 사실을 분리해 기록한다. 추정값에는 산정 방식을 명기한다.
- 주석은 한국어, 기능 설명만("왜"는 DECISIONS 몫).
- 코드 변경의 기본 경로는 dispatch/outbox다. 단 skills/·docs/ 문서 변경은 관례상 직접 경로 허용. 예외를 썼으면 보고에 사유를 남긴다.
- 작업 후 `dotnet run --project server -- measure dev-pack` 게이트를 통과 기준으로 확인한다.
