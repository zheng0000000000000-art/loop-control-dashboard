# 조율자가 멈춘 것을 사람이 먼저 알게 한다.
#
# 왜 필요한가: 조율 세션은 턴 사이에 시계가 없어서 자기가 멈춘 것을 스스로 모른다.
# 2026-07-27에 5시간 38분 공백이 있었고 그 사이 사용자가 남긴 글 두 개를 아무도 읽지 않았다.
# 조율자가 살아 있는 동안 하트비트를 찍고, 이 감시자가 그 나이를 보고 알린다.
#
# 판정은 파일의 나이 하나다. 프로세스 존재 여부로 판정하지 않는다 —
# 프로세스가 살아 있어도 일을 안 할 수 있고, 그건 "돌고 있다"의 증거가 아니다.
param(
  [string]$HeartbeatPath = 'C:\NHN Project\_ops\coordinator-heartbeat.json',
  [int]$StaleMinutes = 25,
  [string]$DiscussionPath = 'C:\NHN Project\team-loop-lite-ai-learning\data\discussions.json',
  [string]$ReaderId = 'usr_claude_coordinator',
  [int]$UnreadMinutes = 10,
  [string]$NtfyServer = 'https://ntfy.sh',
  [string]$Topic = '',
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not $Topic) {
  $settings = Join-Path $PSScriptRoot '..\server\appsettings.json'
  if (Test-Path $settings) {
    $config = Get-Content -Raw -Encoding UTF8 $settings | ConvertFrom-Json
    if ($config.Ntfy -and $config.Ntfy.Topic) { $Topic = $config.Ntfy.Topic }
  }
}
if (-not $Topic) { Write-Output 'topic-missing'; exit 2 }

# 조율자에게 안 읽힌 대화가 있으면 알린다.
# 2026-07-27 실측: 사용자가 05:14:47과 05:17:40에 대화 채널에 글을 남겼는데 조율자의 턴은
# 05:14:11에 이미 끝나 있었다. 대화 채널은 조율자를 깨우지 못한다 — 채팅만 턴을 시작한다.
# 그래서 334분 동안 아무도 읽지 않았고 사람이 물어봐서 알았다.
if (Test-Path $DiscussionPath) {
  # readBy 는 객체({userId,at})와 과거 손편집이 남긴 문자열이 섞여 있다 — 둘 다 인정한다.
  # (coordinator-wake.ps1 과 동일 근거: 2026-07-28 실측, 객체 전용 매칭이 이미 읽은 메시지를
  # 영구히 unread 로 오판했다)
  $discussion = Get-Content -Raw -Encoding UTF8 $DiscussionPath | ConvertFrom-Json
  $unread = @($discussion.messages | Where-Object {
    $_.authorUserId -ne $ReaderId -and -not ($_.readBy | Where-Object { $_ -eq $ReaderId -or $_.userId -eq $ReaderId })
  })
  $unreadMarker = "$HeartbeatPath.unread"
  if ($unread.Count -gt 0) {
    $oldest = [datetimeoffset]::Parse($unread[0].createdAt)
    $waited = [int]([datetimeoffset]::UtcNow - $oldest).TotalMinutes
    $seen = (Test-Path $unreadMarker) -and ((Get-Content -Raw $unreadMarker).Trim() -eq $unread[0].id)
    if (-not $seen -and $waited -ge $UnreadMinutes) {
      $text = "조율자가 안 읽은 대화 $($unread.Count)건. 가장 오래된 것 ${waited}분째. 채팅으로 깨워야 읽는다."
      if ($DryRun) { Write-Output "would-alert-unread: $text" }
      else {
        $f = [IO.Path]::GetTempFileName()
        [IO.File]::WriteAllText($f, $text, [Text.UTF8Encoding]::new($false))
        try {
          Invoke-RestMethod -Uri "$NtfyServer/$Topic" -Method Post `
            -Headers @{ Title = 'Unread message'; Tags = 'envelope'; Priority = '4' } `
            -ContentType 'text/plain; charset=utf-8' -InFile $f | Out-Null
        } finally { Remove-Item $f -Force -ErrorAction SilentlyContinue }
        Set-Content -Path $unreadMarker -Value $unread[0].id -Encoding UTF8
        Write-Output "alerted-unread count=$($unread.Count) waited=${waited}m"
      }
    }
  } elseif (Test-Path $unreadMarker) { Remove-Item $unreadMarker -Force }
}

