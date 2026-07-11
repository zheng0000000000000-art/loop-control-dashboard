# ORCH-01 — 오케스트레이터 관측 스캐폴드 (dotnet run -- orch-observe)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: harness / observe-only. 근거: `ORCHESTRATOR-PROGRAM-VISION.md`의 1단계(프로토타입·엣지케이스 학습)를 코드로 내린다. **발사·커밋·결재는 하지 않는다** — 오케스트레이터가 "무엇을 할지"를 계산·기록만 하는 관측 링을 먼저 세워, 이후 실제 발사 단계의 사양을 데이터로 만든다.

## 왜 관측 전용부터인가
비전 문서 고정점: 결재·기준변경·이양은 프로그램화 이후에도 사람. 그리고 발사 자체가 가장 위험(I-1 지시서 어긋남, FAIL-004 동시발사, PID 오판 데드락). 따라서 **관측 → 발사 결정 계산 → (사람 확인) → 실제 발사**로 단계를 쪼갠다. 이번 범위는 첫 두 개까지.

## 전제 조건
server/ clean. 순차(동시 server/ 작업 금지 — FAIL-004).

## 목표
읽기 전용 CLI `orch-observe`를 만든다. SONNET-QUEUE 자동발사 규칙을 코드로 계산해 "지금이라면 무엇을 발사할지(wouldLaunch)"와 그 차단 사유(blockers)를 JSON으로 출력한다. **어떤 발사·커밋·결재도 수행하지 않는다.**

## 작업
1. 참조 스캐폴드 `docs/handoff/queue/OrchestratorObserverCli.reference.cs`를 `server/OrchestratorObserverCli.cs`로 이식하고, `server/Cli/CliRouter.cs TryRun`에 `orch-observe` 분기를 등록(기존 CLI 분기 패턴 그대로).
2. 관측 입력(모두 read-only):
   - SONNET-QUEUE.md 큐 표 파싱(순번·DI·상태).
   - `git status --porcelain -- server` 로 server/ clean 여부.
   - 실행 감지: **`sonnet-active.pid` 파일 존재로 판정**(claude.exe StartTime 기반 금지 — I-1/데드락 교훈).
   - outbox/*/meta.json 의 `import_pending` 수.
   - WORKSTATE.json 의 현재 diId.
3. 발사 결정 계산(계산만): 진행 항목 유무, 다음 대기 항목, 진행 항목 커밋의 로그 존재 여부(발사↔완료 task ID 결속), 차단 사유 목록, `wouldLaunch` 불리언.
4. JSON 출력: `{observedAt, mode:"observe-only", serverTreeClean, executorRunning, importPendingCount, workstateDiId, queue[], inProgress, nextWaiting, completionCheck, wouldLaunch, wouldLaunchTarget, blockers[], note}`.
5. **안전 불변**: `Process.Start`로 실행자를 띄우지 않는다. git commit/push/approve 미호출. 상태 파일 무변경. git 호출은 읽기 전용(status/log)만.

## 검수 기준 (검증 가능 6개)
1. `dotnet run --project server -c Release -- orch-observe` 가 위 스키마 JSON을 출력하고 exit 0.
2. 큐 파싱이 정확: 현재 SONNET-QUEUE(#1·#2 완료, #3·#4 대기)에서 nextWaiting=FEAT-02, inProgress=null, wouldLaunch는 blockers(실행 executor·dirty 등)에 따라 정확.
3. server/ dirty로 만들면 wouldLaunch=false + blockers에 "server/ dirty" 포함(반대는 반대).
4. `sonnet-active.pid`를 만들면 executorRunning=true로 잡히고, 지우면 false(StartTime에 의존하지 않음 확인).
5. 실행 전후 `git status`·outbox·workstate 불변(부작용 0). `dotnet build server -c Release` 0/0, `verify-behavior` behaviorEqual:true.
6. 코어 3파일(Engine/Storage/Guardrails) 무접촉. 관측 로직은 신규 파일에만.

## v9 산출물
WORKSTATE 갱신(diId ORCH-01), `docs/verification/orch01-observer.md`(6기준 실측), `docs/directives/ORCH01-observer.md` 보관.

## 후속 (이번 범위 아님, 각각 별 지시서)
- ORCH-02: 관측 리포트를 5분 주기로 append(스케줄→이후 C# 타이머). 여전히 발사 안 함.
- ORCH-03: 자식 프로세스 **핸들 직접 소유** 발사(PID 파일 은퇴) + task ID 결속 강제. **발사는 사람 활성 플래그가 있을 때만.**
- 결재·반입·기준변경은 프로그램화 이후에도 사람(불변).

## 경계 / 보고
server/ + 위 문서만. dashboard/·docs/qa/·docs/wiki/ 무접촉. git commit/push 금지. `-c Release`. stdout에 수행요약·검수기준 자가점검표·orch-observe 실행결과 JSON 출력. rate limit 시 마지막 줄 QUOTA_SIGNAL.
