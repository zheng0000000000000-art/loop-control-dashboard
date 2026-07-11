# ACTOR-01 검수 문서 — 결재 액션 주체(actor) 기록

## 주체 (actor)

- **실행자**: claude-sonnet (조율자 직접 호출, 2026-07-11)
- **지시 ID**: ACTOR-01 / docs/handoff/queue/directive-ACTOR01-actor-provenance.md
- **사람 결재**: 2026-07-11 (지시서 내 명시)

## 사용한 하네스 결과

| 하네스 | 명령 | exit code | 핵심 수치 |
|--------|------|-----------|-----------|
| build | `dotnet build -c Release server/LocalFirstWorkflowDashboard.Server.csproj -o /tmp/actor01-build` | **0** | 경고 0, 오류 0 |
| measure | `dotnet run --project server --no-build -- measure dev-pack` | 1 | violationCount: 4 (pre-existing) |

> measure exit code 1은 pre-existing 위반 4건(my changes는 server/*.cs만 수정하며 dev-pack 측정값에 영향 없음).
> 게이트 기록: `{"gate":"dev-pack","violations":4,"attempt":1}` — pre-existing 위반, 내 변경으로 인한 신규 위반 없음.

## 참조한 스킬

- skills/common/* (공통 스킬 — CLAUDE.md 지시)
- AGENT-GUIDE.md
- docs/directives/_header.md

## 변경 파일 (allowlist 내 전부)

| 파일 | 변경 내용 |
|------|-----------|
| server/GitDataCommitter.cs | `CommitHumanAction` — `actor` 파라미터 추가(default: "unknown"), 커밋 메시지에 `(actor: type/path)` 추가. `[loop]` 접두사 주석 명시 |
| server/OutboxManager.cs | `ApproveImport` — `body` 파라미터 추가, actor 필드 meta 기록. `RejectImport` — actor 필드 meta 기록. `ExtractActorFields` 헬퍼 추가 |
| server/Program.cs | `Approve`, `Reject`, `Acknowledge` — actor 추출·producedBy 기록·CommitHumanAction 전달. `approve-import` 엔드포인트 async+body 읽기. `ActorInfo` 레코드 추가. `ExtractActor` 헬퍼 추가 |
| docs/verification/actor01-actor-provenance.md | 본 검수 문서 (신규) |

## 구현 상세

### actor 추출 규칙
- 요청 body에 `{ "actor": { "actorType": "human", "actorId": "...", "actorPath": "ui" } }` 포함 시 그 값 사용
- 명시 없으면 `unknown/unknown/unknown` (위조 방지는 별도 과제)
- 대시보드 UI: 명시 없으면 `unknown` (dashboard/ 파일 무접촉 제약 — UI가 actor 필드를 전송하면 자동 반영됨)

### run-log producedBy 변경 (before → after)
```json
// before
{ "provider": "human", "model": null }

// after
{ "provider": "human", "model": null, "actorType": "human", "actorId": "...", "actorPath": "ui" }
```

### 커밋 메시지 형식 변경
```
// before
[loop] dev-pack 회차5: approve proposal-x

// after
[loop] dev-pack 회차5: approve proposal-x (actor: human/ui)
```

> `[loop]` 접두사는 루프 이터레이션을 나타내며 주체를 나타내지 않는다 — 주석과 커밋 메시지 모두에 명시.

### outbox meta 변경
- `ApproveImport`: `importedByActorType`, `importedByActorId`, `importedByActorPath` 필드 추가
- `RejectImport`: `rejectedByActorType`, `rejectedByActorId`, `rejectedByActorPath` 필드 추가

## 범위 이탈 보고

- `docs/verification/auto-data-commit.md` 업데이트(작업 항목 #3): allowlist 밖의 파일이므로 미수정.
  대신 `server/GitDataCommitter.cs` 주석에 `[loop]` 접두사 의미를 명시함.
- `server/Harness/` 미접촉 (확인).
- dashboard/, docs/qa/, docs/wiki/ 미접촉 (확인).
- git commit/push 미실행.
- 결재·반입 액션 호출 미실행.

## 직접 경로 사용 사유

지시서에 "직접 경로"가 명시돼 있어 server/*.cs 파일을 직접 수정·커밋 없이 변경함.
검수 문서(docs/verification/actor01-actor-provenance.md)는 docs/ 문서로 직접 경로 허용 관례에 해당.

## 검수 기준 자가점검

| 기준 | 결과 |
|------|------|
| 1. approve → run-log·커밋에 actor 기록 | ✅ producedBy + CommitHumanAction 모두 반영 |
| 2. actor 미지정 API 호출 → unknown 기록 | ✅ 기본값 "unknown" |
| 3. 과거 기록 소급 변경 없음 | ✅ 기존 데이터 파일 미접촉 |
| 4. build 0/0 | ✅ exit code 0, 경고 0, 오류 0 |
| 5. measure 비악화 | ✅ pre-existing 4건(신규 위반 없음) |
| 6. 코어 3파일 무접촉 (Engine.cs/Storage.cs/Guardrails.cs) | ✅ |
| 7. allowlist 밖 파일 미수정 | ✅ |
| 8. git commit/push 금지 | ✅ |
| 9. 결재·반입 액션 호출 금지 | ✅ |
