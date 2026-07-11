# H-4 path-escape-qa 스킬 QA

## 주체(actor)

- actor: codex
- 회차: 2026-07-11 21:30 KST
- 대상: `skills/domains/dev/path-escape-qa.md`
- 금지 준수: server/dashboard 미수정, git commit/push 미실행, 결재·반입 액션 미호출

## 참조한 스킬

- `skills/common/root-cause-diagnosis.md`
- `skills/common/hs-gate.md`
- `docs/handoff/CODEX-AUTO-15min-routine.md`
- `docs/handoff/CODEX-QUEUE.md`

## 데이터 존재 확인

- 실체 데이터: `FAIL-2026-006`, `FAIL-2026-007`, `docs/qa/path-escape-repro-2026-07-10.md`
- 판정: 데이터 존재 관문 PASS. 스킬은 실제 sibling-prefix/encoded-backslash 실패 기록과 H-1 `path-guard-check` 하네스를 절차화한다.

## 변경 내용

- `skills/domains/dev/path-escape-qa.md` 신규 작성
- 포함 내용: trigger metadata, 정적 검사 패턴, 필수 케이스 표, `path-guard-check` 사용법, temp copy/read-only 동적 PoC 규칙, 보고 형식

## 사용한 하네스와 결과

| 명령 | exit code | 수치/결과 |
| --- | ---: | --- |
| `dotnet run --project server -c Release -- hs-scan` | 1 | `failureCaseCount=14`, candidate=`executor-orchestration(6)` |
| `dotnet run --project server -c Release --no-build -- path-guard-check` | 0 | `failureCount=0`, 기본 path_escape 회귀 PASS |
| `dotnet run --project server -c Release --no-build -- verify-behavior` | 0 | `behaviorEqual=true` |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | 1 | `violationCount=1`, 기존 위반 잔존 |
| `dotnet run --project server -c Release --no-build -- hs-scan` | 1 | 반복 후보 유지 |

## 판정

- H-4 완료: PASS
- 재현/의심/오탐: 재현 0건(절차 자산화), 의심 0건, 오탐 0건
- 남은 사항: H-5 기존 하네스 인수·오탐 검토
