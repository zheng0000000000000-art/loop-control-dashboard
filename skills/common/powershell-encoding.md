# PowerShell 인코딩 — 이 저장소가 **네 번** 당한 함정

> **트리거**: PowerShell로 파일을 읽거나 쓰거나, 다른 프로세스에 텍스트를 넘길 때. 한국어가 섞여 있으면 **항상**.

## 규칙 (외워라)

| 하는 일 | **쓸 것** | **쓰지 말 것** |
| --- | --- | --- |
| 파일 읽기 | `[IO.File]::ReadAllText($p, [Text.Encoding]::UTF8)` | `Get-Content $p -Raw` ← **인코딩 미지정 = 코드페이지 949로 읽어 한글을 파괴한다** |
| 파일 쓰기 | `[IO.File]::WriteAllText($p, $t, (New-Object Text.UTF8Encoding $false))` | `Set-Content -Encoding UTF8` ← **BOM을 붙인다** |
| 프로세스에 텍스트 전달 | 파일 + **stdin 리다이렉션** `cmd /c "prog - < file"` | `type file \| prog` ← **cmd 파이프가 코드페이지로 재인코딩한다** |
| 프로세스에 텍스트 전달 | 위와 같음 | **명령행 인자로 긴 한글을 넘기는 것** ← 인자도 콘솔 인코딩을 탄다 |
| git 출력 캡처 | `[Console]::OutputEncoding = [Text.Encoding]::UTF8` 를 **먼저** 설정 | 그냥 `git diff \| Out-String` ← 한글 diff가 깨진다 |

**부득이 `Get-Content`를 쓸 거면 `-Encoding UTF8`을 반드시 붙여라.**

## 확인하는 법 — **주장하지 말고 재라**

```powershell
# BOM 검사
$b = [IO.File]::ReadAllBytes($p)
if ($b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF) { throw "BOM이 붙었다: $p" }

# 파괴 검사 — U+FFFD(대체 문자)가 있으면 이미 어딘가에서 디코딩이 실패했다
if ([IO.File]::ReadAllText($p,[Text.Encoding]::UTF8).Contains([char]0xFFFD)) { throw "깨진 문자: $p" }
```

**왕복 시험이 정본이다.** 한글 문자열을 써서 → 넘겨서 → 돌려받아서 → **원본과 같은지 비교하라.**
"UTF-8로 썼다"는 주장은 증거가 아니다.

## 실측 사고 네 건 (전부 이 저장소에서 일어났다)

1. **`run-executor.ps1`이 BOM 없는 UTF-8이라 PS 5.1이 한글 리터럴을 깨뜨렸다.**
   `Get-Allowlist`의 정규식 `'^##\s+허용 파일'`이 매치에 실패 → `FILE-CLAIMS.paths`가 **항상 0**.
   → **P0-06(파일 소유권 사전 차단)이 한 번도 작동한 적이 없었다.** 게이트는 `claimConflictCount: 0`(평화)을 보고했다.
   **빈 배열끼리의 비교였다.**

2. **콘솔 출력의 한글이 깨져 오류 문구를 오독할 뻔했다.** (`寃쎈줈…`)

3. **★ 검수자가 코덱스에 준 입력을 파괴했다 (2026-07-13).**
   fixture를 `Get-Content -Raw`(인코딩 미지정)로 읽어 검수 패킷을 만들었다.
   → **읽는 순간 한글이 깨졌다.** 코덱스는 정상 fixture 5종을 **"malformed JSON"으로 오독**하고
   **없는 결함을 보고했다.** 하마터면 그 보고로 실행자를 반려할 뻔했다.
   > **독립 검수자에게 준 입력이 오염되면 그가 내는 판정도 오염된다. 검수 배선도 검수 대상이다.**
   실제 원인 규명 과정에서 **BOM을 범인으로 오인**하기도 했다 — BOM을 고쳐도 왕복이 계속 깨졌고,
   그제야 **읽기 쪽**(`Get-Content`)이 진범임이 드러났다. **증상을 고치지 말고 원인을 지목하라.**

4. **`type file | codex` 의 cmd 파이프가 코드페이지로 재인코딩했다.**
   `<` 리다이렉션으로 바꾸고 나서야 왕복이 성공했다.

## 왜 이 함정이 반복되는가

**세 계층이 각각 다른 기본 인코딩을 쓴다.**

```
PowerShell 5.1 기본 인코딩  = 시스템 코드페이지 (한국어 Windows = 949)
.NET / 파일 실체            = UTF-8
cmd 파이프                  = 콘솔 코드페이지
```

**한 계층만 맞춰도 다른 계층에서 깨진다.** 그래서 "UTF-8로 썼다"가 증거가 되지 못한다.
**계층을 통과한 뒤에 재라.**

## 이 규칙이 지켜지는 곳 (참고 구현)

- `outputs/launch/run-executor.ps1:24` — `Get-Content $directivePath -Encoding UTF8` (allowlist 파싱)
- `outputs/launch/run-executor.ps1:173` — `StandardInput.BaseStream.Write($payloadBytes, ...)` (stdin에 **바이트를 직접** 쓴다. PS의 writer를 거치지 않는다)
- `outputs/launch/run-codex-review.ps1` — `Read-Utf8` / `Write-Utf8NoBom` / `Assert-Utf8NoBom` + stdin 리다이렉션
