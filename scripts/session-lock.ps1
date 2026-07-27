#Requires -Version 5.1
# 한 저장소를 한 세션만 쓰게 만드는 잠금.
#
# 왜 있는가: 2026-07-27 실측 — 실행 세션과 판정 세션이 같은 작업 트리를 동시에 만졌고,
# 한쪽의 `git add -A` 가 다른 쪽 파일을 자기 커밋에 쓸어 담았다(c081dc4, 9bfda28).
# 커밋 메시지와 내용이 어긋났다. 프롬프트로 "동시에 하지 마라"고 적어봐야 안 지켜진다.
#
# 판정은 프로세스 생사다. 시각이 아니라 실체를 본다 — 죽은 세션의 잠금은 자동으로 회수한다.
# 그래야 세션이 비정상 종료해도 루프가 영구히 막히지 않는다.
param(
  [ValidateSet('Acquire', 'Release', 'Status', 'Owner')]
  [string]$Action = 'Status',
  [string]$LockPath = 'C:\NHN Project\_ops\coordinator.lock',
  [string]$Label = '',
  # 깨우기처럼 claude.exe 조상이 없는 프로세스가, 자기가 띄운 세션 대신 잠금을 쥐어줄 때 쓴다.
  [int]$SessionPid = 0,
  [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

# 이 프로세스를 낳은 세션(claude.exe)을 조상 사슬에서 찾는다.
# 세션마다 프로세스가 다르므로 이것이 곧 세션 식별자다.
function Get-SessionPid {
  $current = $PID
  for ($depth = 0; $depth -lt 12; $depth++) {
    $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$current" -ErrorAction SilentlyContinue
    if (-not $proc) { return 0 }
    if ($proc.Name -eq 'claude.exe') { return [int]$proc.ProcessId }
    if (-not $proc.ParentProcessId) { return 0 }
    $current = [int]$proc.ParentProcessId
  }
  return 0
}

# 그 pid 가 아직 claude.exe 로 살아 있는지. 죽었으면 잠금은 무효다.
function Test-SessionAlive([int]$sessionPid) {
  if ($sessionPid -le 0) { return $false }
  $proc = Get-CimInstance Win32_Process -Filter "ProcessId=$sessionPid" -ErrorAction SilentlyContinue
  return ($null -ne $proc -and $proc.Name -eq 'claude.exe')
}

function Read-Lock {
  if (-not (Test-Path $LockPath)) { return $null }
  try { return Get-Content -Raw -Encoding UTF8 $LockPath | ConvertFrom-Json } catch { return $null }
}

$me = if ($SessionPid -gt 0) { $SessionPid } else { Get-SessionPid }
$held = Read-Lock

# 주인이 죽었으면 회수한다. 회수했다는 사실은 반드시 남긴다 — 조용히 뺏으면 원인 추적이 끊긴다.
$reclaimed = $false
if ($held -and -not (Test-SessionAlive ([int]$held.sessionPid))) {
  $reclaimed = $true
  Remove-Item $LockPath -Force -ErrorAction SilentlyContinue
  $held = $null
}

switch ($Action) {
  'Owner' {
    if ($held) { Write-Output $held.sessionPid } else { Write-Output '0' }
    exit 0
  }

  'Status' {
    if ($reclaimed) { Write-Output 'lock-reclaimed (이전 주인 세션이 죽어 있었다)' }
    if (-not $held) { Write-Output "lock-free  me=$me"; exit 0 }
    if ([int]$held.sessionPid -eq $me) { Write-Output "lock-mine  pid=$me"; exit 0 }
    Write-Output "lock-held-by-other  owner=$($held.sessionPid)  me=$me  label=$($held.label)"
    exit 1
  }

  'Acquire' {
    if ($me -le 0) { Write-Output 'no-session (claude.exe 조상을 못 찾았다 - 잠금을 걸지 않는다)'; exit 2 }
    if ($held -and [int]$held.sessionPid -ne $me) {
      Write-Output "acquire-denied  owner=$($held.sessionPid)  label=$($held.label)"
      exit 1
    }
    $payload = [ordered]@{
      sessionPid = $me
      label      = $Label
      acquiredAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
      host       = $env:COMPUTERNAME
    } | ConvertTo-Json
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LockPath) | Out-Null
    [IO.File]::WriteAllText($LockPath, $payload, [Text.UTF8Encoding]::new($false))
    if (-not $Quiet) { Write-Output "acquired  pid=$me" }
    exit 0
  }

  'Release' {
    if (-not $held) { if (-not $Quiet) { Write-Output 'release-noop (잠금 없음)' }; exit 0 }
    if ([int]$held.sessionPid -ne $me) {
      Write-Output "release-denied  owner=$($held.sessionPid)  me=$me"
      exit 1
    }
    Remove-Item $LockPath -Force
    if (-not $Quiet) { Write-Output "released  pid=$me" }
    exit 0
  }
}
