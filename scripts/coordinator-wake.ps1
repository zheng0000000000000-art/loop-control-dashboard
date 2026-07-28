#Requires -Version 5.1
# 원격에서 조율자를 깨운다 — 사람이 집 컴퓨터 앞에 없어도.
#
# 왜 필요한가: 조율자는 채팅 메시지가 와야 턴이 시작된다. 대화 채널에 글을 남겨도 깨어나지 않는다.
# 2026-07-27 실측 — 사용자가 05:14:47에 글을 남겼는데 조율자 턴은 05:14:11에 끝나 있었고
# 334분 동안 아무도 읽지 않았다. 사용자는 폰에서 대시보드에는 붙을 수 있지만 조율자를 깨울 수 없었다.
#
# 그래서 **안 읽은 글 자체를 방아쇠로 삼는다.** 이 스크립트가 헤드리스 세션을 띄우고,
# 그 세션이 대화를 읽고 이어서 일한다.
#
# 중요한 한계 — 새로 뜨는 것은 **다른 세션**이다. 이 대화의 기억이 없다.
# 그래서 인수인계 문서로만 이어붙는다. 이 저장소가 원래 그렇게 설계돼 있다.
param(
  [string]$DiscussionPath = 'C:\NHN Project\team-loop-lite-ai-learning\data\discussions.json',
  [string]$HeartbeatPath  = 'C:\NHN Project\_ops\coordinator-heartbeat.json',
  # 이 파일이 있으면 깨우지 않는다. 조율자가 명시적으로 멈출 때만 만든다.
  [string]$HoldPath       = 'C:\NHN Project\_ops\coordinator-hold.flag',
  # 막혀서 조율자에게 올라온 것들. 안 다뤄진 항목이 있으면 그것이 최우선 방아쇠다.
  [string]$InboxPath      = 'C:\NHN Project\_ops\coordinator-inbox.md',
  [string]$WorkingDir     = 'C:\Users\1\Documents\Local-First Workflow Dashboard',
  [string]$LogDir         = 'C:\NHN Project\_ops\wake-logs',
  [string]$ReaderId       = 'usr_claude_coordinator',
  [string]$TeamLoopRoot   = 'C:\NHN Project\team-loop-lite-ai-learning',
  [string]$BoardPath      = 'C:\NHN Project\team-loop-lite-ai-learning\data\tasks.json',
  [string]$QueuePath      = 'C:\Users\1\Documents\Local-First Workflow Dashboard\docs\handoff\WORK-QUEUE.md',
  # 백로그 경로를 밖에서 줄 수 있어야 이 배선을 픽스처로 잴 수 있다.
  # 비우면 보충 스크립트의 기본값을 쓴다.
  [string]$BacklogPath    = '',
  [string]$BaseBranch     = 'wp/state-integrity',
  [int]$ChainDepth        = 0,
  # 상한은 폭주 방지용 백스톱일 뿐이다. 진짜 정지는 queue-drained(할 일 없음)와
  # no-progress(아무것도 안 줄었음)다. 5로 두면 일이 남아 있는데도 끊겨서 다음 5분 주기를
  # 기다린다 - 사람이 기다릴 이유가 없는 대기다.
  [int]$MaxChain          = 20,
  [string[]]$ActiveStatuses = @('TODO','READY','IN_PROGRESS','REVIEW','BLOCKED'),
  # 이 표식으로 시작하는 보드 태스크는 사람만 할 수 있다. 실행자가 안 집는다.
  # 조율자가 판단할 일. 세션이 임의로 집지 않는다 - 비용이 걸린 판단이기 때문이다.
  # 2026-07-28 부터 사람 결재가 아니라 조율자 재량이다(ADR-021). 막지 않고 인계함으로 올린다.
  [string]$HumanGateMarker = '[조율자 판단]',
  # 예전 표식. 이미 보드에 올라간 것들이 있어 같이 인식한다.
  [string]$LegacyGateMarker = '[사람 게이트]',
  # 25분이면 24분 공백을 "살아 있다"로 읽는다(2026-07-27 실측: 11:15 커밋 후 11:36 판정이 alive).
  # 깨우기는 알림보다 싸므로 짧게 잡는다. 조율자가 도는 중이면 어차피 하트비트가 갱신된다.
  [int]$StaleMinutes      = 10,
  # 같은 방아쇠로 다시 시도하기까지 기다리는 시간. 짧으면 헛돌고 길면 막힌 채로 오래 있는다.
  [int]$RetryAfterMinutes = 15,
  # 이 횟수를 넘도록 진전이 없으면 조율자에게 넘긴다.
  [int]$MaxAttempts       = 3,
  [int]$TimeoutMinutes    = 30,
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# 판정을 전부 남긴다. 깨운 것만 기록하면 "왜 안 깨웠나"를 추론해야 하고, 추론은 증거가 아니다.
$DecisionLog = Join-Path $LogDir 'decisions.log'
function Write-Line([string]$text) {
  Write-Output $text
  try {
    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
    $stampedLine = '{0}  depth={1}  {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $ChainDepth, $text
    Add-Content -Path $DecisionLog -Value $stampedLine -Encoding UTF8
  } catch { }
}

$loopLogScriptEarly = Join-Path (Split-Path -Parent $PSCommandPath) 'loop-log.ps1'

# 일이 남았는데 멈출 때는 반드시 남긴다. 조용히 끝나면 사람이 알아챌 때까지 아무 일도 안 일어난다.
# 2026-07-28: 사람이 "작업 막힌 것 확인해줄래"라고 물어야 풀리는 상태가 반복됐다.
function Report-Stop([string]$reason, [string]$detail) {
  Write-Line "$reason - $detail"
  $inbox = Join-Path (Split-Path -Parent $HeartbeatPath) 'coordinator-inbox.md'
  $stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
  # [!] 로 쓴다. [ ] 로 쓰면 이 줄이 다음 깨우기의 이유가 되어 루프가 자기 배설물을 먹는다 -
  # 2026-07-28 실측: 네 주기 연속 "woke trigger=inbox=2 -> no-progress before=0 after=1" 이
  # 반복됐고 인계함만 늘었다. 멈춤 보고는 출력이지 입력이 아니다.
  # 사람에게는 그대로 보이고 폰으로도 가지만, 루프를 깨우지는 않는다.
  # 같은 멈춤을 60분 안에 또 적지 않는다. 5분마다 같은 줄이 쌓이면 진짜 항목이 묻힌다.
  $duplicate = $false
  if (Test-Path $inbox) {
    # -like 를 쓰면 안 된다. 대괄호가 문자 클래스로 해석돼 "- [!]" 가 리터럴로 안 잡힌다
    # (2026-07-28 시험으로 잡음: recent=0 이라 중복 억제가 영영 안 걸렸다).
    $recent = @(Get-Content -Encoding UTF8 $inbox |
      Where-Object { $_.StartsWith('- [!]') -and $_.EndsWith("$reason : $detail") })
    if ($recent.Count -gt 0) {
      $lastStamp = ($recent[-1] -split '\s+')[2]
      $parsed = [datetime]::MinValue
      if ([datetime]::TryParse($lastStamp, [ref]$parsed)) {
        if (((Get-Date).ToUniversalTime() - $parsed.ToUniversalTime()).TotalMinutes -lt 60) { $duplicate = $true }
      }
    }
  }
  if ($duplicate) { Write-Line "stop-duplicate-suppressed $reason"; return }
  try { Add-Content -Path $inbox -Encoding UTF8 -Value "- [!] $stamp $reason : $detail" } catch { }
  if (Test-Path $loopLogScriptEarly) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $loopLogScriptEarly `
      -Text "멈춤 · $reason · $detail" 2>&1 | Out-Null
  }
  # 폰으로도 보낸다. 인계함과 대화 채널은 사람이 그 화면을 열어야 보인다 -
  # 2026-07-28 실측: 회로 차단기가 걸려 멈춰 있었는데 사람이 물어야 알았다.
  # 알림이 사람을 찾아가야지 사람이 알림을 찾으러 가면 안 된다.
  $notifyScript = Join-Path (Split-Path -Parent $PSCommandPath) 'notify.ps1'
  if (Test-Path $notifyScript) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $notifyScript `
      -Title "루프 멈춤" -Body "$reason`n$detail" -Tags "octagonal_sign" 2>&1 | Out-Null
  }
}

