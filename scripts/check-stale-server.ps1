#Requires -Version 5.1
# 도는 team-loop 서버가 옛 코드면 알린다.
#
# 왜 있는가: 코드를 고치고 도는 프로세스를 그대로 두는 실수를 2026-07-27~28 에 세 번 했다.
#  1) .NET 8 하네스를 고쳤는데 옛 바이너리로 재고 "안 고쳐졌다"고 오판할 뻔했다
#  2) 스킬 소속 필터를 고쳤는데 옛 서버가 옛 목록을 계속 내줬다
#  3) approve_task 가 reviewSessionPid 를 보냈는데 17분 먼저 뜬 옛 서버가 그 필드를 버렸다.
#     기록이 유실됐고, 나는 "세션이 도구를 안 썼다"고 잘못 읽을 뻔했다
#
# 셋 다 "누가 규칙을 안 지켰나"가 아니라 "내가 반영을 잊었나"다. 사람이 기억할 일이 아니다.
#
# 판정은 두 실체의 비교다. 서버 프로세스의 시작 시각과, 서버가 읽는 코드의 마지막 커밋 시각.
# 코드가 더 새로우면 그 서버는 그 변경을 안 싣고 있다.
param(
  [string]$TeamLoopRoot = 'C:\NHN Project\team-loop-lite-ai-learning',
  # 서버가 실제로 읽는 것들. 여기 밖(예: test/)이 바뀐 것으로는 재시작을 요구하지 않는다.
  [string[]]$ServerPaths = @('server.js', 'src', 'mcp', 'public'),
  [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

$proc = Get-CimInstance Win32_Process -Filter "Name='node.exe'" -ErrorAction SilentlyContinue |
  Where-Object { $_.CommandLine -like '*serve --port*' } |
  Select-Object -First 1

if (-not $proc) {
  if (-not $Quiet) { Write-Output 'server-not-running' }
  exit 0
}

$startedAt = [datetimeoffset]$proc.CreationDate

# 서버가 읽는 경로의 마지막 커밋 시각. 커밋 안 된 변경은 여기서 안 잡힌다 -
# 그건 아직 반영할 대상이 아니라 작업 중인 것이다.
$saved = $ErrorActionPreference
$ErrorActionPreference = 'SilentlyContinue'
try {
  $iso = & git -C $TeamLoopRoot log -1 --format=%cI -- @ServerPaths 2>&1
  $code = $LASTEXITCODE
} finally {
  $ErrorActionPreference = $saved
}
if ($code -ne 0 -or -not "$iso".Trim()) {
  if (-not $Quiet) { Write-Output 'server-code-commit-unknown' }
  exit 0
}

$committedAt = [datetimeoffset]::Parse(("$iso" -split "`n" | Where-Object { $_.Trim() } | Select-Object -Last 1).Trim())
$lagMinutes = [int]($committedAt - $startedAt).TotalMinutes

if ($lagMinutes -le 0) {
  if (-not $Quiet) { Write-Output "server-fresh pid=$($proc.ProcessId)" }
  exit 0
}

Write-Output "server-stale pid=$($proc.ProcessId) - 서버가 뜬 뒤 ${lagMinutes}분 지나 서버 코드가 바뀌었다. 재시작해야 반영된다"
Write-Output "  서버 시작: $($startedAt.ToString('yyyy-MM-dd HH:mm:ss'))"
Write-Output "  코드 커밋: $($committedAt.ToString('yyyy-MM-dd HH:mm:ss'))"
exit 1
