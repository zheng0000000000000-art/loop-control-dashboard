# 지시서 #15 — 반입 결재 UI (인박스 승인·거절 버튼)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 그 파일을 먼저 읽어라.

## 전제 조건

없음 (대시보드 전용 소형 작업 — 서버 API는 이미 존재).

## 배경

`approve-import`/`reject-import` API는 있지만 대시보드에 UI가 없어 반입 결재를 PowerShell로 해야 한다 — 실측된 마찰(사람이 taskId를 못 찾고, 출력이 잘리고, 토큰 입력이 헷갈림). 반입 빈도가 늘고 있으므로 결재 클릭을 UI로 만든다. 사람 결재를 쉽게 만드는 것 자체가 이양 원칙의 백스톱 강화다.

## 작업

1. 인박스의 `import_pending` 항목에 [diff 보기] [승인] [거절] 버튼을 추가한다.
   - diff 보기: `GET /api/outbox/{taskId}`의 diff를 기존 diff 뷰 스타일로 표시. meta의 `contextBytes`·`cutoffRisk`(있으면)·`staleCheck` 관련 정보도 함께.
   - 승인: `POST /api/projects/{projectId}/outbox/{taskId}/approve-import` — 기존 401→토큰 프롬프트 흐름(postProjectAction) 재사용.
   - 거절: 사유 입력(window.prompt)을 받아 reject-import 호출.
2. 409 `dispatch.stale_base` 응답은 사유(변경된 파일 목록)를 사람이 읽을 수 있게 표시한다.
3. 반입 성공 시 "커밋은 자동이 아니다 — git status 확인 후 직접 커밋" 안내 문구를 결과에 표시한다(운영 관례 그대로).
4. ko/en 문자열 추가. 모바일 1열 레이아웃에서도 버튼이 눌리는지 확인.

## 필요 파일

`dashboard/app.js`, `dashboard/index.html`, `dashboard/style.css`, `dashboard/data/lang/*.json`, `server/Program.cs`(라우트 시그니처 참조만 — 수정 금지)

## 구현 경계

- **서버 코드 무수정.** 대시보드·lang만.
- 기존 결재(approval)·체크포인트 UI 로직 무접촉 — 인박스 렌더링에 추가만.
- **본 지시서는 "직접 경로"를 명시한다** (관례 예외 ② — 사람이 UI를 즉시 쓰기 위해 발행함). 대시보드·lang 파일을 작업 트리에서 직접 수정하되 **커밋·push는 하지 않는다** — 사람이 git diff로 확인 후 커밋한다.
- 현재 작업 트리에 있는 다른 변경(appsettings.json, docs/ untracked, outbox 대기)은 건드리지 않는다.

## 검수 기준 (검증 가능 문장 5개)

1. import_pending 항목에서 [승인] 클릭 → 토큰 입력 → `status: imported`가 UI에 반영되고 항목이 인박스에서 사라진다.
2. [거절] 클릭 시 사유가 meta의 `rejectReason`에 저장된다.
3. stale 상태(파일 인위 변경)에서 승인 시 차단 사유와 변경 파일 목록이 화면에 표시된다.
4. 반입 성공 화면에 커밋 안내 문구가 표시된다.
5. `measure dev-pack` 위반 수가 작업 시작 시점보다 증가하지 않는다.

## 보고 형식

`docs/verification/import-approval-ui.md`에 실측(스크린샷 대신 DOM/응답 JSON 발췌 허용), 추측 진행, 사용 경로. 지시서 원문은 `docs/directives/15-import-approval-ui.md`로 보관.
