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
  # 서버 주소와 쿠키는 team-loop CLI 가 로그인 때 저장해 둔 것을 그대로 쓴다. 두 벌로 두면
  # 한쪽만 바뀌어 조용히 인증이 깨진다. 비워 두면 저장된 server 를 쓴다.
  [string]$SessionPath = (Join-Path $env:USERPROFILE '.team-loop-lite\session.json'),
  [string]$ServerUrl = '',
  # 조율자가 판단할 일은 세션이 집을 수 없다. 그래서 보드가 말랐는지 셀 때도 빼야 한다 -
  # 안 그러면 아무도 못 집는 태스크 하나 때문에 백로그가 영원히 안 열린다.
  [string[]]$UnclaimableMarkers = @('[조율자 판단]', '[사람 게이트]'),
  # 남은 항목이 이 수 이하로 떨어지면 사람을 부른다. 루프는 일을 꺼내 쓸 수는 있어도
  # 무엇이 할 일인지는 만들어내지 못한다 - 그건 사람이 넣어야 한다. 다 떨어진 뒤에 조용히
  # 서면 예전과 같은 정지다. 마르기 전에 말해야 한다.
  [int]$LowWater = 2,
  # 같은 알림을 몇 시간 안에 다시 보내지 않는다. 5분마다 울리면 아무도 안 본다.
  [int]$NotifyEveryHours = 12,
  # 루프가 쓰는 상태는 저장소 밖에 둔다. 선언(사람이 쓰는 것)과 상태(루프가 쓰는 것)를
  # 한 파일에 두면 루프가 자기 저장소를 더럽혀 자기 다음 주기를 막는다 -
  # 2026-07-29 실측: BACKLOG.json 미커밋 하나로 main-tree-dirty 가 3주기 연속 찍혔다.
  [string]$StatePath = 'C:\NHN Project\_ops\backlog-state.json',
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# 한도는 scripts/loop-limits.json 에서 읽는다. 사람이 손으로 고치는 파일이다.
# 명령줄로 직접 준 값이 있으면 그것이 우선한다 - 일회성 실험이 파일을 안 건드리게 한다.
# 파일이 없거나 못 읽으면 매개변수 기본값을 그대로 쓴다. 한도를 모른다고 멈추지는 않는다.
function Read-LoopLimit([string]$name, [int]$fallback, $bound) {
  if ($bound.ContainsKey($name)) { return $fallback }
  $limitsPath = Join-Path (Split-Path -Parent $PSCommandPath) 'loop-limits.json'
  if (-not (Test-Path $limitsPath)) { return $fallback }
  try {
    $doc = Get-Content -Raw -Encoding UTF8 $limitsPath | ConvertFrom-Json
    $key = $name.Substring(0,1).ToLower() + $name.Substring(1)
    $entry = $doc.limits.$key
    if ($null -eq $entry) { return $fallback }
    $v = 0
    if ([int]::TryParse([string]$entry.value, [ref]$v)) { return $v }
  } catch { }
  return $fallback
}

$LowWater = Read-LoopLimit 'BacklogLowWater' $LowWater $PSBoundParameters
$NotifyEveryHours = Read-LoopLimit 'BacklogNotifyEveryHours' $NotifyEveryHours $PSBoundParameters


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

# 상태 파일. 없으면 전부 READY 로 본다 - 상태가 없다는 건 아직 아무것도 안 꺼냈다는 뜻이다.
$state = $null
if (Test-Path $StatePath) {
  try { $state = Get-Content -Raw -Encoding UTF8 $StatePath | ConvertFrom-Json } catch { }
}
if (-not $state) { $state = [pscustomobject]@{ items = (New-Object psobject) } }
if (-not $state.items) { $state | Add-Member -NotePropertyName items -NotePropertyValue (New-Object psobject) -Force }

