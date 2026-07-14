# transport 메커니즘 검증 — 한글·emoji·따옴표·역슬래시·여러 줄 포함 프롬프트로 sha256 일치 확인
param([switch]$CorruptedEncoding)

$root = 'C:\Users\1\Documents\Local-First Workflow Dashboard'

function Get-Sha256Hex([byte[]]$bytes) {
  $sha = [System.Security.Cryptography.SHA256]::Create()
  $hash = $sha.ComputeHash($bytes)
  $sha.Dispose()
  return ($hash | ForEach-Object { $_.ToString('x2') }) -join ''
}

# 한글·emoji·따옴표·역슬래시·여러 줄이 섞인 테스트 본문
$testText = "안녕하세요 🎉`n따옴표: `"hello`"`n역슬래시: \\path\\to\\file`n끝."

$contentJson = $testText | ConvertTo-Json -Compress
$payloadLine  = '{"type":"user","message":{"role":"user","content":' + $contentJson + '}}'
$payloadBytes = [System.Text.Encoding]::UTF8.GetBytes($payloadLine + "`n")
$promptBytes  = [System.Text.Encoding]::UTF8.GetBytes($testText)
$payloadSha   = Get-Sha256Hex $promptBytes

Write-Host "=== payload sha256 ==="
Write-Host $payloadSha

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
$null = $proc.Handle

if ($CorruptedEncoding) {
  # 손상 경로: 기본 StreamWriter(UTF-16 LE 계열)로 쓴다
  Write-Host "=== CORRUPTED ENCODING PATH ==="
  $proc.StandardInput.Write($payloadLine + "`n")
  $proc.StandardInput.Flush()
} else {
  # 올바른 경로: UTF-8 바이트 직접
  $proc.StandardInput.BaseStream.Write($payloadBytes, 0, $payloadBytes.Length)
  $proc.StandardInput.BaseStream.Flush()
}
$proc.StandardInput.Close()

$stdoutTask = $proc.StandardOutput.ReadToEndAsync()
$stderrTask = $proc.StandardError.ReadToEndAsync()
$proc.WaitForExit()

$stdout = $stdoutTask.Result
$lines  = $stdout -split "`r?`n"

Write-Host "=== stdout lines ==="
$lines | ForEach-Object { if ($_.Length -gt 0) { Write-Host $_ } }

# replay 이벤트 파싱
$replaySha = ''
foreach ($line in $lines) {
  $line = $line.Trim()
  if ([string]::IsNullOrWhiteSpace($line)) { continue }
  try {
    $obj = $line | ConvertFrom-Json
    if ($obj.type -eq 'user') {
      $content = $obj.message.content
      if ($content -is [string]) {
        $replayBytes = [System.Text.Encoding]::UTF8.GetBytes($content)
        $replaySha   = Get-Sha256Hex $replayBytes
        Write-Host "=== replay content (first 80) ==="
        Write-Host $content.Substring(0, [Math]::Min(80, $content.Length))
      }
    }
  } catch { }
}

Write-Host "=== result ==="
Write-Host "payload sha256: $payloadSha"
Write-Host "replay  sha256: $replaySha"
if ($replaySha -and $payloadSha -eq $replaySha) {
  Write-Host "MATCH ✓ — TRANSPORT_VALID"
} elseif ($replaySha) {
  Write-Host "MISMATCH — TRANSPORT_INVALID"
} else {
  Write-Host "NO REPLAY EVENT — TRANSPORT_INVALID"
}
