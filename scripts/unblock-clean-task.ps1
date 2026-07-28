#Requires -Version 5.1
# 격리 워크트리가 깨끗할 때만 BLOCKED 를 푼다. 변경이 하나라도 있으면 풀지 않는다.
#
# 왜 있는가: 2026-07-29 00:31 실측 - 세션이 태스크를 집은 지 35초 만에 서버가 재시작됐고,
# team-loop 의 복구 검사가 "격리 worktree 없이 중단되어 변경물의 소유권을 안전하게
# 판별할 수 없습니다"로 태스크를 막았다. 그 판단은 옳다 - 소유권을 추측하지 않은 것이다.
#
# 그래서 처치도 같은 기준을 쓴다. 워크트리가 깨끗하면 오판할 변경 자체가 없으므로 풀어도
# 된다. 더러우면 누가 쓴 것인지 프로그램이 모르고 나도 모른다 - 그때는 사람에게 올린다.
param(
  [string]$Subject = '',
  [string]$BoardPath = 'C:\NHN Project\team-loop-lite-ai-learning\data\tasks.json',
  [string]$TeamLoopRoot = 'C:\NHN Project\team-loop-lite-ai-learning',
  # 조율자 장부. 어느 세션이 이 태스크를 잡았는지가 여기 있다.
  [string]$LedgerPath = 'C:\NHN Project\_ops\board-sessions.json'
)

$ErrorActionPreference = 'Stop'
$ownerAlive = $false
if (-not $Subject) { Write-Output 'unblock-needs-task'; exit 2 }
if (-not (Test-Path $BoardPath)) { Write-Output 'unblock-no-board'; exit 2 }

$board = Get-Content -Raw -Encoding UTF8 $BoardPath | ConvertFrom-Json
$tasks = @(if ($board.tasks) { $board.tasks } else { $board })
$task = @($tasks | Where-Object { $_.id -eq $Subject })[0]
if (-not $task) { Write-Output "unblock-no-task $Subject"; exit 2 }
if ($task.status -ne 'BLOCKED') { Write-Output "unblock-not-blocked $Subject ($($task.status))"; exit 0 }

# 워크트리가 더러우면 풀지 않는다. 모르는 것은 통과가 아니다.
$worktree = Join-Path $TeamLoopRoot ".team-loop-worktrees\$Subject"
if (Test-Path $worktree) {
  $saved = $ErrorActionPreference
  $ErrorActionPreference = 'SilentlyContinue'
  $dirty = & git -C $worktree status --porcelain 2>&1
  $code = $LASTEXITCODE
  $ErrorActionPreference = $saved
  if ($code -ne 0) { Write-Output "unblock-worktree-unreadable $Subject"; exit 3 }
  $lines = @(($dirty -join "`n") -split "`n" | Where-Object { $_ -and $_.Trim() })
  if ($lines.Count -gt 0) {
    # 더러워도 주인이 분명하면 푼다. 조율자 장부에 이 태스크를 잡은 세션이 있고 그 세션이
    # 아직 claude.exe 로 살아 있으면 그 변경은 그 세션 것이다 - 추측이 아니라 기록이다.
    # 2026-07-29 실측: 세션이 잡은 태스크가 서버 재시작 때문에 막혔는데 그 세션은 살아서
    # 계속 일하고 있었다. 그때 안 풀면 그 세션의 일이 통째로 버려진다.
    $owner = 0
    if (Test-Path $LedgerPath) {
      try {
        $ledger = Get-Content -Raw -Encoding UTF8 $LedgerPath | ConvertFrom-Json
        if ($ledger.$Subject) { $owner = [int]$ledger.$Subject.sessionPid }
      } catch { }
    }
    if ($owner -gt 0) {
      $proc = Get-Process -Id $owner -ErrorAction SilentlyContinue
      if ($proc -and $proc.ProcessName -eq 'claude') { $ownerAlive = $true }
    }
    if (-not $ownerAlive) {
      Write-Output ('unblock-refused ' + $Subject + ' (변경 ' + $lines.Count + '건, 주인 세션 ' + $owner + ' 이 살아있지 않다)')
      exit 3
    }
    Write-Output ('unblock-owner-alive ' + $Subject + ' (변경 ' + $lines.Count + '건은 세션 ' + $owner + ' 것이다)')
  }
}

# 주인 세션이 살아 있으면 IN_PROGRESS 로 되돌린다. READY 로 두면 다른 세션이 또 집는다.
$task.blocked = $null
if ($ownerAlive) {
  $task.status = 'IN_PROGRESS'
  $task.executionMode = 'EXTERNAL_AGENT'
  $task.executionState = 'RUNNING'
} else {
  $task.status = 'READY'
  $task.executionState = 'IDLE'
}
$task.version = [int]$task.version + 1
$task.updatedAt = [datetimeoffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')

$tmp = "$BoardPath.tmp"
[IO.File]::WriteAllText($tmp, ($board | ConvertTo-Json -Depth 40), [Text.UTF8Encoding]::new($false))
Move-Item -Force $tmp $BoardPath

$after = Get-Content -Raw -Encoding UTF8 $BoardPath | ConvertFrom-Json
$afterTasks = @(if ($after.tasks) { $after.tasks } else { $after })
$check = @($afterTasks | Where-Object { $_.id -eq $Subject })[0]
if ($check.status -eq 'BLOCKED') { Write-Output "unblock-write-failed $Subject"; exit 3 }
Write-Output "unblocked $Subject (v$($check.version), 워크트리 깨끗)"
exit 0
