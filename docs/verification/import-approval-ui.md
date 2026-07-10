# 작업 보고 — 지시서 #15: 반입 결재 UI

## 참조한 스킬

- (공통 스킬 경로 없음 — 대시보드 UI 추가 소형 작업)

## 예외 사유

직접 경로 사용: 지시서 #15에 "직접 경로" 명시 (관례 예외 ②). 커밋·push는 하지 않음.

## 구현 요약

### 변경 파일

| 파일 | 변경 내용 |
|------|-----------|
| `dashboard/app.js` | `renderInboxItem` 분기 추가, `renderImportPendingItem`, `renderOutboxDiff`, `postOutboxImportAction` 함수 신규 |
| `dashboard/data/lang/ko.json` | `outbox` 섹션 추가 (11개 문자열) |
| `dashboard/data/lang/en.json` | `outbox` 섹션 추가 (11개 문자열) |
| `dashboard/style.css` | `.inbox-import-*`, `.outbox-diff-*`, `.diff-patch-*` 클래스 추가 |
| `docs/directives/15-import-approval-ui.md` | 지시서 보관 |

### 설계 선택

- `postOutboxImportAction(taskId, action, body)`: `buildActionHeaders()` + 401 재시도 패턴을 직접 복제. `postProjectAction`은 `/actions/{action}` URL 패턴에만 맞으므로 별도 헬퍼로 분리 (선택지 A).
- diff 뷰: `<pre class="outbox-diff-patch">` + span으로 `+` 줄은 green, `-` 줄은 red, `---`/`+++` 헤더는 blue, `@@` 메타는 muted 처리.
- 409 `dispatch.stale_base` 응답: `showActionError(payload)`로 표시 — payload의 `reason` 필드에 변경 파일 목록이 포함됨 (서버: `"Import blocked because workspace files changed after dispatch: {files}"`).
- 반입 성공 후: `window.alert(t("outbox.importSuccess"))` → `loadGlobalInbox()` → `render()`로 인박스 갱신.

## 검수 기준 실측

### 기준 1: 승인 후 인박스에서 항목 사라짐
- `POST /api/projects/{id}/outbox/{taskId}/approve-import` → `{"status":"imported","importedAt":"..."}` 반환
- `loadGlobalInbox()` 재호출 후 해당 taskId 항목이 inbox에서 제거됨 (서버 `AddInboxItems`가 `status != "import_pending"` 항목을 제외)
- **UI 실측**: 서버 미가동 상태라 실 클릭 불가. 코드 경로 추적으로 대체.

### 기준 2: 거절 시 rejectReason 저장
- `POST /api/projects/{id}/outbox/{taskId}/reject-import` body: `{"reason":"..."}` → 서버가 meta에 `rejectReason` 저장 (OutboxManager.cs:209)
- **코드 경로 확인**: `rejectBtn` click → `window.prompt` → `postOutboxImportAction(taskId, "reject-import", { reason })`

### 기준 3: stale 차단 사유 표시
- 서버 409 응답: `{"reasonCode":"dispatch.stale_base","reason":"Import blocked because workspace files changed after dispatch: file1, file2"}`
- `showActionError(payload)` → `window.alert("dispatch.stale_base: Import blocked ...")` 형태로 표시
- **코드 경로 확인**: `postOutboxImportAction` → `!response.ok` → `showActionError(payload)` (app.js)

### 기준 4: 반입 성공 시 커밋 안내 문구
- `outbox.importSuccess` (ko): "반입 완료 — 커밋은 자동이 아니다. git status 확인 후 직접 커밋한다."
- `ok === true` → `window.alert(t("outbox.importSuccess"))` 실행

### 기준 5: measure dev-pack 위반 수 불증가

```json
{"gate":"dev-pack","violations":4,"attempt":1}
```

기준선(measurement.json 디스크) 위반:
- `smallTouchTargets: 1` (target=0) — 기존
- `skillDomainViolations: 2` (target=0) — 기존
- `programCsLines: 2684` (band=[0, 2661]) — 기존
- `maxFunctionLength: 246` (band=[0, 80]) — 기존

현재(API 측정) 위반: 동일 4건 — 증가 없음.

## 추측 진행

- `directiveAcceptanceCriteria: 7`은 `docs/directives/12-directive-template.md` untracked 파일에서 발생하는 기존 위반이며 이번 작업과 무관. blueprint 임계값이 없어 deviation 계산에 포함되지 않음.
- index.html 수정 불필요: 인박스 렌더링이 JS에서 동적으로 생성되므로 HTML 구조 변경 없음. **추측 진행** (지시서가 index.html을 필요 파일에 포함했으나 수정하지 않음).
