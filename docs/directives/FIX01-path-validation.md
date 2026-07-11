# FIX-01 — 경로 검증 separator-bounded 수정 (FAIL-2026-006/007 해소)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: implementation(보안 수정). 근거: 코덱스 QA가 재현·확정한 `docs/wiki/failures/cases/FAIL-2026-006`(Storage), `FAIL-2026-007`(OutboxManager).

## 전제 조건

작업 트리에 다른 실행자의 미커밋 server/ 변경이 없어야 함(순차). WP-REFACTOR-PROGRAM 완료 후이므로 server/는 정리됨.

## 목표

경로 검증이 `fullPath.StartsWith(root)`만 검사해 형제 접두 디렉터리(`/data` ↔ `/datax`)로 경계를 우회할 수 있는 취약점(sibling-prefix escape)을 **separator-bounded 검사**로 고친다.

## 작업

1. **Storage** (`server/Storage.cs`, FAIL-006): projects.json 경로 해석 등 `DataRoot` 경계 검사를 아래 부록 패턴으로 교체.
2. **OutboxManager** (`server/OutboxManager.cs`, FAIL-007): `ResolveTaskDirectory`, `SafeWorkspacePath`의 `StartsWith(root)` 검사를 동일 패턴으로 교체. `outboxRoot`·`workspaceRoot` 양쪽.
3. 경계 위반 시 기존과 같은 예외/거부 동작 유지(새 동작 추가 아님, 경계만 정확히).

## 부록 — 코덱스 제안 수정 방향 (FAIL-006/007에서)

```csharp
// 기존 (취약):  fullPath.StartsWith(root)
// 수정 (separator-bounded):
bool withinRoot =
    string.Equals(fullPath, root, StringComparison.Ordinal) ||
    fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
```
- `Path.GetFullPath`로 정규화한 뒤 위 검사를 적용한다.
- 정당한 경로(정상 projectId/taskId)는 그대로 통과해야 한다 — 이것이 회귀 없음의 기준.

## 검수 기준 (검증 가능 문장 6개)

1. Storage·OutboxManager의 경계 검사가 separator-bounded 패턴으로 바뀌었다(`rg "StartsWith" server/Storage.cs server/OutboxManager.cs`로 잔존 취약 패턴 없음 확인).
2. **정상 동작 불변**: `verify-behavior`가 `behaviorEqual: true`(정당한 경로 조회·반입 흐름이 안 깨짐).
3. **escape 차단 재현**: FAIL-006/007의 재현 입력(형제 접두 경로)이 이제 거부되는지 확인(코덱스 재현 절차 재실행 또는 단위 확인). 차단되면 성공.
4. `dotnet build server -c Release` 경고 0·오류 0.
5. `measure dev-pack` 위반 수 비악화.
6. 코어 3파일 중 Engine.cs·Guardrails.cs 무접촉(Storage.cs는 코어지만 이번 수정 대상 — 도메인 무지 원칙 유지, 경로 경계 로직만 수정하고 도메인 문자열 추가 금지).

## v9 산출물

`docs/handoff/WORKSTATE.json` 갱신(diId FIX-01), `docs/verification/fix01-path-validation.md`(검수 실측 + escape 차단 증거), `docs/directives/FIX01-path-validation.md`(원문 보관). FAIL-006/007 상태를 `해결됨`으로 갱신 제안(단 docs/wiki는 코덱스 영역이니 직접 수정 말고 verification에 "FAIL-006/007 수정 완료, 코덱스가 상태 갱신 요망"으로 남긴다).

## 경계 / 보고

- server/(Storage.cs·OutboxManager.cs) + 위 v9 문서만. dashboard/·docs/qa/·docs/wiki/ 무접촉. git commit·push 금지. 빌드·CLI는 `-c Release`.
- stdout에 수행요약·검수기준 6개 자가점검표·escape 차단 확인 결과 출력. rate limit 시 마지막 줄 QUOTA_SIGNAL.
