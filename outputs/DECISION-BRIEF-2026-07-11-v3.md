# 결정 브리핑 v3 — 2026-07-11 19:2x (검수자 세션, 전부 실측)

> 이 문서가 사람 결재의 **정본 목록**이다. HUMAN-INBOX.md는 동시 쓰기로 세 번 손상됐고 중복·해소 항목이 뒤섞여 있어, 이번 회차 결정 대상만 여기로 추린다.
> 판정은 전부 실체(exit code·코드 호출부·프로세스·git)로 확인했다. 문서 주장은 근거로 쓰지 않았다.

---

## [해소 19:2x] ★ 0순위였던 "결재 경로가 닫혀 있다" — 원인은 **서버 OFF**였다

- **해소 방법**: 서버를 `bin/Release` **밖**(`C:\Users\1\wf-server-run`)으로 빌드해 `--contentroot server` 로 기동(PID 30040, :5173). **이렇게 하면 서버 실행 중에도 `dotnet build server -c Release`가 exit 0** — 결재(서버)와 검증(빌드)이 서로를 배제하던 **I-3이 풀렸다.** 앞으로 서버는 이 방식으로 띄운다.
- **결과**: `reject-import` 3건 전부 HTTP 200. outbox 반입 3건 → `rejected`(19:24:15~19, 사람이 대시보드에서 직접 실행). **1항 해소.**
- **토큰**: 대시보드 프롬프트에 `RemoteActionToken` 값(`1`)을 넣어야 결재가 통과한다(`RequireDispatchToken`). 코드 버그는 없었다.
- **주체 확인(추측 아님, 사람 본인 진술)**: 19:24:28 `[loop] ... approve proposal-1783765207587`과 반입 거절 3건 **둘 다 사람이 눌렀다.** 무인 결재는 없었다. **그러나 데이터로는 여전히 구분 불가였다 — 물어봐서 알았다.** ACTOR-01 필요성의 네 번째 실증.

---

## (원문 보존) 0순위 — 결재 경로 자체가 닫혀 있었다

**증상**: 대시보드에서 outbox 반입 **승인도 거절도 안 된다.**

**실측한 원인 2개**

1. **서버가 꺼져 있다.** 리슨 포트 0개, `dotnet`/`server` 프로세스 없음. 승인·거절은 `POST /api/projects/{p}/outbox/{taskId}/approve-import|reject-import`라서 서버 없이는 **물리적으로 불가**. (서버를 끈 이유는 Release exe 락으로 빌드가 막혀서였다 — I-3)
2. **토큰이 필요하다.** `OutboxManager.ApproveImport`·`RejectImport` 첫 줄이 `RequireDispatchToken(configuredToken, providedToken)`. 대시보드가 묻는 토큰에 `server/appsettings.json`의 `RemoteActionToken` 값(**현재 `"1"`**)을 넣어야 하고, 취소하면 **401**로 막힌다.

**사람이 할 일**

- 서버 기동: `dotnet run --project server -c Release` (BindUrls `http://localhost:5173`)
- 대시보드 토큰 프롬프트에 `1` 입력
- **단, 켜기 전에 아래 0-B를 먼저 결정할 것.**

**구조 문제(사람 판단)**: 결재는 "항상 사람"인데, 그 사람이 결재하려면 **서버가 떠 있어야** 하고 서버가 뜨면 **빌드·하네스가 exe 락으로 막힌다.** 결재와 검증이 서로를 배제한다. 해법 후보: ①결재용 CLI를 별도로(빌드 락과 무관) ②서버를 별도 출력 경로로 실행 ③결재 시간대를 분리.

---

## ★ 0-B순위 — FEAT-01은 "보류"인데 **코드에는 이미 켜져 있다** (신규 발견, 모순)

