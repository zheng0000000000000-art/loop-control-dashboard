# SONNET-QUEUE — 구현 작업 큐 (조율자가 자동 발사)

> 검수자/오케스트레이터가 지시서를 미리 만들어 순서대로 쌓는다. 조율자(5분 태스크)가 "server clean + 이전 항목 커밋됨 + 다음 대기"일 때 다음 지시서를 sonnet에 자동 발사한다. 이렇게 구현 루프가 사람 개입 없이 큐 소진까지 돈다.

## 큐

| 순번 | DI | 지시서 경로(outputs) | 영역 | 상태 |
| --- | --- | --- | --- | --- |
| 1 | FIX-01 경로검증 separator-bounded | directive-FIX01-path-validation.md | server/ | 완료(13f833a) — 조율자 15:03 재검증: server/Storage.cs·OutboxManager.cs에 IsWithinRoot 코드 실재 확인(git grep), 이전 회차들의 "문서 완료주장·코드 미반영" 불일치 해소됨 |
| 2 | FIX-02 measure outbox 스캔 제외 | directive-FIX02-measure-scope.md | server/ | 완료(9a43f54/49b00d6) |
| 3 | FEAT-02 E2E 실사용 하네스 내재화 (dotnet -- e2e-usage) | queue/directive-FEAT02-e2e-harness.md | server/ | **진행** — 2026-07-11 15:47 사람 승인 발사(PID 30036, 격리 발사: 큐 파일 미열람 지시). 로그 outputs/sonnet-FEAT02.out.log |
| 4 | FEAT-01 한정 이양(게이트 클린 반입 AI 승인) | directive-FEAT01-conditional-delegation.md | server/ | 대기 |
| 5 | ORCH-01 오케스트레이터 관측 스캐폴드 (dotnet -- orch-observe) | queue/directive-ORCH01-observer.md (+참조 queue/OrchestratorObserverCli.reference.cs) | server/ | 대기 — 관측 전용(발사·커밋·결재 없음), 비전문서 1단계 코드화 |
| 6 | HARNESS-01 gate-clean (트리 clean을 정규화 내용 해시로 판정) | queue/directive-HARNESS01-gate-clean.md (+참조 queue/GateCleanCli.reference.cs) | server/ | 대기 — HS-GATE 12/12 즉시제작. FAIL-010 회귀 하네스 |
| 7 | HARNESS-02 hs-scan (승격 심사 트리거 기계 탐지) | queue/directive-HARNESS02-hs-scan.md (+참조 queue/HsScanCli.reference.cs) | server/ | 대기 — 승격 파이프라인이 재량이라 한 번도 안 돌던 문제 해소 |
| 8 | **HARNESS-04 gate-audit** (사람 전용 게이트 위반 감사) | queue/directive-HARNESS04-gate-audit.md | server/ | 대기 — **HS-GATE 12/12 최우선**. 북극성 고정점이 12회 깨진 것을 검출. |
| 9 | HARNESS-03 claim-check (자기보고→실체 대조) | queue/directive-HARNESS03-claim-check.md | server/ | 대기 — HS-GATE 11/12. FIX-01 허위 완료주장 3회의 회귀 하네스 |
| 10 | FIX-03 measure 스캔에서 docs/ 제외 | queue/directive-FIX03-measure-scope-docs.md | server/ | 대기 — 검수자 세션이 유발한 위반 13건 해소(FIX-02 동일 계열) |
| 11 | (추후 검수자가 추가) | — | — | — |

## 자동 발사 규칙 (조율자용)

1. **발사 조건 전부 충족 시에만**: ①server/ clean — **raw `git status`로 판정하지 말 것**(줄바꿈 같은 표현 차이만으로 dirty가 되어 게이트가 영구 잠긴다: FAIL-2026-010). HARNESS-01(`dotnet run -- gate-clean server`) exit 0으로 판정한다. 그 하네스가 서기 전까지는 `git diff --ignore-all-space`가 비었는지로 대신 확인한다. ②실행 중 sonnet 프로세스 없음(newest claude가 5분 이상 전 or 로그 완료) ③현재 `진행` 항목의 커밋이 로그에 존재(완료 확인) ④다음 `대기` 항목 존재.
2. 발사: 해당 지시서 경로로 sonnet 헤드리스 실행(FAIL-005 방식: 프롬프트 인자 직접 전달 + RedirectStandardOutput + PID 확인). 발사 후 그 항목 상태를 `진행`으로 기록(review-log).
3. **순차 엄수**: 이미 `진행` 항목이 있으면 새로 발사하지 않는다(동시 발사 = FAIL-004 재발).
4. 큐가 모두 `완료`면 "구현 큐 소진 — 대기" 기록. 검토 루프(코덱스·조율자)는 계속.
5. QUOTA_SIGNAL 감지 시 발사 중단.

## 발사 프롬프트 템플릿

```
다음 지시서를 읽고 그대로 수행하라: <outputs경로>. 시작 전 AGENT-GUIDE.md와 docs/directives/_header.md를 먼저 읽어라. v9 산출물 문서 생성. 빌드·CLI는 dotnet -c Release. 지정 영역만, 타 실행자 영역(dashboard/·docs/qa/·docs/wiki/) 무접촉. git commit/push 금지. 완료 시 수행요약·검수기준 자가점검표 출력. rate limit 시 마지막 줄 QUOTA_SIGNAL.
```

## 상태 갱신
- 조율자가 발사 시 `진행`, 검수·커밋 완료 시 `완료`로 이 표를 갱신(또는 review-log에 기록).
- 검수자(다음 세션 포함)는 이 큐에 새 지시서를 append해 구현 루프에 연료를 공급한다.
