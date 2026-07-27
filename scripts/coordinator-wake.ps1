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
  [string]$WorkingDir     = 'C:\Users\1\Documents\Local-First Workflow Dashboard',
  [string]$LogDir         = 'C:\NHN Project\_ops\wake-logs',
  [string]$ReaderId       = 'usr_claude_coordinator',
  [string]$BoardPath      = 'C:\NHN Project\team-loop-lite-ai-learning\data\tasks.json',
  [string]$QueuePath      = 'C:\Users\1\Documents\Local-First Workflow Dashboard\docs\handoff\WORK-QUEUE.md',
  [int]$ChainDepth        = 0,
  [int]$MaxChain          = 5,
  [string[]]$ActiveStatuses = @('TODO','READY','IN_PROGRESS','REVIEW','BLOCKED'),
  [int]$StaleMinutes      = 25,
  [int]$TimeoutMinutes    = 30,
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Write-Line([string]$text) { Write-Output $text }

if (-not (Test-Path $DiscussionPath)) { Write-Line 'discussion-missing'; exit 2 }

$discussion = Get-Content -Raw -Encoding UTF8 $DiscussionPath | ConvertFrom-Json
$unread = @($discussion.messages | Where-Object {
  $_.authorUserId -ne $ReaderId -and -not ($_.readBy | Where-Object { $_.userId -eq $ReaderId })
})
# 작업이 남아 있으면 메시지가 없어도 깨운다. 메시지에만 반응하면 그건 루프가 아니라 응답이다.
# 2026-07-27: 334분 공백 동안 할 일이 남아 있었는데 아무도 이어가지 않았다.
# 지시큐는 작업보드다 — 사람이 태스크를 올려두면 조율자가 그것을 집어간다.
$pendingWork = 0
if (Test-Path $BoardPath) {
  try {
    $board = Get-Content -Raw -Encoding UTF8 $BoardPath | ConvertFrom-Json
    $tasks = if ($board.tasks) { $board.tasks } else { $board }
    $pendingWork = @($tasks | Where-Object { $_.status -and $ActiveStatuses -contains $_.status }).Count
  } catch { $pendingWork = 0 }
}

# 작업 큐의 미완 항목도 방아쇠다. 사람이 다음 작업까지 적어두면 끝날 때까지 이어진다.
$queueOpen = 0
if (Test-Path $QueuePath) {
  $queueOpen = @(Select-String -Path $QueuePath -Pattern '^\s*-\s\[\s\]\s' -AllMatches).Count
}

if ($unread.Count -eq 0 -and $pendingWork -eq 0 -and $queueOpen -eq 0) { Write-Line 'nothing-to-do'; exit 0 }
$trigger = if ($unread.Count -gt 0) { "unread=$($unread.Count)" }
  elseif ($pendingWork -gt 0) { "board=$pendingWork" }
  else { "queue=$queueOpen" }

# 조율자가 지금 돌고 있으면 깨우지 않는다. 두 세션이 같은 저장소를 동시에 만지면
# 커밋이 엉킨다. 판정은 하트비트 나이 하나다 — 프로세스 존재로 판정하지 않는다.
if (Test-Path $HeartbeatPath) {
  $beat = Get-Content -Raw -Encoding UTF8 $HeartbeatPath | ConvertFrom-Json
  $age = [int]([datetimeoffset]::UtcNow - [datetimeoffset]::Parse($beat.at)).TotalMinutes
  if ($age -lt $StaleMinutes) { Write-Line "coordinator-alive age=${age}m"; exit 0 }
}

# 같은 글로 두 번 깨우지 않는다. 실패한 세션이 무한히 재시도하면 토큰만 태운다.
# 큐가 방아쇠일 때는 남은 개수를 표식으로 쓴다. 하나 끝나면 개수가 줄어 다음 깨우기가 열린다.
$markerValue = if ($unread.Count -gt 0) { $unread[0].id }
  elseif ($pendingWork -gt 0) { "board:$pendingWork" }
  else { "queue:$queueOpen" }
$marker = "$HeartbeatPath.woke"
if ((Test-Path $marker) -and ((Get-Content -Raw $marker).Trim() -eq $markerValue)) {
  Write-Line "already-woke-for $markerValue"
  exit 0
}

$prompt = @'
너는 이 저장소의 조율자다. 할 일이 남아 있어서 깨어났다.

