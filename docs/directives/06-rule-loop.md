# 규칙 루프 1차 — 개발 팩 측정과 괴리 판정

## 목표

dev-pack 프로젝트가 실제로 돈다. 측정 실행, measurement 기록, blueprint 대비 괴리 판정, 위반을 proposal로 자동 생성, 사람 승인, 사람이 밖에서 수정, 재측정으로 해소 확인, 전 항목 충족 시 aligned까지 규칙 기반으로 처리한다. AI 호출은 사용하지 않는다.

## 제약

기존 제약을 유지한다. 도메인 분리, 주석 정책, 원자 쓰기, role=runtime 원칙을 지킨다. 측정 로직은 Engine.cs에 넣지 않고 DevPackMeasures.cs에 둔다. Engine, Storage, Guardrails는 이 모듈의 존재를 알지 않는다. Program.cs가 measurementProvider id로 공급자를 라우팅한다.

## 데이터

dev-pack 프로젝트는 workflow-definition, workflow-state, run-log, blueprint, review-report를 가진다. definition에는 goals, measure, deviationCheck, changeReview, apply 단계를 둔다. measurementProvider는 dev-pack-checks이며 targetPath는 프로젝트 폴더 기준 상대 경로를 사용한다.

## 측정 계약

measurement.json은 측정 결과의 사실 기록이다. 편집 액션은 만들지 않는다. evidence는 파일과 줄 또는 파일명을 담고 항목당 최대 20개로 제한한다.

## 서버 동작

POST actions/measure는 공급자를 실행하고 measurement.json을 원자 기록한다. 이후 blueprint와 비교해 위반이 없으면 measure와 deviationCheck를 passed로 두고 loopState를 aligned로 만든다. 위반이 있으면 deviationCheck를 warning으로 두고 stageDetails에 지표와 이슈를 기록한 뒤 proposal을 자동 생성한다. 직전 proposal이 submitted이면 새 proposal의 revisionOf로 연결한다.

## UI

현재 컨텍스트 패널에 측정 실행 버튼을 표시한다. 서버 응답의 measurement와 stageDetails를 화면에 반영한다. aligned 상태는 기존 loopState 배지 체계를 사용한다.

## 검수 기준

1. dev-pack 선택 후 측정 실행 시 measurement.json이 생성되고, 각 값이 실제 저장소 상태와 일치한다.
2. ko.json에 "~요" 문장을 임시로 넣고 측정하면 koPoliteEndings=1과 evidence 위치가 표시되며 proposal이 자동 생성된다.
3. 임시 문장을 원복한 뒤 재측정하면 koPoliteEndings=0이 되고 전 항목 충족 시 loopState가 aligned가 된다.
4. measurement.json을 수정하는 엔드포인트가 존재하지 않는다.
5. Engine.cs, Storage.cs, Guardrails.cs에 metricId, 검사 로직, dev-pack 문자열이 없다.
6. 미구현 metric은 value null로 표시되고 괴리 판정에서 제외된다.
7. approve 성공 시 loopIteration이 1 증가한다.
