# 고정 탐침(PROBE-00)을 N회 연속 실행해 컨텍스트 비용의 기준선을 만든다.
# 왜: 다이어트 전/후를 1회씩만 비교하면 캐시 상태 차이를 개선으로 오인한다. 반복해서 분산을 본다.
# 사용: powershell -NoProfile -File run-probe.ps1 -Runs 3 -Label before
param(
  [int]$Runs = 3,
  [string]$Label = 'unlabeled'
)

$root   = 'C:\Users\1\Documents\Local-First Workflow Dashboard'
$runner = Join-Path $root 'outputs\launch\run-executor.ps1'
$marker = Join-Path $root 'outputs\launch\probe-runs.jsonl'

for ($i = 1; $i -le $Runs; $i++) {
  & $runner -TaskId 'PROBE-00'
  # 방금 회차의 usage를 라벨과 함께 따로 적는다(다이어트 전/후를 구분하기 위함).
  $s = Get-Content (Join-Path $root 'outputs\launch\PROBE-00.exit.json') -Raw -Encoding UTF8 | ConvertFrom-Json
  [ordered]@{
    label = $Label; run = $i; at = (Get-Date).ToString('o')
    exitCode = $s.exitCode
    inputTokens = $s.usage.inputTokens
    outputTokens = $s.usage.outputTokens
    cacheCreationTokens = $s.usage.cacheCreationTokens
    cacheReadTokens = $s.usage.cacheReadTokens
    totalTokens = ($s.usage.inputTokens + $s.usage.outputTokens + $s.usage.cacheCreationTokens + $s.usage.cacheReadTokens)
    totalCostUsd = $s.usage.totalCostUsd
    numTurns = $s.usage.numTurns
    durationMs = $s.usage.durationMs
  } | ConvertTo-Json -Compress | Add-Content -Path $marker -Encoding UTF8
  Start-Sleep -Seconds 3
}