> **[2026-07-11 19:3x 실측 관측 — 사람 지시로 `Enabled: true` 유지한 채 서버 기동]**
> - **AI 자동 반입 실적: 0건.** outbox 이력 전체에서 Tier2 시도 2건(`task-20260710022709604`, `022909920`), 둘 다 qwen3:14b 판정 `reject` → `decision: reviewed_not_approved`. **AI가 반입을 실행한 적은 아직 없다.**
> - **그러나 관문은 열려 있다**: 두 건 모두 `eligible: true`, `eligibilityReasons: []`. 즉 **적격 심사는 통과했고 모델 판단만이 유일한 방어선이다.** 모델이 한 번 approve하면 그 순간 사람 없이 반입된다(`OutboxManager.cs:128` → `ApplyAutoImport`).
> - ollama(11434)는 **실제로 떠 있다** — 경로는 살아 있는 상태다.
> - **판단**: 지금 당장의 사고는 없지만, 안전장치가 "qwen3가 계속 거절해주는 것"뿐이다. 이건 안전장치가 아니라 **운(運)이다.** 유지하려면 최소한 `DailyCap`·감사기록·되돌리기 경로가 실증돼야 한다(FEAT-01 지시서 요구사항).

- `server/OutboxManager.cs:128` — 반입 대기(`import_pending`)가 생기면 `tier2Approver.MaybeAutoApprove(this, taskId, taskDirectory, meta)`가 돌고, AI(ollama)가 승인하면 `ApplyAutoImport()`로 **사람 없이 반입이 실행된다.**
- `server/appsettings.json` — `"Tier2Approver": { "Enabled": true, "DailyCap": 5, "Model": "qwen3:14b" }`. **활성 상태.**
- 그런데 SONNET-QUEUE #4 FEAT-01(= 이 기능)은 **"사람 결재 대기·안전 보류"**로 막혀 있다. 문서와 코드가 정반대다.
- **서버를 켜는 순간 이 경로가 살아난다.** 기존 3건은 dispatch 시점 훅이라 즉시 반입되진 않지만, 새 dispatch가 생기면 AI가 반입을 결정한다.

**사람 결정**: (a) `Enabled: false`로 내리고 FEAT-01을 계속 보류 (b) 활성 유지 = FEAT-01 사실상 승인 → 문서를 코드에 맞춘다. **어느 쪽이든 문서와 코드를 일치시켜야 한다.** 지금은 둘 다 아니어서 아무도 진실을 모른다.

---

## 1. outbox 반입 3건 — 거절 권장 (사람이 실행)

| taskId | 상태 | 비고 |
| --- | --- | --- |
| `task-20260710022909920` | import_pending | |
| `task-20260710070612000` | import_pending | 리팩토링 이전 Program.cs base — **stale** |
| `task-20260710090000000` | import_pending | 리팩토링 이전 base — **stale** |

- **거절은 코드상 막히지 않는다**: `RejectImport`에는 `VerifyFreshBase` 검사가 없다(승인에만 있다). 3건 전부 거절 가능.
- 승인을 시도하면 stale base 2건은 어차피 실패하거나 위험하다 — 리팩토링 후 CliRouter 기준으로 재제출받는 게 맞다.
- **검수자·조율자는 이 액션을 대행하지 않는다**(CLAUDE.md 금지선).

## 2. ORCH-01 산출물 — 폐기? 유지? (규칙 vs 실체 충돌)

- 실체는 전부 정상: 커밋 `ee21611` 5파일 **전부 allowlist 내** / `claim-check ORCH-01` MATCH(3/3, exit 0) / build exit 0 / `gate-clean` PASS / `orch-observe` 실동작 exit 0 / verification 문서에 actor·하네스 exit·스킬 기록 완비.
- **그런데 `ACK-ORCH-01` 에코백이 출력에 없다.** 규칙(executor-launch, SONNET-QUEUE 발사규칙 2-②)은 "ACK 없으면 발사 실패 → 산출물 폐기"다.
- **검수자 판단(권고)**: ACK 규칙 쪽 결함이다. `claude -p`는 stdout에 **최종 메시지만** 내보내므로 모델이 수행요약부터 쓰면 "맨 첫 줄 ACK"가 사라진다(로그가 `## 수행 요약`으로 시작한다). 지시서 allowlist를 정확히 지킨 것 자체가 **지시 도착의 실체 증거**다.
- **사람 결정**: (a) 유지 + ACK 규칙 수정(권고) (b) 규칙대로 폐기(되돌림).
- **어느 쪽이든 코덱스 H-00(`launch-check`)에 이 실측을 넘겨야 한다** — 안 넘기면 그 하네스가 정상 산출물을 전부 오탐으로 죽인다.

