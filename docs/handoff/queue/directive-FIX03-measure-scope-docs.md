# FIX-03 — measure 스캔 범위: docs/ 참조 스캐폴드를 코드로 측정하는 문제

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
유형: fix. 계열: **FIX-02와 동일**(measure 스캔 범위). 발생 경위: 검수자 세션이 `docs/handoff/queue/*.reference.cs`(하네스 참조 스캐폴드)를 추가하자 `measure dev-pack`이 이를 **프로덕션 코드로 스캔**해 `functionsWithoutComment` 12건을 보고했다. FEAT-02 실행자가 이 위반을 "Codex concurrent activity"로 오귀인했다.

## 원인
`server/DevPackMeasures.cs`의 `IsGeneratedOrRuntimePath`가 `/bin/`·`/obj/`·`/.git/`·`/.vs/`·`/history/`·`/outbox/`는 제외하지만 **`/docs/`는 제외하지 않는다.** `docs/handoff/queue/*.reference.cs`는 빌드 대상이 아닌 **참조본**인데 코드로 측정된다. FIX-02가 outbox를 제외한 것과 정확히 같은 계열의 누락이다.

## 작업
1. `IsGeneratedOrRuntimePath`에 `/docs/` 제외를 추가한다(참조 스캐폴드·문서는 빌드 대상이 아니다). 주석으로 이유를 남긴다.
2. `skillsWithoutVersion` 위반: `skills/common/hs-gate.md`에 다른 스킬과 동일한 버전 표기를 추가한다(기존 스킬 형식을 따를 것).
3. 측정 재실행으로 두 위반이 사라지는지 확인한다.

## 검수 기준
1. `dotnet run --project server -- measure dev-pack` 에서 `functionsWithoutComment` 12건(출처 docs/handoff/queue/*.reference.cs)이 **사라진다**.
2. `skillsWithoutVersion` 1건이 사라진다.
3. **기존 측정 대상은 줄어들지 않는다** — server/ 실제 코드는 여전히 전부 측정된다(회귀 확인: server/*.cs 측정 건수 불변).
4. 위반 수 비악화. build 0/0, `verify-behavior` true. 코어 3파일 무접촉.

## 금지
**측정 기준을 느슨하게 만들어 게이트를 통과시키는 것이 아니다.** 측정 *대상 범위*의 버그를 고치는 것이다. blueprint·workflow-definition·측정 임계값은 건드리지 않는다(기준 변경은 사람 결재).

## 경계 / 보고
server/DevPackMeasures.cs + skills/common/hs-gate.md + 검증 문서만. git commit/push 금지. `-c Release`.
