# 지시서 #16 — 모바일 결재 토큰 흐름 수정 (치명 버그)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 그 파일을 먼저 읽어라. **직접 경로 명시**(대시보드 파일 직접 수정). git commit/push 금지.

## 증상 / 원인 (확정)

증상: 모바일(Tailscale 원격) 대시보드에서 proposal [승인] 버튼을 눌러도 적용이 안 된다.
원인: `RemoteActionToken` 설정 시 서버 미들웨어(Program.cs)가 모든 POST /api에 토큰을 요구한다(의도된 사양). 프론트는 401 시 `window.prompt`로 토큰을 묻고 메모리 변수 `actionToken`에만 저장 → 새로고침·재접속마다 재입력, prompt 취소 시 조용히 실패. **서버는 고치지 않는다. 프론트만 고친다.**

## 작업 (dashboard/app.js + lang)

1. `actionToken`을 localStorage에 영속화한다: 페이지 로드 시 `localStorage.getItem("actionToken")`로 복원, 토큰 입력 성공(비-401 응답) 시 저장, 잘못된 토큰으로 401 재발 시 저장분 삭제 후 재요청.
2. `postProjectAction`·`postOutboxImportAction` 공통: 요청 전 `remoteActionToken 필요 여부`를 알 수 없으므로 기존 401→prompt→재시도 흐름은 유지하되, ①prompt에 "이 값은 저장되어 다시 묻지 않습니다" 안내 ②prompt 취소 시 조용히 return하지 말고 화면에 "토큰이 필요합니다 — 우측 상단에서 입력하세요" 수준의 명확한 에러 표시(기존 showActionError 재사용).
3. 헤더/설정 영역에 토큰을 한 번 입력·저장·삭제할 수 있는 작은 입력 UI를 추가한다(모바일 1열에서도 접근 가능). 이미 저장돼 있으면 "토큰 설정됨 ✓"만 표시하고 값은 노출하지 않는다.
4. ko/en 문자열 추가. 값 자체는 화면·로그에 절대 출력하지 않는다(redaction).

## 구현 경계

- **서버 코드 무수정.** dashboard/app.js·index.html·style.css·lang만.
- 기존 승인/거절/반입 로직의 성공 경로는 바꾸지 않는다 — 토큰 첨부·저장·에러표시만 보강.
- localStorage 키는 `actionToken` 하나. 값은 평문 저장이나 로컬 개인 도구·Tailscale 사설망 전제로 허용(이 판단을 verification 문서에 명시).

## 검수 기준 (검증 가능 문장 5개)

1. 토큰 저장 후 페이지 새로고침 시 재입력 없이 승인 POST에 X-Action-Token이 첨부된다(네트워크 탭 또는 코드 경로로 확인).
2. 잘못된 토큰 저장 상태에서 승인 시 401 → 저장분 삭제 → 재입력 유도가 동작한다.
3. prompt 취소 시 조용히 실패하지 않고 명확한 에러가 화면에 뜬다.
4. 토큰 값이 화면·콘솔·run-log 어디에도 출력되지 않는다.
5. `measure dev-pack` 위반 수가 작업 시작 시점보다 증가하지 않는다.

## 보고

`docs/verification/mobile-approve-token-fix.md`에 실측·추측 진행·경로. 지시서 원문은 `docs/directives/16-mobile-approve-token-fix.md`에 보관.