# 명시적 정지 표식. 조율자가 "지금은 멈춰라"고 말할 때만 만든다.
# 있다는 사실 자체가 킬 스위치다 - 내가 살아 있다는 사실이 아니라.
if (Test-Path $HoldPath) {
  $reason = try { (Get-Content -Raw -Encoding UTF8 $HoldPath).Trim() } catch { '' }
  Write-Line "coordinator-hold - $(if ($reason) { $reason } else { '사유 없음' })"
  exit 0
}

if (-not (Test-Path $DiscussionPath)) { Write-Line 'discussion-missing'; exit 2 }

# readBy 항목은 서버 API(POST /api/discussions/read)가 쓰는 {userId,at} 객체가 정본이지만,
# 과거 세션들이 파일을 손으로 고치며 문자열만 넣은 항목이 섞여 있다(2026-07-28 실측:
# msg_83e1246d..., msg_9698b225... 둘 다 readBy=["usr_claude_coordinator"] 뿐인데
# 객체 전용 매칭($_.userId -eq $ReaderId)이 이를 못 잡아 이미 읽은 메시지가 영구히
# unread=2 로 잡혀 매 깨우기마다 헛방아쇠가 됐다). 두 모양 다 인정한다.
$discussion = Get-Content -Raw -Encoding UTF8 $DiscussionPath | ConvertFrom-Json
$unread = @($discussion.messages | Where-Object {
  $_.authorUserId -ne $ReaderId -and -not ($_.readBy | Where-Object { $_ -eq $ReaderId -or $_.userId -eq $ReaderId })
})
# 작업이 남아 있으면 메시지가 없어도 깨운다. 메시지에만 반응하면 그건 루프가 아니라 응답이다.
# 2026-07-27: 334분 공백 동안 할 일이 남아 있었는데 아무도 이어가지 않았다.
# 지시큐는 작업보드다 — 사람이 태스크를 올려두면 조율자가 그것을 집어간다.
# 도는 서버가 옛 코드면 알린다. 코드를 고치고 프로세스를 그대로 두는 실수를 하루에 세 번 했고,
# 그중 하나는 approve_task 가 보낸 reviewSessionPid 를 옛 서버가 버려서 기록이 유실됐다.
# 그때 나는 "세션이 도구를 안 썼다"고 잘못 읽을 뻔했다. 사람이 기억할 일이 아니다.
# 깨우지는 않는다 - 재시작은 사람이 볼 수 있을 때 하는 편이 낫다. 대신 반드시 남긴다.
$staleServerScript = Join-Path (Split-Path -Parent $PSCommandPath) 'check-stale-server.ps1'
if (Test-Path $staleServerScript) {
  $staleOut = & powershell -NoProfile -ExecutionPolicy Bypass -File $staleServerScript -TeamLoopRoot $TeamLoopRoot 2>&1
  if ($LASTEXITCODE -ne 0) {
    foreach ($line in @($staleOut)) { Write-Line "$line" }
    if (Test-Path (Join-Path (Split-Path -Parent $PSCommandPath) 'loop-log.ps1')) {
      & powershell -NoProfile -ExecutionPolicy Bypass `
        -File (Join-Path (Split-Path -Parent $PSCommandPath) 'loop-log.ps1') `
        -Text "경고 · team-loop 서버가 옛 코드로 돌고 있다. 재시작해야 최근 변경이 반영된다" 2>&1 | Out-Null
    }
  }
}

# 보드의 좀비를 먼저 회수한다. 큐 sweep 의 거울이다 - 없으면 발사 실패마다 하나씩 쌓이고
# boardReady 는 IN_PROGRESS 를 안 세므로 아무에게도 안 보인다.
# 2026-07-27 실측: 실패한 발사가 태스크를 IN_PROGRESS 로 두고 10.2시간 방치돼 있었다.
$boardSweepScript = Join-Path (Split-Path -Parent $PSCommandPath) 'board-sweep.ps1'
if ((Test-Path $boardSweepScript) -and (Test-Path $BoardPath)) {
  $boardSwept = & powershell -NoProfile -ExecutionPolicy Bypass -File $boardSweepScript -BoardPath $BoardPath 2>&1
  foreach ($line in @($boardSwept)) { if ("$line" -match 'revived') { Write-Line "$line" } }
}

$pendingWork = 0
$boardReady  = @()   # 지금 집을 수 있는 것
$boardReview = @()   # 판정만 남은 것
if (Test-Path $BoardPath) {
  try {
    $board = Get-Content -Raw -Encoding UTF8 $BoardPath | ConvertFrom-Json
    $tasks = @(if ($board.tasks) { $board.tasks } else { $board })
    $tasks = @($tasks | Where-Object { -not $_.archived })
    $pendingWork = @($tasks | Where-Object { $_.status -and $ActiveStatuses -contains $_.status }).Count

    $doneIds = @($tasks | Where-Object { $_.status -eq 'DONE' } | ForEach-Object { $_.id })
    $boardReview = @($tasks | Where-Object { $_.status -eq 'REVIEW' } | Sort-Object { [int]$_.priority })
    # 막힌 것과 선행이 안 끝난 것은 집지 않는다. 집어봐야 같은 자리에서 막힌다.
    # 제목이 사람 게이트 표식으로 시작하는 것도 안 집는다. 발사와 approve/reject 와 기준 파일
    # 변경은 사람 결재다(CLAUDE.md). 보드에 올려 폰에서 보이게는 하되 실행자가 집으면
    # 못 하고 나와서 깨우기 한 번이 헛돈다. 보이는 것과 집히는 것을 가른다.
    $boardReady = @($tasks | Where-Object { $_.status -and (@('TODO','READY') -contains $_.status) -and (-not $_.blocked) -and (-not (($_.title -as [string]).StartsWith($HumanGateMarker))) -and (-not (($_.title -as [string]).StartsWith($LegacyGateMarker))) -and (@(@($_.dependsOnTaskIds) | Where-Object { $_ -and ($doneIds -notcontains $_) }).Count -eq 0) } | Sort-Object { [int]$_.priority })
  } catch { $pendingWork = 0 }
}

