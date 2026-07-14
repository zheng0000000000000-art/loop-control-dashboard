# DI-00-01 반증 시험 스크립트 (9개). 원본 WORKSTATE를 망가뜨리지 않는다.
# Usage: powershell -File outputs/test-temp/run-di0001-tests.ps1

$root = "C:\Users\1\Documents\Local-First Workflow Dashboard"
$server = "$root\server"
$ws = "$root\docs\handoff\WORKSTATE.json"
$tmpDir = "$root\outputs\test-temp"

function Invoke-ServerCmd {
    param([string[]]$Args, [string]$Label)
    Write-Host "`n=== $Label ===" -ForegroundColor Cyan
    $output = & dotnet run --project $server -c Release -- @Args 2>&1
    $exit = $LASTEXITCODE
    Write-Host "Exit: $exit"
    Write-Host $output
    return $exit
}

function Get-WSha {
    return (Get-FileHash $ws -Algorithm SHA256).Hash.ToLower()
}

# ---- 0. 원본 저장 ----
$origRaw = [System.IO.File]::ReadAllText($ws, [System.Text.Encoding]::UTF8)
$origSha = Get-WSha
Write-Host "원본 sha256: $origSha"

# ---- 임시 파일 준비 ----
$req_inprogress = "$tmpDir\req-inprogress.json"
$req_waiting_same = "$tmpDir\req-waiting-same.json"
$req_completed = "$tmpDir\req-completed.json"
$req_wp99 = "$tmpDir\req-wp99.json"
$req_minimal = "$tmpDir\req-minimal.json"
$hd_approved = "$tmpDir\hd-approved.json"

