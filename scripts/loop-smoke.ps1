#Requires -Version 5.1
# 깨우기가 주요 갈래에서 기대한 모드로 가는지 모의 실행으로 확인한다. 하나라도 어긋나면 exit 1.
#
# 왜 있는가: 루프가 자기 코드를 고치게 하려면 그 전에 안전망이 있어야 한다. 망가뜨리면
# 되살릴 주체가 그 망가진 코드다. 문법 검사와 BOM 검사는 통과해도 논리는 깨질 수 있다 -
# 2026-07-29 실측: nothing-to-do 분기가 대입 전 변수를 써서 한 번도 안 걸렸는데 파싱은 통과했다.
#
# 그래서 실제로 돌려서 판정한다. 판정은 출력 문자열이 아니라 would-wake 의 mode 와 종료 코드다.
param(
  [string]$WakeScript = (Join-Path (Split-Path -Parent $PSCommandPath) 'coordinator-wake.ps1'),
  [string]$TeamLoopRoot = 'C:\NHN Project\team-loop-lite-ai-learning',
  [switch]$KeepFixtures
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $WakeScript)) { Write-Output "smoke-no-wake-script $WakeScript"; exit 2 }

$root = Join-Path $env:TEMP ('loopsmoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $root | Out-Null

# 실제 보드를 본떠 만든다. 지어낸 객체로 재면 그 코드를 안 잰 것이다 - 실제 스키마의
# 필드가 빠지면 스크립트가 다른 갈래로 샌다(2026-07-29 실측: archived 를 빼먹어 헛돌았다).
function New-BoardFixture([string]$path, [string]$mode) {
  $source = Join-Path $TeamLoopRoot 'data\tasks.json'
  if (-not (Test-Path $source)) { throw "실제 보드가 없다: $source" }
  $doc = Get-Content -Raw -Encoding UTF8 $source | ConvertFrom-Json
  $tasks = @(if ($doc.tasks) { $doc.tasks } else { $doc })
  foreach ($t in $tasks) { $t.status = 'DONE'; $t | Add-Member -NotePropertyName archived -NotePropertyValue $true -Force }
  if ($mode -ne 'dry') {
    $last = $tasks[$tasks.Count - 1]
    $last | Add-Member -NotePropertyName archived -NotePropertyValue $false -Force
    switch ($mode) {
      'busy'   { $last.status = 'IN_PROGRESS' }
      'review' { $last.status = 'REVIEW' }
      'ready'  { $last.status = 'READY' }
    }
  }
  [IO.File]::WriteAllText($path, (@{ tasks = $tasks } | ConvertTo-Json -Depth 40), [Text.UTF8Encoding]::new($false))
}

try {
  [IO.File]::WriteAllText((Join-Path $root 'disc.json'), (@{ schemaVersion = 1; messages = @(); memories = @() } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
  Set-Content -Path (Join-Path $root 'queue.md') -Encoding UTF8 -Value '# 빈 큐'
  Set-Content -Path (Join-Path $root 'inbox.md') -Encoding UTF8 -Value '# 빈 인계함'
  # 인박스에 미처리 항목이 하나 있는 판. 사람이 처리해야 줄어드는 종류다.
  Set-Content -Path (Join-Path $root 'inbox-pending.md') -Encoding UTF8 -Value "# 인계함`n- [ ] 사람이 볼 것"
  [IO.File]::WriteAllText((Join-Path $root 'backlog-empty.json'), (@{ schemaVersion = 1; items = @() } | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
  foreach ($m in @('dry','busy','review','ready')) { New-BoardFixture (Join-Path $root "board-$m.json") $m }

  # 갈래마다 기대하는 모드. 이것이 이 하네스의 계약이다.
  $cases = @(
    @{ name = '보드 마름 + 백로그 빔';  board = 'dry';    expect = 'plan';    plan = 9 },
    @{ name = '진행 중 있음';           board = 'busy';   expect = 'execute'; plan = 9 },
    @{ name = '판정 대기 있음';         board = 'review'; expect = 'review';  plan = 9 },
    @{ name = '집을 것 있음';           board = 'ready';  expect = 'execute'; plan = 9 },
    @{ name = '계획 한도 0 이면 안 뜬다'; board = 'dry';   expect = 'none';    plan = 0 },
    # 인박스 표식이 억제돼 있어도 보드 일은 굶으면 안 된다.
    # 2026-07-30 실측: inbox:1 표식 하나에 8회 연속 아무것도 안 했다(보드 READY 2 인데도).
    @{ name = '인박스 억제 중에도 보드는 돈다'; board = 'ready'; expect = 'execute'; plan = 9;
       inbox = 'inbox-pending.md'; preMarker = @{ kind = 'inbox'; value = 'inbox:1' } },
    # 음성: 보드에 일이 없으면 인박스 억제가 실제로 막아야 한다.
    @{ name = '인박스 억제 + 보드 빔 = 안 뜬다'; board = 'dry'; expect = 'none'; plan = 0;
       inbox = 'inbox-pending.md'; preMarker = @{ kind = 'inbox'; value = 'inbox:1' } }
  )

  $failed = 0
  foreach ($c in $cases) {
    $ledger = Join-Path $root ('plan-' + $c.board + '-' + $c.plan + '-' + $c.name.Length + '.json')
    if (Test-Path $ledger) { Remove-Item -Force $ledger }
    $inboxFile = Join-Path $root $(if ($c.inbox) { $c.inbox } else { 'inbox.md' })
    # 표식은 종류별 파일이다. 미리 깔아두면 그 종류만 억제된다.
    Get-ChildItem $root -Filter 'hb.json.woke.*' -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem $root -Filter 'hb.json.attempts.*' -ErrorAction SilentlyContinue | Remove-Item -Force
    if ($c.preMarker) {
      Set-Content -Path (Join-Path $root ('hb.json.woke.' + $c.preMarker.kind)) -Encoding UTF8 -Value $c.preMarker.value
    }
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $WakeScript -DryRun `
      -DiscussionPath (Join-Path $root 'disc.json') `
      -BoardPath (Join-Path $root ("board-" + $c.board + ".json")) `
      -QueuePath (Join-Path $root 'queue.md') `
      -InboxPath $inboxFile `
      -HeartbeatPath (Join-Path $root 'hb.json') `
      -HoldPath (Join-Path $root 'nohold.flag') `
      -LogDir (Join-Path $root 'logs') `
      -BacklogPath (Join-Path $root 'backlog-empty.json') `
      -PlanLedgerPath $ledger `
      -PlanMaxPerDay $c.plan `
      -TeamLoopRoot $TeamLoopRoot 2>&1
    $code = $LASTEXITCODE
    $mode = 'none'
    foreach ($line in @($out)) {
      $text = "$line"
      if ($text -match 'would-wake .*mode=([a-z]+)') { $mode = $matches[1] }
    }
    # 계약: 깨울 것이 있으면 exit 1(모의 실행이라 안 깨우고 알리기만 한다), 없으면 0.
    # 이 계약을 하네스가 잘못 알아 처음에 전부 FAIL 로 읽었다(2026-07-29).
    $expectedCode = if ($c.expect -eq 'none') { 0 } else { 1 }
    $ok = ($mode -eq $c.expect) -and ($code -eq $expectedCode)
    "{0,-24} 기대={1,-8} 실제={2,-8} exit={3}/{4} {5}" -f $c.name, $c.expect, $mode, $code, $expectedCode, $(if ($ok) { 'ok' } else { 'FAIL' })
    if (-not $ok) {
      $failed += 1
      foreach ($line in @($out) | Select-Object -Last 4) { "    $line" }
    }
  }

  if ($failed -gt 0) { Write-Output "loop-smoke $failed 건 실패"; exit 1 }
  Write-Output 'loop-smoke ok'
  exit 0
} finally {
  if (-not $KeepFixtures) { Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue }
}
