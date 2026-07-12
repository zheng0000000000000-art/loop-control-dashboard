# WORKSTATE 복구 절차

> **경고**: WORKSTATE.json을 손으로 고치지 마라. Write 툴도, 에디터도 안 된다.
> 손 복구는 멱등 보장을 파괴한다(실증: DI-00-01 사고 — appliedTransitions 누락으로 같은 전이가 exit 0으로 재통과).

## 복구가 필요한 상황

- `git checkout docs/handoff/WORKSTATE.json` 또는 `git reset` 등으로 WORKSTATE가 이전 상태로 되돌아간 경우
- WORKSTATE와 `WORKSTATE.applier-log.jsonl`이 어긋나는 경우

## 절차

### 1. 어긋남 확인

```bash
# applier-log에 기록된 최신 transitionId 확인
tail -5 docs/handoff/WORKSTATE.applier-log.jsonl

# WORKSTATE.appliedTransitions 목록 확인
dotnet run --project server -c Release -- projection
# RUNTIME-INDEX.md의 diId/status가 예상과 다르면 어긋난 것이다
```

### 2. 누락된 전이 파악

`WORKSTATE.applier-log.jsonl` 에 있지만 `WORKSTATE.appliedTransitions`에 없는 transitionId를 찾아라.

```bash
# applier-log의 모든 전이 ID 목록
grep -o '"transitionId":"[^"]*"' docs/handoff/WORKSTATE.applier-log.jsonl

# WORKSTATE의 적용된 전이 목록
grep -A1 '"appliedTransitions"' docs/handoff/WORKSTATE.json
```

### 3. 누락 전이 재적용

누락된 각 전이에 대해 `state-transition`으로 재적용한다.

**현재 WORKSTATE sha256 계산 방법:**

```powershell
$bytes = [System.IO.File]::ReadAllBytes('docs\handoff\WORKSTATE.json')
$sha = [System.Security.Cryptography.SHA256]::Create()
$hash = $sha.ComputeHash($bytes)
($hash | ForEach-Object { $_.ToString('x2') }) -join ''
```

```bash
# 재적용
dotnet run --project server -c Release -- state-transition \
  --transition-id <누락된-ID> \
  --expected-workstate-sha256 <현재-sha256> \
  --request <request-파일.json>
```

### 4. 재적용이 안 되는 경우 → 멈추고 HUMAN-INBOX

- request 파일을 찾을 수 없거나
- sha256 불일치가 해결되지 않거나
- 전이 그래프가 허용하지 않는 상태인 경우

**손으로 고치지 말고 HUMAN-INBOX에 올리고 멈춰라.** 손 복구보다 멈추는 게 낫다.

```
HUMAN-INBOX 등재 항목:
- 상황: git checkout 등으로 WORKSTATE가 되돌아감
- applier-log 최신 항목: <내용>
- WORKSTATE 현재 status: <값>
- 어긋나는 transitionId: <목록>
- 시도한 재적용 결과: <exit code와 오류>
```

## 금지 사항

- ❌ `Write` 툴로 WORKSTATE.json 직접 수정
- ❌ 에디터로 WORKSTATE.json 직접 수정
- ❌ `appliedTransitions` 배열을 손으로 편집
- ❌ `git checkout`, `git reset`으로 WORKSTATE를 일부러 되돌리기

## 근거

DI-00-01 실행자가 `git checkout docs/handoff/WORKSTATE.json`으로 작업본을 날린 뒤 Write 툴로
손복구했고, 그 결과 `appliedTransitions`에서 `TEST-DI0001-2`가 누락돼 멱등이 실제로 깨졌다.
검수자가 재적용을 시도하자 exit 0으로 통과 — 같은 전이가 두 번 적용됐다.
`di-completion-check POST-COMMIT`은 7/7 PASS를 줬는데 상태는 손상돼 있었다.
(사고 기록: 커밋 `302b5c3`)
