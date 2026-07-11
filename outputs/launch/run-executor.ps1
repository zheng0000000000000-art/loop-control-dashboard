# 실행자를 발사하고 종료를 기다린 뒤 완료 신호(sentinel)를 남긴다.
# 왜: 조율자가 5분마다 무조건 LLM 세션을 태우는 대신, 실행자가 끝났을 때만 검수하게 하기 위함(할당량 절감).
# 사용: powershell -NoProfile -File run-executor.ps1 -TaskId LEDGER-04
# 프롬프트는 파일로 전달한다 — 인자 경계에서 잘리는 사고(FAIL-2026-013)를 구조적으로 없앤다.
param(
  [Parameter(Mandatory = $true)][string]$TaskId
)

$root      = 'C:\Users\1\Documents\Local-First Workflow Dashboard'
$launchDir = Join-Path $root 'outputs\launch'
$promptFile= Join-Path $launchDir "$TaskId.prompt.txt"
$outLog    = Join-Path $root "outputs\sonnet-$TaskId.out.log"
$errLog    = Join-Path $root "outputs\sonnet-$TaskId.err.log"
$sentinel  = Join-Path $launchDir "$TaskId.exit.json"

if (-not (Test-Path $promptFile)) { throw "프롬프트 파일이 없다: $promptFile" }

# 프롬프트를 파일에서 읽어 한 줄 인자로 만든다(줄바꿈은 공백으로 접는다).
$prompt  = (Get-Content $promptFile -Raw -Encoding UTF8) -replace "`r?`n", ' '
$argline = '-p "' + $prompt.Replace('"', '\"') + '" --dangerously-skip-permissions'

# 이전 회차의 신호가 남아 있으면 지운다(낡은 신호로 조율자가 오판하는 것을 막는다).
if (Test-Path $sentinel) { Remove-Item $sentinel -Force }

# 지시서의 '## 허용 파일 (allowlist)' 목록을 읽는다 — FILE-CLAIMS의 입력이다(P0-06).
function Get-Allowlist([string]$directivePath) {
  if (-not (Test-Path $directivePath)) { return @() }
  $lines = Get-Content $directivePath -Encoding UTF8
  $inSection = $false
  $paths = @()
  foreach ($l in $lines) {
    if ($l -match '^##\s+허용 파일') { $inSection = $true; continue }
    if ($inSection -and $l -match '^##\s') { break }          # 다음 절에서 멈춘다
    if ($inSection -and $l -match '^\s*-\s+(\S+)') {
      $p = $matches[1].Trim('`')
      if ($p -notmatch '^\(') { $paths += $p }                # '(실행 산출물)' 같은 주석 줄은 뺀다
    }
  }
  return $paths
}

# 파일 소유권 claim을 등록/해제한다(P0-06). 사후 검출(allowlist)을 실행 중 예약으로 확장한다.
function Set-Claim([string]$status, [int]$pid_, [string[]]$paths, [string]$exitCode) {
  $claimsFile = Join-Path $root 'docs\handoff\FILE-CLAIMS.json'
  if (Test-Path $claimsFile) {
    $doc = Get-Content $claimsFile -Raw -Encoding UTF8 | ConvertFrom-Json
  } else {
    $doc = [pscustomobject]@{ schemaVersion = 1; claims = @() }
  }
  $claims = @($doc.claims | Where-Object { $_.claimId -ne "$TaskId-$pid_" })   # 같은 claim은 갱신
  $claims += [pscustomobject]@{
    claimId   = "$TaskId-$pid_"
    actor     = 'sonnet'
    taskId    = $TaskId
    paths     = $paths
    pid       = $pid_
    claimedAt = $started
    expiresAt = ([datetime]$started).AddHours(2).ToString('o')   # 2시간 뒤 자동 만료 후보
    status    = $status                                          # active | released
    exitCode  = $exitCode
  }
  [pscustomobject]@{ schemaVersion = 1; claims = $claims } |
    ConvertTo-Json -Depth 6 | Set-Content $claimsFile -Encoding UTF8
}

$directive = Join-Path $root "docs\handoff\queue\directive-$TaskId*.md"
$dPath     = (Get-ChildItem $directive -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
$allow     = Get-Allowlist $dPath

$started = (Get-Date).ToString('o')
$proc = Start-Process claude.exe -ArgumentList $argline `
          -RedirectStandardOutput $outLog -RedirectStandardError $errLog `
          -PassThru -WorkingDirectory $root

# 핸들을 미리 캐시한다 — 이걸 안 하면 종료 후 ExitCode가 null이 된다(.NET 동작).
# 조율자가 exitCode로 커밋 여부를 판단하므로 null이면 오판한다. 실제로 LEDGER-04에서 null이 나왔다.
$null = $proc.Handle

# PID를 즉시 남긴다 — 대기 중 세션이 죽어도 사람이 추적할 수 있게.
$proc.Id | Out-File (Join-Path $root 'outputs\sonnet-active.pid') -Encoding ascii

# claim 등록: "이 파일들은 지금 이 PID가 잡고 있다". 실행 중 다른 주체가 만지면 scope-check가 검출한다.
Set-Claim -status 'active' -pid_ $proc.Id -paths $allow -exitCode $null

$proc.WaitForExit()
$exitCode = $proc.ExitCode
# 그래도 null이면 -1로 적는다. 모르는 것을 0(성공)으로 적지 않는다.
if ($null -eq $exitCode) { $exitCode = -1 }

# claim 해제. 지우지 않고 released로 남긴다 — 누가 언제 무엇을 잡았는지가 이력이다(고아 코드 109줄 사건).
Set-Claim -status 'released' -pid_ $proc.Id -paths $allow -exitCode "$exitCode"

# 완료 신호. 조율자는 이 파일이 생겼을 때만 검수한다.
$payload = [ordered]@{
  schemaVersion = 1
  taskId        = $TaskId
  pid           = $proc.Id
  exitCode      = $exitCode
  startedAt     = $started
  exitedAt      = (Get-Date).ToString('o')
  outLog        = $outLog
  processed     = $false          # 조율자가 검수 후 true로 바꾼다
  argLength     = $argline.Length # 프롬프트가 잘리지 않고 도착했는지 확인용
}
$payload | ConvertTo-Json | Set-Content $sentinel -Encoding UTF8
