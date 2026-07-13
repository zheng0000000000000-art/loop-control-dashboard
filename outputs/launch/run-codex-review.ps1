# 코덱스를 read-only로 발사해 실행자 산출물을 독립 검수한다. 쓰기가 없으므로 범위 대조가 필요 없다.
# 왜: ADR-002는 "만든 사람이 검증하면 같은 착각을 코드에 새긴다"를 막는다. ADR-015 예외 기간 동안
#     생산(sonnet)과 검증(sonnet)이 같은 actor가 됐다. 코덱스를 read-only 검수자로 붙여 주체를 다시 분리한다.
# 사용: powershell -NoProfile -File run-codex-review.ps1 -TaskId 06C-1
#
# ★ 이것은 "발사"가 아니라 "관찰"이다. 코덱스는 아무것도 못 쓴다:
#   -s read-only  → 커널 레벨 강제(검수자 probe 3 실증: UnauthorizedAccessException)
#   저장소 사본에서 실행 → 원본 무접촉
#   산출물은 코덱스의 최종 메시지 하나뿐 → outputs/review/<TaskId>.codex.md
#
# ⚠ codex exec의 exit code는 "성공"이 아니라 "세션 종료"다. 내부 명령이 실패해도 exit 0을 준다.
#    판정 근거로 쓰지 마라 — 판정은 사람/검수자가 최종 메시지를 읽고 한다.
param(
  [Parameter(Mandatory = $true)][string]$TaskId,
  [string]$Model = ''
)

$ErrorActionPreference = 'Stop'
$root      = 'C:\Users\1\Documents\Local-First Workflow Dashboard'
$reviewDir = Join-Path $root 'outputs\review'
$copyRoot  = Join-Path $env:TEMP "codex-review-$TaskId-$(Get-Date -Format 'yyyyMMddHHmmss')"
$outMd     = Join-Path $reviewDir "$TaskId.codex.md"
$outEvents = Join-Path $reviewDir "$TaskId.codex.events.jsonl"
$outMeta   = Join-Path $reviewDir "$TaskId.codex.meta.json"

New-Item -ItemType Directory -Force -Path $reviewDir | Out-Null

# 지시서를 찾는다 — 없으면 검수 기준이 없다는 뜻이므로 중단한다.
$directive = (Get-ChildItem (Join-Path $root "docs\handoff\queue\directive-$TaskId*.md") -ErrorAction SilentlyContinue | Select-Object -First 1)
if (-not $directive) { throw "검수 중단: 지시서를 못 찾았다 — directive-$TaskId*.md" }

# 검수 대상 diff를 뽑는다 — 이것이 코덱스가 볼 실체다.
Push-Location $root
$diff = & git diff HEAD -- . 2>$null | Out-String
$untracked = (& git ls-files --others --exclude-standard 2>$null) -join "`n"
$headSha = (& git rev-parse HEAD 2>$null).Trim()
Pop-Location

if ([string]::IsNullOrWhiteSpace($diff) -and [string]::IsNullOrWhiteSpace($untracked)) {
  throw "검수 중단: diff가 비었다. 실행자 산출물이 이미 커밋됐거나 아직 안 나왔다."
}

