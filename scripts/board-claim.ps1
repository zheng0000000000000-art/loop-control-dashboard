#Requires -Version 5.1
# 보드 태스크에 "누가 지금 이걸 하고 있다"를 찍는다. 큐의 [>] 진행 중 표기와 같은 짝이다.
#
# 왜 있는가: 2026-07-28 - claim_task 를 못 쓰게 하면서(납품 게이트가 AGENT 실행자 종료 코드를
# 요구해서) 보드에서 IN_PROGRESS 를 찍던 유일한 수단이 같이 사라졌다. 세션이 8분째 돌고 있는데
# 보드는 READY / IDLE 이었다. 사람 눈에 안 보이면 안 돈 것과 같다.
#
# executionMode 는 EXTERNAL_AGENT 로 찍는다. AGENT 로 바뀌는 것이 원래 문제였고,
# 그 모드에서만 team-loop 서버가 claim_task 의 덮어쓰기를 막아준다.
param(
  [ValidateSet('Claim', 'Release')]
  [string]$Action = 'Claim',
  [string]$BoardPath = 'C:\NHN Project\team-loop-lite-ai-learning\data\tasks.json',
  [string]$TaskId = '',
  [string]$SessionId = ''
)

$ErrorActionPreference = 'Stop'
if (-not $TaskId) { Write-Output 'needs-task-id'; exit 2 }
if (-not (Test-Path $BoardPath)) { Write-Output 'board-missing'; exit 2 }

$board = Get-Content -Raw -Encoding UTF8 $BoardPath | ConvertFrom-Json
$tasks = @(if ($board.tasks) { $board.tasks } else { $board })
$task = $tasks | Where-Object { $_.id -eq $TaskId } | Select-Object -First 1
if (-not $task) { Write-Output "task-not-found $TaskId"; exit 1 }

$now = [datetimeoffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')

if ($Action -eq 'Claim') {
  if (@('TODO', 'READY') -notcontains $task.status) {
    Write-Output "claim-skipped $TaskId 는 $($task.status) 다"
    exit 0
  }
  $task.status = 'IN_PROGRESS'
  # 여기서 EXTERNAL_AGENT 를 찍는다. 이것이 있어야 team-loop 서버가 claim_task 의 AGENT 를
  # 무시한다(server.js). 세션이 claim_task 를 불러도 모드가 안 바뀌고, 납품 게이트는
  # 실행자 종료 코드 대신 실제로 돌린 검사를 요구한다.
  $task.executionMode = 'EXTERNAL_AGENT'
  # RUNNING 이어야 한다. IDLE 로 두면 board-sweep 이 "상태 모순"으로 보고 30분 뒤 뺏는다.
  $task.executionState = 'RUNNING'
  $task.executor = [pscustomobject]@{
    tool = 'coordinator-wake'
    session = $SessionId
    setAt = $now
  }
  $task.updatedAt = $now
  Write-Output "board-claimed $TaskId (session=$SessionId)"
} else {
  # 세션이 REVIEW 나 DONE 으로 올려놨으면 손대지 않는다. 되돌리면 일한 결과를 지우는 셈이다.
  if ($task.status -ne 'IN_PROGRESS') {
    Write-Output "board-release-noop $TaskId 는 이미 $($task.status) 다"
    exit 0
  }
  $task.status = 'READY'
  $task.executionState = 'IDLE'
  $task.executor = $null
  $task.updatedAt = $now
  Write-Output "board-released $TaskId (세션이 끝났는데 상태를 안 옮겼다)"
}

$json = $board | ConvertTo-Json -Depth 40
$tmp = "$BoardPath.tmp"
[IO.File]::WriteAllText($tmp, $json, [Text.UTF8Encoding]::new($false))
Move-Item -Force $tmp $BoardPath
exit 0
