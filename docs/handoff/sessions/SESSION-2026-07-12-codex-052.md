# SESSION-2026-07-12-codex-052

## 주체(actor)
- codex

## 작업
- P0-05 데이터 관문 해제: `context-pack-integrity` 하네스 신규 작성.
- `server/Harness/ContextPackIntegrityCli.cs` 추가.
- `server/Harness/HarnessRegistry.cs`에 `context-pack-integrity` 등록.
- `server/Cli/CliRouter.cs`는 수정하지 않음.

## 변경 경로
- `server/Harness/ContextPackIntegrityCli.cs`
- `server/Harness/HarnessRegistry.cs`
- `docs/handoff/sessions/SESSION-2026-07-12-codex-052.md`

## 사용한 하네스와 exit code
- `dotnet build server -c Release` → exit 0
- `dotnet run --project server -c Release -- context-pack-integrity` → exit 1
  - 실제 큐 전체 검사에서 `stale` 2건 검출.
  - `docs/handoff/queue/directive-FEAT01-conditional-delegation.md`의 `docs/handoff/QUOTA-POLICY.md`: expected `4bc62f76041527b6984eb2e9d3e0dc1d5a985c7329b62ae2ac65450711462a5c`, actual `6e001c2cf49aaf34d5ff3c628d7c83b3e5e323882e15b45ac383892ac4b62071`
  - `docs/handoff/queue/directive-LEDGER02-executor-token-wiring.md`의 `docs/handoff/QUOTA-POLICY.md`: expected `4bc62f76041527b6984eb2e9d3e0dc1d5a985c7329b62ae2ac65450711462a5c`, actual `6e001c2cf49aaf34d5ff3c628d7c83b3e5e323882e15b45ac383892ac4b62071`
- `Get-FileHash docs/handoff/QUOTA-POLICY.md -Algorithm SHA256` → exit 0, hash `6E001C2CF49AAF34D5FF3C628D7C83B3E5E323882E15B45AC383892AC4B62071`
- 장애 주입 1: TEMP 사본에서 `requiredInputs[0].path`를 없는 경로로 변경 후 `context-pack-integrity` 실행 → exit 1, `missingCount: 1`
- 장애 주입 2: TEMP 참조 파일을 한 줄 변경하고 TEMP 지시서 사본이 그 파일을 가리키게 한 뒤 `context-pack-integrity` 실행 → exit 1, `staleCount: 1`
- 원복 확인: `dotnet run --project server -c Release -- context-pack-integrity docs/handoff/queue/directive-LEDGER03-fallback-observability.md` → exit 0
- 추가 정상 표본: `dotnet run --project server -c Release -- context-pack-integrity docs/handoff/queue/directive-LEDGER04-metricid-normalization.md` → exit 0
- `dotnet run --project server -c Release -- measure dev-pack` → exit 0
- 보고서 작성 후 재측정: `dotnet run --project server -c Release -- measure dev-pack` → exit 0

{"gate":"dev-pack","violations":0,"attempt":1}
{"gate":"dev-pack","violations":0,"attempt":2}

## 참조한 스킬
- `skills/common/directive-writing.md`
- `skills/common/executor-launch.md`
- `skills/common/hs-gate.md` 2항
- `skills/common/root-cause-diagnosis.md`
- `skills/common/verification.md`

## 지표는 만족했으나 목적은 미달인 부분
- 하네스 구현과 주입 시험은 완료됐고 `measure dev-pack`은 0 위반이다.
- 다만 실제 큐 전체의 `context-pack-integrity`는 `docs/handoff/QUOTA-POLICY.md` 해시 불일치 2건 때문에 exit 1이다. 원본 지시서(`docs/handoff/queue/`)는 이번 쓰기 영역 밖이고, 하네스는 스탬핑 기능을 넣지 않는 조건이므로 수정하지 않았다.
