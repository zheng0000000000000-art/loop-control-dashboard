# Ollama 설치, 서비스 확인, 모델 준비, definition 모델 자동 갱신을 수행한다.
# 이미 준비된 단계는 건너뛰고 필요한 단계만 실행한다.
param(
  [string]$Endpoint = "http://127.0.0.1:11434"
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$DefinitionPaths = @(
  (Join-Path $RepoRoot "dashboard/data/dev-pack/workflow-definition.json"),
  (Join-Path $RepoRoot "dashboard/data/ruined-lab/workflow-definition.json")
)

# 명령이 설치되어 있는지 확인한다.
function Test-Command {
  param([string]$Name)
  return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

# Ollama 명령을 설치한다.
function Install-OllamaIfMissing {
  if (Test-Command "ollama") {
    ollama --version
    return
  }

  if (-not (Test-Command "winget")) {
    throw "winget을 찾을 수 없다. https://ollama.com/download 에서 Ollama를 수동 설치한다."
  }

  winget install --id Ollama.Ollama --exact --accept-package-agreements --accept-source-agreements
}

# Ollama API가 응답하는지 확인한다.
function Test-OllamaApi {
  try {
    Invoke-RestMethod -Uri "$Endpoint/api/tags" -TimeoutSec 5 | Out-Null
    return $true
  } catch {
    return $false
  }
}

# 로컬 Ollama 서버를 시작한다.
function Start-OllamaIfNeeded {
  if (Test-OllamaApi) {
    Write-Host "Ollama API ready: $Endpoint"
    return
  }

  Start-Process -FilePath "ollama" -ArgumentList "serve" -WindowStyle Hidden
  for ($i = 0; $i -lt 30; $i += 1) {
    if (Test-OllamaApi) {
      Write-Host "Ollama API ready: $Endpoint"
      return
    }
    Start-Sleep -Seconds 1
  }

  throw "Ollama API가 응답하지 않는다: $Endpoint"
}

# 설치된 모델 이름 목록을 가져온다.
function Get-InstalledModelNames {
  $lines = ollama list | Select-Object -Skip 1
  $names = @()
  foreach ($line in $lines) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0) { continue }
    $names += ($trimmed -split '\s+')[0]
  }
  return $names
}

# 검토 모델을 고른다: 설치된 qwen3 계열 우선, 없으면 다른 qwen 계열, 그래도 없으면 qwen2.5:14b-instruct를 받는다.
function Select-ReviewModel {
  param([string[]]$Installed)

  $qwen3 = $Installed | Where-Object { $_ -match '(?i)^qwen3' } | Select-Object -First 1
  if ($qwen3) {
    return $qwen3
  }

  $qwenAny = $Installed | Where-Object { $_ -match '(?i)^qwen' } | Select-Object -First 1
  if ($qwenAny) {
    return $qwenAny
  }

  $pulled = "qwen2.5:14b-instruct"
  Write-Host "설치된 qwen 계열 모델이 없어 $pulled 을(를) 받는다."
  ollama pull $pulled
  return $pulled
}

# 폴백 모델을 고른다: 검토 모델이 아닌 설치된 8b급 아무거나, 없으면 llama3.1:8b를 받는다.
function Select-FallbackModel {
  param([string[]]$Installed, [string]$ReviewModel)

  $eightB = $Installed | Where-Object { $_ -ne $ReviewModel -and $_ -match '(?i)8b' } | Select-Object -First 1
  if ($eightB) {
    return $eightB
  }

  $pulled = "llama3.1:8b"
  Write-Host "설치된 8b급 모델이 없어 $pulled 을(를) 받는다."
  ollama pull $pulled
  return $pulled
}