# 보드 태스크를 깨어난 세션이 읽을 수 있는 지시로 편다.
# 이것이 없으면 보드가 깨우기만 하고, 세션은 WORK-QUEUE 에서 엉뚱한 항목을 집는다.
function Format-BoardTask($task) {
  $lines = @("[작업보드 태스크] $($task.id)", "제목: $($task.title)")
  if ($task.priority)    { $lines += "우선순위: $($task.priority)" }
  if ($task.description) { $lines += "", "설명:", $task.description }
  if ($task.acceptanceCriteria) {
    $lines += "", "완료 조건(이것을 만족해야 끝난 것이다):"
    foreach ($c in @($task.acceptanceCriteria)) { $lines += "  - $c" }
  }
  if ($task.allowedPaths) {
    $lines += "", "허용 경로(이 밖은 건드리지 마라):"
    foreach ($a in @($task.allowedPaths)) { $lines += "  - $a" }
  }
  if ($task.skillIds)  { $lines += "", "참조할 스킬: $(@($task.skillIds) -join ', ')" }
  return ($lines -join "`n")
}

# 작업 큐의 미완 항목도 방아쇠다. 사람이 다음 작업까지 적어두면 끝날 때까지 이어진다.
# 주인이 죽은 "진행 중"을 먼저 대기로 되돌린다. 안 그러면 크래시 한 번에 항목이 영구히 잠긴다.
# [>] 는 [ ] 가 아니므로 아래 세기에서 자동으로 빠진다 — 살아 있는 세션이 잡은 것은 안 집힌다.
$queueScript = Join-Path (Split-Path -Parent $PSCommandPath) 'queue-state.ps1'
if (Test-Path $queueScript) {
  $sweep = & powershell -NoProfile -ExecutionPolicy Bypass -File $queueScript -Action Status -QueuePath $QueuePath 2>&1
  if ("$sweep" -match 'sweep:') { Write-Line "queue-sweep - $sweep" }
}

$queueOpen = 0
$queueReview = 0
if (Test-Path $QueuePath) {
  $queueOpen = @(Select-String -Path $QueuePath -Pattern '^\s*-\s\[\s\]\s' -AllMatches).Count
  # 검토 대기는 실행이 끝나고 판정만 남은 것이다. 조율자가 깨어나야 하는 진짜 이유가 이것이다 —
  # 실행은 CLI 세션과 로컬 모델이 돌리고, 조율자는 판정에서만 들어온다(Court/Clerk 분리).
  $queueReview = @(Select-String -Path $QueuePath -Pattern '^\s*-\s\[~\]\s' -AllMatches).Count
}

# 아래 nothing-to-do 검사가 $inboxPending 을 쓴다. 그래서 인계함 등록과 세기를 그 앞에서
# 끝내야 한다 - 2026-07-28 실측: 대입 전에 쓰고 있었고 PowerShell 에서 $null -eq 0 은 False 라
# 그 분기가 한 번도 안 걸렸다. 할 일이 없어도 매 주기 세션이 떴다.
# 조율자 판단이 필요한 보드 태스크를 인계함으로 올린다. 건너뛰기만 하면 아무도 결정하지 않고
# 보드에 영원히 남는다 - 2026-07-27 territory-check 가 그렇게 875분 방치됐다.
# 같은 태스크로 두 번 올리지 않는다. 인계함에 이미 그 id 가 있으면 넘어간다.
$decisionTasks = @($tasks | Where-Object { $_.status -and (@('TODO','READY') -contains $_.status) -and ((($_.title -as [string]).StartsWith($HumanGateMarker)) -or (($_.title -as [string]).StartsWith($LegacyGateMarker))) })
foreach ($decisionTask in $decisionTasks) {
  $already = (Test-Path $InboxPath) -and (Select-String -Path $InboxPath -Pattern ([regex]::Escape($decisionTask.id)) -Quiet)
  if ($already) { continue }
  $stampNow = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $InboxPath) | Out-Null
  Add-Content -Path $InboxPath -Encoding UTF8 -Value "- [ ] $stampNow decision-needed : $($decisionTask.id) $($decisionTask.title)"
  Write-Line "decision-queued $($decisionTask.id)"
  $notifyScript = Join-Path (Split-Path -Parent $PSCommandPath) 'notify.ps1'
  if (Test-Path $notifyScript) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $notifyScript `
      -Title "조율자 판단 필요" -Body "$($decisionTask.id)`n$($decisionTask.title)" `
      -Tags "question" -Priority "3" 2>&1 | Out-Null
  }
}

$inboxPending = 0
if (Test-Path $InboxPath) {
  $inboxPending = @(Select-String -Path $InboxPath -Pattern '^\s*-\s\[\s\]\s' -AllMatches).Count
}

# pendingWork 로 깨우지 않는다. 거기엔 사람 게이트 태스크가 섞여 있어서
# 아무도 집을 수 없는 일로 깨우고, 그다음 no-progress 로 멈춘다.
# 2026-07-27 실측: board-stuck=2 로 깨어나 no-progress before=0 after=1 로 끝났다.
if ($inboxPending -eq 0 -and $unread.Count -eq 0 -and $boardReady.Count -eq 0 -and $boardReview.Count -eq 0 -and $queueOpen -eq 0 -and $queueReview -eq 0) {
  # 보드가 말랐다. 여기서 그냥 끝내면 일감이 조율자로부터만 나온다 - 조율자가 대화에 응답할
  # 때만 태스크가 생기고 그 사이 루프는 멀쩡한데도 선다. 실행과 판정은 이미 조율자 손을
  # 떠났는데 발생만 안 떠나 있었다. 백로그에서 다음 항목을 꺼내 보드에 올린다.
  # 백로그에 선언된 것만 올라간다 - 세션이 없는 일을 발명하면 루프가 자기한테 무한히 일을 만든다.
  $refillScript = Join-Path (Split-Path -Parent $PSCommandPath) 'backlog-refill.ps1'
  $refilled = $false
  if (Test-Path $refillScript) {
    $refillArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $refillScript)
    if ($BacklogPath) { $refillArgs += @('-BacklogPath', $BacklogPath) }
    $refillOut = & powershell @refillArgs 2>&1
    $refillCode = $LASTEXITCODE
    foreach ($line in @($refillOut)) { Write-Line "backlog - $line" }
    # 판정은 종료 코드와 만들어진 id 로 한다. 출력 문구를 세지 않는다.
    if ($refillCode -eq 0) {
      foreach ($line in @($refillOut)) { if ("$line".StartsWith('backlog-created ')) { $refilled = $true } }
    }
  }
  if (-not $refilled) { Write-Line "nothing-to-do (사람 게이트 대기 $pendingWork 건)"; exit 0 }
  # 5분을 기다리지 않는다. 방금 올린 것을 이번 고리에서 바로 집는다.
  if ($ChainDepth -ge $MaxChain) { Write-Line "backlog-refilled - 연쇄 한도라 다음 주기가 집는다"; exit 0 }
  Write-Line "backlog-refilled - 이어서 집는다 depth=$($ChainDepth + 1)"
  & $PSCommandPath -ChainDepth ($ChainDepth + 1) -MaxChain $MaxChain -TimeoutMinutes $TimeoutMinutes
  exit $LASTEXITCODE
}
# 검토 대기가 있으면 그것이 최우선이다. 실행보다 판정이 먼저 밀린다 —
# 판정이 안 나면 그다음 실행이 무엇 위에 쌓이는지 아무도 모른다.
# 조율자 인계함에 안 다뤄진 항목이 있으면 그것이 최우선이다. 막혀서 올라온 것이므로
# 그대로 두면 그 줄기 전체가 선다. 2026-07-28: 인계함이 방아쇠가 아니어서, 막힘이 쌓여도
# 사람이 말을 걸어야 조율자가 읽었다. 스스로 깨우는 고리가 여기서 끊겨 있었다.