## 3. push 대기 8건

`git log origin/main..HEAD` 8건(로컬 커밋만, push 없음). 사람 배치 승인 사항.

## 4. dev-pack proposal 결재

`dashboard/data/dev-pack/patch-proposal.json`의 제안이 계속 리비전으로 갈아치워지는 중(ollama/qwen3·rule-engine 생성). **결재 대상이 움직인다** — `measure` 실행이 새 제안을 만드는 부수 효과가 있다. 승인/거절 또는 "제안 생성 중단"을 결정해야 한다.

## 5. ACTOR-01 — 결재·파일변경에 주체(actor) 기록 (기준 변경 성격)

오늘 세 번 "주체 미상"에 부딪혔고, 그때마다 검수자가 프록시로 추측하다 오귀인했다(FAIL-2026-012). 지시서 초안: `docs/handoff/queue/directive-ACTOR01-actor-provenance.md` (SONNET-QUEUE #12, 승인 전 발사 금지).

## 6. `outputs/quarantine/` 3건 처리

`FEAT01-unauthorized-Tier2Approver.patch` · `Tier2Approver.cs.asfound` · `Tier2ApproverTestCli-unknown-actor.patch` — 지시서 범위를 벗어나 구현된 FEAT-01 코드의 격리본. **0-B 결정과 함께 처리**(살릴지 버릴지).

## 7. `reviewer-session` identity 커밋 허용 여부

검수자 세션이 `reviewer-session <reviewer-session@local>` 이름으로 로컬 커밋해왔다(push 없음). 계속 허용할지, 모든 커밋을 조율자에게 넘길지.

## 8. HUMAN-INBOX 동시 쓰기 손상 (구조 결함)

조율자와 검수자가 같은 파일에 무잠금 append → 오늘 3회 손상. `doc-integrity`는 **끝 잘림만** 잡고 중간 스플라이스는 못 잡는다. 해법: ①주체별 파일 분리 ②단일 기록자 ③append 잠금.

---

## 검수자가 아직 안 한 것 (내 몫, 사람 결재 아님)

- **`measure` 위반 = 4인데 여러 문서가 3으로 적어놨다** → 정정 필요. `measure` 실행이 새 proposal을 생성하는 부수효과가 있어, 결재 대기열을 더 늘리지 않으려고 미뤄뒀다(4항과 함께 처리).
- **`orch-observe`의 blockers에 "사람 안전보류" 개념이 없다** — 지금 `wouldLaunch: true, target: FEAT-01`로 계산한다. 관측 전용이라 무해하지만, **ORCH-03(실제 발사)까지 이대로 가면 안전보류 항목을 자동 발사한다.** ORCH-02 지시서에 반영 예정.
- **복구한 `docs/handoff/queue/OrchestratorObserverCli.reference.cs`가 untracked** — 조율자 커밋 목록 밖이라 방치되면 다음 세션이 또 못 찾는다(오늘 그래서 발사 직전에 막혔다).

## 이번 세션에 처리 완료

- ORCH-01 발사·완료(커밋 `ee21611`, 조율자 검수) — 위 2항 판단만 남음.
- 코덱스 heartbeat 프롬프트 교체(`~/.codex/automations/codex-15-qa`) — 하네스·스킬 제작 재가동. 검증: 다음 회차 SESSION에 `hs-scan` exit code + "픽업 H-00"이 찍히는지.
- stale `sonnet-active.pid`(죽은 PID 11060) 삭제 — `orch-observe`가 `executorRunning: true`로 영구 오판하던 것 해소.
- `outputs/` 실패 실험 잔재 21개 정리(quarantine 3건은 결재 대기라 보존).
