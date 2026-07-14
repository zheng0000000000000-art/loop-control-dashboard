# 06H RECOVERY fixture QA

## 목적

06H는 현재 fail-closed RECOVERY 규칙과 post-provenance RECOVERY 경계를 문서화하고, reconciliation fixture A가 POST-COMMIT manifest에서 실패로 잡히는지 확인한다.

## fixture A

`docs/qa/fixtures/reconciliation/A/WORKSTATE.appliedTransitions`에는 `TEST-DI0001-2`가 없다. 같은 ID는 `WORKSTATE.applier-log.jsonl`에 성공 로그로 존재한다.

직접 무결성 검사는 `log-transition-missing-from-state` 계열 실패로 exit 1이어야 한다.

```bash
dotnet run --project server -c Release -- handoff-integrity \
  --workstate docs/qa/fixtures/reconciliation/A/WORKSTATE.json \
  --applier-log docs/qa/fixtures/reconciliation/A/WORKSTATE.applier-log.jsonl
```

POST-COMMIT manifest는 이 명령의 기대 exit을 0으로 둔다. 따라서 `di-completion-check`는 실제 exit 1과 기대 exit 0의 불일치를 감지해 exit 1이어야 한다.

```bash
dotnet run --project server -c Release -- di-completion-check \
  --gate POST-COMMIT \
  --manifest docs/qa/fixtures/reconciliation/A/GATE-MANIFEST.json \
  --task recon-postcommit-A
```

## 참조한 스킬

- `skills/common/hs-gate.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/verification.md`
- `skills/domains/docs/README.md`
