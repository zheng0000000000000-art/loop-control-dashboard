#Requires -Version 5.1
# 작업보드의 좀비 태스크를 회수한다. 큐 sweep 의 거울이다.
#
# 왜 있는가: 2026-07-27 실측 — 03:22 의 실패한 발사가 태스크를 IN_PROGRESS 로 바꿔놓고
# 아무도 되돌리지 않아 **10.2시간째** 그대로였다. executionState 는 IDLE 인데 status 만
# IN_PROGRESS 인 모순 상태다.
#
# 큐에는 sweep 이 있어 주인 죽은 [>] 를 대기로 되돌리는데 보드에는 없었다. 그 비대칭 때문에
# 발사가 실패할 때마다 좀비가 하나씩 쌓인다. 그리고 boardReady 는 IN_PROGRESS 를 안 세므로
# 아무에게도 안 보인다 — 영구히 막힌 것은 무시된다(FAIL-2026-010).
#
# 판정은 두 가지 실체다. 상태 모순(IN_PROGRESS 인데 executionState 가 IDLE)과 마지막 갱신 나이.
# 실제로 돌고 있는 작업을 뺏지 않으려고 나이 하한을 둔다.
param(
  [string]$BoardPath = 'C:\NHN Project\team-loop-lite-ai-learning\data\tasks.json',
  # 상태가 모순인 경우(IDLE 인데 IN_PROGRESS)에 쓰는 하한. 방금 시작한 것을 뺏지 않기 위한 값이다.
  [int]$StaleMinutes = 30,
  # 상태가 모순이 아니어도 이만큼 낡으면 좀비로 본다. 실행자가 죽었어도 상태를 안 남길 수 있다.
  [int]$HardStaleMinutes = 360,
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $BoardPath)) { Write-Output 'board-missing'; exit 2 }

$raw = Get-Content -Raw -Encoding UTF8 $BoardPath
$board = $raw | ConvertFrom-Json
$tasks = @(if ($board.tasks) { $board.tasks } else { $board })

$now = [datetimeoffset]::UtcNow
$revived = @()

foreach ($task in $tasks) {
  if ($task.archived) { continue }
  if ($task.status -ne 'IN_PROGRESS') { continue }

  $age = 99999
  if ($task.updatedAt) {
    try { $age = [int]($now - [datetimeoffset]::Parse($task.updatedAt)).TotalMinutes } catch { $age = 99999 }
  }

  $idle = ($null -eq $task.executionState) -or ($task.executionState -eq 'IDLE')
  $reason = $null
  if ($idle -and $age -ge $StaleMinutes) {
    $reason = "상태 모순(IN_PROGRESS 인데 executionState=$($task.executionState)) + ${age}분 방치"
  } elseif ($age -ge $HardStaleMinutes) {
    $reason = "executionState=$($task.executionState) 이지만 ${age}분 방치 - 실행자가 상태를 안 남기고 죽은 것으로 본다"
  }
  if (-not $reason) { continue }

  $revived += [pscustomobject]@{ Id = $task.id; Title = $task.title; Age = $age; Reason = $reason }
  if (-not $DryRun) {
    $task.status = 'READY'
    $task.executionState = 'IDLE'
    $task.executor = $null
    $task.updatedAt = $now.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
  }
}

if ($revived.Count -eq 0) { Write-Output 'board-sweep: 좀비 없음'; exit 0 }

foreach ($r in $revived) {
  $verb = if ($DryRun) { 'would-revive' } else { 'revived' }
  Write-Output "board-sweep ${verb}: $($r.Id) [$($r.Age)분] $($r.Title)"
  Write-Output "  근거: $($r.Reason)"
}

if ($DryRun) { exit 1 }

# 통째로 다시 쓰지 않는다 - 원본 구조를 유지한 채 값만 바꿔 직렬화한다.
$json = $board | ConvertTo-Json -Depth 40
$tmp = "$BoardPath.tmp"
[IO.File]::WriteAllText($tmp, $json, [Text.UTF8Encoding]::new($false))
Move-Item -Force $tmp $BoardPath
Write-Output "board-sweep: $($revived.Count)건 READY 로 되돌렸다"
exit 0
