# Review 28bc09d / FAIL-2026-013 prompt truncation correction

검수 대상: `28bc09d`
검수자: codex
일시: 2026-07-11 18:30 KST

## 대상 파악

- `28bc09d`는 FAIL-2026-013의 원인을 "세션 미격리"에서 "발사 프롬프트 잘림"으로 전면 정정했다.
- 변경 파일:
  - `docs/wiki/failures/cases/FAIL-2026-013-launch-prompt-truncation.md` 추가
  - `docs/wiki/failures/cases/FAIL-2026-013-launch-session-resume.md` 삭제
  - `docs/handoff/SONNET-QUEUE.md` 발사 규칙 정정
- 추가 QA 정리:
  - 중복으로 남아 있던 `FAIL-2026-013-tier2test-verification-cli-crashes.md`를 제거하고, Tier2Approver 검증 CLI 실패는 `FAIL-2026-014`로 유지했다.
  - `docs/wiki/failures/index.md`를 `FAIL-2026-013-launch-prompt-truncation.md`와 `FAIL-2026-014-tier2test-verification-cli-crashes.md`로 정정했다.

## 독립 재실행

| 항목 | 결과 |
| --- | --- |
| `dotnet build server -c Release` | FAIL. 실행 중 서버 PID 14252, 5312가 Release exe를 잠금. warnings 10, errors 2 |
| `dotnet run --project server -c Release --no-build -- verify-behavior` | PASS. `behaviorEqual:true` |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | exit 1, `violationCount:4` |
| `dotnet run --project server -c Release --no-build -- doc-integrity` | PASS. checked 12, `brokenCount:0` |

## 불변식

- 기준 파일 무수정: `git diff --stat 28bc09d^..28bc09d -- "**/blueprint.json" "**/workflow-definition.json" server/appsettings.json` 빈 결과.
- 영역 격리: `28bc09d` 자체는 문서/위키/큐만 변경.
- 비밀 미포함: `server/appsettings.json` diff 없음.
- 링크/번호 정합성: `FAIL-2026-013`은 prompt truncation, `FAIL-2026-014`는 tier2test runtime error로 분리.

## 주장 vs 실측

- 일치: `SONNET-QUEUE.md`가 FAIL-013 원인을 prompt truncation으로 설명하고 task ID echo-back 도착 확인을 발사 조건에 넣었다.
- 일치: 새 FAIL-013 문서는 `outputs/probe*.log/txt` 계열 실험 근거를 언급한다. 이번 검수에서는 해당 파일 존재를 확인했다.
- 불일치/잔여: `docs/handoff/KNOWN-ISSUES.md`에는 아직 "세션 미격리" 원인 설명이 남아 있다. 최신 FAIL-013 정정과 문서 간 불일치다.
- 잔여: FEAT-01 `WORKSTATE.json`은 아직 tier2test 전체 PASS/measure 3을 주장하지만, 직전 독립 검수에서 `FAIL-2026-014`로 불일치가 확인됐다.

## 판정

판정: 조건부 PASS

이유: `28bc09d`의 핵심 정정 문서와 SONNET-QUEUE 발사 규칙은 새 원인에 맞게 갱신됐다. 다만 `KNOWN-ISSUES.md`에 이전 원인 설명이 남아 있어 후속 정정이 필요하고, FEAT-01 검증 상태 불일치는 `FAIL-2026-014`로 계속 열려 있다.
