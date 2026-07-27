#Requires -Version 5.1
# 루프가 무엇을 시작하고 무엇으로 끝냈는지를 사람이 보는 자리에 남긴다.
#
# 왜 있는가: 2026-07-28 - 사람이 "작업을 안 한다니까?"라고 물었는데 실제로는 세션이 돌고 있었다.
# 도는 증거(decisions.log, 잠금 파일, 워크트리)가 전부 로컬 파일이라 폰에서 안 보였다.
# 큐 항목으로 도는 바퀴는 특히 아무 데도 안 나타났다. 안 보이면 안 돈 것과 같다.
#
# 세션에게 "남겨라"고 시키지 않는다. 세션이 잊거나 죽으면 그만이다. 깨우기가 직접 쓴다.
# 그래서 [루프] 접두사를 붙인다 - 기계가 쓴 줄과 모델이 쓴 줄은 신뢰도가 다르고,
# 읽는 사람이 그 둘을 구분할 수 있어야 한다.
param(
  [Parameter(Mandatory = $true)][string]$Text,
  [string]$DiscussionPath = 'C:\NHN Project\team-loop-lite-ai-learning\data\discussions.json',
  [string]$AuthorId = 'usr_claude_coordinator'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $DiscussionPath)) { exit 0 }

try {
  $raw = Get-Content -Raw -Encoding UTF8 $DiscussionPath
  $doc = $raw | ConvertFrom-Json
  $stamp = [datetimeoffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
  $suffix = [guid]::NewGuid().ToString('N').Substring(0, 20)
  $entry = [pscustomobject]@{
    id = "msg_$suffix"
    authorUserId = $AuthorId
    createdAt = $stamp
    content = "[루프] $Text"
  }
  $doc.messages = @($doc.messages) + $entry
  $json = $doc | ConvertTo-Json -Depth 40
  $tmp = "$DiscussionPath.tmp"
  [IO.File]::WriteAllText($tmp, $json, [Text.UTF8Encoding]::new($false))
  Move-Item -Force $tmp $DiscussionPath
} catch {
  # 기록에 실패해도 루프를 죽이지 않는다. 알림이 안 갔다고 일한 것을 잃으면 안 된다.
  exit 0
}
exit 0
