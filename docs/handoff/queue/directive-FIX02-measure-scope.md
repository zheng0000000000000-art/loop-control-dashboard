# FIX-02 — measure 스캔 범위에서 outbox 제외

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: implementation(버그 수정). 근거: R-04 검수 중 발견 — `measure dev-pack`의 maxFunctionLength 위반 증거가 현행 코드가 아니라 `outbox/task-20260710070612000/files/server/Program.cs`(옛 stale 사본)를 가리켰다. measure가 워크스페이스 실제 코드뿐 아니라 outbox 하위 사본까지 스캔해 위반을 오카운트한다.

## 전제 조건
server/ 작업트리 clean(선행 sonnet DI 커밋 완료). 순차.

## 목표
`measure`(구조 지표: programCsLines·appJsLines·maxFunctionLength·skillDomainViolations 등)가 스캔하는 파일 집합에서 `outbox/` 하위를 제외한다. 실제 소스(server/·dashboard/ 등)만 측정한다.

## 작업
1. measure 스캔 로직(DevPackMeasures.cs 또는 파일 열거 지점)에서 파일 열거 시 `outbox/`(및 필요시 `bin/`·`obj/`) 경로를 제외한다.
2. 제외는 경로 기준으로 명확히(FIX-01의 separator-bounded 교훈 참고 — `outbox` 접두 오탐 없이 `outbox/` 디렉터리만).
3. 제외 후 재측정 시 outbox 사본에서 오던 위반이 사라지는지 확인.

## 검수 기준 (검증 가능 5개)
1. measure가 outbox/ 하위 파일을 더 이상 스캔하지 않는다(코드 확인 + 실측).
2. `measure dev-pack` 실행 시 maxFunctionLength 위반(옛 outbox 사본발) 이 사라지고, 위반 수가 현행 코드 기준으로 정확해진다(R-04로 실제 해소됐으므로 감소 기대).
3. `verify-behavior`가 `behaviorEqual: true`(measure 외 동작 불변).
4. `dotnet build server -c Release` 경고 0·오류 0.
5. 코어 3파일 도메인 무지 유지(측정 스캔 범위는 도메인 로직 아님 — 경로 필터만).

## v9 산출물
WORKSTATE 갱신(diId FIX-02), `docs/verification/fix02-measure-scope.md`, `docs/directives/FIX02-measure-scope.md` 보관.

## 경계 / 보고
server/ + 위 문서만. dashboard/·docs/qa/·docs/wiki/ 무접촉. git commit/push 금지. `-c Release`. stdout에 수행요약·검수기준 자가점검표·측정 전후 위반 수 출력. rate limit 시 마지막 줄 QUOTA_SIGNAL.
