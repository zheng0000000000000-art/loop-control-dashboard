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
  [string]$TeamLoopRoot   = 'C:\NHN Project\team-loop-lite-ai-learning',
  [string]$BoardPath      = 'C:\NHN Project\team-loop-lite-ai-learning\data\tasks.json',
  [string]$QueuePath      = 'C:\Users\1\Documents\Local-First Workflow Dashboard\docs\handoff\WORK-QUEUE.md',
  [int]$ChainDepth        = 0,
  [int]$MaxChain          = 5,
  [string[]]$ActiveStatuses = @('TODO','READY','IN_PROGRESS','REVIEW','BLOCKED'),
  # 25분이면 24분 공백을 "살아 있다"로 읽는다(2026-07-27 실측: 11:15 커밋 후 11:36 판정이 alive).
  # 깨우기는 알림보다 싸므로 짧게 잡는다. 조율자가 도는 중이면 어차피 하트비트가 갱신된다.
  [int]$StaleMinutes      = 10,
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

if (-not (Test-Path $DiscussionPath)) { Write-Line 'discussion-missing'; exit 2 }

$discussion = Get-Content -Raw -Encoding UTF8 $DiscussionPath | ConvertFrom-Json
$unread = @($discussion.messages | Where-Object {
  $_.authorUserId -ne $ReaderId -and -not ($_.readBy | Where-Object { $_.userId -eq $ReaderId })
})
# 작업이 남아 있으면 메시지가 없어도 깨운다. 메시지에만 반응하면 그건 루프가 아니라 응답이다.
# 2026-07-27: 334분 공백 동안 할 일이 남아 있었는데 아무도 이어가지 않았다.
# 지시큐는 작업보드다 — 사람이 태스크를 올려두면 조율자가 그것을 집어간다.
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
    $boardReady = @($tasks | Where-Object { $_.status -and (@('TODO','READY') -contains $_.status) -and (-not $_.blocked) -and (@(@($_.dependsOnTaskIds) | Where-Object { $_ -and ($doneIds -notcontains $_) }).Count -eq 0) } | Sort-Object { [int]$_.priority })
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
$queueOpen = 0
$queueReview = 0
if (Test-Path $QueuePath) {
  $queueOpen = @(Select-String -Path $QueuePath -Pattern '^\s*-\s\[\s\]\s' -AllMatches).Count
  # 검토 대기는 실행이 끝나고 판정만 남은 것이다. 조율자가 깨어나야 하는 진짜 이유가 이것이다 —
  # 실행은 CLI 세션과 로컬 모델이 돌리고, 조율자는 판정에서만 들어온다(Court/Clerk 분리).
  $queueReview = @(Select-String -Path $QueuePath -Pattern '^\s*-\s\[~\]\s' -AllMatches).Count
}

if ($unread.Count -eq 0 -and $pendingWork -eq 0 -and $queueOpen -eq 0 -and $queueReview -eq 0) {
  Write-Line 'nothing-to-do'; exit 0
}
# 검토 대기가 있으면 그것이 최우선이다. 실행보다 판정이 먼저 밀린다 —
# 판정이 안 나면 그다음 실행이 무엇 위에 쌓이는지 아무도 모른다.
$trigger = if ($boardReview.Count -gt 0) { "board-review=$($boardReview.Count)" }
  elseif ($queueReview -gt 0) { "review=$queueReview" }
  elseif ($unread.Count -gt 0) { "unread=$($unread.Count)" }
  elseif ($boardReady.Count -gt 0) { "board=$($boardReady.Count)" }
  elseif ($pendingWork -gt 0) { "board-stuck=$pendingWork" }
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
$markerValue = if ($boardReview.Count -gt 0) { "board-review:$($boardReview[0].id)" }
  elseif ($queueReview -gt 0) { "review:$queueReview" }
  elseif ($unread.Count -gt 0) { $unread[0].id }
  elseif ($boardReady.Count -gt 0) { "board:$($boardReady[0].id)" }
  else { "queue:$queueOpen" }