$trigger = if ($inboxPending -gt 0) { "inbox=$inboxPending" }
  elseif ($boardReview.Count -gt 0) { "board-review=$($boardReview.Count)" }
  elseif ($queueReview -gt 0) { "review=$queueReview" }
  elseif ($unread.Count -gt 0) { "unread=$($unread.Count)" }
  elseif ($boardReady.Count -gt 0) { "board=$($boardReady.Count)" }
  else { "queue=$queueOpen" }

# 조율자가 지금 돌고 있으면 깨우지 않는다. 두 세션이 같은 저장소를 동시에 만지면
# 커밋이 엉킨다. 판정은 하트비트 나이 하나다 — 프로세스 존재로 판정하지 않는다.
if (Test-Path $HeartbeatPath) {
  $age = $null
  try {
    $beat = Get-Content -Raw -Encoding UTF8 $HeartbeatPath | ConvertFrom-Json
    $age = [int]([datetimeoffset]::UtcNow - [datetimeoffset]::Parse($beat.at)).TotalMinutes
  } catch {
    # 못 읽으면 살아 있다고 말하지 않는다. 모르는 것은 통과가 아니다.
    Write-Line 'heartbeat-unreadable - 깨운다'
    $age = $null
  }
  # 파일이 주장하는 시각(at)이 실제로 쓰인 시각(mtime)보다 뒤면 그건 지어낸 값이다.
  # mtime 은 파일 시스템이 찍으므로 위조할 수 없다. 2026-07-28 확인: 하트비트를 쓰는 코드가
  # 저장소에 없어서 모델이 시각을 손으로 계산해 타이핑해 왔고, 그래서 값이 흔들렸다.
  # skew 크기와 무관하게 잡힌다 - 2분이든 9시간이든 지어낸 것은 지어낸 것이다.
  if ($null -ne $age) {
    try {
      $writtenAt = [datetimeoffset](Get-Item $HeartbeatPath).LastWriteTimeUtc
      $claimedAt = [datetimeoffset]::Parse($beat.at)
      $fabricated = [int](($claimedAt - $writtenAt).TotalSeconds)
      if ($fabricated -gt 30) {
        Write-Line "heartbeat-fabricated at 이 mtime 보다 ${fabricated}초 앞선다 - 손으로 쓴 값이다. 깨운다"
        $age = $null
      }
    } catch { }
  }

  # 미래로 찍힌 하트비트는 살아 있다의 증거가 아니다. 고장의 증거다.
  # 2026-07-27 실측: 한 세션이 오프셋을 두 번 적용해 하트비트를 미래로 찍었고,
  # age 가 음수가 되면서 age -lt 10 을 통과해 6시간 47분 동안 아무도 안 깨웠다.
  # 이 검사가 fail-open 이었다. 음수는 전부 통과였다.
  if (($null -ne $age) -and ($age -lt 0)) {
    Write-Line "heartbeat-in-future skew=$([Math]::Abs($age))m - 고장으로 보고 깨운다"
    $age = $null
  }
  # 하트비트로 깨우기를 막지 않는다. 조율자가 살아 있다는 것과 루프가 멈춰야 한다는 것은 다르다.
  # 이 검사는 원래 "두 세션이 같은 트리를 망가뜨리는 것"을 막으려던 대리 판단이었는데,
  # 그 진짜 위험은 이제 다른 것들이 막는다 - 세션별 워크트리, 커밋 구간 잠금,
  # 진행 중 표기, 착지 전 clean 요구.
  # 2026-07-28 실측: 스케줄러가 133번 돌면서 전부 coordinator-alive 로 물러났다.
  # 조율자가 일하고 있다는 이유로 루프가 멈춰 있었다. 지시하는 쪽이 존재하는 것이
  # 실행을 막는 스위치면 안 된다. 킬 스위치는 파일 하나여야 한다.
  if ($null -ne $age) { Write-Line "coordinator-heartbeat age=${age}m (막지 않는다)" }
}

# 본 저장소가 dirty 면 시작하지 않는다. 세 가지가 한꺼번에 어긋난다 -
# 깨우기는 커밋 안 된 큐를 읽는데 워크트리는 커밋된 HEAD 에서 뜨므로 세션이 그 항목을 못 본다.
# 게다가 착지가 어차피 clean 을 요구해서 거부된다. 2026-07-27 실측: 세션 하나를 헛돌렸다.
# 생성물만 더러운 것은 막지 않는다. 착지가 생성물 충돌을 이미 자동 해소한다 -
# 2026-07-28 실측: 22:16 착지가 "생성물 충돌 3건 자동 해소"로 통과했는데 22:31 주기는
# 같은 종류의 더러움으로 시작조차 못 했다. 검사가 자기가 지키려는 것보다 엄했다.
# 목록은 scripts/generated-paths.txt 하나를 session-worktree.ps1 과 같이 읽는다.
$generatedList = Join-Path (Split-Path -Parent $PSCommandPath) 'generated-paths.txt'
$generatedPrefixes = @()
if (Test-Path $generatedList) {
  $generatedPrefixes = @(Get-Content -Encoding UTF8 $generatedList |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -and (-not $_.StartsWith('#')) })
}
$mainDirty = & git -C $WorkingDir status --porcelain 2>&1
# 목록을 못 읽으면 예전처럼 전부 막는다. 모르는 것은 통과가 아니다.
$sourceDirty = @()
foreach ($line in @($mainDirty)) {
  $entry = "$line"
  if (-not $entry.Trim()) { continue }
  $path = if ($entry.Length -gt 3) { $entry.Substring(3).Trim() } else { $entry.Trim() }
  if ($path.Contains(' -> ')) { $path = ($path -split ' -> ')[-1] }
  $path = $path.Trim('"').Replace([char]92, '/')
  $isGenerated = $false
  foreach ($g in $generatedPrefixes) { if ($path.StartsWith($g)) { $isGenerated = $true; break } }
  if (-not $isGenerated) { $sourceDirty += $path }
}
if ($sourceDirty.Count -gt 0) {
  Write-Line "main-tree-dirty - 소스 $($sourceDirty.Count)건이 커밋되지 않았다($($sourceDirty[0])). 착지가 거부되므로 시작하지 않는다"
  exit 0
}

