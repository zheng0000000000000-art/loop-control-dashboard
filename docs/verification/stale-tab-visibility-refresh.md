# 백그라운드 탭이 최신 상태를 안 받아오던 문제 — visibilitychange 갱신

## 배경

사람이 모바일(Tailscale) 대시보드에서 승인 버튼을 눌러도 반응이 없다고 신고했는데,
서버 로그를 보면 `/actions/approve` 요청 자체가 한 번도 도달하지 않았다. 같은 시각
서버 API를 직접 조회하면 실제로는 데이터가 바뀌어 있었다(예: ruined-lab의
`unityExport` 단계가 실제로는 `completed`인데 화면은 `진행 중`으로 표시).

원인: 5초 폴링(`startPolling`)은 탭이 포그라운드에 있을 때만 정상 동작한다. 모바일
브라우저는 탭이 백그라운드로 가면 배터리 절약을 위해 `setInterval` 타이머를 그대로
멈추거나 크게 늦추는 경우가 흔하다. 탭을 다시 보이게 해도 그 자체로는 새로 데이터를
가져오는 트리거가 없어서, 실제로 페이지를 리로드하기 전까지는 오래된 화면을 계속
보여준다 — 서버가 막은 게 아니라 클라이언트가 갱신을 안 한 것이다.

## 수정

`dashboard/app.js`의 `bindEvents`에 `visibilitychange` 리스너를 추가했다 — 탭이 다시
보이게 되는 순간(`!document.hidden`) `refreshRuntimeData()`를 즉시 한 번 더 호출한다.
기존 폴링 타이머의 에러 처리 패턴(`.catch(() => {})`)을 그대로 따랐다.

## 실측

검증용 사본 서버(포트 5299)에서 `document.hidden`을 `true`→`false`로 강제 전환하며
`visibilitychange` 이벤트를 디스패치하고, `window.fetch`를 가로채 호출 여부를 확인했다:
`hidden:false`로 전환되는 즉시 `fetch`가 호출됨을 확인(`window.__refreshFired === true`).

## 게이트

```json
{"gate":"dev-pack","violations":4,"attempt":6}
```

기존과 동일한 4건(모두 이 세션에서 이미 기록한 기존/누적 위반), 신규 위반 없음.
`appJsLines`는 2674(band 0~2692) — 여유 있음.

## 참조한 스킬

- `skills/common/verification.md`