if (-not (Test-Path $HeartbeatPath)) { Write-Output 'heartbeat-missing'; exit 2 }

$beat = Get-Content -Raw -Encoding UTF8 $HeartbeatPath | ConvertFrom-Json
$at = [datetimeoffset]::Parse($beat.at)
$ageMinutes = [int]([datetimeoffset]::UtcNow - $at).TotalMinutes

# 이미 알린 정지를 다시 알리지 않는다. 같은 정지로 계속 울리면 사람이 알림을 끈다.
$markerPath = "$HeartbeatPath.alerted"
$alreadyAlerted = (Test-Path $markerPath) -and ((Get-Content -Raw $markerPath).Trim() -eq $beat.at)

# 미래로 찍힌 하트비트는 조용함보다 나쁘다. 조용한 것은 알리기라도 하는데
# 이건 살아 있다고 거짓말해서 알림 자체를 막는다. 깨우기와 감시자가 같은 검사를 쓰고 있어서
# 안전망 둘이 한꺼번에 눈을 감았다(2026-07-27 실측: 6시간 47분 무응답).
$inFuture = $ageMinutes -lt 0
$skewMinutes = [Math]::Abs($ageMinutes)

if ((-not $inFuture) -and ($ageMinutes -lt $StaleMinutes)) {
  if (Test-Path $markerPath) { Remove-Item $markerPath -Force }
  Write-Output "alive age=${ageMinutes}m"
  exit 0
}

if ($alreadyAlerted) { Write-Output "stale age=${ageMinutes}m (already alerted)"; exit 0 }

$body = if ($inFuture) {
  "하트비트가 ${skewMinutes}분 미래로 찍혀 있다. 살아 있다는 뜻이 아니라 고장이다. 깨우기가 이걸 살아 있다로 읽으면 루프가 조용히 죽는다. 마지막 작업: $($beat.note)"
} else {
  "조율자가 ${ageMinutes}분째 조용하다. 마지막 작업: $($beat.note)"
}
if ($DryRun) { Write-Output "would-alert: $body"; exit 1 }

# 본문을 UTF-8 파일로 넘긴다. PowerShell 5.1에서 -Body 에 byte[] 를 주면 본문이 비어 도착한다
# (2026-07-27 실측: 알림 제목만 오고 message 가 빈 문자열이었다). 터미널 표시로 판단하지 말고
# 보낸 뒤 ntfy 에서 되읽어 확인한다 — skills/common/powershell-encoding 과 같은 이유다.
$headers = if ($inFuture) {
  @{ Title = 'Heartbeat in the future'; Tags = 'rotating_light'; Priority = '5' }
} else {
  @{ Title = 'Coordinator stalled'; Tags = 'warning'; Priority = '4' }
}
$bodyFile = [IO.Path]::GetTempFileName()
[IO.File]::WriteAllText($bodyFile, $body, [Text.UTF8Encoding]::new($false))
try {
  Invoke-RestMethod -Uri "$NtfyServer/$Topic" -Method Post -Headers $headers `
    -ContentType 'text/plain; charset=utf-8' -InFile $bodyFile | Out-Null
} finally {
  Remove-Item $bodyFile -Force -ErrorAction SilentlyContinue
}
Set-Content -Path $markerPath -Value $beat.at -Encoding UTF8
Write-Output "alerted age=${ageMinutes}m"
exit 1
