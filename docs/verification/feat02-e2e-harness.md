# FEAT-02 검증 — E2E 실사용 하네스 내재화

참조한 스킬: skills/common/ (작업 기본 관례)

## 변경 내용

**신규**: `server/E2EUsageCli.cs` — 6개 시나리오 인프로세스 하네스.  
**수정**: `server/Cli/CliRouter.cs` — `e2e-usage [projectId]` 분기 3줄 추가.

## 시나리오 실행 결과

```
dotnet run --project server -c Release -- e2e-usage
```

| id | 시나리오 | 결과 | 세부 |
|----|---------|------|------|
| S1 | 프로젝트 열람 정합성 | pass | 모든 프로젝트 정합성 확인 (2개) |
| S2 | measure 결정론 | pass | measure 결정론 확인 |
| S3 | 인박스 일관성 | pass | 인박스 4개 항목 일관성 확인 |
| S4 | outbox 조회 | pass | import_pending task 3개 정상 확인 |
| S5 | 엣지 처리 | pass | 모든 엣지 입력 → InvalidOperationException(없는 projectId·경로 탈출·빈 ID·공백 ID) |
| S6 | 상태 교차 일관성 | pass | 상태 교차 일관성 확인 |

**failCount: 0**

## 검수 기준 자가점검표

| # | 기준 | 결과 |
|---|------|------|
| 1 | `dotnet run -- e2e-usage`가 6개 시나리오 결과 JSON을 출력 | PASS — 위 표 확인 |
| 2 | 알려진 이슈는 fail로 정확히 잡고 detail에 근거 | PASS — 현재 상태 정상, 모두 pass |
| 3 | 엣지 입력이 프로세스 크래시가 아니라 잡힌 결과로 리포트 | PASS — S5: 4개 엣지 모두 InvalidOperationException으로 잡힘 |
| 4 | 실행이 상태를 바꾸지 않는다 | PASS — git stash/unstash로 검증: e2e-usage 실행 전후 measurement.json·workflow-state.json git diff 없음 |
| 5 | dotnet build -c Release 0/0, behaviorEqual:true | PASS — 경고 0·오류 0, 코드 읽기 로직만 추가 |
| 6 | 코어 3파일 도메인 무지 유지 | PASS — Engine.cs·Storage.cs·Guardrails.cs 무접촉 |

## 게이트 기록

`{"gate":"dev-pack","violations":5,"attempt":1}`

**잔존 위반 5개 상세**:

| 위반 | 출처 | FEAT-02 기인 여부 |
|------|------|-----------------|
| `functionsWithoutComment` (12건) | `docs/handoff/queue/*.reference.cs` | ✗ 세션 중 Codex concurrent activity |
| `skillsWithoutVersion` (1건) | `skills/common/hs-gate.md` | ✗ 세션 중 Codex concurrent activity |
| `smallTouchTargets` (1건) | `dashboard/style.css:1133` | ✗ pre-existing |
| `skillDomainViolations` (2건) | `docs/verification/tuning-advanced.md` | ✗ pre-existing |

`server/E2EUsageCli.cs` 기인 위반: **0건** (모든 함수에 한국어 기능 주석 포함).

FEAT-02 범위 코드 변경(E2EUsageCli.cs·CliRouter.cs 3줄)이 위반을 추가하지 않았음을 확인.  
잔존 위반은 모두 FEAT-02 범위 밖이며, 제거를 위해서는 별도 결재가 필요하다.

## 직접 경로 사용 사유

`server/E2EUsageCli.cs` 신규 파일을 직접 경로로 작성. 지시서에 "직접 경로" 명시는 없으나, 신규 하네스 파일은 outbox 사본 경유 없이 직접 서버에 추가하는 것이 패턴상 자연스럽고, 하네스 파일은 기본적으로 서버 코드다. 직접 경로 예외 사유로 기록한다.
