#Requires -Version 5.1
# team-loop 서버를 재시작한다. 돌고 있는 것을 멈추고 같은 명령으로 다시 띄운다.
#
# 왜 있는가: check-stale-server.ps1 이 "재시작해야 반영된다"고 경고만 하고 지나갔다.
# 2026-07-28 실측: 22:26 주기가 그 경고를 찍고 그대로 진행했고, 재시작은 사람이 손으로 했다.
# 감지기와 처치가 둘 다 있는데 연결이 없으면 감지기는 사람을 부르는 장치일 뿐이다.
param(
  [string]$TeamLoopRoot = 'C:\NHN Project\team-loop-lite-ai-learning',
  [int]$Port = 4173,
  [string]$LogPath = 'C:\NHN Project\_ops\team-loop-serve.log',
  [int]$WaitSeconds = 40
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $TeamLoopRoot)) { Write-Output "restart-no-root $TeamLoopRoot"; exit 2 }
$entry = Join-Path $TeamLoopRoot 'bin\team-loop.js'
if (-not (Test-Path $entry)) { Write-Output "restart-no-entry $entry"; exit 2 }

# node 를 못 찾으면 멈춘다. 죽이기만 하고 못 띄우면 서버가 아예 없어진다 -
# 고치려다 더 망가뜨리는 처치는 처치가 아니다.
$node = (Get-Command node -ErrorAction SilentlyContinue).Source
if (-not $node) { Write-Output 'restart-no-node'; exit 2 }

function Get-ListenerPid([int]$port) {
  $conn = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
  if (-not $conn) { return 0 }
  return [int]@($conn)[0].OwningProcess
}

$before = Get-ListenerPid $Port
if ($before -gt 0) {
  try { Stop-Process -Id $before -Force -ErrorAction Stop } catch { Write-Output "restart-stop-failed $before : $($_.Exception.Message)"; exit 3 }
  # 포트가 실제로 놓일 때까지 기다린다. 안 놓인 채로 띄우면 새 프로세스가 즉시 죽는다.
  for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 500
    if ((Get-ListenerPid $Port) -eq 0) { break }
  }
}

Start-Process -FilePath $node `
  -ArgumentList './bin/team-loop.js', 'serve', '--port', "$Port" `
  -WorkingDirectory $TeamLoopRoot `
  -RedirectStandardOutput $LogPath -RedirectStandardError "$LogPath.err" `
  -WindowStyle Hidden

# 떴는지는 포트로 판정한다. 프로세스가 살아 있다는 것과 서비스가 뜬 것은 다르다.
for ($i = 0; $i -lt $WaitSeconds; $i++) {
  Start-Sleep -Seconds 1
  $now = Get-ListenerPid $Port
  if ($now -gt 0 -and $now -ne $before) {
    Write-Output "restarted pid=$now (before=$before)"
    exit 0
  }
}
Write-Output "restart-did-not-listen (before=$before, ${WaitSeconds}초 기다림)"
exit 3
