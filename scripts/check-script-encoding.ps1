#Requires -Version 5.1
# 한글이 든 .ps1 에 UTF-8 BOM 이 있는지 검사한다. 없으면 exit 1.
#
# 왜 있는가: PowerShell 5.1 은 BOM 없는 .ps1 을 ANSI 로 읽는다. 한글이 깨지고,
# 깨진 바이트가 없는 중괄호로 파싱돼 "예상되지 않은 '}'" 가 엉뚱한 줄에서 난다.
# 2026-07-27 하루에 세 번 밟았다. 세 번째는 편집 도구가 BOM 을 떼어냈고 나는 문법을 의심했다.
# 사람이 기억할 일이 아니다. 검사가 잡을 일이다.

param(
  [string]$Root = (Split-Path -Parent $PSScriptRoot),
  [switch]$Fix
)

$ErrorActionPreference = 'Stop'
$skipDirs = @('bin', 'obj', 'node_modules', '.git', 'history')
$bad = @()

# 한글이 없는 스크립트는 BOM 이 없어도 안 깨진다. 있는 것만 본다.
foreach ($file in Get-ChildItem -Path $Root -Filter '*.ps1' -Recurse -File) {
  $segments = $file.FullName.Split([char]92)
  $skip = $false
  foreach ($segment in $segments) { if ($skipDirs -contains $segment) { $skip = $true; break } }
  if ($skip) { continue }

  $bytes = [IO.File]::ReadAllBytes($file.FullName)
  if ($bytes.Length -lt 3) { continue }
  if ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { continue }

  # ASCII 밖 바이트가 하나라도 있으면 BOM 이 필요하다.
  $needsBom = $false
  foreach ($b in $bytes) { if ($b -gt 0x7F) { $needsBom = $true; break } }
  if (-not $needsBom) { continue }

  $bad += $file.FullName
  if ($Fix) { [IO.File]::WriteAllBytes($file.FullName, ([byte[]]@(0xEF, 0xBB, 0xBF) + $bytes)) }
}

if ($bad.Count -eq 0) { Write-Output 'ps1-bom ok'; exit 0 }

if ($Fix) {
  Write-Output "ps1-bom fixed=$($bad.Count)"
  foreach ($f in $bad) { Write-Output "  + $f" }
  exit 0
}

Write-Output "ps1-bom missing=$($bad.Count)"
foreach ($f in $bad) { Write-Output "  ! $f" }
Write-Output '고치려면: powershell -File scripts/check-script-encoding.ps1 -Fix'
exit 1
