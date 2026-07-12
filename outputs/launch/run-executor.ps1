# 실행자를 발사하고 종료를 기다린 뒤 완료 신호(sentinel)를 남긴다.
# 왜: 조율자가 5분마다 무조건 LLM 세션을 태우는 대신, 실행자가 끝났을 때만 검수하게 하기 위함(할당량 절감).
# 사용: powershell -NoProfile -File run-executor.ps1 -TaskId LEDGER-04
# 프롬프트는 stdin으로 전달한다 — 인자 경계 사고(FAIL-2026-013)와 transport receipt(ADR-010)를 위함.
param(
  [Parameter(Mandatory = $true)][string]$TaskId
)

$root        = 'C:\Users\1\Documents\Local-First Workflow Dashboard'
$launchDir   = Join-Path $root 'outputs\launch'
$promptFile  = Join-Path $launchDir "$TaskId.prompt.txt"
$outJsonl    = Join-Path $root "outputs\sonnet-$TaskId.out.jsonl"  # stream-json 원문 JSONL
$outLog      = Join-Path $root "outputs\sonnet-$TaskId.out.log"    # 사람이 읽는 보고문
$errLog      = Join-Path $root "outputs\sonnet-$TaskId.err.log"
$sentinel    = Join-Path $launchDir "$TaskId.exit.json"
$usageLog    = Join-Path $launchDir 'usage-ledger.jsonl'
$evidenceOut = Join-Path $launchDir "$TaskId.transport.json"

if (-not (Test-Path $promptFile)) { throw "프롬프트 파일이 없다: $promptFile" }

# 지시서의 '## 허용 파일 (allowlist)' 목록을 읽는다 — FILE-CLAIMS의 입력이다(P0-06).
function Get-Allowlist([string]$directivePath) {
  if ([string]::IsNullOrWhiteSpace($directivePath) -or -not (Test-Path $directivePath)) { return ,@() }
  $lines = Get-Content $directivePath -Encoding UTF8
  $inSection = $false
  $paths = @()
  foreach ($l in $lines) {
    if ($l -match '^##\s+허용 파일') { $inSection = $true; continue }
    if ($inSection -and $l -match '^##\s') { break }
    if ($inSection -and $l -match '^\s*-\s+(\S+)') {
      $p = $matches[1].Trim('`')
      if ($p -notmatch '^\(') { $paths += $p }
    }
  }
  return ,$paths
}

# 파일 소유권 claim을 등록/해제한다(P0-06).
function Set-Claim([string]$status, [int]$pid_, $paths, $allowlistSource, $exitCode) {
  $claimsFile = Join-Path $root 'docs\handoff\FILE-CLAIMS.json'
  if (Test-Path $claimsFile) {
    $doc = Get-Content $claimsFile -Raw -Encoding UTF8 | ConvertFrom-Json
  } else {
    $doc = [pscustomobject]@{ schemaVersion = 2; claims = @() }
  }
  $claims = @($doc.claims | Where-Object { $_.claimId -ne "$TaskId-$pid_" })
  $claims += [pscustomobject]@{
    claimId         = "$TaskId-$pid_"
    actor           = 'sonnet'
    taskId          = $TaskId
    paths           = @($paths)
    allowlistSource = $allowlistSource
    pid             = $pid_
    claimedAt       = $started
    expiresAt       = ([datetime]$started).AddHours(2).ToString('o')
    status          = $status
    exitCode        = $exitCode
  }
  $out = [pscustomobject]@{ schemaVersion = 2; claims = $claims } | ConvertTo-Json -Depth 6
  if ($claims.Count -eq 1 -and $out -notmatch '"claims":\s*\[') {
    $out = $out -replace '("claims":\s*)(\{)', '$1[$2' -replace '(\})(\s*\})\s*$', '$1]$2'
  }
  $out | Set-Content $claimsFile -Encoding UTF8
}

# sha256을 바이트 배열로 계산해 소문자 64자리 hex로 반환한다.
function Get-Sha256Hex([byte[]]$bytes) {
  $sha = [System.Security.Cryptography.SHA256]::Create()
  $hash = $sha.ComputeHash($bytes)
  $sha.Dispose()
  return ($hash | ForEach-Object { $_.ToString('x2') }) -join ''
}

# stdout JSONL에서 첫 번째 사용자 프롬프트 replay 이벤트만 찾아 반환한다.
# tool_result user 이벤트(content가 배열)는 제외한다 — content가 문자열인 것만 카운트.
# StringReader로 줄 단위 읽기 — 대형 배열 분기 없이 안전하게.
function Get-ReplayEvents([string]$text) {
  $found = [System.Collections.Generic.List[object]]::new()
  $reader = [System.IO.StringReader]::new($text)
  try {
    $ln = $reader.ReadLine()
    while ($null -ne $ln) {
      $ln = $ln.Trim()
      if ($ln.Length -gt 0 -and $ln[0] -eq '{') {
        try {
          $obj = ConvertFrom-Json -InputObject $ln
          # type=user 이고 content가 문자열인 이벤트만 — tool_result 배열은 제외
          if ($obj -and $obj.type -eq 'user' -and $obj.message.content -is [string]) {
            $found.Add($obj)
          }
        } catch { }
      }
      $ln = $reader.ReadLine()
    }
  } finally {
    $reader.Dispose()
  }
  return ,$found
}

