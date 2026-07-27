#Requires -Version 5.1
# 보드 태스크에 "누가 지금 이걸 하고 있다"를 찍고, 어느 세션이 실행했는지를 조율자 장부에 남긴다.
#
# 왜 있는가: 2026-07-28 - claim_task 를 못 쓰게 하면서(납품 게이트가 AGENT 실행자 종료 코드를
# 요구해서) 보드에서 IN_PROGRESS 를 찍던 유일한 수단이 같이 사라졌다. 세션이 8분째 돌고 있는데
# 보드는 READY / IDLE 이었다. 사람 눈에 안 보이면 안 돈 것과 같다.
#
# 세션 pid 를 왜 task.executor 안에 안 두는가: executor 는 team-loop 이 소유한 필드다.
# server.js 의 여섯 자리가 sanitizeExecutorInput 으로 통째로 덮어쓴다. 세션이 claim_task 나
# submit_task_result 를 부르는 순간 우리가 넣은 값은 사라진다. 남의 장부에 우리 기록을 적으면
# 그쪽이 지울 때 같이 지워진다. 조율자의 장부는 조율자가 갖는다.
#
# executionMode 는 EXTERNAL_AGENT 로 찍는다. AGENT 로 바뀌는 것이 원래 문제였고,
# 그 모드에서만 team-loop 서버가 claim_task 의 덮어쓰기를 막아준다.
param(
  [ValidateSet('Claim', 'Release')]
  [string]$Action = 'Claim',
  [string]$BoardPath = 'C:\NHN Project\team-loop-lite-ai-learning\data\tasks.json',
  # 조율자 장부. 어느 세션이 어느 태스크를 실행했는지만 담는다.
  # MCP approve_task 가 이것을 읽어 "이 세션이 실행했나"를 판별한다.
  [string]$LedgerPath = 'C:\NHN Project\_ops\board-sessions.json',
  [string]$TaskId = '',
  [string]$SessionId = '',
  # 세션 식별자는 claude.exe pid 다. session-lock.ps1 과 같은 것을 세션이라고 부른다.
  [int]$SessionPid = 0
)

$ErrorActionPreference = 'Stop'
if (-not $TaskId) { Write-Output 'needs-task-id'; exit 2 }
if (-not (Test-Path $BoardPath)) { Write-Output 'board-missing'; exit 2 }

function Read-Ledger {
  if (-not (Test-Path $LedgerPath)) { return (New-Object psobject) }
  try { return Get-Content -Raw -Encoding UTF8 $LedgerPath | ConvertFrom-Json } catch { return (New-Object psobject) }
}

function Save-Ledger($ledger) {
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LedgerPath) | Out-Null
  [IO.File]::WriteAllText($LedgerPath, ($ledger | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
}

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
  $task.executionMode = 'EXTERNAL_AGENT'
  # RUNNING 이어야 한다. IDLE 로 두면 board-sweep 이 "상태 모순"으로 보고 30분 뒤 뺏는다.
  $task.executionState = 'RUNNING'
  $task.executor = [pscustomobject]@{ tool = 'coordinator-wake'; session = $SessionId; setAt = $now }
  $task.updatedAt = $now

  $ledger = Read-Ledger
  $entry = [pscustomobject]@{ sessionPid = $SessionPid; sessionId = $SessionId; claimedAt = $now }
  $ledger | Add-Member -NotePropertyName $TaskId -NotePropertyValue $entry -Force
  Save-Ledger $ledger
  Write-Output "board-claimed $TaskId (session=$SessionId pid=$SessionPid)"
} else {
  # 장부는 지우지 않는다. 실행 세션이 누구였는지는 승인 시점에 필요한 정보다.
  # 세션이 REVIEW 나 DONE 으로 올려놨으면 상태도 손대지 않는다 - 되돌리면 일한 결과를 지운다.
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
