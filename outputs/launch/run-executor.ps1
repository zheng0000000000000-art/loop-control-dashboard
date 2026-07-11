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
$outJson   = Join-Path $root "outputs\sonnet-$TaskId.out.json"   # claude -p --output-format json 원문
$outLog    = Join-Path $root "outputs\sonnet-$TaskId.out.log"    # 사람이 읽는 보고문(원문에서 .result만 추출)
$errLog    = Join-Path $root "outputs\sonnet-$TaskId.err.log"
$sentinel  = Join-Path $launchDir "$TaskId.exit.json"
$usageLog  = Join-Path $launchDir 'usage-ledger.jsonl'           # 상위 모델 토큰 원장(append만)

if (-not (Test-Path $promptFile)) { throw "프롬프트 파일이 없다: $promptFile" }

# 프롬프트를 파일에서 읽어 한 줄 인자로 만든다(줄바꿈은 공백으로 접는다).
$prompt  = (Get-Content $promptFile -Raw -Encoding UTF8) -replace "`r?`n", ' '
# --output-format json: 상위 모델의 usage(토큰·캐시·비용)를 받기 위함(LEDGER-05).
# 재지 않으면 "로컬로 내릴 수 있는가"를 영원히 감으로 답하게 된다.
$argline = '-p "' + $prompt.Replace('"', '\"') + '" --output-format json --dangerously-skip-permissions'

# 이전 회차의 신호가 남아 있으면 지운다(낡은 신호로 조율자가 오판하는 것을 막는다).
if (Test-Path $sentinel) { Remove-Item $sentinel -Force }

# 지시서의 '## 허용 파일 (allowlist)' 목록을 읽는다 — FILE-CLAIMS의 입력이다(P0-06).
# 주의: `return @()` 는 PowerShell이 $null로 접는다. 쉼표 연산자(,)로 빈 배열을 보존한다.
function Get-Allowlist([string]$directivePath) {
  if ([string]::IsNullOrWhiteSpace($directivePath) -or -not (Test-Path $directivePath)) { return ,@() }
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
  return ,$paths
}

# 파일 소유권 claim을 등록/해제한다(P0-06). 사후 검출(allowlist)을 실행 중 예약으로 확장한다.
# allowlistSource: 어느 지시서에서 뽑았는가. null이면 "allowlist를 모른다" — 빈 목록과 다르다.
# 모르는 것을 '비어 있음'으로 적으면 하네스가 "아무 파일도 안 잡았다"로 오판한다.
function Set-Claim([string]$status, [int]$pid_, $paths, $allowlistSource, $exitCode) {
  $claimsFile = Join-Path $root 'docs\handoff\FILE-CLAIMS.json'
  if (Test-Path $claimsFile) {
    $doc = Get-Content $claimsFile -Raw -Encoding UTF8 | ConvertFrom-Json
  } else {
    $doc = [pscustomobject]@{ schemaVersion = 2; claims = @() }
  }
  $claims = @($doc.claims | Where-Object { $_.claimId -ne "$TaskId-$pid_" })   # 같은 claim은 갱신
  $claims += [pscustomobject]@{
    claimId         = "$TaskId-$pid_"
    actor           = 'sonnet'
    taskId          = $TaskId
    paths           = @($paths)          # 항상 배열. 비어 있으면 [] 로 남는다
    allowlistSource = $allowlistSource   # null = 지시서를 못 찾음(= allowlist 미상)
    pid             = $pid_
    claimedAt       = $started
    expiresAt       = ([datetime]$started).AddHours(2).ToString('o')   # 2시간 뒤 만료 후보(보조 근거)
    status          = $status                                          # active | released
    exitCode        = $exitCode
  }
  $out = [pscustomobject]@{ schemaVersion = 2; claims = $claims } | ConvertTo-Json -Depth 6
  # ConvertTo-Json은 항목이 1개인 배열을 객체로 접는다 — claims는 항상 배열이어야 한다.
  if ($claims.Count -eq 1 -and $out -notmatch '"claims":\s*\[') {
    $out = $out -replace '("claims":\s*)(\{)', '$1[$2' -replace '(\})(\s*\})\s*$', '$1]$2'
  }
  $out | Set-Content $claimsFile -Encoding UTF8
}

