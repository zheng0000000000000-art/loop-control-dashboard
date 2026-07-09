# 스킬: UI 작업법

버전: 1 | 대상: `dashboard/index.html`·`style.css`·`app.js`에 화면 요소를 추가·수정할 때

## 절차

1. **색은 항상 `style.css`의 CSS 변수로 쓴다.** `:root`와 `[data-theme="dark"]`에 라이트/다크 짝으로 정의돼 있다: `--green`/`--green-bg`, `--yellow`/`--yellow-bg`, `--red`/`--red-bg`, `--blue`/`--blue-bg`, `--gray`/`--gray-bg`, `--orange`/`--orange-bg`. 새 의미가 필요하면 이 패턴대로(라이트+다크 값, `-bg` 짝) 변수를 추가하고 하드코딩된 hex를 절대 다른 파일에 쓰지 않는다.
2. **상태 색의 의미는 고정돼 있다 — 새로 정하지 않는다.**
   - 녹색(`--green`): `completed`, `passed`, `approved`, `loop_aligned`, `mode_normal` — 통과.
   - 노랑(`--yellow`): `warning`, `gate_blocked`, `mode_degraded` — 주의.
   - 빨강(`--red`): `failed`, `failed_upstream`, `loop_halted` — 이상·중단.
   - 파랑(`--blue`): `pending_review`, `in_progress`, `loop_paused` — 진행/대기 중.
   - 회색(`--gray`): `not_started`, `blocked`, `waiting`, `suspended_tracks` — 아직 시작 전/보류.
   - 주황(`--orange`): `regressed`, `rollback` — 악화·긴급 롤백(가장 최근에 추가된 의미, 녹→빨강처럼 강한 경고이되 `failed`와는 다른 "이전엔 됐는데 지금 안 되는" 상황 전용).
3. **새 배지·카드·패널이 필요하면 기존 컴포넌트를 재사용한다.** `createStatusBadge(status, label)` / `setStatusBadge(node, status, label)`(className `status-badge status-{status}`), `.cost-pill`, `.tag`, `.panel`. 새 시각 패턴(새 모양의 배지, 새 레이아웃 프리미티브)을 발명하지 않는다 — 기존 클래스에 상태 이름만 추가하는 쪽을 먼저 검토한다.
4. **버튼·클릭 요소는 `min-height: 44px` 이상을 목표로 한다.** 특히 좁은 화면 미디어쿼리(`@media (max-width: 800px)`) 안에서는 승인/거절처럼 실제로 터치하는 버튼에 명시적으로 44px를 준다.
5. **변경한 화면을 좁은 화면(<800px)에서 반드시 확인한다.** `.dashboard-grid`는 800px 미만에서 1열로 재배치되며 순서가 `approval → pipeline → detail → log`다(결재함이 항상 맨 위). 새 패널을 추가하면 이 순서 규칙에 맞춰 `grid-template-areas`를 조정한다.
6. **`preview_screenshot`이 타임아웃되는 경우가 잦다.** 그럴 땐 `preview_inspect`로 `boundingBox`(좌표)와 `styles`(계산된 색상·크기)를 직접 확인한다 — 스크린샷보다 오히려 정확하고, 자동화된 검증 문서에 숫자로 남길 수 있다.

## 지켜야 할 것

- 인라인 `style=` 속성이나 JS에서 문자열로 style을 조립하는 것 금지(`inlineStyles` 측정 지표가 이를 감시한다) — 클래스와 CSS 변수로 표현한다.
- 폰트는 `:root`의 기존 스택(Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif) 밖의 폰트를 추가하지 않는다(`newFontFamilies` 지표가 이를 감시한다). 새 폰트가 꼭 필요하면 이 스킬 문서와 기준 목록을 사람 결재로 먼저 갱신한다.
- 다크 테마 오버라이드가 두 군데(`[data-theme="dark"] { --color: ... }` 루트 변수와 `[data-theme="dark"] .status-x { color: ...; background: ...; }` 명시적 클래스)에 중복 존재하는 것이 이 저장소의 기존 패턴이다 — 새 상태 색을 추가할 때 두 곳 모두에 값을 넣는다.

## 완료 판정

- `hardcodedColors`·`inlineStyles`·`smallTouchTargets`·`newFontFamilies` 네 지표를 measure로 확인했을 때 새로 늘어난 위반이 없다(기존에 있던 위반은 별개 — 고치라는 지시가 없으면 그대로 둔다).
- 800px 미만 뷰포트에서 레이아웃이 깨지지 않고 결재함이 최상단에 온다.
- 라이트/다크 두 테마 모두에서 색이 의도대로 보인다(`preview_inspect`로 `getComputedStyle` 확인).
