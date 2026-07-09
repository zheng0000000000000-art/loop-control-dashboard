# 지시서 #11 — Context Engineering 1단계: Context Budget 계측 + 지시서 헤더 참조화

발행일: 2026-07-10. #10(AI 결재자)과 병행 발행. 충돌 방지 경계는 아래 명시.

## 불변 제약 (공통 헤더)

- 코어 3파일(Engine.cs / Storage.cs / Guardrails.cs)은 도메인 무지를 유지한다. 이번 작업의 도메인 문자열(budget, context 등)을 넣지 않는다.
- 생성≠검토. 기준 파일(blueprint.json, workflow-definition.json)은 수정하지 않는다 — 기준 변경은 사람만.
- 결재·반입·ack를 대행하지 않는다. approve/reject/approve-import/reject-import 미호출.
- 예측과 사실을 분리해 기록한다. 추정값에는 산정 방식을 명기한다.
- 주석은 한국어, 기능 설명만("왜"는 DECISIONS 몫).
- 코드 변경의 기본 경로는 dispatch/outbox다. 단 skills/·docs/ 문서 변경은 관례상 직접 경로 허용. 예외를 썼으면 보고에 사유를 남긴다.
- 작업 후 dotnet run --project server -- measure dev-pack 게이트를 통과 기준으로 확인한다.

참고: 본 지시서는 불변 제약을 인라인으로 싣는 마지막 지시서다. 작업 B 완료 후 차기 지시서부터는 docs/directives/_header.md 참조 1줄로 대체된다.

## 배경

상위 모델(구독) 할당량 소모의 주범은 dispatch 실행자가 매번 읽는 컨텍스트다(지시서 인라인 헤더 반복 + repo 재탐색). 절감 주장에는 기준선이 필요하므로 측정을 먼저 만들고, 가장 싼 절감(헤더 참조화)을 같이 넣는다. 이 계측은 차기 blueprint(상위 모델 할당량 절감)의 지표 공급원이 된다.

## 작업 A — Context Budget 계측

- dispatch가 outbox task 사본을 만들 때, 사본에 포함된 파일 전체에 대해 다음을 산출한다:
  - contextBytes — 파일 바이트 합계 (실측)
  - estimatedContextTokens — contextBytes / 4 (추정, 산정 방식을 필드 옆 주석 또는 메타에 명기)
  - contextFileCount — 파일 수 (실측)
- 세 값을 해당 task의 기록(task.json 또는 기존 task 메타 위치)에 저장하고, 이벤트 로그에 context.budget 이벤트로 남긴다(schemaVersion 준수, ko/en 템플릿 추가).
- 구현은 신규 파일(예: server/ContextBudget.cs)로 한다. Program.cs는 현재 2684줄로 밴드(2661) 위반 상태다 — 위반을 악화시키지 않는다. OutboxManager에는 측정 훅 호출 최소 삽입만 허용.
- 계기판 표시는 이번 범위가 아니다.

## 작업 B — 지시서 헤더 참조화 (Directive Template)

- docs/directives/_header.md를 신설하고, 위 "불변 제약" 전문을 그대로 옮긴다. 서두에 "지시서 #11까지는 인라인, #12부터 본 파일 참조"를 명기한다.
- skills/common/directive-writing.md를 개정한다: 지시서는 인라인 헤더 대신 "이 지시서는 docs/directives/_header.md의 불변 제약을 따른다" 1줄을 쓴다. 자기완결 원칙은 "외부 링크 금지, 저장소 내 상대경로 참조 허용"으로 완화됨을 기술한다.
- 관례 문서(CLAUDE.md/AGENTS.md ## 관례)에 같은 내용 1줄을 추가한다.

## 경계 (#10과의 충돌 방지)

- 결재 흐름 코드(approval 경로, pacing, 인박스 결재 항목)는 무접촉 — #10의 영역이다.
- 이번 작업의 서버 접촉 범위: 신규 파일 1개 + OutboxManager 훅 호출 + 이벤트 템플릿. 그 외 서버 코드 무접촉.

## 검수 기준 (검증 가능 문장 6개)

1. dispatch 1회 실행 후 해당 outbox task 기록에 contextBytes·estimatedContextTokens·contextFileCount가 0이 아닌 실측값으로 존재한다.
2. 동일 지시를 재dispatch하면 세 값이 정확히 일치한다(결정론).
3. docs/directives/_header.md가 존재하고 불변 제약 전문을 담고 있다.
4. skills/common/directive-writing.md가 참조 1줄 관례를 기술하고, 인라인 전문 요구가 제거되어 있다.
5. rg -n "budget|contextBytes" server/Engine.cs server/Storage.cs server/Guardrails.cs 결과가 비어 있다.
6. measure dev-pack 위반 수가 작업 시작 시점(3건)보다 증가하지 않았다.

## 후속 예고 (이번 범위 아님)

- #12 Context Pack: 지시서가 선언한 "필요 파일" 목록만 outbox 사본에 복사 — 본 계측의 전/후 수치로 효과를 증명한다.
- #13 Context Delta: 이벤트 로그+git 기반 "지난 회차 이후 변경 요약" 동봉.
