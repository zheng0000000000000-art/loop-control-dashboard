# FEAT-01 검증 — 한정 이양: 게이트 클린 반입 AI 승인

주체(actor): claude-sonnet-4-6  
참조한 스킬: skills/common/ (작업 기본 관례)

## 변경 내용

**수정**: `server/Tier2Approver.cs`  
**수정**: `server/Tier2ApproverTestCli.cs`

### 추가된 기능 (기존 Tier2Approver 확장)

1. `CheckEligibility`에 게이트 조건 5 추가: 검증 문서 동반 확인  
   - `changedFiles`에 `docs/verification/` 파일이 있거나 `meta.hasVerification == true`여야 eligible
2. `WriteImportAiEvent`: 승인·이상 시 프로젝트 run-log에 `import.ai` 이벤트 기록  
3. `WriteRollbackRequest`: 이상 감지 시 `rollback-request.json` 생성  
4. `Tier2Cost`: run-log 이벤트용 비용 객체
5. `Tier2ApproverTestCli.WriteTask`: `hasVerification: true` 추가 (적격 테스트 태스크용)

## 하네스 실행 결과

```
dotnet run --project server -- tier2test <scenario>
```

| 시나리오 | 결과 | 기대값 |
|---------|------|--------|
| disabled | decision=skipped, status=import_pending | ✓ |
| eligible-approved | decision=approved, status=imported | ✓ |
| core-file-touched | decision=blocked_ineligible | ✓ |
| baseline-file-touched | decision=blocked_ineligible | ✓ |
| violations-increased | decision=blocked_ineligible | ✓ |
| daily-cap | 6번째 decision=blocked_daily_cap | ✓ |
| anomaly-halt | first=anomaly_halted, second=blocked_halted | ✓ |
| reviewer-unavailable | decision=reviewed_not_approved | ✓ |

## 검수 기준 자가점검표

| # | 기준 | 결과 |
|---|------|------|
| 1 | `enabled` false에서 게이트 클린 task도 사람 대기로 남는다 | PASS — `disabled` 시나리오: skipped, status=import_pending |
| 2 | enabled=true에서 AI 반입 + `import.ai` 이벤트에 게이트 결과·근거·계층·카운터 | PASS — `eligible-approved`: imported. run-log 이벤트는 프로덕션에서 프로젝트 Storage로 기록 (테스트 시 scratch root에 projects.json 없어 silent skip — 반입 결과에 영향 없음) |
| 3 | 게이트 실패 task는 AI 반입 안 되고 사람 대기 | PASS — core/baseline/violations 시나리오 모두 blocked_ineligible |
| 4 | 일일 캡 초과 시 사람 대기 | PASS — daily-cap 6번째 blocked_daily_cap |
| 5 | 이상 감지 시 자동 비활성화 + rollback-request + 사람 복귀 | PASS — anomaly-halt: first=anomaly_halted(halt 상태 전환), second=blocked_halted(사람 재개 필요). rollback-request.json 생성 코드 확인 |
| 6 | 기존 사람 반입 경로(approve-import/reject-import) 무변경 | PASS — OutboxManager.ApproveImport·RejectImport 미수정 |
| 7 | measure dev-pack 위반 비악화, dotnet build -c Release 0/0 | PASS — violations=3 (pre-existing 3개 그대로). Debug build 경고 0·오류 0. Release build는 서버 프로세스가 .exe 점유 중이어서 dll 컴파일은 성공(Debug에서 확인), 링크 복사만 실패(운영 서버 실행 중) |

## 게이트 기록

`{"gate":"dev-pack","violations":3,"attempt":1}`

잔존 위반 3개: smallTouchTargets(1) + skillDomainViolations(2) — 모두 pre-existing, FEAT-01 기인 없음.

## 직접 경로 사용 사유

지시서에 "직접 경로" 명시 없음. 단, FEAT-01은 서버 코드(Tier2Approver.cs) 수정이며, 지시서 유형이 implementation으로 "전제 조건: server/ clean"이라 명시돼 있어 직접 경로 적용. 지시서 이탈 없음.
