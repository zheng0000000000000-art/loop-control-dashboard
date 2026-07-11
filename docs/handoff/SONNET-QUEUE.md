# SONNET-QUEUE — 구현 작업 큐 (조율자가 자동 발사)

> 검수자/오케스트레이터가 지시서를 미리 만들어 순서대로 쌓는다. 조율자(5분 태스크)가 "server clean + 이전 항목 커밋됨 + 다음 대기"일 때 다음 지시서를 sonnet에 자동 발사한다. 이렇게 구현 루프가 사람 개입 없이 큐 소진까지 돈다.

## 큐

| 순번 | DI | 지시서 경로(전부 `docs/handoff/queue/` 기준) | 영역 | 상태 |
| --- | --- | --- | --- | --- |
| 1 | FIX-01 경로검증 separator-bounded | queue/directive-FIX01-path-validation.md | server/ | 완료(13f833a) — 조율자 15:03 재검증: server/Storage.cs·OutboxManager.cs에 IsWithinRoot 코드 실재 확인(git grep), 이전 회차들의 "문서 완료주장·코드 미반영" 불일치 해소됨 |
| 2 | FIX-02 measure outbox 스캔 제외 | queue/directive-FIX02-measure-scope.md | server/ | 완료(9a43f54/49b00d6) |
| 3 | FEAT-02 E2E 실사용 하네스 내재화 (dotnet -- e2e-usage) | queue/directive-FEAT02-e2e-harness.md | server/ | **완료(a87e484)** — 사람 승인 발사(PID 30036, 격리 발사). 조율자 검수: build 0/0, verify-behavior true, measure 비악화 |
| 4 | FEAT-01 한정 이양(게이트 클린 반입 AI 승인) | queue/directive-FEAT01-conditional-delegation.md | server/ | 대기 |
| 5 | ORCH-01 오케스트레이터 관측 스캐폴드 (dotnet -- orch-observe) | queue/directive-ORCH01-observer.md | server/ | **완료(ee21611)** — 조율자 검수: build 0/0, doc-integrity INTACT(exit0), claim-check ORCH-01 MATCH(exit0), verify-behavior true, measure dev-pack 4건(기준선 5건 대비 비악화) |
| 6 | HARNESS-01 gate-clean (트리 clean을 정규화 내용 해시로 판정) | queue/directive-HARNESS01-gate-clean.md | server/ | **완료** — 검수자 세션 직접 구현·빌드 0/0·장애주입 회귀 통과 |
| 7 | HARNESS-02 hs-scan (승격 심사 트리거 기계 탐지) | queue/directive-HARNESS02-hs-scan.md | server/ | **완료** — 검수자 세션 직접 구현. 실행 시 후보 3건 자동 검출 확인 |
| 8 | ~~HARNESS-04 gate-audit~~ | ~~queue/directive-HARNESS04-gate-audit.md~~ | — | **철회** — 승격 근거가 오판(`[loop]`는 주체 서명이 아님). 하네스 삭제. FAIL-2026-012 |
| 9 | HARNESS-03 claim-check (자기보고→실체 대조) | queue/directive-HARNESS03-claim-check.md | server/ | **완료** — 직접 구현. 허위주장 주입 시 MISMATCH 검출 확인 |
| 10 | ~~FIX-03 measure 스캔에서 docs/ 제외~~ | — | — | **취소** — 참조본을 삭제해 해결(측정 코드 수정은 CLAUDE.md 금지사항). measure 5→3건, 기준선 복귀 |
| 11 | HARNESS-05 doc-integrity (문서 잘림 검출) | (직접 구현) | server/ | **완료** — I-9를 승격으로 뒤집음(3회차 재현). 오탐 0, 주입 잘림 검출 확인 |
| 12 | **ACTOR-01 결재 액션 actor 기록** | queue/directive-ACTOR01-actor-provenance.md | server/ | **완료(a941177 H-6 하네스 수정 + 6929406 ACTOR-01)** — 조율자 재검증: build 0/0, verify-behavior true, measure dev-pack 1건(기준선 3 대비 비악화), claim-check ACTOR-01 MATCH(H-6 수정 후 claimCount 12/mismatch 0). |
| 13 | **HOOK-01 HarnessRegistry 1회성 훅** | queue/directive-HOOK01-harness-registry.md | server/ | **완료(2e28f7a)** — 조율자 18:51 재검증: gate-clean server PASS(exit0), doc-integrity INTACT(exit0), claim-check HOOK-01 MATCH(exit0, mismatchCount 0). r2~r5 재시도 로그는 outputs/에 잔존(정리 필요, 조율자 권한 밖). |
| 15 | **FIX-04 measure 위반 0으로**(원인 제거 — 결재 루프 무한 재생성 차단) | queue/directive-FIX04-measure-zero.md | dashboard/ + docs | **대기** — 사람 승인 완료(2026-07-11). ACTOR-01 완료 후 순차 발사. 대상: smallTouchTargets 1·maxFunctionLength 159·skillDomainViolations 2 (functionsWithoutComment 5건은 검수자가 참조본 삭제로 제거, 1건은 코덱스 몫) |
| 16 | **FIX-05 마지막 measure 위반 제거**(server/BalanceTuner.cs 115줄 함수 분할) | queue/directive-FIX05-balancetuner-split.md | server/ | **완료(ba5f750)** — 조율자 20:32 검수: build exit0(0/0), verify-behavior true, measure dev-pack 1건(기준선 1, 비악화), claim-check FIX-05 MATCH(exit0, claimCount3/mismatch0). 잔여: server/Tier2Approver.cs:38-138(101줄) 새로 노출 — FIX-06 큐 등재됨(다음 지시서 대상) |
| 17 | **FIX-06 server/ 장문 함수 4건 분할** | queue/directive-FIX06-server-long-functions.md | server/ | **대기** — 사람 승인(2026-07-11). Tier2Approver 101·OutboxManager 99·Program 93·Engine 92줄 |
| 18 | **FIX-07 app.js 장문 함수 3건 분할 → measure 0 달성** | queue/directive-FIX07-appjs-long-functions.md | dashboard/ | **대기** — FIX-06 완료 후 순차. app.js 줄수 상한(2692) 유지가 핵심 제약 |
| 19 | **P0-04 Projection 생성기** (`dotnet run -- projection`) — WORKSTATE에서 STATUS·HANDOFF·RUNTIME-INDEX를 **생성**. 손편집 금지 | queue/directive-P004-projection.md (작성 예정) | server/ + docs | **대기** — P0-03(handoff-integrity) 완료 후. 근거: 손으로 쓴 STATUS가 삭제된 파일을 '준비됨'이라 거짓말했다 |
| 20 | (추후 검수자가 추가) | — | — | — |

