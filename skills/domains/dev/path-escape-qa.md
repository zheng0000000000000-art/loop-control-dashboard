# 스킬: path-escape QA — root 경계는 접두사가 아니라 디렉터리 경계다

버전: 1 | 도메인: dev | 트리거: 경로 검증, `Path.GetFullPath`, `StartsWith(root)`, outbox/task path, workspace path, project path, path traversal, sibling-prefix escape | 대상: 서버·CLI·파일 경로 검증 변경

## 왜 이 스킬이 있나

`FAIL-2026-006`과 `FAIL-2026-007`은 같은 계열이었다. 둘 다 `Path.GetFullPath` 뒤에 문자열 접두사만 비교해 `data`와 `data-escape`, `outbox`와 `outbox-escape`를 같은 root 내부로 오판했다.

핵심 교훈:

> root 포함 여부는 `fullPath.StartsWith(root)`로 판정하지 않는다. `fullPath == root` 또는 `fullPath.StartsWith(root + separator)`처럼 separator-bounded로 판정하거나 `Path.GetRelativePath` 결과가 root 밖인지 확인한다.

## 먼저 확인할 것

1. 변경 코드가 파일 경로를 보안 경계로 쓰는가?
2. 입력이 config, URL route, task id, relative path, template path, workspace path 중 하나인가?
3. Windows에서 backslash가 경로 구분자로 해석될 수 있는가?
4. 동적 재현이 상태 변경을 요구하는가? 상태 변경이면 하지 말고 논리 PoC 또는 temp copy에서만 수행한다.

## 정적 검사

아래 패턴을 먼저 찾는다.

```powershell
rg -n "GetFullPath|StartsWith\\(|GetRelativePath|Path\\.Combine|Resolve.*Path|Safe.*Path" server
```

취약 의심:

```csharp
var fullPath = Path.GetFullPath(Path.Combine(root, input));
if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) ...
```

안전 패턴:

```csharp
var normRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
var normPath = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
var inside = string.Equals(normPath, normRoot, StringComparison.OrdinalIgnoreCase)
    || normPath.StartsWith(normRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
    || normPath.StartsWith(normRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
```

또는:

```csharp
var relative = Path.GetRelativePath(root, candidate);
var inside = !relative.StartsWith("..") && !Path.IsPathRooted(relative);
```

## 필수 케이스

| 케이스 | root | candidate/input | 기대 |
| --- | --- | --- | --- |
| root 자체 | `C:\repo\data` | `C:\repo\data` | 허용 |
| 정상 자식 | `C:\repo\data` | `C:\repo\data\dev-pack\state.json` | 허용 |
| sibling-prefix | `C:\repo\data` | `C:\repo\data-escape\state.json` | 차단 |
| parent traversal to sibling | `C:\repo\data` | `C:\repo\data\..\data-escape\state.json` | 차단 |
| outbox sibling | `C:\repo\outbox` | `C:\repo\outbox-escape\poc\meta.json` | 차단 |
| encoded backslash route | `/api/outbox/{taskId}` | `..%5Coutbox-escape%5Cpoc` | 차단 |
| encoded dot/backslash route | `/api/outbox/{taskId}` | `%2e%2e%5Coutbox-escape%5Cpoc` | 차단 |

## 하네스

기본 회귀는 H-1 하네스를 우선 쓴다.

```powershell
dotnet run --project server -c Release --no-build -- path-guard-check
```

기대:

- exit code 0
- `failureCount=0`
- `storage-sibling-prefix`, `outbox-sibling-prefix`, `parent-traversal-to-sibling`의 `actualWithinRoot=false`

특정 root/candidate를 검사할 때:

```powershell
dotnet run --project server -c Release --no-build -- path-guard-check <root> <candidate>
```

기대:

- 내부 경로는 exit 0
- sibling-prefix나 traversal 경로는 exit 1

## 동적 PoC 규칙

- 서버 데이터나 outbox를 직접 바꾸지 않는다.
- 필요하면 temp copy에서만 수행한다.
- 결재·반입·approve/reject 계열 API는 호출하지 않는다.
- 읽기 전용 GET만 허용한다.
- 재현 결과는 `docs/qa/path-escape-*.md`에 남기고, 진짜 버그면 `docs/wiki/failures/`에 FAIL 등록한다.

## 보고 형식

보고에는 아래를 반드시 적는다.

- actor
- root/candidate 또는 URI
- 정적 근거: 취약/안전 패턴 위치
- 하네스 결과: 명령, exit code, `failureCount`
- 동적 PoC를 했다면 temp copy 경로와 read-only 여부
- 판정: PASS / FAIL / 의심 / 오탐

## 한 줄

`data-escape`는 `data`의 자식이 아니다. `outbox-escape`는 `outbox`의 자식이 아니다. 문자열 접두사와 디렉터리 경계를 혼동하지 마라.
