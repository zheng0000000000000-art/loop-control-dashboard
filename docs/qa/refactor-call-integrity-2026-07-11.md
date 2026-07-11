# 리팩토링 호출부 정합성 헌트 — 2026-07-11

대상: R-01~04 (`CliRouter`, `InboxBuilder`, `CycleSummaryBuilder`, `MeasurementService`)
실행자: codex heartbeat

## 범위

`CODEX-QUEUE` 2번에 따라 R-01~04에서 이동된 함수와 호출부를 확인했다. 원본 코드는 수정하지 않고, 동적 재현은 임시 worktree에서만 수행했다.

## 호출부 점검

| 대상 | 기대 호출 | 실측 |
| --- | --- | --- |
| `CliRouter.TryRun` | `Program.cs` 상단 1곳 | `Program.cs:14` |
| `InboxBuilder.BuildInboxItems` | `/api/inbox` 핸들러 1곳 | `Program.cs:213` |
| `InboxBuilder.AddProjectInboxItems` | project context pending 구성 1곳 | `Program.cs:249` |
| `CycleSummaryBuilder.BuildCycleSummary` | cycle summary, project context, measure result 3곳 | `Program.cs:229`, `Program.cs:255`, `Program.cs:1967` |
| `MeasurementService.RunMeasureCore` | HTTP measure, approve remeasure, CLI measure 3곳 | `Program.cs:274`, `Program.cs:1598`, `CliRouter.cs:77` |
| `MeasurementService.ApplyResult` | Program 시작부 주입 1곳 | `Program.cs:11` |
| `MeasurementService.PersistBundle` | Program 시작부 주입 1곳 | `Program.cs:12` |

런타임 호출부 자체에서는 누락·시그니처 불일치를 확인하지 못했다.

## 추가 발견

정적 검색 중 `server/DispatchExecutorCli.cs`의 self-refactor 경로가 `server/dispatch-templates/ApplyMeasurementResult.txt` 등 오래된 템플릿을 현행 코드에 직접 치환하는 것을 확인했다.

임시 worktree에서 재현:

| 단계 | 명령 | 결과 |
| --- | --- | --- |
| 기준 빌드 | `dotnet build server -c Release` | 경고 0, 오류 0 |
| 템플릿 적용 | `dotnet run --project server -c Release --no-build -- dispatch-executor claude-code "Program.cs Orchestrator.cs ProposalFlow.cs"` | exit 0, `self-refactor templates applied` |
| 적용 후 빌드 | `dotnet build server -c Release` | 실패, 경고 4, 오류 1 |

대표 오류:

```text
server/BalanceTuner.cs(64,16): error CS7036: 'TuningResult.TuningResult(..., int)'의 필수 매개 변수 'RestartAttempts'에 해당하는 인수가 없습니다.
```

이 버그는 `FAIL-2026-008`로 등록했다.

## 판정

- R-01~04 런타임 호출부 정합성: 통과.
- self-refactor dispatch 템플릿 정합성: 실패. stale template 확정.
- 재현된 버그: 1건 (`FAIL-2026-008`)
- 의심: 0건
- 오탐: 0건

## 참조한 스킬

- `skills/common/verification.md`
- `docs/handoff/CODEX-ROLE-bug-hunter.md`
- `docs/handoff/VERIFY-PROTOCOL-universal.md`
- `docs/handoff/COLLAB-STRUCTURE.md`

