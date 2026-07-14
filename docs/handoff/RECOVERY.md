# WORKSTATE 복구 절차

> **경고**: WORKSTATE.json을 손으로 고치지 말라. Write 모드도, 에디터도 금지한다.
> 이 복구는 멱등 보장을 훼손할 수 있다. 실제 사고 근거는 DI-00-01에서 `appliedTransitions`가 누락됐는데도 같은 전이가 exit 0으로 재통과한 사건이다.

## 현재 판단

현재 저장소는 provenance가 충분히 증명되기 전의 fail-closed 시대다. 이 상태에서는 WORKSTATE를 제자리에서 복구하지 않는다. `WORKSTATE.applier-log.jsonl`과 `WORKSTATE.json`이 어긋나면 자동 재적용이 아니라 격리, 신뢰 가능한 스냅샷 복원, HUMAN-INBOX 보고로 멈춘다.

provenance 기반 StateApplier RECOVERY가 도입되고 검증되기 전까지 L1 fast-path는 비활성이다.

## 복구가 필요한 상황

- `git checkout docs/handoff/WORKSTATE.json`, `git reset` 등으로 WORKSTATE가 이전 상태로 되돌아간 경우
- `WORKSTATE.applier-log.jsonl`에는 성공 전이가 있는데 `WORKSTATE.appliedTransitions`에는 없는 경우
- 같은 transitionId의 성공 로그가 서로 충돌하는 경우
- 과거 전이의 idempotency를 현재 증거만으로 검증할 수 없는 경우

## 현재 fail-closed 시대 절차

### 1. 어긋남 확인

```bash
dotnet run --project server -c Release -- handoff-integrity
dotnet run --project server -c Release -- projection
```

필요하면 사고 fixture처럼 명시 경로를 지정해 확인한다.

```bash
dotnet run --project server -c Release -- handoff-integrity \
  --workstate docs/qa/fixtures/reconciliation/A/WORKSTATE.json \
  --applier-log docs/qa/fixtures/reconciliation/A/WORKSTATE.applier-log.jsonl
```

### 2. 즉시 격리

- WORKSTATE.json을 손으로 수정하지 않는다.
- `state-transition`으로 누락 전이를 재적용하지 않는다.
- L1 fast-path 또는 자동 RECOVERY를 사용하지 않는다.
- 현재 `WORKSTATE.json`, `WORKSTATE.applier-log.jsonl`, `RUNTIME-INDEX.md`, 관련 git sha를 보존한다.

### 3. 신뢰 가능한 스냅샷 복원

복원은 사람이 확인한 신뢰 스냅샷만 사용한다. 신뢰 스냅샷이 없으면 복원하지 않고 HUMAN-INBOX로 넘긴다.

```text
trusted-human-receipt-required
exit 1
```

### 4. HUMAN-INBOX 보고

```text
HUMAN-INBOX 탑재 항목:
- 상황: git checkout 등으로 WORKSTATE가 되돌아간 정황
- 현재 git commit:
- applier-log 최신 항목:
- WORKSTATE 현재 status:
- 어긋나는 transitionId 목록:
- 실행한 확인 명령:
- handoff-integrity exit code와 failure code:
- 신뢰 스냅샷 존재 여부:
```

## provenance 이후 RECOVERY 절차

provenance 이후에도 복구는 StateApplier의 receipt-backed RECOVERY만 허용한다. 수동 편집, Write 모드 수정, transitionId 배열 직접 편집은 계속 금지한다.

| 레벨 | 조건 | 허용 행동 | 실패 코드 |
| --- | --- | --- | --- |
| L1 | receipt가 있고 현재 상태와 expected hash가 일치한다 | StateApplier RECOVERY fast-path | `receipt-replay-failed` |
| L2 | applier-log와 WORKSTATE 사이에 성공 전이 누락이 있다 | receipt-backed replay 후보로 격리 | `log-transition-missing-from-state` |
| L3 | 같은 transitionId의 성공 로그가 서로 충돌한다 | 자동 복구 금지, HUMAN-INBOX | `duplicate-success-log-conflict` |
| L4 | legacy 전이의 idempotency를 검증할 수 없다 | 자동 복구 금지, HUMAN-INBOX | `legacy-idempotency-unverifiable` |
| FATAL | 전이 ID 충돌 또는 provenance 자체가 모순된다 | 즉시 중단, 기준 변경 필요 | `transition-id-collision` / exit 4 |

## 금지 사항

- `Write` 모드로 WORKSTATE.json 직접 수정
- 에디터로 WORKSTATE.json 직접 수정
- `appliedTransitions` 배열을 손으로 편집
- `git checkout`, `git reset`으로 WORKSTATE만 되돌리기
- TRUSTED_BASELINE 전 자동 발사, 자동 조율, 자동 RECOVERY 구현
- provenance 없는 `state-transition` 단일 재적용으로 복구했다고 간주하기

## 근거

DI-00-01 실행자가 `git checkout docs/handoff/WORKSTATE.json`으로 작업본을 되돌린 뒤 Write 모드로 자가 복구했고, 그 결과 `appliedTransitions`에서 `TEST-DI0001-2`가 누락돼 멱등성이 실제로 깨졌다. 검수자가 재적용을 시도하자 exit 0으로 통과해 같은 전이가 두 번 적용됐고, `di-completion-check POST-COMMIT`은 7/7 PASS를 주는 동안 상태는 여전히 비정상이었다.

사고 기록 commit: `302b5c3`
