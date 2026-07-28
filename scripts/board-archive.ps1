#Requires -Version 5.1
# 끝난 보드 태스크를 아카이브한다. 서버가 정한 조건(DONE 이고 미착지 변경이 없을 것)을
# 그대로 따른다 - 여기서 다시 판정하지 않는다.
#
# 왜 있는가: 인증 붙은 HTTP 호출을 매번 손으로 다시 짜고 있었다. 쿠키를 -Headers 에 넣으면
# PowerShell 5.1 이 조용히 버린다는 것도 매번 다시 밟는다(2026-07-28 실측, 401 로 나온다).
# 한 곳에 두고 쓴다.
param(
  [string]$TaskId = '',
  [switch]$Unarchive,
  [string]$SessionPath = (Join-Path $env:USERPROFILE '.team-loop-lite\session.json'),
  [string]$ServerUrl = ''
)

$ErrorActionPreference = 'Stop'
if (-not $TaskId) { Write-Output 'archive-needs-task'; exit 2 }
if (-not (Test-Path $SessionPath)) { Write-Output "archive-no-session $SessionPath"; exit 3 }

try { $session = Get-Content -Raw -Encoding UTF8 $SessionPath | ConvertFrom-Json }
catch { Write-Output "archive-session-unreadable $($_.Exception.Message)"; exit 3 }
$cookie = [string]$session.cookie
if (-not $cookie) { Write-Output 'archive-no-cookie'; exit 3 }
$cookieValue = [Uri]::UnescapeDataString(($cookie -split '=', 2)[1])
if (-not $ServerUrl) { $ServerUrl = [string]$session.server }
if (-not $ServerUrl) { Write-Output 'archive-no-server'; exit 3 }

# 쿠키는 그릇에 담는다. -Headers 에 넣으면 버려진다.
$uriBase = [Uri]$ServerUrl
$webSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$webSession.Cookies.Add((New-Object System.Net.Cookie('team_loop_session', $cookieValue, '/', $uriBase.Host)))
$headers = @{ 'X-Team-Loop-Client' = 'cli'; 'Accept' = 'application/json' }

# expectedVersion 은 서버가 지금 들고 있는 값이어야 한다. 손으로 적으면 어긋난다.
try {
  # 단일 태스크 읽기 경로는 없다(404). MCP 도 /api/bootstrap 에서 골라 쓴다.
  $boot = Invoke-RestMethod -Uri "$ServerUrl/api/bootstrap" -Method Get -WebSession $webSession -Headers $headers
  $current = @($boot.tasks | Where-Object { $_.id -eq $TaskId })[0]
} catch {
  Write-Output "archive-read-failed $TaskId : $($_.Exception.Message)"
  exit 3
}
$task = $current
if (-not $task.id) { Write-Output "archive-no-task $TaskId"; exit 2 }

$action = if ($Unarchive) { 'unarchive' } else { 'archive' }
if ((-not $Unarchive) -and $task.archived) { Write-Output "archive-already $TaskId"; exit 0 }
if ($Unarchive -and (-not $task.archived)) { Write-Output "archive-not-archived $TaskId"; exit 0 }

$body = @{ expectedVersion = [int]$task.version } | ConvertTo-Json
$bytes = [Text.Encoding]::UTF8.GetBytes($body)
try {
  $resp = Invoke-RestMethod -Uri "$ServerUrl/api/tasks/$TaskId/$action" -Method Post -Body $bytes -WebSession $webSession -Headers $headers -ContentType 'application/json; charset=utf-8'
} catch {
  Write-Output "archive-failed $TaskId : $($_.Exception.Message)"
  exit 3
}

# 서버가 그렇게 했다고 믿지 않고 다시 읽는다.
$bootAfter = Invoke-RestMethod -Uri "$ServerUrl/api/bootstrap" -Method Get -WebSession $webSession -Headers $headers
$after = @($bootAfter.tasks | Where-Object { $_.id -eq $TaskId })[0]
$afterTask = $after
if ($Unarchive) {
  if ($afterTask.archived) { Write-Output "archive-verify-failed $TaskId"; exit 3 }
  Write-Output "unarchived $TaskId (v$($afterTask.version))"
} else {
  if (-not $afterTask.archived) { Write-Output "archive-verify-failed $TaskId"; exit 3 }
  Write-Output "archived $TaskId (v$($afterTask.version))"
}
exit 0