$marker = "$HeartbeatPath.woke"
if ((Test-Path $marker) -and ((Get-Content -Raw $marker).Trim() -eq $markerValue)) {
  Write-Line "already-woke-for $markerValue"
  exit 0
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

결론을 낸다.
- 통과면 [~] 를 [x] 로 바꾸고 **무엇을 다시 돌려서 확인했는지** 한 줄 적는다.
- 미달이면 [~] 를 [ ] 로 되돌리고 **무엇이 부족한지** 적는다. 네가 직접 고치지 마라 — 다음 실행자가 집는다.
- 판단이 갈리면 대화 채널에 남기고 멈춘다. 애매한 것을 통과시키지 마라.

지켜라.
- approve/reject/import 대행과 sonnet 발사는 하지 않는다. 그건 사람 몫이다.
- 기준 파일(blueprint.json, workflow-definition.json)이나 측정 코드를 고쳐서 통과시키지 마라.
'@

$executePrompt = @'
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
- WORK-QUEUE.md 의 **맨 위 미완 항목([ ]) 하나만** 한다. 여러 개를 몰아 하지 마라.
- 실행이 끝나면 그 항목을 [~](검토 대기) 로 바꾸고 무엇을 했는지·무엇으로 확인했는지 한 줄 덧붙인다.
  **[x] 로 직접 넘기지 마라. 실행자는 자기 일을 완료로 선언하지 않는다.**
  하다가 새로 알게 된 일이 있으면 큐 아래에 추가한다 — 다음 세션이 그것을 집는다.
- 작업하는 동안 C:\NHN Project\_ops\coordinator-heartbeat.json 의 at 을 갱신한다.

지켜라.
- 커밋 전에 measure dev-pack 이 violations 0 이어야 한다. 아니면 커밋하지 마라.
- push 는 게이트가 전부 통과했을 때만 한다.
- approve/reject/import 대행과 sonnet 발사는 하지 않는다.
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
if ($boardTask) {
  $prompt = $prompt + "`n`n--- 이번에 할 것 (WORK-QUEUE 보다 이것이 먼저다) ---`n" +
    (Format-BoardTask $boardTask) +
    "`n`n이 태스크를 team-loop MCP 로 처리한다. claim_task 로 집고, 끝나면 submit_task_result 와" +
    "`nrequest_review_task 로 REVIEW 까지만 올린다. verify_task 로 스스로 통과시키지 마라 — 판정은 따로다." +
    "`nteam-loop 저장소는 $TeamLoopRoot 이다."
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
  Write-Line "woke exit=$($proc.ExitCode) trigger=$trigger mode=$(if ($isReview) { 'review' } else { 'execute' }) log=$log"
} finally {
  Pop-Location
}

# 작업이 끝나면 스스로 다음을 부른다. 안 그러면 매번 스케줄러를 기다려 루프가 느려진다.
# 깊이를 제한하는 이유는 실패한 항목이 큐에 남아 무한히 재시도하면 토큰만 태우기 때문이다.
if ($ChainDepth -ge $MaxChain) { Write-Line "chain-limit reached ($MaxChain)"; exit 0 }

$stillOpen = 0; $stillReview = 0
if (Test-Path $QueuePath) {
  $stillOpen = @(Select-String -Path $QueuePath -Pattern '^\s*-\s\[\s\]\s' -AllMatches).Count
  $stillReview = @(Select-String -Path $QueuePath -Pattern '^\s*-\s\[~\]\s' -AllMatches).Count
}
if ($stillOpen -eq 0 -and $stillReview -eq 0) { Write-Line 'queue-drained'; exit 0 }
if ($stillOpen -ge $queueOpen -and $stillReview -eq $queueReview -and $unread.Count -eq 0) {
  # 큐가 줄지 않았다. 같은 항목을 다시 시도하면 같은 자리에서 또 막힌다.
  Write-Line "no-progress open=$stillOpen (was $queueOpen) — 사람 확인이 필요하다"
  exit 4
}

Write-Line "chaining depth=$($ChainDepth + 1) remaining=$stillOpen"
& $PSCommandPath -ChainDepth ($ChainDepth + 1) -MaxChain $MaxChain -TimeoutMinutes $TimeoutMinutes
exit $LASTEXITCODE
