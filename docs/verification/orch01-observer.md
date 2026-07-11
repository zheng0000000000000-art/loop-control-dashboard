# ORCH-01 — 오케스트레이터 관측 스캐폴드 검증

## 주체 (actor)

**claude-sonnet-4-6** (ORCH-01 지시서 직접 수행, 2026-07-11 19:00 세션)

직접 경로 사유: 지시서에 "직접 경로" 명시는 없으나, `docs/verification/`, `docs/directives/` 문서 변경은 CLAUDE.md 관례상 직접 경로 허용.

---

## 작업 내용

- `docs/handoff/queue/OrchestratorObserverCli.reference.cs`를 `server/OrchestratorObserverCli.cs`로 이식
- `server/Cli/CliRouter.cs` TryRun에 `orch-observe` 분기 추가 (기존 CLI 분기 패턴)
- 한국어 함수 주석 누락 5개 추가 (measure 위반 해소)

---

## 사용한 하네스와 결과

| 하네스/명령 | 명령 | exit code | 핵심 수치/결과 |
|---|---|---|---|
| `dotnet build server -c Release` | `dotnet build server -c Release` | 0 | 경고 0, 오류 0 |
| `dotnet run --project server -c Release -- orch-observe` | 동좌 | 0 | JSON 스키마 출력, mode:"observe-only", wouldLaunch:false, blockers 2건 |
| `dotnet run --project server -c Release -- verify-behavior` | 동좌 | 0 | behaviorEqual:true |
| `dotnet run --project server -- measure dev-pack` | 동좌 | 1 | violationCount:5 (사전 위반 5건, 내 파일 기인 0건) |
| `gate-clean` 하네스 | `dotnet run --project server -c Release -- gate-clean server` | 1 | contentDirtyCount:2 (CliRouter.cs 수정 + OrchestratorObserverCli.cs 신규 — 예상된 변경) |

### orch-observe 최종 출력 (2026-07-11T19:04:58)

```json
{
  "observedAt": "2026-07-11T19:04:58.5223541+09:00",
  "mode": "observe-only",
  "serverTreeClean": false,
  "representationOnlyCount": 0,
  "executorRunning": true,
  "importPendingCount": 3,
  "workstateDiId": "HOOK-01",
  "inProgress": null,
  "nextWaiting": "FEAT-01 한정 이양(게이트 클린 반입 AI 승인)",
  "completionCheck": null,
  "wouldLaunch": false,
  "wouldLaunchTarget": null,
  "blockers": [
    "server/ 실내용 변경 2건",
    "실행 중 executor(PID 파일 존재)"
  ],
  "note": "관측 전용. wouldLaunch=true여도 이 프로그램은 발사하지 않는다."
}
```

---

## measure 게이트 기록

`{"gate":"dev-pack","violations":5,"attempt":1}`

잔여 위반 목록 (모두 내 allowlist 밖 사전 위반, 수정 불가):

1. `functionsWithoutComment`: 5건 — `docs/handoff/queue/OrchestratorObserverCli.reference.cs` (내 allowlist 밖)
2. `smallTouchTargets`: 1건 — `dashboard/style.css:1133` (dashboard/ 무접촉)
3. `skillDomainViolations`: 2건 — `docs/verification/tuning-advanced.md` (이전 세션 문서, allowlist 밖)
4. `programCsLines`: 2296 — `server/Program.cs` (allowlist 밖)
5. `appJsLines`: 2692 — `dashboard/app.js` (dashboard/ 무접촉)

내 변경(OrchestratorObserverCli.cs) 기인 위반: 0건 (함수 주석 5개 추가 후 해소).

---

## 검수 기준 자가점검표

| # | 기준 | 결과 | 근거 |
|---|---|---|---|
| 1 | `orch-observe` exit 0, 스키마 JSON 출력 | ✓ PASS | exit 0, mode:"observe-only" JSON 출력 확인 |
| 2 | 큐 파싱 정확: inProgress=null, nextWaiting=FEAT-01(현재 최초 대기), wouldLaunch는 blockers에 따라 정확 | ✓ PASS | inProgress:null, nextWaiting:"FEAT-01 한정 이양...", blockers:2건 → wouldLaunch:false |
| 3 | server/ dirty → wouldLaunch=false, blockers에 dirty 포함 | ✓ PASS | "server/ 실내용 변경 2건" blockers에 포함, wouldLaunch:false |
| 4 | `sonnet-active.pid` 존재 → executorRunning=true | ✓ PASS | 파일 존재(git status `??` sonnet-active.pid), executorRunning:true 확인 |
| 5 | verify-behavior behaviorEqual:true, git/outbox/workstate 불변 | ✓ PASS | behaviorEqual:true, 상태 파일 미변경(측정 실행 결과 파일은 measure 명령이 갱신) |
| 6 | 코어 3파일(Engine/Storage/Guardrails) 무접촉, 관측 로직은 신규 파일만 | ✓ PASS | gate-clean 결과: OrchestratorObserverCli.cs(신규)+CliRouter.cs만 변경. 코어 3파일 미포함 |

---

## 참조한 스킬

- AGENT-GUIDE.md (필수)
- docs/directives/_header.md (불변 제약)
- /skills/common/ (해당 파일 미존재 또는 이번 세션 미확인 — CLAUDE.md 지시에 따라 읽으려 했으나 glob 결과 없음)

---

## 안전 불변 확인

- Process.Start(실행자 발사) 없음 ✓
- git commit/push 없음 ✓
- approve/reject 호출 없음 ✓
- 상태 파일 무변경(measure 갱신 제외) ✓
- git 호출: status/log/show 읽기 전용만 ✓