$directive = Join-Path $root "docs\handoff\queue\directive-$TaskId*.md"
$dPath     = (Get-ChildItem $directive -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
$allow     = Get-Allowlist $dPath
if (-not $dPath) { Write-Warning "지시서를 못 찾았다($TaskId). allowlistSource=null 로 남긴다 — 빈 allowlist가 아니라 '미상'이다." }

$started = (Get-Date).ToString('o')
$proc = Start-Process claude.exe -ArgumentList $argline `
          -RedirectStandardOutput $outJson -RedirectStandardError $errLog `
          -PassThru -WorkingDirectory $root

# 핸들을 미리 캐시한다 — 이걸 안 하면 종료 후 ExitCode가 null이 된다(.NET 동작).
# 조율자가 exitCode로 커밋 여부를 판단하므로 null이면 오판한다. 실제로 LEDGER-04에서 null이 나왔다.
$null = $proc.Handle

# PID를 즉시 남긴다 — 대기 중 세션이 죽어도 사람이 추적할 수 있게.
$proc.Id | Out-File (Join-Path $root 'outputs\sonnet-active.pid') -Encoding ascii

# claim 등록: "이 파일들은 지금 이 PID가 잡고 있다". 실행 중 다른 주체가 만지면 scope-check가 검출한다.
Set-Claim -status 'active' -pid_ $proc.Id -paths $allow -allowlistSource $dPath -exitCode $null

$proc.WaitForExit()
$exitCode = $proc.ExitCode
# 그래도 null이면 -1로 적는다. 모르는 것을 0(성공)으로 적지 않는다.
if ($null -eq $exitCode) { $exitCode = -1 }

# claim 해제. 지우지 않고 released로 남긴다 — 누가 언제 무엇을 잡았는지가 이력이다(고아 코드 109줄 사건).
Set-Claim -status 'released' -pid_ $proc.Id -paths $allow -allowlistSource $dPath -exitCode $exitCode

# 상위 모델 usage를 뽑는다(LEDGER-05). 파싱 실패는 조용히 넘기지 않고 null로 남긴다 — 모르는 것을 0으로 적지 않는다.
$usage = $null; $report = $null
if (Test-Path $outJson) {
  try {
    $j = Get-Content $outJson -Raw -Encoding UTF8 | ConvertFrom-Json
    $report = $j.result
    $usage = [ordered]@{
      inputTokens         = $j.usage.input_tokens
      outputTokens        = $j.usage.output_tokens
      cacheCreationTokens = $j.usage.cache_creation_input_tokens   # 진짜 비용은 여기 있다
      cacheReadTokens     = $j.usage.cache_read_input_tokens
      totalCostUsd        = $j.total_cost_usd
      numTurns            = $j.num_turns
      durationMs          = $j.duration_ms
      isError             = $j.is_error
      stopReason          = $j.stop_reason
      sessionId           = $j.session_id
    }
  } catch {
    $usage = [ordered]@{ parseError = $_.Exception.Message }      # 실패를 감추지 않는다
  }
}

# 사람이 읽는 보고문을 따로 남긴다 — 기존 소비자(조율자·검수자)가 그대로 읽게.
if ($report) { $report | Set-Content $outLog -Encoding UTF8 }

# 상위 모델 토큰 원장에 append. 통째로 다시 쓰지 않는다(CLAUDE.md 기록 파일 규칙).
$ledgerLine = [ordered]@{
  taskId = $TaskId; actor = 'sonnet'; pid = $proc.Id
  startedAt = $started; exitedAt = (Get-Date).ToString('o')
  exitCode = $exitCode; usage = $usage
} | ConvertTo-Json -Depth 6 -Compress
Add-Content -Path $usageLog -Value $ledgerLine -Encoding UTF8

# 완료 신호. 조율자는 이 파일이 생겼을 때만 검수한다.
$payload = [ordered]@{
  schemaVersion = 2
  taskId        = $TaskId
  pid           = $proc.Id
  exitCode      = $exitCode
  startedAt     = $started
  exitedAt      = (Get-Date).ToString('o')
  outLog        = $outLog
  outJson       = $outJson
  processed     = $false          # 조율자가 검수 후 true로 바꾼다
  argLength     = $argline.Length # 프롬프트가 잘리지 않고 도착했는지 확인용
  usage         = $usage          # 상위 모델 토큰·비용(LEDGER-05)
}
$payload | ConvertTo-Json -Depth 6 | Set-Content $sentinel -Encoding UTF8
