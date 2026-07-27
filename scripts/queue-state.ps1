#Requires -Version 5.1
# 작업 큐의 "진행 중" 상태를 프로그램이 찍고 프로그램이 회수한다.
#
# 왜 있는가: 2026-07-27 실측 — 큐에 대기/검토 대기/완료만 있고 그 사이가 없어서
# 두 세션이 같은 항목을 동시에 집었다. 한 세션이 세션 격리를 만드는 동안
# 다른 깨어난 세션이 같은 항목을 잡고 같은 잠금을 시험하고 있었다.
#
# 낡은 "진행 중"이 항목을 영구히 붙잡으면 그게 더 나쁘다 — 영구히 막힌 것은 무시된다(FAIL-2026-010).
# 그래서 주인 세션이 죽으면 대기로 되돌린다. 판정은 프로세스 생사다.
param(
  [ValidateSet('Sweep', 'Claim', 'Release', 'Status')]
  [string]$Action = 'Status',
  [string]$QueuePath = 'C:\Users\1\Documents\Local-First Workflow Dashboard\docs\handoff\WORK-QUEUE.md',
  [int]$SessionPid = 0,
  [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

function Test-SessionAlive([int]$id) {
  if ($id -le 0) { return $false }
  $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$id" -ErrorAction SilentlyContinue
  return ($null -ne $proc -and $proc.Name -eq 'claude.exe')
}

function Write-Note([string]$text) { if (-not $Quiet) { Write-Output $text } }

if (-not (Test-Path $QueuePath)) { Write-Output 'queue-missing'; exit 2 }

# 파일을 통째로 다시 쓰지 않는다. 해당 줄만 바꾼다 — 재작성은 깨진 한글을 조용히 퍼뜨린다.
$lines = [IO.File]::ReadAllLines($QueuePath, [Text.UTF8Encoding]::new($false))
$dirty = $false

# 주인이 죽은 "진행 중"을 대기로 되돌린다. 되돌린 사실은 반드시 남긴다.
$swept = 0
for ($i = 0; $i -lt $lines.Count; $i++) {
  if ($lines[$i] -notmatch '^- \[>\] ') { continue }
  $owner = 0
  if ($lines[$i] -match 'session=(\d+)') { $owner = [int]$Matches[1] }
  if (Test-SessionAlive $owner) { continue }
  $lines[$i] = ($lines[$i] -replace '\s*—\s*진행 중 \(session=\d+[^)]*\)', '') -replace '^- \[>\] ', '- [ ] '
  $swept++
  $dirty = $true
  Write-Note "sweep: 주인 세션 $owner 이 죽어 대기로 되돌린다"
}

switch ($Action) {
  'Status' {
    $open = @($lines | Where-Object { $_ -match '^- \[ \] ' }).Count
    $busy = @($lines | Where-Object { $_ -match '^- \[>\] ' }).Count
    $review = @($lines | Where-Object { $_ -match '^- \[~\] ' }).Count
    Write-Output "open=$open  busy=$busy  review=$review  swept=$swept"
  }

  'Claim' {
    if ($SessionPid -le 0) { Write-Output 'claim-needs-session-pid'; exit 2 }
    $target = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
      if ($lines[$i] -match '^- \[ \] ') { $target = $i; break }
    }
    if ($target -lt 0) { Write-Output 'claim-nothing-open'; exit 1 }
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $lines[$target] = ($lines[$target] -replace '^- \[ \] ', '- [>] ') + " — 진행 중 (session=$SessionPid, since=$stamp)"
    $dirty = $true
    Write-Output "claimed: $($lines[$target].Substring(0, [Math]::Min(80, $lines[$target].Length)))"
  }

  'Release' {
    if ($SessionPid -le 0) { Write-Output 'release-needs-session-pid'; exit 2 }
    $found = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
      if ($lines[$i] -notmatch '^- \[>\] ') { continue }
      if ($lines[$i] -notmatch "session=$SessionPid\b") { continue }
      $lines[$i] = ($lines[$i] -replace '\s*—\s*진행 중 \(session=\d+[^)]*\)', '') -replace '^- \[>\] ', '- [ ] '
      $found++
      $dirty = $true
    }
    Write-Output "released=$found"
  }
}

if ($dirty) {
  [IO.File]::WriteAllText($QueuePath, ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
}
exit 0