[System.IO.File]::WriteAllText($req_inprogress,
    '{"status":"in_progress","nextActions":["TEST: completed 되돌림 검증"]}',
    [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($req_waiting_same,
    '{"status":"waiting","nextActions":["TEST-4: same-status nextActions 갱신"]}',
    [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($req_completed,
    '{"status":"completed"}',
    [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($req_wp99,
    '{"wpId":"WP-99","nextActions":["TEST-5: WP-99 검증"]}',
    [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($req_minimal,
    '{"nextActions":["TEST-6: LEDGER-04 canonical 검증"]}',
    [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($hd_approved,
    '{"approved":true}',
    [System.Text.UTF8Encoding]::new($false))

# ================================================================
# TEST 1 & 2: completed WORKSTATE 필요 → 원본 status 교체
# ================================================================
Write-Host "`n[SETUP] completed WORKSTATE 생성"
$completedRaw = $origRaw.Replace('"status": "waiting"', '"status": "completed"')
[System.IO.File]::WriteAllText($ws, $completedRaw, [System.Text.UTF8Encoding]::new($false))
$completedSha = Get-WSha
Write-Host "completed sha256: $completedSha"

# TEST 1: completed → in_progress (human-decision 없음) → exit 1
$e1 = Invoke-ServerCmd @("state-transition", "--transition-id", "TEST-DI0001-1",
    "--expected-workstate-sha256", $completedSha, "--request", $req_inprogress) "TEST 1: completed→in_progress (no hd)"
$shaAfter1 = Get-WSha
if ($e1 -eq 1) { Write-Host "TEST 1 PASS (exit 1)" -ForegroundColor Green } else { Write-Host "TEST 1 FAIL (expected 1, got $e1)" -ForegroundColor Red }
if ($shaAfter1 -eq $completedSha) { Write-Host "TEST 1 WORKSTATE sha 불변 PASS" -ForegroundColor Green } else { Write-Host "TEST 1 WORKSTATE sha 변경됨 FAIL" -ForegroundColor Red }

# TEST 2: completed → in_progress + --human-decision approved:true → exit 0
$e2 = Invoke-ServerCmd @("state-transition", "--transition-id", "TEST-DI0001-2",
    "--expected-workstate-sha256", $completedSha, "--request", $req_inprogress,
    "--human-decision", $hd_approved) "TEST 2: completed→in_progress (hd=approved)"
if ($e2 -eq 0) { Write-Host "TEST 2 PASS (exit 0)" -ForegroundColor Green } else { Write-Host "TEST 2 FAIL (expected 0, got $e2)" -ForegroundColor Red }

# ---- 원본 복원 ----
Write-Host "`n[RESTORE] 원본 WORKSTATE 복원"
[System.IO.File]::WriteAllText($ws, $origRaw, [System.Text.UTF8Encoding]::new($false))
$restoreSha = Get-WSha
Write-Host "복원 후 sha256: $restoreSha (원본과 일치: $($restoreSha -eq $origSha))"

# projection 재실행으로 derived files 복원
$_ = Invoke-ServerCmd @("projection") "복원 후 projection"

# ================================================================
# TEST 3: waiting → completed (verifying 건너뜀) → exit 1
# ================================================================
$wsSha3 = Get-WSha
$e3 = Invoke-ServerCmd @("state-transition", "--transition-id", "TEST-DI0001-3",
    "--expected-workstate-sha256", $wsSha3, "--request", $req_completed) "TEST 3: waiting→completed (skip verifying)"
if ($e3 -eq 1) { Write-Host "TEST 3 PASS (exit 1)" -ForegroundColor Green } else { Write-Host "TEST 3 FAIL (expected 1, got $e3)" -ForegroundColor Red }

# ================================================================
# TEST 4: waiting → waiting (nextActions만 갱신) → exit 0
# ================================================================
$wsSha4 = Get-WSha
$e4 = Invoke-ServerCmd @("state-transition", "--transition-id", "TEST-DI0001-4",
    "--expected-workstate-sha256", $wsSha4, "--request", $req_waiting_same) "TEST 4: waiting→waiting (same status)"
if ($e4 -eq 0) { Write-Host "TEST 4 PASS (exit 0)" -ForegroundColor Green } else { Write-Host "TEST 4 FAIL (expected 0, got $e4)" -ForegroundColor Red }

# test 4 후 원본 복원
[System.IO.File]::WriteAllText($ws, $origRaw, [System.Text.UTF8Encoding]::new($false))
$_ = Invoke-ServerCmd @("projection") "TEST 4 후 복원 projection"

# ================================================================
# TEST 5: wpId=WP-99 (WP-REGISTRY 없음) → exit 1
# ================================================================
$wsSha5 = Get-WSha
$e5 = Invoke-ServerCmd @("state-transition", "--transition-id", "TEST-DI0001-5",
    "--expected-workstate-sha256", $wsSha5, "--request", $req_wp99) "TEST 5: wpId=WP-99 (registry miss)"
if ($e5 -eq 1) { Write-Host "TEST 5 PASS (exit 1)" -ForegroundColor Green } else { Write-Host "TEST 5 FAIL (expected 1, got $e5)" -ForegroundColor Red }

# ================================================================
# TEST 6: WORKSTATE diId=LEDGER-04 사본 → candidate 비canonical → exit 1
# ================================================================
Write-Host "`n[SETUP] LEDGER-04 WORKSTATE 생성"
$ledgerRaw = $origRaw.Replace('  "diId": "DI-00-01"', '  "diId": "LEDGER-04"')
[System.IO.File]::WriteAllText($ws, $ledgerRaw, [System.Text.UTF8Encoding]::new($false))
$ledgerSha = Get-WSha
Write-Host "LEDGER-04 sha256: $ledgerSha"

$e6 = Invoke-ServerCmd @("state-transition", "--transition-id", "TEST-DI0001-6",
    "--expected-workstate-sha256", $ledgerSha, "--request", $req_minimal) "TEST 6: candidate diId=LEDGER-04 (비canonical)"
if ($e6 -eq 1) { Write-Host "TEST 6 PASS (exit 1)" -ForegroundColor Green } else { Write-Host "TEST 6 FAIL (expected 1, got $e6)" -ForegroundColor Red }

# ---- 원본 복원 ----
[System.IO.File]::WriteAllText($ws, $origRaw, [System.Text.UTF8Encoding]::new($false))
$_ = Invoke-ServerCmd @("projection") "TEST 6 후 복원 projection"

# ================================================================
# TEST 7: projection 두 번 연속 → STATUS.md 멱등(sha256 동일)
# ================================================================
$_ = Invoke-ServerCmd @("projection") "TEST 7: projection 1회차"
$sha7a = (Get-FileHash "$root\docs\STATUS.md" -Algorithm SHA256).Hash.ToLower()
$_ = Invoke-ServerCmd @("projection") "TEST 7: projection 2회차"
$sha7b = (Get-FileHash "$root\docs\STATUS.md" -Algorithm SHA256).Hash.ToLower()
Write-Host "`n=== TEST 7: projection 멱등 ==="
if ($sha7a -eq $sha7b) { Write-Host "TEST 7 PASS (sha 동일: $sha7a)" -ForegroundColor Green } else { Write-Host "TEST 7 FAIL (sha 불일치: $sha7a vs $sha7b)" -ForegroundColor Red }

# ================================================================
# TEST 8: STATUS.md 한 줄 수정 → projection → 덮어써진다
# ================================================================
$statusPath = "$root\docs\STATUS.md"
$statusBefore = [System.IO.File]::ReadAllText($statusPath, [System.Text.Encoding]::UTF8)
$statusEdited = $statusBefore.Replace("# STATUS — 현재 상태", "# STATUS — 수동편집됨")
[System.IO.File]::WriteAllText($statusPath, $statusEdited, [System.Text.UTF8Encoding]::new($false))
$shaEditedStatus = (Get-FileHash $statusPath -Algorithm SHA256).Hash.ToLower()
$_ = Invoke-ServerCmd @("projection") "TEST 8: 손편집 후 projection"
$shaAfterProjection = (Get-FileHash $statusPath -Algorithm SHA256).Hash.ToLower()
Write-Host "`n=== TEST 8: STATUS.md 손편집 덮어쓰기 ==="
if ($shaAfterProjection -ne $shaEditedStatus) { Write-Host "TEST 8 PASS (손편집 덮어써짐)" -ForegroundColor Green } else { Write-Host "TEST 8 FAIL (손편집이 살아있음)" -ForegroundColor Red }

# ================================================================
# TEST 9: 정상 전이 1회 → exit 0, STATUS.md·RUNTIME-INDEX·WORKSTATE 동일 상태
# ================================================================
$req9 = "$tmpDir\req-test9.json"
[System.IO.File]::WriteAllText($req9,
    '{"status":"in_progress","nextActions":["TEST-9: 정상 전이 검증 - DI-00-01 구현 진행 중"]}',
    [System.Text.UTF8Encoding]::new($false))

$wsSha9 = Get-WSha
$e9 = Invoke-ServerCmd @("state-transition", "--transition-id", "TEST-DI0001-9",
    "--expected-workstate-sha256", $wsSha9, "--request", $req9) "TEST 9: 정상 전이 waiting→in_progress"
if ($e9 -eq 0) { Write-Host "TEST 9 exit 0 PASS" -ForegroundColor Green } else { Write-Host "TEST 9 exit FAIL (expected 0, got $e9)" -ForegroundColor Red }

# 상태 일치 확인
$wsAfter = [System.Text.Json.JsonDocument]::Parse([System.IO.File]::ReadAllText($ws))
$wsStatus = $wsAfter.RootElement.GetProperty("status").GetString()
$statusMd = [System.IO.File]::ReadAllText($statusPath)
$runtimeMd = [System.IO.File]::ReadAllText("$root\docs\context\RUNTIME-INDEX.md")
$statusHasInProgress = $statusMd.Contains("in_progress")
$runtimeHasInProgress = $runtimeMd.Contains("in_progress")
Write-Host "WORKSTATE status=$wsStatus  STATUS.md has in_progress=$statusHasInProgress  RUNTIME-INDEX has in_progress=$runtimeHasInProgress"
if ($wsStatus -eq "in_progress" -and $statusHasInProgress -and $runtimeHasInProgress) {
    Write-Host "TEST 9 상태 일치 PASS" -ForegroundColor Green
} else {
    Write-Host "TEST 9 상태 불일치 FAIL" -ForegroundColor Red
}

# TEST 9 후 원본 복원
Write-Host "`n[RESTORE] 최종 복원"
[System.IO.File]::WriteAllText($ws, $origRaw, [System.Text.UTF8Encoding]::new($false))
$_ = Invoke-ServerCmd @("projection") "최종 projection"

Write-Host "`n=== 모든 테스트 완료 ==="