## 자동 발사 규칙 (조율자용)

1. **발사 조건 전부 충족 시에만**: ①server/ clean — **raw `git status`로 판정하지 말 것**(줄바꿈 같은 표현 차이만으로 dirty가 되어 게이트가 영구 잠긴다: FAIL-2026-010). **`dotnet run --project server -c Release -- gate-clean server` exit 0**으로 판정한다(구현 완료). 전체 하네스 목록·배선은 `docs/handoff/HARNESSES.md`. ②실행 중 sonnet 프로세스 없음(newest claude가 5분 이상 전 or 로그 완료) ③현재 `진행` 항목의 커밋이 로그에 존재(완료 확인) ④다음 `대기` 항목 존재.
2. 발사: sonnet 헤드리스 실행. **네 가지를 모두 해야 발사다**:
   ① 프롬프트 인자 직접 전달 ② **지시 도착 확인 필수** — 프롬프트 첫 줄을 "받은 task ID를 그대로 출력하라"로 하고, **실행자 출력에 그 task ID가 없으면 발사 실패로 간주하고 산출물을 폐기**한다(FAIL-2026-013: 프롬프트가 명령행 인자 경계에서 **잘려서 도착**했다. 실행자는 지시서를 받은 적이 없었고, 저장소를 읽고 알아서 판단해 **안전 보류 항목까지 구현**했다. '지시서 이탈'의 진짜 정체다) ③ RedirectStandardOutput + PID 파일로 실행 확인(FAIL-005) ④ 완료 후 산출물이 지시서 `## 허용 파일 (allowlist)` 범위 안인지 대조(scope-check, HS-06).
   발사 후 그 항목 상태를 `진행`으로 기록(review-log).
3. **순차 엄수**: 이미 `진행` 항목이 있으면 새로 발사하지 않는다(동시 발사 = FAIL-004 재발).
4. 큐가 모두 `완료`면 "구현 큐 소진 — 대기" 기록. 검토 루프(코덱스·조율자)는 계속.
5. QUOTA_SIGNAL 감지 시 발사 중단.

## 발사 프롬프트 템플릿

```
**이전 대화 맥락이 있다면 전부 무시하라. 지금 할 일은 이것뿐이다.** 다음 지시서 하나만 읽고 그대로 수행하라: docs/handoff/queue/<지시서>. 지시서의 `## 허용 파일 (allowlist)` 밖은 수정 금지. 다른 큐/지시서 파일은 읽지 마라. 시작 전 AGENT-GUIDE.md와 docs/directives/_header.md를 먼저 읽어라. v9 산출물 문서 생성. 빌드·CLI는 dotnet -c Release. 지정 영역만, 타 실행자 영역(dashboard/·docs/qa/·docs/wiki/) 무접촉. git commit/push 금지. 완료 시 수행요약·검수기준 자가점검표 출력. **verification 문서에 ①주체(actor: 누가 했는가) ②사용한 하네스와 결과(명령·exit code·수치) ③참조한 스킬을 반드시 기록하라 — 없으면 조율자가 반려한다.** **한도·중단이 임박하면 종료 전에 마지막 세 줄을 출력하라: ``QUOTA_SIGNAL`` / ``CHANGED: <지금까지 수정한 파일 목록>`` / ``NEXT: <다음에 할 일 한 줄>``. 부분 작업물은 되돌리지 말고 그대로 두되 verification 문서에 "상태: 미완(한도)"로 적어라.** 상세: docs/handoff/QUOTA-POLICY.md
```

## 상태 갱신
- 조율자가 발사 시 `진행`, 검수·커밋 완료 시 `완료`로 이 표를 갱신(또는 review-log에 기록).
- 검수자(다음 세션 포함)는 이 큐에 새 지시서를 append해 구현 루프에 연료를 공급한다.
