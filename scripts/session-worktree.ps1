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
  [ValidateSet('Create', 'Land', 'Remove', 'List')]
  [string]$Action = 'List',
  [string]$RepoRoot = 'C:\Users\1\Documents\Local-First Workflow Dashboard',
  [string]$WorktreeRoot = 'C:\NHN Project\_ops\worktrees',
  [int]$SessionPid = 0,
  [string]$BaseBranch = 'wp/state-integrity'
)

$ErrorActionPreference = 'Stop'

# 측정이 매번 다시 쓰는 파일들. 병합 충돌이 여기서만 나면 세션 것을 취하고 다시 생성한다.
# 내용이 아니라 생성물이라, 어느 쪽을 고르든 재생성하면 같아진다.
$GeneratedPaths = @(
  'dashboard/data/',
  'docs/context/RUNTIME-INDEX.md',
  'docs/STATUS.md',
  'docs/handoff/HANDOFF.md',
  'docs/handoff/WORKSTATE.json'
)

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

$branch = "session/$SessionPid"
$dir = Join-Path $WorktreeRoot "session-$SessionPid"

switch ($Action) {
  'List' {
    (Invoke-Git $RepoRoot @('worktree', 'list')).Text
    exit 0
  }

  'Create' {
    if ($SessionPid -le 0) { Write-Output 'create-needs-session-pid'; exit 2 }
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
    if ($SessionPid -le 0) { Write-Output 'remove-needs-session-pid'; exit 2 }
    Invoke-Git $RepoRoot @('worktree', 'remove', '--force', $dir) | Out-Null
    Invoke-Git $RepoRoot @('branch', '-D', $branch) | Out-Null
    Invoke-Git $RepoRoot @('worktree', 'prune') | Out-Null
    Write-Output "removed $branch"
    exit 0
  }

  'Land' {
    if ($SessionPid -le 0) { Write-Output 'land-needs-session-pid'; exit 2 }

    $ahead = Invoke-Git $RepoRoot @('rev-list', '--count', "$BaseBranch..$branch")
    if ($ahead.Code -ne 0) { Write-Output "land-no-branch $branch"; exit 0 }
    if ($ahead.Text.Trim() -eq '0') { Write-Output "land-nothing (커밋 없음)"; exit 0 }

    $head = Invoke-Git $RepoRoot @('rev-parse', '--abbrev-ref', 'HEAD')
    if ($head.Text.Trim() -ne $BaseBranch) {
      Write-Output "land-refused (본 저장소가 $BaseBranch 가 아니라 $($head.Text.Trim()))"
      exit 1
    }

    $dirty = Invoke-Git $RepoRoot @('status', '--porcelain')
    if ($dirty.Text.Trim() -ne '') {
      Write-Output "land-refused (본 저장소 트리가 clean 이 아니다)"
      exit 1
    }

    $merge = Invoke-Git $RepoRoot @('merge', '--no-ff', '-m', "세션 $SessionPid 작업 착지 ($branch)", $branch)
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
    $commit = Invoke-Git $RepoRoot @('commit', '--no-verify', '-m', "세션 $SessionPid 작업 착지 ($branch) - 생성물 충돌 $($conflicts.Count)건은 세션 것을 취하고 재측정으로 덮는다")
    if ($commit.Code -ne 0) {
      Invoke-Git $RepoRoot @('merge', '--abort') | Out-Null
      Write-Output "land-conflict-resolve-failed`n$($commit.Text)"
      exit 1
    }
    Write-Output "landed $branch (생성물 충돌 $($conflicts.Count)건 자동 해소 - 재측정 필요)"
    exit 0
  }
}
