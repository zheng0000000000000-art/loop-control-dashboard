#Requires -Version 5.1
# 보드 태스크의 회로 차단기를 연다. 열기 전에 원인으로 알려진 것(워크트리에 data 가 없음)을
# 먼저 없앤다 - 원인을 안 없애고 열면 같은 자리에서 또 닫힌다.
#
# 왜 있는가: 2026-07-28 tsk_1a113f64adb67331dac2 가 워크트리에 data 가 없어 node --test 가
# ENOENT 로 깨졌고 회로가 열렸다. 씨딩하니 533/533 통과했다. 씨딩도 해제도 사람이 손으로 했다.
#
# 한 번만 연다(remedies.json 의 maxPerDay=1). 두 번 닫히면 씨딩으로 안 풀리는 원인이고,
# 그건 사람이 봐야 한다. 무한히 재시도하면 진짜 망가진 태스크가 계속 돈다.
param(
  # 대상 태스크. remedy.ps1 이 -Subject 로 넘긴다.
  [string]$Subject = '',
  [string]$BoardPath = 'C:\NHN Project\team-loop-lite-ai-learning\data\tasks.json',
  [string]$TeamLoopRoot = 'C:\NHN Project\team-loop-lite-ai-learning'
)

$ErrorActionPreference = 'Stop'
if (-not $Subject) { Write-Output 'clear-circuit-needs-task'; exit 2 }
if (-not (Test-Path $BoardPath)) { Write-Output 'clear-circuit-no-board'; exit 2 }

$board = Get-Content -Raw -Encoding UTF8 $BoardPath | ConvertFrom-Json
$tasks = @(if ($board.tasks) { $board.tasks } else { $board })
$task = @($tasks | Where-Object { $_.id -eq $Subject })[0]
if (-not $task) { Write-Output "clear-circuit-no-task $Subject"; exit 2 }
if (-not $task.automationGuard) { Write-Output "clear-circuit-no-guard $Subject"; exit 2 }
if (-not $task.automationGuard.circuitOpen) { Write-Output "clear-circuit-not-open $Subject"; exit 0 }

# 알려진 원인부터 없앤다. 씨딩은 사본만 만들고 원본 data 를 건드리지 않는다.
$isolate = Join-Path (Split-Path -Parent $PSCommandPath) 'teamloop-isolate.ps1'
if (Test-Path $isolate) {
  $seed = & powershell -NoProfile -ExecutionPolicy Bypass -File $isolate -Action Ensure -TaskId $Subject -TeamLoopRoot $TeamLoopRoot 2>&1
  Write-Output "clear-circuit-seed $(($seed | Select-Object -Last 1))"
}

$task.automationGuard.circuitOpen = $false
$task.automationGuard.sameFailureCount = 0
$task.automationGuard.lastFailureSignature = ''
$task.version = [int]$task.version + 1
$task.updatedAt = [datetimeoffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')

$tmp = "$BoardPath.tmp"
[IO.File]::WriteAllText($tmp, ($board | ConvertTo-Json -Depth 40), [Text.UTF8Encoding]::new($false))
Move-Item -Force $tmp $BoardPath

# 쓴 뒤에 다시 읽어 확인한다. 썼다고 믿지 않는다.
$after = Get-Content -Raw -Encoding UTF8 $BoardPath | ConvertFrom-Json
$afterTasks = @(if ($after.tasks) { $after.tasks } else { $after })
$check = @($afterTasks | Where-Object { $_.id -eq $Subject })[0]
if ($check.automationGuard.circuitOpen) { Write-Output "clear-circuit-write-failed $Subject"; exit 3 }
Write-Output "clear-circuit-opened $Subject (v$($check.version))"
exit 0