# 다른 세션이 커밋 중이면 깨우지 않는다. 두 세션이 같은 트리를 만지면 한쪽의 스테이징이
# 다른 쪽 파일을 쓸어 담는다(2026-07-27 실측: c081dc4, 9bfda28).
$lockScript = Join-Path (Split-Path -Parent $PSCommandPath) 'session-lock.ps1'
if (Test-Path $lockScript) {
  $lockState = & powershell -NoProfile -ExecutionPolicy Bypass -File $lockScript -Action Status 2>&1
  if ("$lockState" -match 'lock-held-by-other') {
    Write-Line "session-locked - $lockState"
    exit 0
  }
}

# 같은 글로 두 번 깨우지 않는다. 실패한 세션이 무한히 재시도하면 토큰만 태운다.
# 큐가 방아쇠일 때는 남은 개수를 표식으로 쓴다. 하나 끝나면 개수가 줄어 다음 깨우기가 열린다.
$markerValue = if ($inboxPending -gt 0) { "inbox:$inboxPending" }
  elseif ($boardReview.Count -gt 0) { "board-review:$($boardReview[0].id)" }
  elseif ($queueReview -gt 0) { "review:$queueReview" }
  elseif ($unread.Count -gt 0) { $unread[0].id }
  elseif ($boardReady.Count -gt 0) { "board:$($boardReady[0].id)" }
  else { "queue:$queueOpen" }
$marker = "$HeartbeatPath.woke"
$attemptMarker = "$HeartbeatPath.attempts"

