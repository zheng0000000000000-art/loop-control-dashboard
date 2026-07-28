#Requires -Version 5.1
# 폰으로 알린다. 토픽은 이 저장소의 appsettings 하나를 정본으로 읽는다.
#
# 왜 있는가: 막힘이 조율자 인계함과 대화 채널에는 남는데, 사람이 그 화면을 열어봐야만 보인다.
# 2026-07-28 실측 - 회로 차단기가 걸려 태스크가 멈춰 있었는데 사람이 "보드 돌려줘"라고 물어야
# 조율자가 알았다. 알림이 사람을 찾아가야지 사람이 알림을 찾으러 가면 안 된다.
#
# 알림에 실패해도 던지지 않는다. 알림이 안 갔다고 부르는 쪽의 작업을 잃으면 안 된다.
param(
  [Parameter(Mandatory = $true)][string]$Title,
  [Parameter(Mandatory = $true)][string]$Body,
  [string]$Priority = '4',
  [string]$Tags = 'warning',
  [string]$SettingsPath = 'C:\Users\1\Documents\Local-First Workflow Dashboard\server\appsettings.json',
  [string]$NtfyServer = 'https://ntfy.sh'
)

$ErrorActionPreference = 'Continue'

try {
  $config = Get-Content -Raw -Encoding UTF8 $SettingsPath | ConvertFrom-Json
} catch {
  Write-Output 'ntfy-settings-unreadable'
  exit 0
}
if (-not $config.Ntfy -or -not $config.Ntfy.Enabled -or -not $config.Ntfy.Topic) {
  Write-Output 'ntfy-not-configured'
  exit 0
}
if ($config.Ntfy.Server) { $NtfyServer = $config.Ntfy.Server }

# HTTP 헤더는 ASCII 만 담는다. 제목에 한글을 그대로 넣으면 거절된다
# (2026-07-28 실측: "요청에 잘못된 헤더 값이 있습니다"). RFC 2047 로 인코딩해서 보낸다.
# 본문은 UTF-8 파일로 가므로 한글이 그대로 살아 있다.
function ConvertTo-HeaderSafe([string]$text) {
  if ($text -match '^[ -~]*$') { return $text }
  $bytes = [Text.Encoding]::UTF8.GetBytes($text)
  return '=?UTF-8?B?' + [Convert]::ToBase64String($bytes) + '?='
}

# 본문을 UTF-8 파일로 넘긴다. PowerShell 5.1 에서 -Body 에 byte[] 를 주면 본문이 비어 도착한다
# (2026-07-27 실측: 제목만 오고 message 가 빈 문자열이었다).
$bodyFile = [IO.Path]::GetTempFileName()
[IO.File]::WriteAllText($bodyFile, $Body, [Text.UTF8Encoding]::new($false))
try {
  Invoke-RestMethod -Uri "$NtfyServer/$($config.Ntfy.Topic)" -Method Post `
    -Headers @{ Title = (ConvertTo-HeaderSafe $Title); Tags = $Tags; Priority = $Priority } `
    -ContentType 'text/plain; charset=utf-8' -InFile $bodyFile | Out-Null
  Write-Output 'notified'
} catch {
  Write-Output "notify-failed: $($_.Exception.Message)"
} finally {
  Remove-Item $bodyFile -Force -ErrorAction SilentlyContinue
}
exit 0