# replay 이벤트에서 content 텍스트를 추출한다 — 문자열이면 그대로, 블록 배열이면 text 필드를 이어붙인다.
function Get-ContentText($contentField) {
  if ($contentField -is [string]) { return $contentField }
  if ($contentField -is [System.Array]) {
    return ($contentField | ForEach-Object {
      if ($_ -is [string]) { $_ }
      elseif ($_.type -eq 'text') { $_.text }
      else { '' }
    }) -join ''
  }
  return ''
}

$directive = Join-Path $root "docs\handoff\queue\directive-$TaskId*.md"
$dPath     = (Get-ChildItem $directive -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
if (-not $dPath) {
    throw "발사 중단: 지시서를 못 찾았다 — 패턴: $directive (TaskId=$TaskId 를 확인하라)"
}
$allow = Get-Allowlist $dPath
if ($allow.Count -eq 0) {
    throw "발사 중단: Get-Allowlist가 빈 배열을 반환했다 — 지시서: $dPath ('^## 허용 파일' 섹션이 있는지, BOM 인코딩인지 확인하라)"
}

# 이전 sentinel 제거
if (Test-Path $sentinel) { Remove-Item $sentinel -Force }

# --- 프롬프트 준비 ---
# sourceSha256: 파일 원본 바이트의 해시
$sourceBytes = [System.IO.File]::ReadAllBytes($promptFile)
$sourceSha256 = Get-Sha256Hex $sourceBytes
$sourceByteLength = $sourceBytes.Length

# 프롬프트 본문(UTF-8 문자열). 파일 원본 그대로(BOM 제거).
$promptText = [System.Text.Encoding]::UTF8.GetString($sourceBytes).TrimStart([char]0xFEFF)

# stdin payload: stream-json 한 줄. content는 JSON 문자열로 직렬화해 삽입한다.
$contentJson = $promptText | ConvertTo-Json -Compress
$payloadLine = '{"type":"user","message":{"role":"user","content":' + $contentJson + '}}'

# payloadSha256: stdin으로 실제 보내는 바이트(개행 포함)의 해시
$payloadBytes = [System.Text.Encoding]::UTF8.GetBytes($payloadLine + "`n")
$payloadSha256 = Get-Sha256Hex $payloadBytes
$payloadByteLength = $payloadBytes.Length

$started = (Get-Date).ToString('o')

# --- claude 프로세스 시작 ---
$psi = [System.Diagnostics.ProcessStartInfo]::new('claude.exe')
$psi.Arguments = '-p --verbose --input-format stream-json --output-format stream-json --replay-user-messages --dangerously-skip-permissions'
$psi.WorkingDirectory = $root
$psi.RedirectStandardInput  = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError  = $true
$psi.UseShellExecute        = $false
$psi.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
$psi.StandardErrorEncoding  = [System.Text.UTF8Encoding]::new($false)

$proc = [System.Diagnostics.Process]::new()
$proc.StartInfo = $psi
$null = $proc.Start()
$null = $proc.Handle   # 핸들 캐시 — 종료 후 ExitCode null 방지

# 핸들을 캐시한 직후 PID를 기록한다.
$proc.Id | Out-File (Join-Path $root 'outputs\sonnet-active.pid') -Encoding ascii

Set-Claim -status 'active' -pid_ $proc.Id -paths $allow -allowlistSource $dPath -exitCode $null

# stdout/stderr 비동기 읽기를 stdin 쓰기 전에 시작한다 — 먼저 시작해야 stdout 버퍼 교착을 막는다.
$stdoutTask = $proc.StandardOutput.ReadToEndAsync()
$stderrTask = $proc.StandardError.ReadToEndAsync()

# ★ UTF-8 바이트를 BaseStream에 직접 쓴다 — PowerShell 5.1 기본 writer는 한글을 깨뜨린다(ADR-010 §6).
$proc.StandardInput.BaseStream.Write($payloadBytes, 0, $payloadBytes.Length)
$proc.StandardInput.BaseStream.Flush()
$proc.StandardInput.Close()

$proc.WaitForExit()
$exitCode = $proc.ExitCode
if ($null -eq $exitCode) { $exitCode = -1 }

$stdoutAll = $stdoutTask.Result
$stderrAll = $stderrTask.Result

# stderr를 errLog에 쓴다.
if ($stderrAll) { $stderrAll | Set-Content $errLog -Encoding UTF8 }

# stdout JSONL을 파일로 보관한다.
if ($stdoutAll) { $stdoutAll | Set-Content $outJsonl -Encoding UTF8 }

# --- transport receipt 계산 ---
$replayEvents = Get-ReplayEvents $stdoutAll

$transportVerdict  = 'TRANSPORT_INVALID'
$replaySha256      = ''
$replayByteLength  = 0
$replayEventCount  = $replayEvents.Count

if ($replayEventCount -eq 1) {
  $replayText   = Get-ContentText $replayEvents[0].message.content
  $replayBytes  = [System.Text.Encoding]::UTF8.GetBytes($replayText)
  $replaySha256 = Get-Sha256Hex $replayBytes
  $replayByteLength = $replayBytes.Length

  $payloadContentSha = Get-Sha256Hex ([System.Text.Encoding]::UTF8.GetBytes($promptText))
  if ($replaySha256 -eq $payloadContentSha) { $transportVerdict = 'TRANSPORT_VALID' }
} elseif ($replayEventCount -eq 0) {
  Write-Warning "replay 이벤트 없음 — TRANSPORT_INVALID"
} else {
  Write-Warning "replay 이벤트 $replayEventCount 개 (1개여야 한다) — TRANSPORT_INVALID"
}

# CLI 버전 수집
$cliVersion = & claude.exe --version 2>&1 | Select-Object -First 1
if (-not $cliVersion) { $cliVersion = 'unknown' }

# transport evidence 파일을 남긴다.
$evidence = [ordered]@{
  schemaVersion    = 1
  taskId           = $TaskId
  cliVersion       = "$cliVersion"
  sourceSha256     = $sourceSha256
  payloadSha256    = (Get-Sha256Hex ([System.Text.Encoding]::UTF8.GetBytes($promptText)))
  replaySha256     = $replaySha256
  sourceByteLength = $sourceByteLength
  payloadByteLength = ([System.Text.Encoding]::UTF8.GetBytes($promptText)).Length
  replayByteLength = $replayByteLength
  replayEventCount = $replayEventCount
  pid              = $proc.Id
  startedAt        = $started
  exitedAt         = (Get-Date).ToString('o')
  verdict          = $transportVerdict
}
$evidence | ConvertTo-Json -Depth 4 | Set-Content $evidenceOut -Encoding UTF8

$transportValid = ($transportVerdict -eq 'TRANSPORT_VALID')

# --- usage 추출 (stream-json JSONL에서) ---
$usage  = $null
$report = $null
$_usageReader = [System.IO.StringReader]::new($stdoutAll)
try {
  $_ln = $_usageReader.ReadLine()
  while ($null -ne $_ln) {
    $_ln = $_ln.Trim()
    if ($_ln.Length -gt 0 -and $_ln[0] -eq '{') {
      try {
        $obj = ConvertFrom-Json -InputObject $_ln
        if ($obj -and $obj.type -eq 'result') {
          $report = $obj.result
          $usage = [ordered]@{
            inputTokens         = $obj.usage.input_tokens
            outputTokens        = $obj.usage.output_tokens
            cacheCreationTokens = $obj.usage.cache_creation_input_tokens
            cacheReadTokens     = $obj.usage.cache_read_input_tokens
            totalCostUsd        = $obj.total_cost_usd
            numTurns            = $obj.num_turns
            durationMs          = $obj.duration_ms
            isError             = $obj.is_error
            stopReason          = $obj.stop_reason
            sessionId           = $obj.session_id
          }
        }
      } catch { }
    }
    $_ln = $_usageReader.ReadLine()
  }
} finally { $_usageReader.Dispose() }

# 사람이 읽는 보고문
if ($report) { $report | Set-Content $outLog -Encoding UTF8 }

Set-Claim -status 'released' -pid_ $proc.Id -paths $allow -allowlistSource $dPath -exitCode $exitCode

# 토큰 원장 append
$ledgerLine = [ordered]@{
  taskId = $TaskId; actor = 'sonnet'; pid = $proc.Id
  startedAt = $started; exitedAt = (Get-Date).ToString('o')
  exitCode = $exitCode; usage = $usage
} | ConvertTo-Json -Depth 6 -Compress
Add-Content -Path $usageLog -Value $ledgerLine -Encoding UTF8

# 완료 sentinel
$payload = [ordered]@{
  schemaVersion    = 2
  taskId           = $TaskId
  pid              = $proc.Id
  exitCode         = $exitCode
  startedAt        = $started
  exitedAt         = (Get-Date).ToString('o')
  outLog           = $outLog
  outJson          = $outJsonl
  processed        = $false
  transportValid   = $transportValid
  transportEvidence = $evidenceOut
  usage            = $usage
}
$payload | ConvertTo-Json -Depth 6 | Set-Content $sentinel -Encoding UTF8