# 같은 값으로 두 번 깨우지 않는다. 그런데 그것만 있으면 재시도가 영구히 막힌다 -
# 2026-07-28 실측: 세션이 진전 없이 끝난 뒤 already-woke-for 로 계속 걸려 아무도 다시 안 집었고,
# 사람이 알려줘서야 풀렸다. 사람이 알려줘야 풀리면 그건 감시가 아니다.
#
# 그래서 식은 뒤에는 다시 시도하고, 정해진 횟수를 넘으면 조율자에게 넘긴다.
# 무한 재시도는 토큰만 태우고, 영구 차단은 루프를 죽인다. 둘 사이를 시간과 횟수로 가른다.
if ((Test-Path $marker) -and ((Get-Content -Raw $marker).Trim() -eq $markerValue)) {
  $markerAge = [int]([datetimeoffset]::UtcNow - [datetimeoffset](Get-Item $marker).LastWriteTimeUtc).TotalMinutes
  $attempts = 0
  if (Test-Path $attemptMarker) {
    $recorded = (Get-Content -Raw $attemptMarker).Trim() -split ' ', 2
    if ($recorded.Count -eq 2 -and $recorded[1] -eq $markerValue) { $attempts = [int]$recorded[0] }
  }
  if ($markerAge -lt $RetryAfterMinutes) {
    Write-Line "already-woke-for $markerValue (${markerAge}분 전, ${RetryAfterMinutes}분 뒤 재시도)"
    exit 0
  }
  if ($attempts -ge $MaxAttempts) {
    # 보드를 얼려두지 않는다. 세션이 못 하는 일이면 조율자가 가져간다 -
    # 막힌 항목 하나가 보드 전체를 굳히면 그게 제일 나쁘다.
    Write-Line "stalled $markerValue - ${attempts}회 시도했고 진전이 없다. 조율자에게 넘긴다"
    $escalation = Join-Path (Split-Path -Parent $HeartbeatPath) 'coordinator-inbox.md'
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    Add-Content -Path $escalation -Encoding UTF8 -Value "- [ ] $stamp $markerValue : ${attempts}회 시도 무진전. 조율자가 직접 볼 것."
    if (Test-Path $loopLogScriptEarly) {
      & powershell -NoProfile -ExecutionPolicy Bypass -File $loopLogScriptEarly `
        -Text "막힘 · $markerValue · ${attempts}회 시도 무진전 · 조율자 처리 대기" 2>&1 | Out-Null
    }
    # 표식을 지운다. 다음 방아쇠(다른 일)가 이것 때문에 막히면 안 된다.
    Remove-Item $marker -Force -ErrorAction SilentlyContinue
    Remove-Item $attemptMarker -Force -ErrorAction SilentlyContinue
    exit 5
  }
  $attempts = $attempts + 1
  Set-Content -Path $attemptMarker -Value "$attempts $markerValue" -Encoding UTF8
  Write-Line "retrying $markerValue (${attempts}/${MaxAttempts}회, 표식 ${markerAge}분 됨)"
} elseif (Test-Path $attemptMarker) {
  # 방아쇠가 바뀌었으면 시도 횟수를 잊는다. 다른 일에서 막힌 것을 이월하면 안 된다.
  Remove-Item $attemptMarker -Force -ErrorAction SilentlyContinue
}

$reviewPrompt = @'
너는 이 저장소의 조율자다. **판정하러** 깨어났다. 실행은 이미 끝나 있다.

너는 실행자가 아니다. 새 기능을 만들지 마라. 남이 한 것을 판정한다 —
"법원은 새로운 아이디어를 생성하지 않는다. 선택과 판결만 수행한다."

읽어라.
1. docs/context/RUNTIME-INDEX.md
2. docs/handoff/WORK-QUEUE.md 의 **[~] 검토 대기 항목**
3. git log 로 그 항목에 해당하는 커밋의 diff

판정해라. **자기보고는 증거가 아니다. 하네스를 직접 다시 돌려서 대조한다.**
- 그 항목이 적어둔 완료 조건이 실제로 만족되는가. 문장이 아니라 exit code 로 확인한다.
- 게이트를 직접 재실행한다: measure dev-pack, handoff-integrity, doc-integrity, 그리고 그 항목에 해당하는 검사.
- 지표는 맞는데 목적이 미달인 부분이 있는가. 있으면 그것을 적는다.

결론을 낸다. **승인은 네 몫이다**(2026-07-27 사용자 결재, ADR-020).
- 통과면 [~] 를 [x] 로 바꾸고 **무엇을 다시 돌려서 확인했는지** 한 줄 적는다.
  보드 태스크면 verify_task 로 승인한다. 대시보드 결재면 approve 한다. 코덱스 산출물이면 import 한다.
- 미달이면 [~] 를 [ ] 로 되돌리고 **무엇이 부족한지** 적는다. 네가 직접 고치지 마라 — 다음 실행자가 집는다.
  보드 태스크면 REVIEW 에서 되돌린다. 코덱스 산출물이면 reject 하고 사유를 남긴다.
- 판단이 갈리면 대화 채널에 남기고 멈춘다. 애매한 것을 통과시키지 마라.

지켜라.
- **네가 실행한 것은 네가 승인하지 않는다.** 승인은 실행한 세션이 아닌 판정 세션의 일이다.
  이 세션이 방금 실행도 했다면 승인하지 말고 [~] 로 두고 다음 판정 세션에 넘겨라.
- **발사(sonnet/codex spawn)는 조율자 재량이다**(2026-07-28, ADR-021). 비용이 발생하므로
  왜 쐈는지와 무엇을 기대했는지를 반드시 남긴다. 근거 없이 쏘지 마라.
  보드에서 [조율자 판단] 이나 [사람 게이트] 로 시작하는 태스크는 네가 임의로 집지 마라 -
  그건 조율자가 인계함에서 판단한다.
- 기준 파일(blueprint.json, workflow-definition.json)이나 측정 코드를 고쳐서 통과시키지 마라.
  **여기만은 사람 결재다.** 측정 기준을 스스로 고쳐 통과하면 나머지 측정이 전부 의미를 잃는다.
'@

$executePrompt = @'
너는 이 저장소의 조율자다. 할 일이 남아 있어서 깨어났다.

먼저 이것부터 읽어라. 순서를 지켜라.
1. docs/context/RUNTIME-INDEX.md
2. docs/handoff/WORK-QUEUE.md          <- 여기서 다음 할 일을 집는다
3. C:\NHN Project\team-loop-lite-ai-learning\data\discussions.json 의 안 읽은 메시지
   (readBy 에 usr_claude_coordinator 가 없는 것)

그다음에 해라.
- C:\NHN Project\_ops\coordinator-inbox.md 에 "- [ ]" 로 남은 항목이 있으면 그것이 최우선이다.
  막혀서 올라온 것이다. 원인을 실체로 확인하고 처리한 뒤 그 줄의 [ ] 를 [x] 로 바꾼다.
  못 풀면 [ ] 로 두고 무엇을 확인했는지 그 줄 아래에 덧붙인다. 지우지 마라.
- 읽은 메시지에 readBy 를 추가해 읽음으로 표시한다.
- 같은 파일에 authorUserId 를 usr_claude_coordinator 로 해서 답을 남긴다.
- 사람이 시킨 것이 있으면 그것이 최우선이다.
- WORK-QUEUE.md 의 **맨 위 미완 항목([ ]) 하나만** 한다. 여러 개를 몰아 하지 마라.
- 네가 맡은 항목은 [>] (진행 중) 으로 표시돼 있다. 그것을 해라. 다른 항목을 집지 마라.
- 실행이 끝나면 그 항목을 [~](검토 대기) 로 바꾸고 무엇을 했는지·무엇으로 확인했는지 한 줄 덧붙인다.
  제목 뒤의 "진행 중 (session=...)" 꼬리표는 지운다.
  **[x] 로 직접 넘기지 마라. 실행자는 자기 일을 완료로 선언하지 않는다.**
  하다가 새로 알게 된 일이 있으면 큐 아래에 추가한다 — 다음 세션이 그것을 집는다.
- 작업하는 동안 하트비트를 갱신한다. **JSON 을 손으로 쓰지 마라. 이 명령을 돌려라:**

    bash scripts/heartbeat-touch.sh "지금 무엇을 하는 중인지"

  **시각을 네가 계산하지 마라.** 2026-07-27~28 실측 - 시각을 손으로 타이핑해 온 탓에
  값이 흔들렸다. 한 번은 9시간 미래로 찍혀 루프가 6시간 47분 죽었고, 다른 때는 2분 미래였다.
  이제 깨우기가 at 을 파일 mtime 과 대조한다. 손으로 쓴 미래 값은 그 자리에서 잡힌다.

지켜라.
- 커밋 전에 measure dev-pack 이 violations 0 이어야 한다. 아니면 커밋하지 마라.
- **push 하지 마라.** 너는 세션 전용 워크트리의 session/<id> 브랜치에서 일하고 있다.
  커밋만 해라. 본 저장소가 네 브랜치를 병합해서 착지시키고 그때 한 번만 민다.
  네가 직접 밀면 브랜치가 엉킨다.
- **네가 한 일을 네가 승인하지 마라.** [~] 까지만 옮기고 판정 세션에 넘긴다.
  approve/verify_task/import 는 판정 세션의 일이다(ADR-020).
- **발사(sonnet/codex spawn)는 조율자 재량이다**(2026-07-28, ADR-021). 비용이 발생하므로
  왜 쐈는지와 무엇을 기대했는지를 반드시 남긴다. 근거 없이 쏘지 마라.
  보드에서 [조율자 판단] 이나 [사람 게이트] 로 시작하는 태스크는 네가 임의로 집지 마라 -
  그건 조율자가 인계함에서 판단한다.
  보드에서 제목이 [사람 게이트] 로 시작하는 태스크는 집지 마라.
- 확신이 없으면 추측으로 진행하지 말고 대화 채널에 질문을 남기고 멈춰라.
'@

# 판정과 실행은 다른 일이다. 한 프롬프트로 둘 다 시키면 실행자가 자기 일을 자기가 통과시킨다.
$isReview = ($queueReview -gt 0) -or ($boardReview.Count -gt 0)
$prompt = if ($isReview) { $reviewPrompt } else { $executePrompt }

# 보드가 방아쇠면 그 태스크를 프롬프트에 박는다. "보드를 봐라"가 아니라 무엇을 할지를 준다 —
# 깨어난 세션은 이 대화의 기억이 없어서, 가리키기만 하면 다른 것을 집는다.
$boardTask = if ($boardReview.Count -gt 0) { $boardReview[0] }
  elseif ((-not $isReview) -and ($unread.Count -eq 0) -and ($boardReady.Count -gt 0)) { $boardReady[0] }
  else { $null }
# team-loop 코드를 고치는 태스크면 격리 워크트리를 먼저 만들고 그 경로를 준다.
# 공유 메인 트리에서 작업하면 team-loop 이 소유권을 판별하지 못해 스스로 차단한다
# (2026-07-28 실측). 프롬프트로 "워크트리에서 해라"고 시키는 것으로는 안 지켜진다 -
# 경로를 만들어서 건네주는 것이 코드가 할 수 있는 최선이고, 오염은 끝나고 검사한다.
$teamLoopIsolation = ''
$isolateScript = Join-Path (Split-Path -Parent $PSCommandPath) 'teamloop-isolate.ps1'
if ($boardTask -and (Test-Path $isolateScript) -and ("$($boardTask.description)" -notmatch 'Local-First Workflow Dashboard')) {
  $ensured = & powershell -NoProfile -ExecutionPolicy Bypass -File $isolateScript `
    -Action Ensure -TeamLoopRoot $TeamLoopRoot -TaskId $boardTask.id 2>&1
  foreach ($line in @($ensured)) { Write-Line "$line" }
  $teamLoopWorktree = Join-Path $TeamLoopRoot ".team-loop-worktrees\$($boardTask.id)"
  $teamLoopIsolation = "`n`n**team-loop 코드는 반드시 이 격리 워크트리 안에서만 고쳐라:**`n" +
    "  $teamLoopWorktree`n" +
    "메인 트리($TeamLoopRoot)의 src/ 나 test/ 를 건드리지 마라. 건드리면 team-loop 이 변경물의" +
    "`n소유권을 판별하지 못해 태스크를 스스로 차단한다. 끝나고 조율자가 검사한다."
}

if ($boardTask) {
  $prompt = $prompt + "`n`n--- 이번에 할 것 (WORK-QUEUE 보다 이것이 먼저다) ---`n" +
    (Format-BoardTask $boardTask) +
    "`n`n이 태스크를 team-loop MCP 로 처리한다. **claim_task 를 쓰지 마라.**" +
    "`nclaim_task 는 executionMode 를 AGENT 로 바꾸는데, AGENT 모드에서는 납품 게이트가" +
    "`nteam-loop 이 spawn 한 실행자의 종료 코드를 요구한다(src/delivery-gate.js)." +
    "`n너는 그 실행자가 아니라서 기록이 없고 EXECUTOR_RESULT_MISSING 으로 막힌다." +
    "`n2026-07-27 실측: 한 세션이 코드를 다 짜고도 여기서 막혀 리뷰로 못 올렸다." +
    "`n`nHUMAN 모드 그대로 일해라. submit_task_result -> verify_task -> request_review_task 순이다." +
    "`nREVIEW 까지만 올린다. 승인은 판정 세션의 일이다." +
    $teamLoopIsolation +
    "`n`n대상 저장소는 태스크 설명에 적힌 것을 따른다. 보드는 team-loop 것이지만 태스크는" +
    "`n다른 저장소를 가리킬 수 있다. 안 적혀 있으면 team-loop($TeamLoopRoot) 이다."
}

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$promptFile = Join-Path $LogDir "wake-$stamp.prompt.txt"
[IO.File]::WriteAllText($promptFile, $prompt, [Text.UTF8Encoding]::new($false))

if ($DryRun) {
  Write-Line "would-wake trigger=$trigger mode=$(if ($isReview) { 'review' } else { 'execute' }) task=$(if ($boardTask) { $boardTask.id } else { 'none' }) prompt=$promptFile"
  exit 1
}

$log = Join-Path $LogDir "wake-$stamp.log"
Set-Content -Path $marker -Value $markerValue -Encoding UTF8

# 프롬프트를 명령행 인자로 넘기지 않는다. 콘솔 코드페이지를 거치면서 한글이 깨진다 —
# 2026-07-27 실측: 깨어난 세션이 "?덈뒗" 을 받았다. UTF-8 파일을 stdin 으로 넣으면 온전하다.
# 세션마다 별도 워크트리를 준다. 두 세션이 같은 파일을 만지는 창을 닫는다 -
# 커밋 구간 잠금과 큐의 진행 중 표기는 절반만 막았다(2026-07-27).
# 만들기에 실패하면 본 저장소에서 돈다. 격리를 못 해도 루프를 죽이지는 않는다.
$worktreeScript = Join-Path (Split-Path -Parent $PSCommandPath) 'session-worktree.ps1'
$sessionDir = $WorkingDir
$sessionId = ''
if (Test-Path $worktreeScript) {
  $created = & powershell -NoProfile -ExecutionPolicy Bypass -File $worktreeScript `
    -Action Create -SessionId $stamp -RepoRoot $WorkingDir 2>&1
  if ($LASTEXITCODE -eq 0) {
    $sessionId = $stamp
    $sessionDir = Join-Path 'C:\NHN Project\_ops\worktrees' "session-$stamp"
    Write-Line "worktree $sessionDir"
  } else {
    Write-Line "worktree-failed - 본 저장소에서 돈다: $created"
  }
}

Push-Location $sessionDir
try {
  $proc = Start-Process -FilePath 'claude.exe' `
    -ArgumentList @('-p', '--output-format', 'text', '--permission-mode', 'bypassPermissions') `
    -RedirectStandardInput $promptFile `
    -RedirectStandardOutput $log -RedirectStandardError "$log.err" `
    -NoNewWindow -PassThru
  # 이 세션이 무엇을 하는지 큐에 찍는다. 안 찍으면 다음 세션이 같은 항목을 집는다 -
  # 2026-07-27 실측: 한 세션이 세션 격리를 만드는 동안 다른 세션이 같은 항목을 잡았다.
  if ((Test-Path $queueScript) -and (-not $isReview) -and (-not $boardTask)) {
    # 워크트리 쪽 큐에 찍는다. 본 저장소에 찍으면 트리가 dirty 가 되어 착지가 거부된다.
    # 이 표기는 세션의 커밋과 함께 착지한다.
    $claimPath = if ($sessionId) { Join-Path $sessionDir 'docs\handoff\WORK-QUEUE.md' } else { $QueuePath }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $queueScript -Action Claim `
      -QueuePath $claimPath -SessionPid $proc.Id 2>&1 | ForEach-Object { Write-Line "queue-$_" }
  }

  # 보드에 "지금 이걸 하고 있다"를 찍는다. claim_task 를 못 쓰게 하면서 이 표시가 사라졌다 -
  # 2026-07-28 실측: 세션이 8분째 도는데 보드는 READY / IDLE 이었다.
  # executionMode 는 건드리지 않는다. 그것을 AGENT 로 바꾸는 것이 원래 문제였다.
  $boardClaimScript = Join-Path (Split-Path -Parent $PSCommandPath) 'board-claim.ps1'
  if ($boardTask -and (Test-Path $boardClaimScript)) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $boardClaimScript -Action Claim `
      -BoardPath $BoardPath -TaskId $boardTask.id -SessionId $stamp -SessionPid $proc.Id 2>&1 |
      ForEach-Object { Write-Line "$_" }
  }

  # 사람이 보는 자리에 남긴다. decisions.log 도 잠금 파일도 워크트리도 전부 로컬이라 폰에서 안 보인다.
  # 세션에게 시키지 않는다 - 세션이 잊거나 죽으면 그만이다. 깨우기가 직접 쓴다.
  $loopLogScript = Join-Path (Split-Path -Parent $PSCommandPath) 'loop-log.ps1'
  if (Test-Path $loopLogScript) {
    $what = if ($boardTask) { "보드 · $($boardTask.title)" } else { $trigger }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $loopLogScript `
      -Text "시작 · $(if ($isReview) { '판정' } else { '실행' }) · $what · $stamp" 2>&1 | Out-Null
  }

  # 띄운 세션이 사는 동안 잠금을 쥐어준다. 커밋 구간만 쥐면 그 사이에 다른 세션이 끼어든다 -
  # 2026-07-27 실측: 조율자가 도커·CI 를 기다리는 동안 하트비트가 낡아 깨우기가 판정 세션을
  # 띄웠고, 두 세션이 같은 항목을 동시에 했다. 하트비트만으로는 긴 작업을 못 덮는다.
  if (Test-Path $lockScript) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $lockScript -Action Acquire `
      -SessionPid $proc.Id -Label "wake:$trigger" -Quiet 2>&1 | Out-Null
  }
  if (-not $proc.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    $proc.Kill()
    Write-Line "wake-timeout after ${TimeoutMinutes}m log=$log"
    exit 3
  }
  if (Test-Path $lockScript) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $lockScript -Action Release `
      -SessionPid $proc.Id -Quiet 2>&1 | Out-Null
  }
  # 격리를 지켰는지 본다. 되돌리지는 않는다 - 남의 작업일 수 있고 조용히 지우는 것이 더 나쁘다.
  if ($boardTask -and (Test-Path $isolateScript)) {
    $contamination = & powershell -NoProfile -ExecutionPolicy Bypass -File $isolateScript `
      -Action Check -TeamLoopRoot $TeamLoopRoot 2>&1
    if ($LASTEXITCODE -ne 0) {
      foreach ($line in @($contamination)) { Write-Line "$line" }
      if (Test-Path $loopLogScript) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $loopLogScript `
          -Text "경고 · 세션이 격리 밖에서 team-loop 을 고쳤다 · $stamp" 2>&1 | Out-Null
      }
    }
  }

  if ($boardTask -and (Test-Path $boardClaimScript)) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $boardClaimScript -Action Release `
      -BoardPath $BoardPath -TaskId $boardTask.id 2>&1 | ForEach-Object { Write-Line "$_" }
  }

  if ($sessionId) {
    $landed = & powershell -NoProfile -ExecutionPolicy Bypass -File $worktreeScript `
      -Action Land -SessionId $sessionId -RepoRoot $WorkingDir 2>&1
    $landCode = $LASTEXITCODE
    foreach ($line in @($landed)) { Write-Line "land: $line" }
    if ($landCode -eq 0) {
      & powershell -NoProfile -ExecutionPolicy Bypass -File $worktreeScript `
        -Action Remove -SessionId $sessionId -RepoRoot $WorkingDir 2>&1 | Out-Null
      # 푸시는 여기서 한 번만. 세션이 각자 밀면 브랜치가 엉킨다.
      & git -C $WorkingDir push -q origin $BaseBranch 2>&1 | Out-Null
      if ($LASTEXITCODE -ne 0) { Write-Line 'push-failed - 다음 착지에서 함께 밀린다' }
    } else {
      # 브랜치를 지우지 않는다. 일한 결과를 버리지 않는다.
      # exit 하면 아래 finally 가 Pop-Location 을 한다. 여기서 또 하면 스택이 어긋난다.
      Report-Stop 'land-failed' "브랜치 session/$sessionId 를 남기고 멈춘다. 착지가 안 됐다"
      exit 4
    }
  }
  if (Test-Path $loopLogScript) {
    $tail = if ($sessionId) { "$landed" } else { '워크트리 없이 본 저장소에서 돌았다' }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $loopLogScript `
      -Text "끝 · $stamp · $tail" 2>&1 | Out-Null
  }
  Write-Line "woke exit=$($proc.ExitCode) trigger=$trigger mode=$(if ($isReview) { 'review' } else { 'execute' }) log=$log"
} finally {
  Pop-Location
}

