# FIX-06 검증 문서 — server/ 장문 함수 일괄 분할

## ① 주체 (actor)

- **sonnet** (Claude Sonnet 4.6, claude-sonnet-4-6) — 직접 경로 수행
- 사유: 지시서에 "직접 경로" 명시 없음. 단, CLAUDE.md 관례상 docs/verification 문서는 직접 경로 허용.

## ② 사용한 하네스와 결과

| 하네스 | 명령 | exit code | 핵심 수치 |
| --- | --- | --- | --- |
| measure (Tier2Approver.cs 후) | `dotnet run --project server -- measure dev-pack` | 1 | violationCount=1, maxFunctionLength=99(app.js만) |
| measure (OutboxManager.cs 후) | `dotnet run --project server -- measure dev-pack` | 1 | violationCount=1, maxFunctionLength=99(app.js만) |
| measure (Program.cs 후) | `dotnet run --project server -- measure dev-pack` | 1 | violationCount=1, maxFunctionLength=99(app.js만) |
| measure (Engine.cs 후) | `dotnet run --project server -- measure dev-pack` | 1 | violationCount=1, maxFunctionLength=99(app.js만), functionsWithoutComment=0 |
| verify-behavior | `dotnet run --project server -- verify-behavior` | 0 | behaviorEqual: true |
| build-verify | `dotnet run --project server -- build-verify` | 0 | verdict: PASS, 경고 0, 오류 0 |

## ③ 참조한 스킬

- `skills/common/root-cause-diagnosis.md`

## 분할 내역

### 1. server/Tier2Approver.cs (101줄 → 71줄)

- `MaybeAutoApprove` 3-오버로드 클러스터(38-138줄=101줄)에서 try/catch 블록(31줄)을 추출
- 신규 헬퍼: `ApplyImportAndRecord(...)` — "반입을 실행하고 위반 변화 이상 감지 시 정지·감사 기록한다."

### 2. server/OutboxManager.cs (99줄 → 73줄)

- `DispatchAsync`에서 meta 필드 할당 블록(14줄)과 파일 기록 블록(14줄)을 추출
- 신규 헬퍼: `AttachExecutionMeta(...)` — "meta에 실행 추적 필드와 컨텍스트 예산을 추가한다."
- 신규 헬퍼: `WriteDispatchFiles(...)` — "실행 결과 파일을 task 디렉터리에 기록한다."

### 3. server/Program.cs (93줄 → 72줄)

- `Approve`에서 nextStage/approvePatch 계산 블록(22줄)을 추출
- 신규 헬퍼: `ComputeNextStagePatch(...)` — "다음 단계 전환 패치와 적용 단계 진입 여부·nextStageId를 계산한다."

### 4. server/Engine.cs (92줄 → 65줄)

- `ApplyStatePatch`에서 stageStatuses 블록(15줄)과 blockInfo 블록(20줄)을 추출
- 신규 헬퍼: `ApplyStageStatuses(...)` — "단계 상태 묶음을 nextState에 적용한다."
- 신규 헬퍼: `ApplyBlockInfo(...)` — "차단 정보 묶음을 nextState에 적용한다."

## 게이트 기록

```json
{"gate":"dev-pack","violations":1,"attempt":4}
```

violations=1은 `dashboard/app.js:751-849`(99줄) 때문 — FIX-07 담당이므로 유지. server/ 파일은 evidence에서 전부 사라짐.

## 검수 기준 자가점검

| 기준 | 결과 |
| --- | --- |
| maxFunctionLength evidence에 server/ 없음 | ✓ |
| functionsWithoutComment = 0 | ✓ |
| verify-behavior: behaviorEqual true | ✓ (exit 0) |
| build-verify: verdict PASS | ✓ (exit 0) |
| blueprint.json·workflow-definition.json·DevPackMeasures.cs 무수정 | ✓ |
| 코어 파일에 도메인 지식 미유입 | ✓ (Engine.cs는 순수 구조 분할만) |
| git commit/push 금지 | ✓ |

## FIX-07 인계

남는 위반: `maxFunctionLength=99`, evidence: `dashboard/app.js:751-849`. FIX-07 담당.
