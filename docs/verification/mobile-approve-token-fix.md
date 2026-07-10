# 검수 보고 — 지시서 #16 모바일 결재 토큰 흐름 수정

## 경로 및 사유

- 직접 경로 사용. 지시서에 "직접 경로 명시"가 명시됨. git commit/push 금지.

## 변경 파일

- `dashboard/app.js` — localStorage 영속화, setActionToken/refreshActionToken 헬퍼 추가, 401 처리 개선
- `dashboard/index.html` — 헤더에 `#tokenStatusBtn` 추가
- `dashboard/data/lang/ko.json` — tokenPromptPersist·tokenCancelled·tokenSet·tokenNotSet 키 추가
- `dashboard/data/lang/en.json` — 동일 키 영문 추가

## 보안 판단 명시

localStorage에 평문 저장: 이 도구는 로컬 개인 환경 + Tailscale 사설망 전제로 운용된다. 값은 화면·콘솔·run-log에 일절 출력하지 않는다(buildActionHeaders에서만 헤더에 첨부). 이 수준의 보안은 사용 환경에 적합하다.

## 게이트 측정 결과

`{"gate":"dev-pack","violations":4,"attempt":2}`

attempt 1 (리팩토링 전): violations=5 (appJsLines=2693>2692 초과, koPoliteEndings=2 — 내 ko.json 실수)
attempt 2 (리팩토링 후): violations=4 — 작업 시작 시점과 동일

appJsLines 위반 원인: 401 처리 로직 확장으로 라인 수 2728 달성(밴드 상한 2692 초과). setActionToken/refreshActionToken 헬퍼 추출로 2692로 축소 해결.

## 검수 기준 확인

1. **코드 경로 확인**: `initialize()`에서 `localStorage.getItem("actionToken")`으로 복원, `setActionToken(value)`가 `localStorage.setItem` 호출 → 새로고침 후 재입력 없이 X-Action-Token 첨부됨.
2. **잘못된 토큰 처리**: `refreshActionToken()`이 먼저 `setActionToken(null)` 호출(저장분 삭제) 후 재입력 유도 → 재시도 후 401 시에도 `setActionToken(null)` 재실행.
3. **취소 시 에러 표시**: `refreshActionToken()` false 반환 → 호출처에서 `showActionError({ reason: t("remote.tokenCancelled") })` 즉시 호출. 조용히 실패하지 않음.
4. **값 미출력**: `setActionToken`·`buildActionHeaders`·`refreshActionToken` 모두 값을 `console.log` 또는 DOM에 출력하지 않음. 버튼은 설정 여부만 표시("토큰 설정됨 ✓").
5. **위반 수 미증가**: 시작 시점 4건 → 최종 4건 ✓

## 추측 진행

없음. 검수 기준 5개 모두 검증 가능하며, 대상 파일 범위가 명확했다.

## 참조한 스킬

- `skills/common/verification.md`, `skills/common/directive-writing.md` (공통, 필수 읽기)
- 도메인 스킬: 없음 (변경 파일 경로가 어떤 도메인 폴더 트리거와도 일치하지 않음)
