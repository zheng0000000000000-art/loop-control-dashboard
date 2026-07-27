#Requires -Version 5.1
# 보드 태스크를 team-loop 의 격리 워크트리 안에서만 하게 만든다.
#
# 왜 있는가: 내 워크트리 격리는 Local-First 저장소만 덮는다. 보드 태스크가 team-loop 코드를
# 고치면 세션은 공유 메인 트리에서 작업했다. 2026-07-28 실측 - 한 세션이 src/worktree.js 를
# 메인 트리와 태스크 워크트리 두 군데에 흩어놓았고, team-loop 이 스스로 태스크를 차단했다:
# "이전 실행이 격리 worktree 없이 중단되어 변경물의 소유권을 안전하게 판별할 수 없습니다."
# team-loop 판단이 옳다. 누구 변경인지 모르는 상태를 통과시키면 안 된다.
#
# team-loop 에 createTaskWorktree 가 이미 있다. src/worktree.js 머리에 이렇게 적혀 있다 -
# "각 태스크가 자기 worktree 와 브랜치를 갖고, 에이전트는 자기 체크아웃 밖 파일을
# 물리적으로 건드릴 수 없다." 우리가 그걸 안 쓰고 있었을 뿐이다.
param(
  [ValidateSet('Ensure', 'Check')]
  [string]$Action = 'Ensure',
  [string]$TeamLoopRoot = 'C:\NHN Project\team-loop-lite-ai-learning',
  [string]$TaskId = ''
)

$ErrorActionPreference = 'Stop'

# data/ 는 지식과 보드 상태다. 서버가 늘 쓰므로 오염으로 세지 않는다.
# 배열을 "$out" 으로 문자열화하지 마라. PowerShell 은 배열을 줄바꿈이 아니라 공백으로 합친다.
# 2026-07-28 실측: 세 줄이 한 줄로 붙어 data/ 로 시작하는 바람에 오염이 통째로 걸러졌고
# 음성 사례가 조용히 통과했다. 배열은 배열로 다룬다.
function Get-SourceChanges {
  $lines = @(& git -C $TeamLoopRoot status --porcelain 2>&1 | ForEach-Object { "$_" } | Where-Object { $_.Trim() })
  return @($lines | Where-Object {
    $parts = $_.Trim() -split '\s+', 2
    $target = if ($parts.Count -gt 1) { $parts[1] } else { $parts[0] }
    -not $target.StartsWith('data/')
  })
}

if ($Action -eq 'Check') {
  $dirty = Get-SourceChanges
  if ($dirty.Count -eq 0) { Write-Output 'teamloop-clean'; exit 0 }
  # 되돌리지 않는다. 남의 작업일 수 있고, 일한 결과를 조용히 지우는 것이 더 나쁘다.
  Write-Output "teamloop-contaminated $($dirty.Count)건 - 세션이 격리 밖에서 team-loop 을 고쳤다"
  foreach ($line in $dirty) { Write-Output "  ! $line" }
  exit 1
}

if (-not $TaskId) { Write-Output 'ensure-needs-task-id'; exit 2 }

$dir = Join-Path $TeamLoopRoot ".team-loop-worktrees\$TaskId"
# 이미 있으면 그대로 쓴다. createTaskWorktree 는 -B 로 브랜치를 되감고 기존 워크트리를
# 지우고 다시 만든다 - 앞 세션이 남긴 작업이 있으면 그것을 날린다.
if (Test-Path $dir) { Write-Output "teamloop-worktree-exists $dir"; exit 0 }

# node -e 는 기준 URL 이 없어서 상대 경로 import 를 해석하지 못한다. 절대 file:// 로 준다.
# 2026-07-28 실측: './src/worktree.js' 로 줬더니 esm/resolve 에서 죽었고 연쇄가 끊겼다.
# 내 시험이 이미 있는 워크트리로만 돌아 일찍 반환되는 가지만 봤고, 만드는 가지는 안 태웠다.
# 시험이 지나간 자리와 코드가 도는 자리가 다르면 그 시험은 그 코드를 안 잰 것이다.
$moduleUrl = ([uri](Join-Path $TeamLoopRoot 'src\worktree.js')).AbsoluteUri
$script = "import { createTaskWorktree } from '$moduleUrl'; const made = await createTaskWorktree(process.argv[1], process.argv[2]); console.log(made.dir);"
$made = & node --input-type=module -e $script $TeamLoopRoot $TaskId 2>&1
if ($LASTEXITCODE -ne 0) { Write-Output "teamloop-worktree-failed`n$made"; exit 1 }
Write-Output "teamloop-worktree-created $($made | Select-Object -Last 1)"
exit 0
