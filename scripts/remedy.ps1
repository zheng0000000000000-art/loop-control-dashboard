#Requires -Version 5.1
# 선언된 실패 모양에 선언된 처치를 적용한다. 표에 없으면 아무것도 하지 않는다.
#
# 왜 있는가: 루프가 실패에 대해 할 줄 아는 것이 보고뿐이었다. 2026-07-28 실측 - land-failed,
# chain-limit, no-progress 전부 인계함에 적고 폰으로 알리고 끝이었다. 감지기와 처치 스크립트가
# 둘 다 있는데 아무도 연결하지 않았다. 그래서 사람 개입 횟수가 처리량을 정했다.
#
# 지어내지 않는다: remedies.json 에 선언된 모양만 처치한다. 모르는 모양은 그대로 사람에게 올린다.
# 처치를 추론하게 두면 증상만 지우고 원인을 덮는다.
#
# 종료 코드로 말한다. 0=처치했으니 다시 해봐라 · 2=모르는 모양 · 3=자동 처치 없음(사람 몫)
# · 4=처치 한도 초과(처치가 안 듣는다) · 5=처치를 돌렸는데 실패
param(
  [Parameter(Mandatory = $true)]
  [string]$Shape,
  # 처치 대상. 태스크 id 처럼 모양마다 뜻이 다르다. 장부에 같이 남는다.
  [string]$Subject = '',
  [string]$RemediesPath = '',
  [string]$LedgerPath = 'C:\NHN Project\_ops\remedy-ledger.json',
  # 처치 하나가 이 시간을 넘으면 끊는다.
  [int]$ActionTimeoutSeconds = 120,
  [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
# $PSScriptRoot 는 param 기본값 자리에서 비어 있다(PS 5.1 실측). 이 저장소의 다른
# 스크립트들과 같이 $PSCommandPath 로 잡는다.
$scriptDir = Split-Path -Parent $PSCommandPath
if (-not $RemediesPath) { $RemediesPath = Join-Path $scriptDir 'remedies.json' }

# 하루 안에 같은 모양(같은 대상)을 몇 번 처치했는지. 처치가 반복되면 처치가 안 듣는 것이다.
function Get-RecentCount([string]$path, [string]$key) {
  if (-not (Test-Path $path)) { return 0 }
  try { $ledger = Get-Content -Raw -Encoding UTF8 $path | ConvertFrom-Json } catch { return 0 }
  $entry = $ledger.$key
  if (-not $entry) { return 0 }
  $cutoff = (Get-Date).ToUniversalTime().AddHours(-24)
  $count = 0
  foreach ($stamp in @($entry)) {
    $parsed = [datetime]::MinValue
    if ([datetime]::TryParse([string]$stamp, [ref]$parsed)) {
      if ($parsed.ToUniversalTime() -gt $cutoff) { $count++ }
    }
  }
  return $count
}

function Add-Attempt([string]$path, [string]$key) {
  $ledger = $null
  if (Test-Path $path) { try { $ledger = Get-Content -Raw -Encoding UTF8 $path | ConvertFrom-Json } catch { } }
  if (-not $ledger) { $ledger = New-Object psobject }
  $existing = @()
  if ($ledger.$key) { $existing = @($ledger.$key) }
  $existing += (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
  $ledger | Add-Member -NotePropertyName $key -NotePropertyValue $existing -Force
  New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
  [IO.File]::WriteAllText($path, ($ledger | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
}

function Invoke-Remedy([string]$shape, [string]$subject, [string]$remediesPath, [string]$ledgerPath) {
  if (-not (Test-Path $remediesPath)) { return @{ Code = 2; Text = "remedy-table-missing $remediesPath" } }
  try { $table = Get-Content -Raw -Encoding UTF8 $remediesPath | ConvertFrom-Json }
  catch { return @{ Code = 2; Text = "remedy-table-unreadable $($_.Exception.Message)" } }

  $entry = @($table.shapes | Where-Object { $_.id -eq $shape })[0]
  # 모르는 모양은 처치하지 않는다. 비슷한 것을 골라 쓰지도 않는다.
  if (-not $entry) { return @{ Code = 2; Text = "remedy-unknown-shape $shape" } }

  if (-not $entry.action) {
    return @{ Code = 3; Text = "remedy-none $shape : $($entry.escalation)" }
  }

  $key = if ($subject) { "$shape/$subject" } else { $shape }
  $used = Get-RecentCount $ledgerPath $key
  if ($used -ge [int]$entry.maxPerDay) {
    return @{ Code = 4; Text = "remedy-exhausted $key ($used/$($entry.maxPerDay) 회, 24시간) : $($entry.escalation)" }
  }

  $script = Join-Path (Split-Path -Parent $PSCommandPath) ([string]$entry.action)
  if (-not (Test-Path $script)) { return @{ Code = 5; Text = "remedy-action-missing $($entry.action)" } }

  $callArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script)
  foreach ($a in @($entry.args)) { if ($a) { $callArgs += [string]$a } }
  if ($subject) { $callArgs += @('-Subject', $subject) }
  # 출력을 파이프로 받지 않는다. 처치가 서버 같은 오래 사는 프로세스를 띄우면 그 손자가
  # 파이프 손잡이를 물고 있어 캡처가 끝나지 않는다 - 2026-07-28 실측: remedy 를 거쳐 부르면
  # 재시작이 멈춰 있었고 직접 부르면 즉시 끝났다. 파일로 받으면 손잡이가 얽히지 않는다.
  $outFile = Join-Path $env:TEMP ('remedy-' + [guid]::NewGuid().ToString('N') + '.txt')
  $errFile = "$outFile.err"
  # Start-Process 는 인자를 문자열로 이어 붙인다. 공백이 든 경로를 그대로 넘기면 쪼개진다 -
  # 2026-07-28 실측: -File 'C:\NHN' 까지만 넘어가 처치가 실패했다.
  $quotedArgs = @()
  foreach ($a in $callArgs) {
    $text = [string]$a
    if ($text -match '\s') { $quotedArgs += ([char]34 + $text + [char]34) } else { $quotedArgs += $text }
  }
  # -Wait 를 쓰지 않는다. Start-Process 의 -Wait 는 자손까지 기다려서, 서버 같은 데몬을
  # 띄우는 처치에서는 영영 안 끝난다 - 2026-07-28 실측: 재시작 처치가 180초 시간 제한에
  # 걸렸는데 서버는 이미 떠 있었다. 직접 자식만 기다린다.
  $proc = Start-Process -FilePath 'powershell' -ArgumentList $quotedArgs -PassThru -NoNewWindow -RedirectStandardOutput $outFile -RedirectStandardError $errFile
  # 핸들을 먼저 잡아 둔다. -PassThru 로 받은 객체는 이걸 안 하면 ExitCode 가 비어 있다 -
  # 2026-07-28 실측: 재시작이 성공했는데 exit= 가 비어 remedy-failed 로 읽혔다.
  $null = $proc.Handle
  # 처치가 안 끝나면 끊는다. 처치가 루프를 멈추면 그건 처치가 아니라 새 장애다.
  if (-not $proc.WaitForExit($ActionTimeoutSeconds * 1000)) {
    try { Stop-Process -Id $proc.Id -Force -ErrorAction Stop } catch { }
    Add-Attempt $ledgerPath $key
    return @{ Code = 5; Text = ('remedy-timeout ' + $shape + ' (' + $ActionTimeoutSeconds + '초)') }
  }
  $code = $proc.ExitCode
  $out = @()
  foreach ($f in @($outFile, $errFile)) {
    if (Test-Path $f) {
      $out += @(Get-Content -Encoding UTF8 $f | Where-Object { $_ -and $_.Trim() })
      Remove-Item -Force $f -ErrorAction SilentlyContinue
    }
  }
  Add-Attempt $ledgerPath $key
  if ($code -ne 0) {
    return @{ Code = 5; Text = "remedy-failed $shape (exit=$code) : $(($out | Select-Object -Last 1))" }
  }
  return @{ Code = 0; Text = "remedy-applied $shape ($($entry.action)) : $(($out | Select-Object -Last 1))" }
}

if ($SelfTest) {
  $tmp = Join-Path $env:TEMP ('remedyself-' + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  try {
    $table = Join-Path $tmp 'remedies.json'
    $ledger = Join-Path $tmp 'ledger.json'
    $fixture = @{ shapes = @(
      @{ id = 'auto-ok'; action = 'restart-teamloop-server.ps1'; args = @(); maxPerDay = 2; escalation = 'e1' },
      @{ id = 'declare-only'; action = $null; maxPerDay = 0; escalation = '사람이 로그인해야 한다' }
    ) }
    [IO.File]::WriteAllText($table, ($fixture | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))

    $r1 = Invoke-Remedy 'nope' '' $table $ledger
    "모르는 모양 -> $($r1.Code) (기대 2)"
    $r2 = Invoke-Remedy 'declare-only' '' $table $ledger
    "자동 처치 없음 -> $($r2.Code) (기대 3) : $($r2.Text)"
    # 한도 초과: 장부를 미리 채운다
    Add-Attempt $ledger 'auto-ok'
    Add-Attempt $ledger 'auto-ok'
    $r3 = Invoke-Remedy 'auto-ok' '' $table $ledger
    "한도 초과 -> $($r3.Code) (기대 4)"
    $r4 = Invoke-Remedy 'auto-ok' '' 'C:\없는표.json' $ledger
    "표 없음 -> $($r4.Code) (기대 2)"

    if ($r1.Code -ne 2 -or $r2.Code -ne 3 -or $r3.Code -ne 4 -or $r4.Code -ne 2) {
      Write-Output 'remedy self-test FAIL'
      exit 1
    }
    Write-Output 'remedy self-test ok'
    exit 0
  } finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
  }
}

$result = Invoke-Remedy $Shape $Subject $RemediesPath $LedgerPath
Write-Output $result.Text
exit $result.Code
