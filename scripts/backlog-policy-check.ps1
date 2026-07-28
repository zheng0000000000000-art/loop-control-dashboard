#Requires -Version 5.1
# 백로그의 priority 가 tier 와 factors 에서 계산한 값과 같은지 검사한다. 어긋나면 exit 1.
#
# 왜 있는가: 우선순위를 손으로 정하면 그 이유가 사람 머리에만 남는다. 다음 세션은 숫자만 보고
# 왜 그런지 모른 채 따르거나 마음대로 바꾼다. 산문으로 근거를 적어도 안 읽는다.
# 그래서 사실(tier·factors)만 적게 하고 숫자는 계산한다. 계산이 안 맞으면 커밋을 막는다.
#
# 값의 정본은 이 파일이다. docs/plan/BACKLOG-POLICY.md 의 표는 사람을 위한 설명이다.
param(
  [string]$BacklogPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'docs\plan\BACKLOG.json'),
  [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

# 로드맵 층. INTENT-DIGEST 2절의 합의를 숫자로 옮긴 것이다.
$TierBase = @{ 'P0' = 20; 'P1' = 14; 'P2' = 8; 'P3' = 2 }
# 가중치. 바꾸려면 BASELINE-CHANGES 에 남긴다 - 이건 판단이지 사실이 아니다.
$FactorWeight = @{ 'measuredFailure' = 6; 'blocksOthers' = 4; 'removesManualToil' = 4; 'unverifiedRisk' = 2; 'speculative' = -4 }

# 한 항목의 기대 우선순위. tier 나 factor 이름이 낯설면 계산하지 않고 위반으로 돌린다 -
# 모르는 이름을 0 으로 치면 오타 하나가 조용히 우선순위를 낮춘다.
function Get-ExpectedPriority($item, [ref]$problem) {
  $tier = [string]$item.tier
  if (-not $TierBase.ContainsKey($tier)) { $problem.Value = "tier 를 모른다: '$tier'"; return $null }
  $sum = [int]$TierBase[$tier]
  foreach ($f in @($item.factors)) {
    $name = [string]$f
    if (-not $FactorWeight.ContainsKey($name)) { $problem.Value = "factor 를 모른다: '$name'"; return $null }
    $sum += [int]$FactorWeight[$name]
  }
  return $sum
}

function Invoke-Check([string]$path) {
  $violations = @()
  if (-not (Test-Path $path)) { return @("backlog-missing $path") }
  try { $backlog = Get-Content -Raw -Encoding UTF8 $path | ConvertFrom-Json }
  catch { return @("backlog-unreadable $($_.Exception.Message)") }
  if (-not $backlog.items) { return @('backlog-has-no-items') }

  foreach ($item in $backlog.items) {
    $id = [string]$item.id
    if ($null -eq $item.tier) { $violations += "$id : tier 가 없다"; continue }
    if ($null -eq $item.priority) { $violations += "$id : priority 가 없다"; continue }
    $problem = ''
    $expected = Get-ExpectedPriority $item ([ref]$problem)
    if ($null -eq $expected) { $violations += "$id : $problem"; continue }
    if ([int]$item.priority -ne $expected) {
      $violations += "$id : priority=$($item.priority) 인데 tier=$($item.tier) factors=[$(@($item.factors) -join ',')] 로는 $expected 다"
    }
  }
  return $violations
}

if ($SelfTest) {
  $tmp = Join-Path $env:TEMP ('policyself-' + [guid]::NewGuid().ToString('N') + '.json')
  try {
    # 음성: 계산과 맞는 것 - 위반 0 이어야 한다
    $good = @{ items = @(
      @{ id = 'a'; tier = 'P0'; factors = @('measuredFailure', 'blocksOthers'); priority = 30 },
      @{ id = 'b'; tier = 'P1'; factors = @('unverifiedRisk'); priority = 16 },
      @{ id = 'c'; tier = 'P2'; factors = @(); priority = 8 }
    ) }
    [IO.File]::WriteAllText($tmp, ($good | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    $goodHits = @(Invoke-Check $tmp)

    # 양성: 숫자가 어긋난 것, tier 오타, factor 오타 - 셋 다 잡아야 한다
    $bad = @{ items = @(
      @{ id = 'x'; tier = 'P0'; factors = @('measuredFailure'); priority = 99 },
      @{ id = 'y'; tier = 'P9'; factors = @(); priority = 1 },
      @{ id = 'z'; tier = 'P0'; factors = @('measuredFailur'); priority = 26 }
    ) }
    [IO.File]::WriteAllText($tmp, ($bad | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    $badHits = @(Invoke-Check $tmp)

    "self-test 음성 위반 = $($goodHits.Count) (기대 0)"
    "self-test 양성 위반 = $($badHits.Count) (기대 3)"
    if ($goodHits.Count -ne 0 -or $badHits.Count -ne 3) {
      $goodHits + $badHits | ForEach-Object { "  $_" }
      Write-Output 'backlog-policy self-test FAIL'
      exit 1
    }
    Write-Output 'backlog-policy self-test ok'
    exit 0
  } finally {
    Remove-Item -Force $tmp -ErrorAction SilentlyContinue
  }
}

$violations = Invoke-Check $BacklogPath
if ($violations.Count -gt 0) {
  $violations | ForEach-Object { Write-Output $_ }
  Write-Output "backlog-policy $($violations.Count) 건"
  exit 1
}
Write-Output 'backlog-policy ok'
exit 0
