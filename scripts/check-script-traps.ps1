#Requires -Version 5.1
# 이 저장소에서 실제로 밟은 PowerShell 함정을 정적으로 잡는다. 하나라도 걸리면 exit 1.
#
# 왜 있는가: 2026-07-28 하루에 같은 종류의 실수를 세 번 했다. 매번 시험이 잡아줬지만
# 시험을 안 썼으면 그대로 통과했을 코드였다. 보고서에 "조심하겠다"고 적는 것은 강제가 아니다.
# 규칙은 사람이 기억할 일이 아니라 검사가 잡을 일이다.
#
# 새 규칙을 넣을 때: 반드시 -SelfTest 에 양성 사례와 음성 사례를 같이 넣는다.
# 안 잡는 검사기는 통과 도장만 찍는다.
param(
  [string]$Root = (Split-Path -Parent $PSScriptRoot),
  [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
$skipDirs = @('bin', 'obj', 'node_modules', '.git', 'history')

# 주석을 떼어낸다. 함정을 설명하는 주석 자체가 걸리면 검사기가 자기 문서를 반려한다.
# 따옴표 안의 # 은 주석이 아니다 - 앞의 따옴표 개수가 짝수일 때만 주석으로 본다.
function Remove-Comment([string]$line) {
  $single = 0
  $double = 0
  for ($i = 0; $i -lt $line.Length; $i++) {
    $ch = $line[$i]
    if ($ch -eq "'" -and $double % 2 -eq 0) { $single++ }
    elseif ($ch -eq '"' -and $single % 2 -eq 0) { $double++ }
    elseif ($ch -eq '#' -and $single % 2 -eq 0 -and $double % 2 -eq 0) { return $line.Substring(0, $i) }
  }
  return $line
}

# 규칙 1: -like 패턴에 대괄호가 있으면 리터럴로 안 잡힌다.
# 실측(2026-07-28): `$_ -like "- [!]*$reason"` 이 한 줄도 못 잡았다. -like 는 와일드카드라
# [ ] 가 문자 클래스다. 중복 억제가 영영 안 걸렸고 시험이 recent=0 으로 잡아냈다.
function Test-LikeBracket([string]$code) {
  $found = @()
  foreach ($m in [regex]::Matches($code, '-like\s+(?<q>["''])(?<pat>.*?)\k<q>')) {
    $pat = $m.Groups['pat'].Value
    if ($pat.Contains('[') -or $pat.Contains(']')) { $found += $m.Value }
  }
  return $found
}

# 규칙 2: Invoke-Git 는 Code 와 Text 를 돌려준다. .Out 은 없는 속성이라 늘 $null 이다.
# 실측(2026-07-28): `$unlanded.Out` 이 $null 이라 개수가 0 으로 남았고 착지 안 한 커밋을
# 지키는 가드가 조용히 안 걸렸다. 첫 시험에서 커밋이 그대로 지워지는 것을 보고 알았다.
# 파일 전체에 Invoke-Git 이 있는지를 인자로 받는다. 줄 단위로 보면 정의와 사용이 다른 줄에
# 있어서 못 잡는다 - 2026-07-28 자기 시험이 이걸 잡았다(기대 1, 실제 0).
function Test-InvokeGitField([string]$code, [bool]$fileUsesInvokeGit) {
  if (-not $fileUsesInvokeGit) { return @() }
  $found = @()
  foreach ($m in [regex]::Matches($code, '\$\w+\.(?<field>Out|Output|StdOut)\b')) { $found += $m.Value }
  return $found
}

# 규칙 3: Invoke-RestMethod / Invoke-WebRequest 의 -Headers 에 Cookie 를 넣으면 조용히 버려진다.
# 실측(2026-07-28): 8/1 까지 유효한 토큰인데 401 이 났다. 같은 토큰을 WebRequestSession 에
# 넣으니 바로 통했다. 버리면서 아무 말도 안 하니 만료로 오진하게 된다.
function Test-HeadersCookie([string]$code) {
  if ($code -notmatch 'Cookie') { return @() }
  if ($code -notmatch '@\{') { return @() }
  $found = @()
  foreach ($m in [regex]::Matches($code, "@\{[^}]*'?`"?Cookie'?`"?\s*=")) { $found += $m.Value.Trim() }
  return $found
}

# 규칙 4: 빈 문자열을 첫 인자로 주는 Replace/Split 은 항상 예외를 던진다.
# 실측(2026-07-28): 파이썬으로 스크립트를 만들다 역슬래시가 이스케이프로 먹혀
# Replace('', '/') 가 파일에 박혔다. 문법은 통과하고 실행 때 터진다.
# 이스케이프가 삼켜진 자리는 대개 이렇게 빈 인자로 남는다.
function Test-EmptyReplace([string]$code) {
  $found = @()
  foreach ($m in [regex]::Matches($code, "\.(Replace|Split)\(\s*''\s*,")) { $found += $m.Value }
  foreach ($m in [regex]::Matches($code, '\.(Replace|Split)\(\s*""\s*,')) { $found += $m.Value }
  return $found
}

function Invoke-Scan([string]$root) {
  $violations = @()
  foreach ($file in Get-ChildItem -Path $root -Filter '*.ps1' -Recurse -File) {
    $segments = $file.FullName.Split([char]92)
    $skip = $false
    foreach ($segment in $segments) { if ($skipDirs -contains $segment) { $skip = $true; break } }
    if ($skip) { continue }
    # 검사기 자신은 건너뛴다. 함정의 예제를 문자열로 담아야 하는 유일한 파일이라
    # 자기 자신을 반려하게 된다 - 2026-07-28 자기 시험이 이걸 잡았다.
    if ($file.Name -eq 'check-script-traps.ps1') { continue }

    $text = [IO.File]::ReadAllText($file.FullName)
    $usesInvokeGit = $text.Contains('Invoke-Git')
    $lines = [IO.File]::ReadAllLines($file.FullName)
    for ($n = 0; $n -lt $lines.Length; $n++) {
      $code = Remove-Comment $lines[$n]
      if (-not $code.Trim()) { continue }
      foreach ($hit in (Test-LikeBracket $code)) {
        $violations += "$($file.FullName):$($n + 1) like-bracket : $hit  (대괄호는 문자 클래스다. StartsWith/EndsWith 나 -match 를 써라)"
      }
      foreach ($hit in (Test-EmptyReplace $code)) {
        $violations += "$($file.FullName):$($n + 1) empty-replace : $hit  (빈 문자열 인자는 실행 때 터진다. 이스케이프가 삼켜진 자리다)"
      }
      foreach ($hit in (Test-HeadersCookie $code)) {
        $violations += "$($file.FullName):$($n + 1) headers-cookie : $hit  (PowerShell 5.1 은 -Headers 의 Cookie 를 버린다. WebRequestSession 을 써라)"
      }
      foreach ($hit in (Test-InvokeGitField $code $usesInvokeGit)) {
        $violations += "$($file.FullName):$($n + 1) invoke-git-field : $hit  (Invoke-Git 는 .Code 와 .Text 만 돌려준다)"
      }
    }
  }
  return $violations
}

if ($SelfTest) {
  $tmp = Join-Path $env:TEMP ('trapself-' + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  try {
    # 양성: 두 함정을 다 밟은 파일
    $bad = @(
      '$r = @($lines | Where-Object { $_ -like "- [!]*$reason" })',
      '$n = Invoke-Git $Root @(''rev-list'')',
      '[void][int]::TryParse(($n.Out).Trim(), [ref]$c)',
      '$h = @{ ''X-Team-Loop-Client'' = ''cli''; ''Cookie'' = $cookie }',
      '$p = $p.Replace('''', ''/'')'
    ) -join "`r`n"
    [IO.File]::WriteAllText((Join-Path $tmp 'bad.ps1'), $bad, [Text.UTF8Encoding]::new($true))
    # 음성: 고친 형태 + 함정을 설명하는 주석만 있는 파일
    $good = @(
      '$r = @($lines | Where-Object { $_.StartsWith(''- [!]'') -and $_.EndsWith($reason) })',
      '# -like 에 [ ] 를 쓰면 안 된다. 그리고 $x.Out 도 없는 속성이다. 주석은 안 걸려야 한다.',
      '$n = Invoke-Git $Root @(''rev-list'')',
      '[void][int]::TryParse(($n.Text).Trim(), [ref]$c)',
      '$ws = New-Object Microsoft.PowerShell.Commands.WebRequestSession',
      '$p = $p.Replace([char]92, ''/'')'
    ) -join "`r`n"
    [IO.File]::WriteAllText((Join-Path $tmp 'good.ps1'), $good, [Text.UTF8Encoding]::new($true))

    $all = Invoke-Scan $tmp
    $badHits = @($all | Where-Object { $_ -match 'bad\.ps1' })
    $goodHits = @($all | Where-Object { $_ -match 'good\.ps1' })
    $likeHits = @($badHits | Where-Object { $_ -match 'like-bracket' })
    $fieldHits = @($badHits | Where-Object { $_ -match 'invoke-git-field' })
    $cookieHits = @($badHits | Where-Object { $_ -match 'headers-cookie' })
    $emptyHits = @($badHits | Where-Object { $_ -match 'empty-replace' })

    "self-test 양성 like-bracket = $($likeHits.Count) (기대 1)"
    "self-test 양성 invoke-git-field = $($fieldHits.Count) (기대 1)"
    "self-test 음성 위반 = $($goodHits.Count) (기대 0)"
    "self-test 양성 headers-cookie = $($cookieHits.Count) (기대 1)"
    "self-test 양성 empty-replace = $($emptyHits.Count) (기대 1)"
    if ($likeHits.Count -ne 1 -or $fieldHits.Count -ne 1 -or $cookieHits.Count -ne 1 -or $emptyHits.Count -ne 1 -or $goodHits.Count -ne 0) {
      $all | ForEach-Object { "  $_" }
      Write-Output 'script-traps self-test FAIL'
      exit 1
    }
    Write-Output 'script-traps self-test ok'
    exit 0
  } finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
  }
}

$violations = Invoke-Scan $Root
if ($violations.Count -gt 0) {
  $violations | ForEach-Object { Write-Output $_ }
  Write-Output "script-traps $($violations.Count) 건"
  exit 1
}
Write-Output 'script-traps ok'
exit 0
