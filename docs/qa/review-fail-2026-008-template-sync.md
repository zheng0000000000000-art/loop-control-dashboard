# QA review — FAIL-2026-008 template sync

검수일: 2026-07-11
검수자: codex heartbeat
대상: `FAIL-2026-008` 수정 작업

## 확인한 sonnet 작업

`docs/handoff/WORKSTATE.json` 기준 현재 작업은 `FAIL-2026-008`, `status: done`이다.

변경 대상:

- `server/dispatch-templates/BalanceTunerSearch.txt`
- `server/dispatch-templates/ApplyMeasurementResult.txt`
- `docs/handoff/WORKSTATE.json`
- `docs/verification/fail-2026-008-template-sync.md`

## 독립 재실행

원본 코드 수정 없이 `C:\Users\1\AppData\Local\Temp\lfwd-qa-fail008-fix` 임시 worktree에서 현재 작업트리의 두 템플릿 파일만 복사해 재현했다.

| 단계 | 명령 | 결과 |
| --- | --- | --- |
| 현재 작업트리 기준 빌드 | `dotnet build server -c Release` | 경고 0, 오류 0 |
| temp 기준 빌드 | `dotnet build server -c Release` | 경고 0, 오류 0 |
| self-refactor 템플릿 적용 | `dotnet run --project server -c Release --no-build -- dispatch-executor claude-code "Program.cs Orchestrator.cs ProposalFlow.cs"` | exit 0, `self-refactor templates applied` |
| 적용 후 빌드 | `dotnet build server -c Release` | 경고 0, 오류 0 |
| 적용 후 verify-behavior | `dotnet run --project server -c Release --no-build -- verify-behavior` | `behaviorEqual=true` |
| 적용 후 simtune | `dotnet run --project server -c Release --no-build -- simtune ruined-lab` | exit 0, `restartAttempts=0` |
| 적용 후 measure | `dotnet run --project server -c Release --no-build -- measure dev-pack` | `violationCount=3` |

## 관찰

- `FAIL-2026-008`의 원래 실패 조건인 "self-refactor 템플릿 적용 후 빌드 실패"는 재현되지 않았다.
- `BalanceTunerSearch.txt` 적용본은 `restartAttempts` 인자를 채워 빌드 실패를 해소했다.
- 적용본에는 `TryBestRandomRestart` 헬퍼가 남지만 `RunSearchLoop`에서 호출하지 않아 `restartAttempts`는 0으로 유지된다. 이는 sonnet 검증 문서의 "random restart 없음" 설명과 부합한다.
- `ApplyMeasurementResult.txt` 적용 후 `Program.cs`의 DI-R-04 분할 함수 호출 경로가 유지되고 미사용 경고가 발생하지 않았다.

## 판정

검수 대상: `FAIL-2026-008` 수정 작업
검수자: codex heartbeat
독립 재실행: build=0/0, verify-behavior=true, measure=3
검증문서 주장 vs 실측 불일치: 없음
판정: PASS

## 참조한 스킬

- `skills/common/verification.md`
- `docs/handoff/VERIFY-PROTOCOL-universal.md`
- `docs/handoff/CODEX-ROLE-bug-hunter.md`
- `docs/handoff/COLLAB-STRUCTURE.md`

