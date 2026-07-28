#Requires -Version 5.1
# 보드가 마르면 백로그에서 다음 일감을 꺼내 보드 태스크로 올린다.
#
# 왜 있는가: 2026-07-28 - 실행과 판정은 이미 조율자 손을 떠났는데 "무슨 일이 있는가"를 정하는
# 것만 조율자 대화에 묶여 있었다. 그래서 큐가 마르면 루프는 멀쩡한데도 멈췄다. 조율자가
# 단일 장애점이었다. 일감은 파일에서 나와야 한다.
#
# 발명하지 않는다: 백로그에 선언된 항목만 올린다. 세션이 스스로 일을 지어내게 하면 루프가
# 자기한테 무한히 일을 만든다. 새 항목을 넣는 것은 사람과 조율자가 하고, 꺼내 쓰는 것만
# 프로그램이 한다.
#
# 한 번에 하나만 올린다. 보드가 말랐다고 백로그를 통째로 쏟으면 그건 보충이 아니라 범람이다.
param(
  [string]$BoardPath = 'C:\NHN Project\team-loop-lite-ai-learning\data\tasks.json',
  [string]$BacklogPath = 'C:\Users\1\Documents\Local-First Workflow Dashboard\docs\plan\BACKLOG.json',
  [string]$ServerUrl = 'http://localhost:4173',
  # 조율자가 판단할 일은 세션이 집을 수 없다. 그래서 보드가 말랐는지 셀 때도 빼야 한다 -
  # 안 그러면 아무도 못 집는 태스크 하나 때문에 백로그가 영원히 안 열린다.
  [string[]]$UnclaimableMarkers = @('[조율자 판단]', '[사람 게이트]'),
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $BacklogPath)) { Write-Output 'backlog-missing'; exit 2 }
if (-not (Test-Path $BoardPath)) { Write-Output 'board-missing'; exit 2 }

# 백로그를 못 읽으면 아무것도 하지 않는다. 모르는 것은 통과가 아니다.
try {
  $backlog = Get-Content -Raw -Encoding UTF8 $BacklogPath | ConvertFrom-Json
} catch {
  Write-Output "backlog-unreadable $($_.Exception.Message)"
  exit 2
}
if (-not $backlog.items) { Write-Output 'backlog-empty'; exit 0 }

try {
  $board = Get-Content -Raw -Encoding UTF8 $BoardPath | ConvertFrom-Json
} catch {
  Write-Output "board-unreadable $($_.Exception.Message)"
  exit 2
}
$tasks = @(if ($board.tasks) { $board.tasks } else { $board })
$tasks = @($tasks | Where-Object { -not $_.archived })

# 끝난 태스크의 id 집합. 의존이 풀렸는지 보는 데 쓴다.
$doneIds = @{}
foreach ($t in $tasks) { if ($t.status -eq 'DONE') { $doneIds[[string]$t.id] = $true } }

# 세션이 지금 집을 수 있는 태스크인가. 의존이 안 풀렸으면 못 집는다 -
# 상태가 READY 여도 못 집는 것은 보드가 마른 것과 같다.
function Test-Claimable($task) {
  if (@('TODO', 'READY') -notcontains $task.status) { return $false }
  $title = [string]$task.title
  foreach ($m in $UnclaimableMarkers) { if ($title.StartsWith($m)) { return $false } }
  foreach ($dep in @($task.dependsOnTaskIds)) {
    if ($dep -and (-not $doneIds.ContainsKey([string]$dep))) { return $false }
  }
  return $true
}

$claimable = @($tasks | Where-Object { Test-Claimable $_ })
$inFlight = @($tasks | Where-Object { @('IN_PROGRESS', 'REVIEW') -contains $_.status })

if ($claimable.Count -gt 0 -or $inFlight.Count -gt 0) {
  Write-Output "backlog-idle (집을 수 있는 것 $($claimable.Count), 진행 중 $($inFlight.Count))"
  exit 0
}

# 여기부터는 보드가 말랐다. 백로그에서 우선순위가 가장 높은 것 하나를 꺼낸다.
$candidates = @($backlog.items | Where-Object { $_.status -eq 'READY' -and (-not $_.boardTaskId) })
if ($candidates.Count -eq 0) {
  Write-Output 'backlog-exhausted - 꺼낼 항목이 없다. 사람이 새 항목을 넣어야 한다'
  exit 0
}
$pick = @($candidates | Sort-Object -Property @{ Expression = { [int]$_.priority }; Descending = $true })[0]

# 이 보드는 team-loop 것이다. 다른 저장소를 겨냥한 항목은 올릴 곳이 다르므로 올리지 않는다 -
# 조용히 엉뚱한 보드에 올리느니 안 올리는 편이 낫다.
if ($pick.targetRepo -and $pick.targetRepo -ne 'team-loop') {
  Write-Output "backlog-wrong-board $($pick.id) 는 $($pick.targetRepo) 를 겨냥한다"
  exit 0
}

# 같은 제목이 이미 보드에 있으면 또 올리지 않는다. 두 번 올리면 같은 일을 두 번 시킨다.
$existing = @($tasks | Where-Object { [string]$_.title -eq [string]$pick.title })[0]
if ($existing) {
  Write-Output "backlog-already-on-board $($pick.id) -> $($existing.id)"
  if (-not $DryRun) {
    $pick.boardTaskId = [string]$existing.id
    $pick.status = 'IN_BOARD'
    [IO.File]::WriteAllText($BacklogPath, ($backlog | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
  }
  exit 0
}

if ($DryRun) { Write-Output "backlog-would-create $($pick.id) $($pick.title)"; exit 0 }

$body = @{
  title = [string]$pick.title
  description = [string]$pick.description
  priority = [int]$pick.priority
  allowedPaths = @($pick.allowedPaths)
  acceptanceCriteria = @($pick.acceptanceCriteria)
  verificationProfile = 'repository-basic'
  approvalPolicy = 'USER_CONFIRM'
}
$json = $body | ConvertTo-Json -Depth 20
$bytes = [Text.Encoding]::UTF8.GetBytes($json)

# 올리는 데 실패하면 백로그를 건드리지 않는다. 소비했다고 적어놓고 실제로는 안 올라갔으면
# 그 항목은 영원히 사라진다.
try {
  $resp = Invoke-RestMethod -Uri "$ServerUrl/api/tasks" -Method Post -Body $bytes -ContentType 'application/json; charset=utf-8'
} catch {
  Write-Output "backlog-create-failed $($pick.id) : $($_.Exception.Message)"
  exit 3
}
$newId = [string]$resp.task.id
if (-not $newId) { Write-Output "backlog-create-no-id $($pick.id)"; exit 3 }

$pick.boardTaskId = $newId
$pick.status = 'IN_BOARD'
[IO.File]::WriteAllText($BacklogPath, ($backlog | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))

Write-Output "backlog-created $newId $($pick.id) $($pick.title)"
exit 0