# 작업이 끝나면 스스로 다음을 부른다. 안 그러면 매번 스케줄러를 기다려 루프가 느려진다.
# 깊이를 제한하는 이유는 실패한 항목이 큐에 남아 무한히 재시도하면 토큰만 태우기 때문이다.
if ($ChainDepth -ge $MaxChain) {
  Report-Stop 'chain-limit' "$MaxChain 회 연쇄했다. 남은 일이 있으면 다음 주기에 이어진다"
  exit 0
}

# 진행 여부는 큐와 보드를 **같이** 센다. 큐만 세면 보드 태스크를 끝낸 세션이
# "진전 없음"으로 판정돼 루프가 멈춘다 — 정작 일은 됐는데.
$stillOpen = 0; $stillReview = 0
if (Test-Path $QueuePath) {
  $stillOpen = @(Select-String -Path $QueuePath -Pattern '^\s*-\s\[\s\]\s' -AllMatches).Count
  $stillReview = @(Select-String -Path $QueuePath -Pattern '^\s*-\s\[~\]\s' -AllMatches).Count
}
$stillBoardReady = 0; $stillBoardReview = 0
if (Test-Path $BoardPath) {
  try {
    $laterBoard = Get-Content -Raw -Encoding UTF8 $BoardPath | ConvertFrom-Json
    $laterTasks = @(if ($laterBoard.tasks) { $laterBoard.tasks } else { $laterBoard })
    $laterTasks = @($laterTasks | Where-Object { -not $_.archived })
    $stillBoardReady = @($laterTasks | Where-Object { @('TODO','READY') -contains $_.status }).Count
    $stillBoardReview = @($laterTasks | Where-Object { $_.status -eq 'REVIEW' }).Count
  } catch { }
}

