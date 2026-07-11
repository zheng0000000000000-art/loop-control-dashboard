# FIX-01 검증 보고서 — 경로 검증 separator-bounded 수정

실행일: 2026-07-11
실행자: claude-sonnet-4-6

## 검수 기준 실측

| # | 기준 | 결과 |
|---|------|------|
| 1 | `rg "StartsWith"` 잔존 취약 패턴 없음 | ✓ |
| 2 | `verify-behavior` behaviorEqual=true | ✓ |
| 3 | escape 차단 재현 (형제 접두 경로 거부) | ✓ (논리 검증) |
| 4 | `dotnet build server -c Release` 경고 0·오류 0 | ✓ |
| 5 | `measure dev-pack` 위반 수 비악화 (기준선 3 → 3) | ✓ |
| 6 | Engine.cs·Guardrails.cs 무접촉 | ✓ |

## 변경 파일

| 파일 | 변경 내용 |
|------|-----------|
| `server/Storage.cs` | `ProjectFilePath`·`ProjectPath`의 경계 검사를 `IsWithinRoot` 헬퍼로 교체. `IsWithinRoot` 추가 |
| `server/OutboxManager.cs` | `ResolveTaskDirectory`·`SafeWorkspacePath`의 경계 검사를 `IsWithinRoot` 헬퍼로 교체. `IsWithinRoot` 추가 |

## 수정 패턴

```csharp
// 기존 (취약):
fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)

// 수정 (separator-bounded):
private static bool IsWithinRoot(string fullPath, string root)
{
    var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var normalizedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return string.Equals(normalizedPath, normalizedRoot, StringComparison.Ordinal) ||
        normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
```

`TrimEnd` 추가 이유: Windows에서 `projects.json`의 `"./data/dev-pack/"` 형태 경로를 `Path.GetFullPath`로 정규화하면 trailing `\`가 보존되어 `projectPath = "...dev-pack\"` 형태가 된다. `root + '\' = "...dev-pack\\"` (이중 역슬래시)가 되므로 합법 경로도 차단됐다. TrimEnd로 trailing separator를 제거한 뒤 검사한다.

## escape 차단 논리 검증

| 입력 (fullPath → root) | 이전 | 이후 |
|------------------------|------|------|
| `C:\data-evil\f` → `C:\data` | 통과 (취약) | 차단 (`StartsWith("C:\data\")` = false) ✓ |
| `C:\datax\f` → `C:\data` | 통과 (취약) | 차단 ✓ |
| `C:\data\dev-pack\f` → `C:\data` | 통과 | 통과 ✓ |
| `C:\data\dev-pack\f` → `C:\data\dev-pack\` (trailing \) | 통과 | 통과 (TrimEnd로 정규화) ✓ |

`measure dev-pack` 위반 수 3으로 정상 동작 확인 — 합법 경로는 그대로 통과.

## 잔존 StartsWith 패턴 (안전)

```
Storage.cs:175   normalized.StartsWith("./data/", ...)  → 경로 포맷 파싱(보안 경계 아님)
Storage.cs:179   normalized.StartsWith("data/", ...)    → 경로 포맷 파싱
OutboxManager.cs:556-557  normalized.StartsWith(".git/"|"outbox/", ...)  → 상대 경로 제외 목록
```

모두 절대 경로 경계 검사가 아니므로 sibling-prefix 취약점 해당 없음.

## 게이트 기록

`{"gate":"dev-pack","violations":3,"attempt":1}`

## 직접 경로 사용 사유

지시서에 "직접 경로" 명시는 없으나, SONNET-QUEUE 발사 템플릿("git commit/push 금지")에 따라 직접 수정·미커밋 방식으로 진행했다. 이전 DI-R-01~04와 동일 패턴.

## 추측 진행 없음

## FAIL-006/007 수정 완료

FAIL-2026-006(Storage), FAIL-2026-007(OutboxManager) 직접 원인 해소. docs/wiki 상태 갱신은 코덱스 영역이므로 이 보고서에 "FAIL-006/007 수정 완료, 코덱스가 상태 갱신 요망"으로 남긴다.

## 참조한 스킬

없음
