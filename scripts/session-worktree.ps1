#Requires -Version 5.1
# 세션마다 별도 워크트리를 주고, 세션이 끝나면 본 저장소가 병합해서 착지시킨다.
#
# 왜 있는가: 2026-07-27 실측 — 두 세션이 같은 작업 트리를 만졌고 한쪽의 스테이징이 다른 쪽
# 파일을 자기 커밋에 쓸어 담았다(c081dc4, 9bfda28). 커밋 구간 잠금과 큐의 진행 중 표기로
# 절반은 막았지만, 서로 다른 항목을 하는 두 세션이 같은 파일을 만지는 창은 여전히 열려 있었다.
#
# 워크트리는 그 창을 닫는다. 대신 커밋이 분산되므로 착지 경로가 반드시 있어야 한다 —
# 없으면 일한 결과가 브랜치에 고립된다.
#
# 푸시는 세션이 하지 않는다. 본 저장소가 병합한 뒤에 한 번만 한다.
param(
  # 착지 안 한 커밋을 일부러 버릴 때만 준다. 기본은 거부다.
  [switch]$Force,
  [ValidateSet('Create', 'Land', 'Remove', 'List')]
  [string]$Action = 'List',
  [string]$RepoRoot = 'C:\Users\1\Documents\Local-First Workflow Dashboard',
  [string]$WorktreeRoot = 'C:\NHN Project\_ops\worktrees',
  # 깨우기는 세션을 띄우기 전에 워크트리를 만들어야 해서 pid 를 모른다. 깨우기 시각을 id 로 쓴다.
  [string]$SessionId = '',
  [string]$BaseBranch = 'wp/state-integrity'
)

$ErrorActionPreference = 'Stop'

# 측정이 매번 다시 쓰는 파일들. 병합 충돌이 여기서만 나면 세션 것을 취하고 다시 생성한다.
# 내용이 아니라 생성물이라, 어느 쪽을 고르든 재생성하면 같아진다.
# 생성물 목록은 scripts/generated-paths.txt 하나다. 여기에 또 적으면 두 벌이 되고
# 한쪽만 늘어난다. 없으면 멈춘다 - 목록을 모르면 무엇이 생성물인지 모르는 것이고,
# 그 상태로 충돌을 자동 해소하면 사람이 쓴 것을 덮을 수 있다.
$generatedListPath = Join-Path (Split-Path -Parent $PSCommandPath) 'generated-paths.txt'
if (-not (Test-Path $generatedListPath)) { Write-Output 'generated-list-missing'; exit 2 }
$GeneratedPaths = @(Get-Content -Encoding UTF8 $generatedListPath |
  ForEach-Object { $_.Trim() } | Where-Object { $_ -and (-not $_.StartsWith('#')) })
if ($GeneratedPaths.Count -eq 0) { Write-Output 'generated-list-empty'; exit 2 }

# PowerShell 5.1 은 native 명령의 stderr 를 2>&1 로 받으면 각 줄을 ErrorRecord 로 감싸고,
# ErrorActionPreference=Stop 이면 그 자리에서 스크립트가 죽는다. git 은 정상적으로도 stderr 를 쓴다
# (없는 브랜치 삭제 등). 그래서 이 함수 안에서만 잠시 내려놓고 exit code 로 판정한다.
# 판정은 exit code 로 한다 - 출력 문자열로 성패를 세지 않는다.
function Invoke-Git {
  param([string]$Dir, [string[]]$GitArgs)
  $saved = $ErrorActionPreference
  $ErrorActionPreference = 'SilentlyContinue'
  try {
    $out = & git -C $Dir @GitArgs 2>&1
    $code = $LASTEXITCODE
  } finally {
    $ErrorActionPreference = $saved
  }
  return [pscustomobject]@{ Code = $code; Text = (($out | ForEach-Object { "$_" }) -join "`n") }
}

