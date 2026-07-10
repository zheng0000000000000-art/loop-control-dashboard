# 지시서 #12 — 지시서 템플릿 렌더러 (Directive Compiler 1단계)

이 지시서는 docs/directives/_header.md의 불변 제약을 따른다. 작업 시작 전 그 파일을 먼저 읽어라.

## 전제 조건

미충족 시 중단하고 보고한다.

- #11-R(ContextBudget 재적용 + stale 가드)이 반입 완료되어 `server/ContextBudget.cs`가 main에 존재해야 한다. 없으면 작업하지 말고 보고한다 — 이번 outbox 제출물이 stale 가드의 보호를 받아야 하기 때문이다.

## 배경

지시서의 절반 이상(전제 조건·구현 경계·게이트 검수 기준·보고 형식)은 시스템 상태에서 기계적으로 도출된다. 지금은 상위 모델이 매번 재생성한다 — "생성보다 선택" 철학 위반이자 할당량 낭비다. 프로그램이 조립하고, 판단(목적·작업·나머지 검수 기준)만 사람/AI가 채운다. 이것은 로드맵 Axis 1 Directive Compiler의 첫 실물이며, `--from-violation` 모드는 재귀 개선(시스템이 자기 위반으로 개선 지시를 스스로 기안)의 앞반부다.

## 작업 A — 템플릿

`docs/directives/_template.md`

기존 지시서(#10, #11, #11-R)의 골격을 그대로 옮긴다: 1. 헤더 참조 1줄 2. 전제 조건 3. 배경 4. 작업 5. 필요 파일 6. 구현 경계 7. 검수 기준(3~7개) 8. 보고 형식. 자동 채움 구간은 `<!--auto:...-->` 마커, 판단 구간은 `{{placeholder}}`로 표시한다. 5. 필요 파일은 신설 필드다 — 실행자가 읽어야 하는 파일 목록을 지시서가 선언한다(차기 Context Pack이 이 선언을 소비한다).

## 작업 B — 렌더러 CLI

`dotnet run --project server -- directive-draft <projectId> [--title "..."]`

- 전제 조건 자동: outbox에서 `import_pending` task 목록을 읽어 기입. 0건이면 "대기 반입 없음"을 명시.
- 구현 경계 자동: 최신 measure 결과의 위반을 지표명·실측값·밴드와 함께 기입(예: "programCsLines 2684 / 밴드 [0,2661] — 악화 금지, 신규 로직은 별도 파일"). 불변 제약은 `_header` 참조 1줄로 대체.
- 검수 기준 자동 2개: "measure 위반 수 비악화" + "코어 3파일 무접촉(rg 검사)". 나머지 항목은 `{{검수기준}}` 플레이스홀더.
- 산출: `docs/directives/drafts/draft-<projectId>-<제목슬러그>.md`. stdout에도 출력.

## 작업 C — 재귀 앞반부

`--from-violation <지표명>`

현재 위반 중인 지표 1개를 지정하면 제목·배경·작업 스텁을 위반 데이터로 채운 초안을 생성한다(예: `--from-violation maxFunctionLength` → 배경에 "Program.cs:714-959 ApplyMeasurementResult 246줄 / 상한 …" 실측 기입). 위반 중이 아닌 지표를 지정하면 오류와 함께 현재 위반 목록을 안내한다. 초안 생성까지만이다 — 발행·실행은 사람 몫.

## 필요 파일

- `server/Program.cs`(CLI 분기 참조)
- `server/OutboxManager.cs`(outbox 읽기)
- `dashboard/data/*/measurement.json`(측정 결과 저장 위치)
- `docs/directives/` 기존 지시서 2~3개(골격 참조)

## 구현 경계

- CLI + 템플릿만. 서버 HTTP 라우트 무추가.
- 신규 파일(예: `server/DirectiveDraftCli.cs`)로 구현 — `Program.cs`에는 CLI 분기 최소 추가만, 줄수 위반 악화 금지.
- 코어 3파일 무접촉.
- 기준 파일 무수정.
- 기존 지시서·검증 문서 무수정.
- 서버 코드는 outbox 경로로 제출(반입=사람). 템플릿·docs는 직접 경로 허용.
- 커밋·push 금지.

## 검수 기준

1. `directive-draft dev-pack --title "테스트"` 실행 시 초안 md가 생성되고, 전제 조건 섹션에 현재 `import_pending` task 목록(또는 "대기 반입 없음")이 실측으로 들어 있다.
2. 구현 경계 섹션에 최신 measure 위반이 지표명·실측값·밴드와 함께 자동 기입되어 있다.
3. 검수 기준 섹션에 자동 2개(게이트 비악화·코어 청결)가 포함되고 나머지는 플레이스홀더로 남아 있다.
4. `--from-violation`으로 실제 위반 지표 1건의 초안이 생성되고, 배경에 그 지표의 실측값이 들어 있다. 비위반 지표 지정 시 오류와 위반 목록이 출력된다.
5. 동일 시스템 상태에서 2회 실행한 산출이 동일하다(생성 시각 필드 제외 — 결정론).
6. `rg -in "directive|draft|template" server/Engine.cs server/Storage.cs server/Guardrails.cs` 매치 0건.
7. `measure dev-pack` 위반 수가 작업 시작 시점보다 증가하지 않는다.

## 보고 형식

`docs/verification/directive-draft.md`에 검수 기준 7개 실측(생성된 초안 전문 1건 포함), 추측 진행 목록, 사용 경로와 예외 사유. 지시서 원문은 `docs/directives/12-directive-template.md`로 보관.
