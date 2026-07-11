# FAIL-2026-014 — Tier2Approver 검증 CLI가 일부 시나리오에서 예외로 종료됨

- 상태: 확인됨
- failureClass: verification_gap, harness_runtime_error, design_learning
- 구성요소: tier2-approver, verification-harness
- 발견일: 2026-07-11
- 발견자: codex

## 요약

FEAT-01 자체검증 문서는 `tier2test` 8개 시나리오가 모두 PASS라고 주장했다. 독립 재실행에서는 `eligible-approved`, `core-file-touched`, `daily-cap` 시나리오가 `DirectoryNotFoundException`으로 종료됐다. 또한 `measure dev-pack`은 문서 주장 `violations=3`과 달리 `violations=4`를 반환했다.

## 재현

```powershell
dotnet run --project server -c Release --no-build -- tier2test eligible-approved
dotnet run --project server -c Release --no-build -- tier2test core-file-touched
dotnet run --project server -c Release --no-build -- tier2test daily-cap
dotnet run --project server -c Release --no-build -- measure dev-pack
```

관측:

- `eligible-approved`: `docs/audit/tier2-import-approvals-state.json` 경로가 없어 `DirectoryNotFoundException`.
- `core-file-touched`: `outbox/task-0000/meta.json` 경로가 없어 `DirectoryNotFoundException`.
- `daily-cap`: `docs/audit/tier2-import-approvals-state.json` 경로가 없어 `DirectoryNotFoundException`.
- `measure dev-pack`: `violationCount:4`.

## 기대

- 검증 CLI는 테스트 루트에 필요한 디렉터리를 생성하거나, 실패를 구조화된 JSON 결과로 반환해야 한다.
- 자체검증 문서의 PASS 표는 독립 실행자가 같은 명령으로 재현할 수 있어야 한다.
- measure 위반 수는 문서 주장과 실측이 일치해야 한다.

## 영향

검증 CLI가 일부 시나리오에서 예외로 죽으면 FEAT-01의 조건부 반입 게이트를 독립적으로 신뢰할 수 없다. 특히 "자가보고를 신뢰하지 않고 하네스를 재실행한다"는 프로토콜의 핵심 경로에서 문서와 실측이 갈라진다.

## 관련

- `docs/verification/feat01-conditional-delegation.md`
- `server/Tier2Approver.cs`
- `server/Tier2ApproverTestCli.cs`
- `docs/qa/review-feat01-conditional-delegation.md`
