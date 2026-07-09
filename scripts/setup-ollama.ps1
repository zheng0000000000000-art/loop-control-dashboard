# Ollama 설치, 서비스 확인, 모델 준비를 수행한다.
# 이미 준비된 단계는 건너뛰고 필요한 단계만 실행한다.
param(
  [string[]]$Models = @("qwen2.5:14b-instruct", "llama3.1:8b"),
  [string]$Endpoint = "http://localhost:11434"
)

$ErrorActionPreference = "Stop"

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

# 필요한 모델을 내려받는다.
function Pull-Models {
  $list = ollama list
  foreach ($model in $Models) {
    if ($list -match [regex]::Escape($model)) {
      Write-Host "Model already present: $model"
      continue
    }

    ollama pull $model
  }
}

Install-OllamaIfMissing
Start-OllamaIfNeeded
Pull-Models
ollama list
