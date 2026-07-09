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

# definition 파일의 tier1 model/fallbackModel 값을 텍스트 치환으로 갱신한다.
function Update-DefinitionModels {
  param([string]$Path, [string]$Model, [string]$FallbackModel)

  if (-not (Test-Path $Path)) {
    Write-Host "definition 없음, 건너뜀: $Path"
    return
  }

  $content = Get-Content -Path $Path -Raw -Encoding utf8
  $content = $content -replace '("model":\s*)"[^"]*"', "`$1""$Model"""
  $content = $content -replace '("fallbackModel":\s*)"[^"]*"', "`$1""$FallbackModel"""
  Set-Content -Path $Path -Value $content -NoNewline -Encoding utf8
  Write-Host "definition 갱신: $Path"
}

Install-OllamaIfMissing
Start-OllamaIfNeeded

$installed = Get-InstalledModelNames
$reviewModel = Select-ReviewModel -Installed $installed
$installed = Get-InstalledModelNames
$fallbackModel = Select-FallbackModel -Installed $installed -ReviewModel $reviewModel

foreach ($path in $DefinitionPaths) {
  Update-DefinitionModels -Path $path -Model $reviewModel -FallbackModel $fallbackModel
}

ollama list
Write-Host "선택된 검토 모델(tier1 model): $reviewModel"
Write-Host "선택된 폴백 모델(tier1 fallbackModel): $fallbackModel"