function Get-ItemState([string]$id) {
  $entry = $state.items.$id
  if (-not $entry) { return [pscustomobject]@{ status = 'READY'; boardTaskId = $null } }
  return $entry
}
function Set-ItemState([string]$id, [string]$status, [string]$boardTaskId) {
  $state.items | Add-Member -NotePropertyName $id -NotePropertyValue ([pscustomobject]@{
    status = $status; boardTaskId = $boardTaskId
    at = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
  }) -Force
}
function Save-BacklogState {
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $StatePath) | Out-Null
  [IO.File]::WriteAllText($StatePath, ($state | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
}

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
# 사람에게 알린다. 실패해도 던지지 않는다 - 알림이 안 갔다고 보충을 잃으면 안 된다.
# 같은 알림을 반복하지 않도록 마지막 시각을 백로그 파일에 남긴다.
function Send-BacklogAlert([string]$title, [string]$body, [string]$priority) {
  $lastRaw = [string]$state.lastAlertAt
  if ($lastRaw) {
    $last = [datetime]::MinValue
    if ([datetime]::TryParse($lastRaw, [ref]$last)) {
      if (((Get-Date).ToUniversalTime() - $last.ToUniversalTime()).TotalHours -lt $NotifyEveryHours) { return $false }
    }
  }
  $notify = Join-Path (Split-Path -Parent $PSCommandPath) 'notify.ps1'
  if (Test-Path $notify) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $notify -Title $title -Body $body -Tags 'clipboard' -Priority $priority 2>&1 | Out-Null
  }
  $state | Add-Member -NotePropertyName 'lastAlertAt' -NotePropertyValue ((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')) -Force
  return $true
}

$candidates = @($backlog.items | Where-Object { (Get-ItemState ([string]$_.id)).status -eq 'READY' })
if ($candidates.Count -eq 0) {
  Write-Output 'backlog-exhausted - 꺼낼 항목이 없다. 사람이 새 항목을 넣어야 한다'
  if (-not $DryRun) {
    $sent = Send-BacklogAlert '백로그가 비었다' ('보드가 말랐는데 꺼낼 일감이 없다.' + [Environment]::NewLine + 'docs/plan/BACKLOG.json 에 항목을 넣어야 루프가 다시 돈다.') '4'
    if ($sent) {
      Write-Output 'backlog-alert-sent exhausted'
      Save-BacklogState
    }
  }
  exit 0
}
# 동점이면 id 오름차순으로 가른다. 정렬이 흔들리면 같은 상태에서 다른 것이 뽑혀
# 재현이 안 된다 - BACKLOG-POLICY.md 1절.
$pick = @($candidates |
  Sort-Object -Property @{ Expression = { [int]$_.priority }; Descending = $true },
                        @{ Expression = { [string]$_.id }; Descending = $false })[0]

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
    Set-ItemState ([string]$pick.id) 'IN_BOARD' ([string]$existing.id)
    Save-BacklogState
  }
  exit 0
}

if ($DryRun) { Write-Output "backlog-would-create $($pick.id) $($pick.title)"; exit 0 }

# POST 는 인증이 필요하다. 2026-07-28 실측: 헤더 없이 보내 403 이 났고
# "X-Team-Loop-Client header is required for POST requests" 였다(server.js requireTrustedClientHeader).
# 쿠키가 없으면 올리지 않는다 - 인증 없이 올린 척하면 백로그만 소비되고 보드는 그대로다.
if (-not (Test-Path $SessionPath)) { Write-Output "backlog-no-session $SessionPath"; exit 3 }
try {
  $session = Get-Content -Raw -Encoding UTF8 $SessionPath | ConvertFrom-Json
} catch {
  Write-Output "backlog-session-unreadable $($_.Exception.Message)"
  exit 3
}
$cookie = [string]$session.cookie
if (-not $cookie) { Write-Output 'backlog-no-cookie - team-loop login 이 필요하다'; exit 3 }
# 저장된 값은 "이름=값" 형태다. 쿠키 그릇에는 값만 넣는다.
$cookieValue = [Uri]::UnescapeDataString(($cookie -split '=', 2)[1])
if (-not $cookieValue) { Write-Output 'backlog-cookie-unparsable'; exit 3 }
if (-not $ServerUrl) { $ServerUrl = [string]$session.server }
if (-not $ServerUrl) { Write-Output 'backlog-no-server'; exit 3 }

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
  # 쿠키를 -Headers 에 넣으면 안 된다. PowerShell 5.1 의 Invoke-RestMethod 는 Headers 의
  # Cookie 를 조용히 버린다 - 2026-07-28 실측: 토큰은 8/1 까지 유효한데 401 이 났고,
  # WebRequestSession 으로 같은 토큰을 넣으니 바로 통했다. 버리면서 아무 말도 안 한다.
  $uri = [Uri]"$ServerUrl/api/tasks"
  $webSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
  $webSession.Cookies.Add((New-Object System.Net.Cookie('team_loop_session', $cookieValue, '/', $uri.Host)))
  $headers = @{ 'X-Team-Loop-Client' = 'cli'; 'Accept' = 'application/json' }
  $resp = Invoke-RestMethod -Uri $uri -Method Post -Body $bytes -WebSession $webSession -Headers $headers -ContentType 'application/json; charset=utf-8'
} catch {
  Write-Output "backlog-create-failed $($pick.id) : $($_.Exception.Message)"
  exit 3
}
$newId = [string]$resp.task.id
if (-not $newId) { Write-Output "backlog-create-no-id $($pick.id)"; exit 3 }

Set-ItemState ([string]$pick.id) 'IN_BOARD' $newId
Save-BacklogState

$remaining = @($backlog.items | Where-Object { (Get-ItemState ([string]$_.id)).status -eq 'READY' }).Count
if ($remaining -le $LowWater) {
  $sent = Send-BacklogAlert '백로그가 얼마 안 남았다' ("남은 일감 $remaining 건." + [Environment]::NewLine + '다 떨어지면 루프가 선다. docs/plan/BACKLOG.json 에 넣어라.') '3'
  if ($sent) { Write-Output "backlog-alert-sent low-water $remaining" }
}
Save-BacklogState

Write-Output "backlog-created $newId $($pick.id) $($pick.title) (남은 $remaining 건)"
exit 0