먼저 이것부터 읽어라. 순서를 지켜라.
1. docs/context/RUNTIME-INDEX.md
2. docs/handoff/WORK-QUEUE.md          <- 여기서 다음 할 일을 집는다
3. C:\NHN Project\team-loop-lite-ai-learning\data\discussions.json 의 안 읽은 메시지
   (readBy 에 usr_claude_coordinator 가 없는 것)

그다음에 해라.
- 읽은 메시지에 readBy 를 추가해 읽음으로 표시한다.
- 같은 파일에 authorUserId 를 usr_claude_coordinator 로 해서 답을 남긴다.
- 사람이 시킨 것이 있으면 그것이 최우선이다.
- 시킨 것이 없으면 WORK-QUEUE.md 의 **맨 위 미완 항목 하나만** 한다. 여러 개를 몰아 하지 마라.
- 끝내면 그 항목을 [x] 로 바꾸고 무엇을 했는지 한 줄 덧붙인다.
  하다가 새로 알게 된 일이 있으면 큐 아래에 추가한다 — 다음 세션이 그것을 집는다.
- 작업하는 동안 C:\NHN Project\_ops\coordinator-heartbeat.json 의 at 을 갱신한다.

지켜라.
- 커밋 전에 measure dev-pack 이 violations 0 이어야 한다. 아니면 커밋하지 마라.
- push 는 게이트가 전부 통과했을 때만 한다.
- approve/reject/import 대행과 sonnet 발사는 하지 않는다.
- 확신이 없으면 추측으로 진행하지 말고 대화 채널에 질문을 남기고 멈춰라.
'@

if ($DryRun) { Write-Line "would-wake trigger=$trigger"; exit 1 }

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$log = Join-Path $LogDir "wake-$stamp.log"
$promptFile = Join-Path $LogDir "wake-$stamp.prompt.txt"
[IO.File]::WriteAllText($promptFile, $prompt, [Text.UTF8Encoding]::new($false))

Set-Content -Path $marker -Value $markerValue -Encoding UTF8

# 프롬프트를 명령행 인자로 넘기지 않는다. 콘솔 코드페이지를 거치면서 한글이 깨진다 —
# 2026-07-27 실측: 깨어난 세션이 "?덈뒗" 을 받았다. UTF-8 파일을 stdin 으로 넣으면 온전하다.
Push-Location $WorkingDir
try {
  $proc = Start-Process -FilePath 'claude.exe' `
    -ArgumentList @('-p', '--output-format', 'text', '--permission-mode', 'bypassPermissions') `
    -RedirectStandardInput $promptFile `
    -RedirectStandardOutput $log -RedirectStandardError "$log.err" `
    -NoNewWindow -PassThru
  if (-not $proc.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    $proc.Kill()
    Write-Line "wake-timeout after ${TimeoutMinutes}m log=$log"
    exit 3
  }
  Write-Line "woke exit=$($proc.ExitCode) trigger=$trigger log=$log"
} finally {
  Pop-Location
}

# 작업이 끝나면 스스로 다음을 부른다. 안 그러면 매번 스케줄러를 기다려 루프가 느려진다.
# 깊이를 제한하는 이유는 실패한 항목이 큐에 남아 무한히 재시도하면 토큰만 태우기 때문이다.
if ($ChainDepth -ge $MaxChain) { Write-Line "chain-limit reached ($MaxChain)"; exit 0 }

$stillOpen = 0
if (Test-Path $QueuePath) {
  $stillOpen = @(Select-String -Path $QueuePath -Pattern '^\s*-\s\[\s\]\s' -AllMatches).Count
}
if ($stillOpen -eq 0) { Write-Line 'queue-drained'; exit 0 }
if ($stillOpen -ge $queueOpen -and $unread.Count -eq 0) {
  # 큐가 줄지 않았다. 같은 항목을 다시 시도하면 같은 자리에서 또 막힌다.
  Write-Line "no-progress open=$stillOpen (was $queueOpen) — 사람 확인이 필요하다"
  exit 4
}

Write-Line "chaining depth=$($ChainDepth + 1) remaining=$stillOpen"
& $PSCommandPath -ChainDepth ($ChainDepth + 1) -MaxChain $MaxChain -TimeoutMinutes $TimeoutMinutes
exit $LASTEXITCODE
