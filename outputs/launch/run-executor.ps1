# 실행자를 발사하고 종료를 기다린 뒤 완료 신호(sentinel)를 남긴다.
# 왜: 조율자가 5분마다 무조건 LLM 세션을 태우는 대신, 실행자가 끝났을 때만 검수하게 하기 위함(할당량 절감).
# 사용: powershell -NoProfile -File run-executor.ps1 -TaskId LEDGER-04
# 프롬프트는 파일로 전달한다 — 인자 경계에서 잘리는 사고(FAIL-2026-013)를 구조적으로 없앤다.
param(
  [Parameter(Mandatory = $true)][string]$TaskId
)

$root      = 'C:\Users\1\Documents\Local-First Workflow Dashboard'
$launchDir = Join-Path $root 'outputs\launch'
$promptFile= Join-Path $launchDir "$TaskId.prompt.txt"
$outLog    = Join-Path $root "outputs\sonnet-$TaskId.out.log"
$errLog    = Join-Path $root "outputs\sonnet-$TaskId.err.log"
$sentinel  = Join-Path $launchDir "$TaskId.exit.json"

if (-not (Test-Path $promptFile)) { throw "프롬프트 파일이 없다: $promptFile" }

# 프롬프트를 파일에서 읽어 한 줄 인자로 만든다(줄바꿈은 공백으로 접는다).
$prompt  = (Get-Content $promptFile -Raw -Encoding UTF8) -replace "`r?`n", ' '
$argline = '-p "' + $prompt.Replace('"', '\"') + '" --dangerously-skip-permissions'

# 이전 회차의 신호가 남아 있으면 지운다(낡은 신호로 조율자가 오판하는 것을 막는다).
if (Test-Path $sentinel) { Remove-Item $sentinel -Force }

$started = (Get-Date).ToString('o')
$proc = Start-Process claude.exe -ArgumentList $argline `
          -RedirectStandardOutput $outLog -RedirectStandardError $errLog `
          -PassThru -WorkingDirectory $root

# 핸들을 미리 캐시한다 — 이걸 안 하면 종료 후 ExitCode가 null이 된다(.NET 동작).
# 조율자가 exitCode로 커밋 여부를 판단하므로 null이면 오판한다. 실제로 LEDGER-04에서 null이 나왔다.
$null = $proc.Handle

# PID를 즉시 남긴다 — 대기 중 세션이 죽어도 사람이 추적할 수 있게.
$proc.Id | Out-File (Join-Path $root 'outputs\sonnet-active.pid') -Encoding ascii

$proc.WaitForExit()
$exitCode = $proc.ExitCode
# 그래도 null이면 -1로 적는다. 모르는 것을 0(성공)으로 적지 않는다.
if ($null -eq $exitCode) { $exitCode = -1 }

# 완료 신호. 조율자는 이 파일이 생겼을 때만 검수한다.
$payload = [ordered]@{
  schemaVersion = 1
  taskId        = $TaskId
  pid           = $proc.Id
  exitCode      = $exitCode
  startedAt     = $started
  exitedAt      = (Get-Date).ToString('o')
  outLog        = $outLog
  processed     = $false          # 조율자가 검수 후 true로 바꾼다
  argLength     = $argline.Length # 프롬프트가 잘리지 않고 도착했는지 확인용
}
$payload | ConvertTo-Json | Set-Content $sentinel -Encoding UTF8
