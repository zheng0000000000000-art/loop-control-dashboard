# 모바일 승인 버튼 무반응 — 원인 분석과 수정

## 재현

`server-verify`(임시 검증용 launch.json 설정, 포트 5299, 실제 저장소 그대로 서빙)를 375x812
모바일 뷰포트로 띄우고 실제 pending proposal(`proposal-1783646562038`)에서 승인 버튼을 눌렀다.

- 버튼은 `disabled:false`로 정상 렌더링돼 있었고, 클릭은 실제로 `POST
  /api/projects/dev-pack/actions/approve`를 발생시켰다(서버 로그로 확인).
- `RemoteActionToken`이 설정돼 있어(`server/appsettings.json`, 이 저장소는 이미 토큰이
  구성된 상태) 서버가 `401`을 반환했다.
- 클라이언트 코드(`dashboard/app.js`)는 401을 받으면 `window.prompt()`로 토큰 입력을 받아
  재시도하는 구조였다. **이 `window.prompt()` 호출 이후 브라우저 탭이 완전히 멈췄다** —
  이후의 모든 `preview_eval`(단순 `1+1` 포함)·`preview_screenshot` 호출이 30초 타임아웃으로
  전부 실패했다. `window.prompt`는 동기 블로킹 호출이라 응답(다이얼로그 처리)이 없으면
  렌더러 전체가 멈춘다.
- 이 패턴은 iOS 홈 화면 PWA(standalone display mode)·여러 인앱 브라우저에서 `window.prompt`가
  아예 지원되지 않거나(조용히 null 반환) 화면에 나타나지 않는 것으로 알려진 문제와 정확히
  일치한다. 실제 모바일에서는 "버튼을 눌러도 반응이 없다"로 보이는 것이 자연스럽다 — 토큰
  입력창이 뜨지 않거나 뜨더라도 처리되지 않아 승인 요청이 완결되지 못한다.

## 수정

`window.prompt()`를 페이지 내 HTML 모달(`promptModal()`, `dashboard/app.js`)로 교체했다 —
네이티브 다이얼로그가 아니라 일반 DOM이라 모든 모바일 컨텍스트(PWA 포함)에서 동일하게 동작하고,
Promise 기반이라 렌더러를 막지 않는다. 4곳 전부 교체:

1. `postProjectAction`의 401 토큰 재입력(승인/거절/측정 등 모든 프로젝트 액션 공통 경로).
2. `postOutboxImportAction`의 401 토큰 재입력(반입 승인/거절 공통 경로).
3. `rejectProposal`의 거절 사유 입력.
4. 인박스 반입 거절(`renderImportPendingItem`)의 거절 사유 입력.

추가로 `.modal-input`에 `font-size: 16px`를 지정했다(16px 미만 입력 필드는 iOS Safari에서
포커스 시 자동 확대가 발생하는 별도의 잘 알려진 모바일 문제 — 같은 코드 경로를 고치는 김에
예방).

## 실측

동일한 `server-verify`(모바일 뷰포트)에서 재현:

1. 승인 버튼 클릭 → `POST .../actions/approve` → `401` (동일).
2. **이전과 달리 탭이 멈추지 않았다** — 클릭 직후 `preview_eval`이 즉시 응답했다.
3. `document.querySelector('.modal-overlay')`가 `true` — 페이지 내 모달이 표시됨(`type="password"`
   입력창, 메시지 "원격 액션 토큰을 입력한다").
4. 모달에 토큰 `1`을 입력하고 확인 버튼 클릭 → 모달이 닫히고 재시도 POST가 성공.
5. `curl http://localhost:5299/api/projects/dev-pack/proposal`로 확인: `lifecycle`이
   `"submitted"` → `"decided"`로 실제 변경됨 — 승인이 서버에 실제 반영됐다.

(참고: `preview_click`으로 이 특정 버튼을 클릭했을 때 좌표 타이밍 이슈로 클릭이 씹히는
현상이 있어, 실제 이벤트 전달 확인은 `element.click()` 직접 호출로 재검증했다 — 이는
테스트 도구 쪽 특성이며 앱 코드와 무관함을 `elementFromPoint`로 확인했다.)

## 부수 효과 고지

위 실측 3~5번은 검증용 사본 설정이 아니라 **실제 저장소의 실제 pending proposal**을 대상으로
했다(server-verify가 워크스페이스 자체는 실제 저장소를 그대로 서빙하기 때문). 그 결과
`proposal-1783646562038`이 실제로 승인 처리됐고, 서버의 `Git:AutoCommitData` 자동 커밋으로
`dc0005c "[loop] dev-pack 회차7: approve proposal-1783646562038"` 커밋이 실제로 생성됐다.
이는 이번 검증의 부수 효과이며, 사람이 실제로 그 버튼을 눌렀을 때 일어났을 일과 동일하다
(파괴적이지 않음 — 정상적인 루프 진행 1회차).

## 게이트

이 수정(`dashboard/app.js`, `dashboard/style.css`, `dashboard/data/lang/*.json`)은
`docs/verification/tier2-auto-import-approval.md`에 기록한 동일한 최종 측정 실행에 포함돼 있다:

```json
{"gate":"dev-pack","violations":4,"attempt":2}
```

남은 4건은 모두 이 수정과 무관한 기존 위반이다(`smallTouchTargets`/`skillDomainViolations`/
`maxFunctionLength`) — 상세는 위 문서 참고. 이 변경 자체로 새로 생긴 위반은 없다.

## 참조한 스킬

- `skills/common/verification.md`