# 인계함도 센다. 인계함 때문에 깨어났는데 진전을 큐로만 재면 늘 no-progress 다 -
# 2026-07-28 실측: before=0 after=1 이 네 주기 연속 나왔고 before 는 큐+보드만 셌다.
$stillInbox = 0
if (Test-Path $InboxPath) {
  $stillInbox = @(Select-String -Path $InboxPath -Pattern '^\s*-\s\[\s\]\s' -AllMatches).Count
}
$before = $queueOpen + $queueReview + $boardReady.Count + $boardReview.Count + $inboxPending
$after = $stillOpen + $stillReview + $stillBoardReady + $stillBoardReview + $stillInbox
if ($after -eq 0) { Write-Line 'queue-drained'; exit 0 }

# 개수가 같아도 상태가 옮겨갔으면 진전이다. READY 하나가 REVIEW 로 가면 합계는 그대로다.
$moved = ($stillReview -ne $queueReview) -or ($stillBoardReview -ne $boardReview.Count) -or ($stillInbox -ne $inboxPending)
if (($after -ge $before) -and (-not $moved) -and ($unread.Count -eq 0)) {
  # 아무것도 안 줄고 아무것도 안 옮겨갔다. 같은 항목을 또 시도하면 같은 자리에서 막힌다.
  Report-Stop 'no-progress' "before=$before after=$after - 아무것도 줄지 않았다"
  exit 4
}

Write-Line "chaining depth=$($ChainDepth + 1) queue=$stillOpen/$stillReview board=$stillBoardReady/$stillBoardReview inbox=$stillInbox"

& $PSCommandPath -ChainDepth ($ChainDepth + 1) -MaxChain $MaxChain -TimeoutMinutes $TimeoutMinutes
exit $LASTEXITCODE