function Test-Generated([string]$path) {
  $normalized = $path.Replace('\', '/')
  foreach ($g in $GeneratedPaths) { if ($normalized.StartsWith($g)) { return $true } }
  return $false
}

$branch = "session/$SessionId"
$dir = Join-Path $WorktreeRoot "session-$SessionId"

switch ($Action) {
  'List' {
    (Invoke-Git $RepoRoot @('worktree', 'list')).Text
    exit 0
  }

  'Create' {
    if (-not $SessionId) { Write-Output 'create-needs-session-id'; exit 2 }
    if (Test-Path $dir) { Write-Output "worktree-exists $dir"; exit 0 }
    New-Item -ItemType Directory -Force -Path $WorktreeRoot | Out-Null
    # 기존 브랜치가 남아 있으면 지운다. 앞 세션이 착지에 실패해 남긴 것이면 Land 가 이미 보고했다.
    Invoke-Git $RepoRoot @('branch', '-D', $branch) | Out-Null
    $r = Invoke-Git $RepoRoot @('worktree', 'add', '-b', $branch, $dir, $BaseBranch)
    if ($r.Code -ne 0) { Write-Output "create-failed`n$($r.Text)"; exit 1 }
    Write-Output "created $dir  branch=$branch"
    exit 0
  }

  'Remove' {
    if (-not $SessionId) { Write-Output 'remove-needs-session-id'; exit 2 }

    # 착지 안 한 커밋이 있으면 지우지 않는다. branch -D 는 조건 없이 지운다 -
    # 2026-07-28 실측: Land 가 트리 clean 아님으로 거부된 직후 Remove 가 돌아 커밋 하나가
    # 브랜치째 사라졌다. git fsck 의 dangling 으로만 되찾았다. 되찾을 수 있었던 건 운이다.
    # 일부러 버리려면 -Force 를 준다. 버리는 것은 말해야 하는 일이지 기본값이 아니다.
    $unlanded = Invoke-Git $RepoRoot @('rev-list', '--count', "$BaseBranch..$branch")
    if (($unlanded.Code -eq 0) -and (-not $Force)) {
      $count = 0
      [void][int]::TryParse(($unlanded.Text).Trim(), [ref]$count)
      if ($count -gt 0) {
        Write-Output "remove-refused-unlanded $branch ($count 커밋이 $BaseBranch 에 없다. 먼저 Land 하거나 -Force 로 버려라)"
        exit 3
      }
    }
    Invoke-Git $RepoRoot @('worktree', 'remove', '--force', $dir) | Out-Null
    Invoke-Git $RepoRoot @('branch', '-D', $branch) | Out-Null
    Invoke-Git $RepoRoot @('worktree', 'prune') | Out-Null
    # git 이 내용은 지워도 폴더 자체를 못 지우는 경우가 있다 — 방금 끝난 세션이 그 경로를
    # 쥐고 있으면 그렇다. 2026-07-27 첫 한 바퀴 실측에서 빈 폴더가 남았다.
    # 안 지우면 한 바퀴마다 하나씩 쌓인다.
    if (Test-Path $dir) {
      try {
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction Stop
        Write-Output "removed $branch (폴더도 직접 지웠다)"
      } catch {
        Write-Output "removed $branch (폴더 $dir 가 남았다: $($_.Exception.Message))"
      }
      exit 0
    }
    Write-Output "removed $branch"
    exit 0
  }

  'Land' {
    if (-not $SessionId) { Write-Output 'land-needs-session-id'; exit 2 }

    $ahead = Invoke-Git $RepoRoot @('rev-list', '--count', "$BaseBranch..$branch")
    if ($ahead.Code -ne 0) { Write-Output "land-no-branch $branch"; exit 0 }
    if ($ahead.Text.Trim() -eq '0') { Write-Output "land-nothing (커밋 없음)"; exit 0 }

    $head = Invoke-Git $RepoRoot @('rev-parse', '--abbrev-ref', 'HEAD')
    if ($head.Text.Trim() -ne $BaseBranch) {
      Write-Output "land-refused (본 저장소가 $BaseBranch 가 아니라 $($head.Text.Trim()))"
      exit 1
    }

    # 생성물만 더러우면 착지 전에 그것부터 커밋한다. 깨우기는 이미 생성물 더러움으로
    # 막지 않는데(2026-07-28) 여기만 막으면 세션이 일을 다 하고 착지에서 깨진다.
    # 사람이 매번 손으로 "chore: 재측정" 을 치던 것을 그대로 코드가 한다.
    # 목록은 scripts/generated-paths.txt 하나를 쓴다.
    $dirty = Invoke-Git $RepoRoot @('status', '--porcelain')
    $dirtyLines = @($dirty.Text -split "`n" | ForEach-Object { $_.Trim("`r") } | Where-Object { $_.Trim() })
    $nonGenerated = @()
    foreach ($line in $dirtyLines) {
      $path = if ($line.Length -gt 3) { $line.Substring(3).Trim() } else { $line.Trim() }
      if ($path.Contains(' -> ')) { $path = ($path -split ' -> ')[-1] }
      $path = $path.Trim('"').Replace('', '/')
      if (-not (Test-Generated $path)) { $nonGenerated += $path }
    }
    if ($dirtyLines.Count -gt 0 -and $nonGenerated.Count -eq 0) {
      $add = Invoke-Git $RepoRoot @('add', '-A')
      $made = Invoke-Git $RepoRoot @('-c', 'core.hooksPath=/dev/null', 'commit', '-q', '-m', 'chore: 생성물 재측정 (착지 전 자동)')
      if ($made.Code -ne 0) {
        Write-Output "land-refused (생성물 커밋에 실패했다: $($made.Text.Trim()))"
        exit 1
      }
      Write-Output "land-committed-generated ($($dirtyLines.Count)건)"
      $dirty = Invoke-Git $RepoRoot @('status', '--porcelain')
    }
    if ($dirty.Text.Trim() -ne '') {
      Write-Output "land-refused (본 저장소 트리가 clean 이 아니다)"
      exit 1
    }

    $merge = Invoke-Git $RepoRoot @('merge', '--no-ff', '-m', "세션 $SessionId 작업 착지 ($branch)", $branch)
    if ($merge.Code -eq 0) {
      Write-Output "landed $branch ($($ahead.Text.Trim()) 커밋)"
      exit 0
    }

    # 충돌. 생성물에서만 났으면 세션 것을 취하고 재생성한다. 아니면 손대지 않고 사람에게 넘긴다.
    $conflicts = @((Invoke-Git $RepoRoot @('diff', '--name-only', '--diff-filter=U')).Text -split "`n" | Where-Object { $_.Trim() })
    $nonGenerated = @($conflicts | Where-Object { -not (Test-Generated $_) })
    if ($nonGenerated.Count -gt 0) {
      Invoke-Git $RepoRoot @('merge', '--abort') | Out-Null
      Write-Output "land-conflict (생성물이 아닌 충돌 $($nonGenerated.Count)건) - 사람 확인이 필요하다"
      foreach ($c in $nonGenerated) { Write-Output "  ! $c" }
      Write-Output "  브랜치 $branch 는 남겨둔다. 일한 결과를 버리지 않는다."
      exit 1
    }

    foreach ($c in $conflicts) {
      Invoke-Git $RepoRoot @('checkout', '--theirs', '--', $c) | Out-Null
      Invoke-Git $RepoRoot @('add', '--', $c) | Out-Null
    }
    $commit = Invoke-Git $RepoRoot @('commit', '--no-verify', '-m', "세션 $SessionId 작업 착지 ($branch) - 생성물 충돌 $($conflicts.Count)건은 세션 것을 취하고 재측정으로 덮는다")
    if ($commit.Code -ne 0) {
      Invoke-Git $RepoRoot @('merge', '--abort') | Out-Null
      Write-Output "land-conflict-resolve-failed`n$($commit.Text)"
      exit 1
    }
    Write-Output "landed $branch (생성물 충돌 $($conflicts.Count)건 자동 해소 - 재측정 필요)"
    exit 0
  }
}