# 저장소를 사본으로 복사한다 — 원본은 코덱스 시야에서 완전히 제외한다.
# bin/obj/.git은 제외(용량·무관). .git 제외로 코덱스가 git 명령으로 원본을 건드릴 여지도 없앤다.
New-Item -ItemType Directory -Force -Path $copyRoot | Out-Null
& robocopy $root $copyRoot /E /NFL /NDL /NJH /NJS /NP `
  /XD '.git' 'bin' 'obj' '.vs' 'node_modules' 'history' | Out-Null

# diff와 지시서를 사본 안에 증거 파일로 넣는다 — 코덱스가 읽을 입력이다.
$evidenceDir = Join-Path $copyRoot '_review'
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null
Set-Content (Join-Path $evidenceDir 'CHANGES.diff') $diff -Encoding UTF8
Set-Content (Join-Path $evidenceDir 'UNTRACKED.txt') $untracked -Encoding UTF8

# 검수 프롬프트를 만든다. 코덱스에게 "고쳐라"가 아니라 "반증해라"를 시킨다.
$prompt = @"
너는 독립 검수자다. 코드를 고치지 마라 — 너는 아무것도 쓸 수 없다(read-only 샌드박스).
너의 유일한 산출물은 이 답변 하나다.

## 무엇을 검수하는가

작업 ID: $TaskId
계약(지시서): docs/handoff/queue/$($directive.Name)
변경 내용: _review/CHANGES.diff (추적 파일의 diff) · _review/UNTRACKED.txt (신규 파일 목록)
기준 커밋: $headSha

## 왜 네가 있는가

이 저장소의 원칙(ADR-002): **만든 사람이 검증하면 같은 착각을 코드에 새긴다.**
지금 생산자와 1차 검증자가 같은 모델이다. 너는 **다른 주체**로서 그 착각을 깨러 왔다.

실제로 직전 작업(05H)에서 이런 일이 있었다:
지시서가 "규칙 2 = stateIdSet ⊆ successfulLogIdSet"이라고 했는데,
구현은 allLogIdSet을 썼다. 그러자 **실패했다고 기록된 전이가 상태에 있어도 하네스가 PASS를 줬다.**
모든 fixture가 통과했고, 게이트가 전부 초록이었다. **반증 시험이 없는 규칙은 규칙이 아니라 주석이기 때문이다.**

## 네가 할 일 — 이 순서로

1. **지시서를 읽고, 계약을 문장 단위로 뽑아라.** ("~해야 한다" / "~하지 마라" / 완료 기준의 각 항)
2. **diff를 읽고, 각 계약 문장이 실제로 코드에 있는지 대조하라.** 구현이 계약을 **미묘하게 바꾼 곳**을 찾아라 —
   집합을 넓히기, 조건을 느슨하게, 예외를 추가, 검사를 다른 경로로 우회. **05H는 인자 하나가 달랐을 뿐이다.**
3. **각 완료 기준에 대해 "이걸 통과시키면서도 목적을 배신하는 방법"을 만들어 보라.**
   그 방법이 지금 구현에 실제로 존재하는지 확인하라.
4. **테스트/fixture가 무엇을 못 잡는지 말하라.** 빠진 반증 케이스를 구체적 입력으로 제시하라.
5. 저장소를 자유롭게 읽어라(read-only). 필요하면 파일을 열어 호출부를 추적하라.

## 하지 마라

- 코드를 고치려 하지 마라. 못 쓴다. 시도하면 OS가 거부한다.
- "잘 만들어졌습니다" 같은 총평으로 끝내지 마라. **구체적 위치(파일:줄)와 구체적 반증 입력**이 없으면 검수가 아니다.
- 스타일·네이밍 지적에 시간을 쓰지 마라. **계약 위반과 우회로만** 본다.
- 확신 없는 것을 단정하지 마라. 모르면 "확인 못 함"이라고 쓰고 무엇을 봐야 하는지 적어라.

## 답변 형식 (이대로)

### 판정
PASS / FAIL / 확인 불가  — 한 줄 근거

### 계약 위반 (있으면. 없으면 "없음")
| # | 계약(지시서 어디) | 구현(파일:줄) | 어긋난 점 | 이걸 드러내는 반증 입력 |

### 지표는 통과하지만 목적을 배신하는 경로
(각 항목: 어떻게 통과시키는가 / 무엇이 배신되는가 / 지금 구현에 실재하는가)

### 테스트가 못 잡는 것
(빠진 반증 케이스를 구체적 입력으로)

### 확인 못 한 것
(무엇을, 왜)
"@

# read-only 주장을 믿지 않는다 — 실행 전 사본의 지문을 찍어둔다(경로+크기+수정시각).
function Get-TreeFingerprint([string]$path) {
  $sb = [Text.StringBuilder]::new()
  Get-ChildItem $path -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
    [void]$sb.AppendLine("$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)")
  }
  $bytes = [Text.Encoding]::UTF8.GetBytes($sb.ToString())
  $sha = [Security.Cryptography.SHA256]::Create()
  $hex = ($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join ''
  $sha.Dispose()
  return @{ Hash = $hex; Count = (Get-ChildItem $path -Recurse -File -Force).Count }
}

$before = Get-TreeFingerprint $copyRoot

$started = (Get-Date).ToString('o')
$sw = [Diagnostics.Stopwatch]::StartNew()

# codex exec — 비대화 헤드리스. read-only 샌드박스. 작업 경로는 사본.
$modelArg = if ($Model) { @('-m', $Model) } else { @() }
$codexArgs = @(
  'exec', '--json',
  '-s', 'read-only',
  '--skip-git-repo-check',
  '-C', $copyRoot,
  '-o', $outMd
) + $modelArg + @($prompt)

& codex @codexArgs 2>&1 | Set-Content $outEvents -Encoding UTF8
$exit = $LASTEXITCODE
$sw.Stop()

# 사본이 정말 안 바뀌었는지 재서 확인한다. 샌드박스가 강제됐다는 주장의 증거다.
$after = Get-TreeFingerprint $copyRoot
$sandboxHeld = ($before.Hash -eq $after.Hash)
if (-not $sandboxHeld) {
  Write-Warning "*** 샌드박스 침해: 사본이 변경됐다 (파일 수 $($before.Count) → $($after.Count)). 이 검수 결과를 믿지 마라. ***"
}

$meta = [ordered]@{
  schemaVersion = 1
  taskId        = $TaskId
  reviewer      = 'codex'
  authMode      = 'chatgpt-subscription'
  copyRoot      = $copyRoot
  baseCommit    = $headSha
  directive     = $directive.Name
  startedAt     = $started
  exitedAt      = (Get-Date).ToString('o')
  durationMs    = $sw.ElapsedMilliseconds
  codexExitCode = $exit
  note          = 'codex exec의 exit code는 성공이 아니라 세션 종료다. 판정 근거로 쓰지 마라 — 최종 메시지를 읽어라.'
  sandboxHeld   = $sandboxHeld
  treeHashBefore = $before.Hash
  treeHashAfter  = $after.Hash
  fileCountBefore = $before.Count
  fileCountAfter  = $after.Count
  reviewOut     = $outMd
  events        = $outEvents
}
$meta | ConvertTo-Json -Depth 4 | Set-Content $outMeta -Encoding UTF8

Write-Host "코덱스 검수 완료 — $outMd  (exit=$exit, $([math]::Round($sw.Elapsed.TotalSeconds,1))s)"
Write-Host "  샌드박스 유지: $sandboxHeld  (사본 트리 해시 불변 = 코덱스가 아무것도 못 썼다)"
Write-Host "★ exit code를 판정으로 쓰지 마라. 최종 메시지를 읽어라."
