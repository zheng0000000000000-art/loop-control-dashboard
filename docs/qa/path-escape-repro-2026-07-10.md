# S-01/S-02 경로 escape 재현

검증일: 2026-07-10

참조한 스킬: `skills/common/verification.md`, `skills/domains/dev/file-navigation.md`.

역할: QA / 버그 헌터. 코드 수정 없이 `server/`를 읽고, 동적 PoC는 임시 사본에서만 수행했다.

## 하네스 교차검증

임시 사본: `%TEMP%/lfwd-pathqa-5ce6510537e048fcb62a9a3973e0edd8`

| 명령 | exit code | 결과 |
| --- | ---: | --- |
| `dotnet run -c Release --project server -- verify-behavior` | 0 | `{"behaviorEqual":true,"snapshot":"docs/behavior-snapshot.json"}` |
| `dotnet run -c Release --project server -- measure dev-pack` | 1 | `{"projectId":"dev-pack","violationCount":3,"proposalId":"proposal-1783689636927","proposalLifecycle":"submitted","createdBy":{"provider":"ollama","model":"qwen3:8b"},"currentStage":"changeReview","overallStatus":"warning"}` |

판정: 리팩토링 후 현행 동작은 기존 스냅샷과 일치한다. 이번 경로 문제는 새 리팩토링 회귀로 보지 않고 기존 로직의 경계 검증 문제로 기록한다.

## S-01 판정

대상: `server/Storage.cs:188-190`

```text
var fullPath = Path.GetFullPath(Path.Combine(DataRoot, relativePath));
if (!fullPath.StartsWith(DataRoot, StringComparison.OrdinalIgnoreCase))
```

판정: 재현됨.

정적 근거: 경로 정규화 후 `StartsWith(DataRoot)`만 확인한다. `fullPath == DataRoot` 또는 `fullPath.StartsWith(DataRoot + Path.DirectorySeparatorChar)` 형태의 separator 경계 확인은 없다.

논리 PoC:

```json
{
  "DataRoot": "C:\\repo\\dashboard\\data",
  "ConfiguredPath": "./data/../data-escape",
  "RelativePath": "../data-escape",
  "FullPath": "C:\\repo\\dashboard\\data-escape",
  "StartsWithDataRoot": true,
  "IsEqualOrSeparatorBounded": false
}
```

관찰: `data-escape`는 `data`의 자식이 아닌 sibling prefix 디렉터리지만 현행 검사에서는 통과한다. 실제 `projects.json` 수정은 금지되어 논리 PoC로 제한했다.

## S-02 판정

대상: `server/OutboxManager.cs:647-665`

```text
var fullPath = Path.GetFullPath(Path.Combine(outboxRoot, taskId));
if (!fullPath.StartsWith(outboxRoot, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(fullPath))
```

```text
var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
if (!fullPath.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
```

판정: 재현됨.

정적 근거: `ResolveTaskDirectory`와 `SafeWorkspacePath` 모두 separator-bounded root 검사를 하지 않는다.

동적 PoC(읽기 전용 GET):

1. 임시 사본에 harmless sibling directory `%TEMP%/.../outbox-escape/poc/meta.json` 생성.
2. 임시 서버를 `http://localhost:5317`로 실행.
3. `GET /api/outbox/..%5Coutbox-escape%5Cpoc` 호출.

관찰 결과:

| URI | status | 관찰 |
| --- | ---: | --- |
| `http://localhost:5317/api/outbox/..%5Coutbox-escape%5Cpoc` | 200 | sibling `outbox-escape/poc/meta.json` 내용 반환 |
| `http://localhost:5317/api/outbox/%2e%2e%5Coutbox-escape%5Cpoc` | 200 | sibling `outbox-escape/poc/meta.json` 내용 반환 |
| `http://localhost:5317/api/outbox/..%2Foutbox-escape%2Fpoc` | 404 | slash variant는 route/normalization 경로에서 차단 |
| `http://localhost:5317/api/outbox/%2e%2e%2Foutbox-escape%2Fpoc` | 404 | slash variant는 route/normalization 경로에서 차단 |

반환 body 발췌:

```json
{
  "schemaVersion": 2,
  "taskId": "poc-outside-outbox",
  "projectId": "qa",
  "status": "import_pending",
  "changedFiles": [],
  "diff": ""
}
```

쓰기 반입(`approve-import`)은 금지에 따라 호출하지 않았다.

## 자산화

- `FAIL-2026-006`: S-01 project path sibling-prefix escape.
- `FAIL-2026-007`: S-02 outbox read sibling-prefix escape.

## 체크리스트 제안

경로 검증 QA 체크리스트는 재사용 가능하다. 다만 `POST /api/contributions`는 저장소 데이터 파일을 변경해 이번 지시의 쓰기 영역(`docs/qa/`, `docs/wiki/failures/`)을 벗어나므로 호출하지 않았다. 제안 내용:

```text
경로 root 검증은 Path.GetFullPath 후 StartsWith(root)만 보지 말고, fullPath == root 또는 fullPath.StartsWith(root + separator) 조건을 확인하는 QA 체크를 추가한다. encoded backslash, sibling-prefix(root-evil), ../root-evil 케이스를 포함한다.
```