# 실행자(제안 생성) 모델을 고른다: 검토 모델과 같은 qwen3 계열의 설치된 8b급, 없으면 qwen3:8b를 받는다.
# JSON 스키마 준수 실측상 계열이 다른 8b 모델(예: llama3.1:8b)은 응답 형식이 불안정해 검토 모델과 같은 계열을 우선한다.
function Select-ExecutorModel {
  param([string[]]$Installed, [string]$ReviewModel)

  $qwen3EightB = $Installed | Where-Object { $_ -ne $ReviewModel -and $_ -match '(?i)^qwen3' -and $_ -match '(?i)8b' } | Select-Object -First 1
  if ($qwen3EightB) {
    return $qwen3EightB
  }

  $pulled = "qwen3:8b"
  Write-Host "설치된 qwen3 8b급 모델이 없어 $pulled 을(를) 받는다."
  ollama pull $pulled
  return $pulled
}

# 텍스트 안에서 parentKey 아래 tier1 블록 하나를 찾아 그 범위 안의 필드만 치환한다.
# tier1 블록에는 중첩 객체가 없으므로 여는 중괄호 다음 첫 닫는 중괄호까지가 블록 전체다.
function Set-Tier1Field {
  param([string]$Content, [string]$ParentKey, [string]$FieldName, [string]$Value)

  $parentIdx = $Content.IndexOf('"' + $ParentKey + '"')
  if ($parentIdx -lt 0) { return $Content }

  $tier1Idx = $Content.IndexOf('"tier1"', $parentIdx)
  if ($tier1Idx -lt 0) { return $Content }

  $braceOpen = $Content.IndexOf('{', $tier1Idx)
  $braceClose = $Content.IndexOf('}', $braceOpen)
  if ($braceOpen -lt 0 -or $braceClose -lt 0) { return $Content }

  $block = $Content.Substring($braceOpen, $braceClose - $braceOpen + 1)
  $pattern = '("' + $FieldName + '":\s*)"[^"]*"'
  if ($block -notmatch $pattern) { return $Content }

  $updatedBlock = [regex]::Replace($block, $pattern, ('$1"' + $Value + '"'))
  return $Content.Substring(0, $braceOpen) + $updatedBlock + $Content.Substring($braceClose + 1)
}

# definition 파일의 reviewerPolicy.tier1과 executorPolicy.tier1 모델 값을 갱신한다.
function Update-DefinitionModels {
  param([string]$Path, [string]$ReviewModel, [string]$FallbackModel, [string]$ExecutorModel)

  if (-not (Test-Path $Path)) {
    Write-Host "definition 없음, 건너뜀: $Path"
    return
  }

  $content = Get-Content -Path $Path -Raw -Encoding utf8
  $content = Set-Tier1Field -Content $content -ParentKey "reviewerPolicy" -FieldName "model" -Value $ReviewModel
  $content = Set-Tier1Field -Content $content -ParentKey "reviewerPolicy" -FieldName "fallbackModel" -Value $FallbackModel
  $content = Set-Tier1Field -Content $content -ParentKey "executorPolicy" -FieldName "model" -Value $ExecutorModel
  # Set-Content -Encoding utf8는 Windows PowerShell 5.1에서 BOM을 붙이므로 BOM 없는 UTF-8로 직접 쓴다.
  [System.IO.File]::WriteAllText($Path, $content, (New-Object System.Text.UTF8Encoding($false)))
  Write-Host "definition 갱신: $Path"
}

Install-OllamaIfMissing
Start-OllamaIfNeeded

$installed = Get-InstalledModelNames
$reviewModel = Select-ReviewModel -Installed $installed
$installed = Get-InstalledModelNames
$fallbackModel = Select-FallbackModel -Installed $installed -ReviewModel $reviewModel
$installed = Get-InstalledModelNames
$executorModel = Select-ExecutorModel -Installed $installed -ReviewModel $reviewModel

foreach ($path in $DefinitionPaths) {
  Update-DefinitionModels -Path $path -ReviewModel $reviewModel -FallbackModel $fallbackModel -ExecutorModel $executorModel
}

ollama list
Write-Host "선택된 검토 모델(reviewerPolicy.tier1.model): $reviewModel"
Write-Host "선택된 검토 폴백 모델(reviewerPolicy.tier1.fallbackModel): $fallbackModel"
Write-Host "선택된 실행자 모델(executorPolicy.tier1.model): $executorModel"
