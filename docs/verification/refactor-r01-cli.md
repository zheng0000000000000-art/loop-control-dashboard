# DI-R-01 검증 보고서 — CLI 분리 (Program.cs 해체 1/4)

실행일: 2026-07-10  
실행자: claude-sonnet-4-6

## 검수 기준 6개 실측

### 1. `server/Cli/CliRouter.cs` 존재 + 8개 CLI 명령 처리

생성됨. TryRun()이 아래 8개 명령을 처리한다:
- snapshot-behavior → BehaviorSnapshotCli.Snapshot()
- verify-behavior → BehaviorSnapshotCli.Verify()
- dispatch-executor → DispatchExecutorCli.Run(args)
- measure → RunMeasureCli(args) (CliRouter 내부)
- simtest → RunSimTestCli(args) (CliRouter 내부)
- simtune → RunSimTuneCli(args) (CliRouter 내부)
- refeedbacktest → RunRefeedbackTestCli() (CliRouter 내부)
- tier2test → Tier2ApproverTestCli.Run(args)

**결과: ✓ 통과**

### 2. Program.cs 상단 CLI 분기 대체 + 줄수 감소

- 변경 전: 2801줄 (밴드 [0,2661] 위반)
- 변경 후: **2517줄** (밴드 복귀 ✓)
- 상단 8개 if-블록 → `if (CliRouter.TryRun(args) is int code) return code;` 1줄

**결과: ✓ 통과**

### 3. verify-behavior 동일 확인

```
{"behaviorEqual":true,"snapshot":"docs/behavior-snapshot.json"}
```

**결과: ✓ 통과 (behaviorEqual=true, exit 0)**

### 4. 표본 대조 (measure·simtest·refeedbacktest)

| 명령 | 이동 전 기대 | 이동 후 결과 |
|------|-------------|-------------|
| measure dev-pack | violationCount:4 | violationCount:3 (programCsLines 해소로 감소) |
| simtest ruined-lab | reproducible:true | reproducible:true ✓ |
| refeedbacktest | escalated:true, verdict:uncertain | escalated:true, verdict:uncertain ✓ |

simtune는 장시간 실행이므로 기동 확인 수준으로 대체 (빌드 통과로 컴파일 정상 확인).

**결과: ✓ 통과**

### 5. 빌드 경고 0·오류 0 + 코어 3파일 무접촉

```
dotnet build server -c Release → 경고 0개, 오류 0개
rg -in "budget|contextBytes" server/Engine.cs server/Storage.cs server/Guardrails.cs → 출력 없음 (무접촉 ✓)
```

**결과: ✓ 통과**

### 6. measure dev-pack 위반 수 비악화 (가능하면 감소)

- 기준선: 4 violations
- 이동 후: **3 violations** (programCsLines 위반 해소로 1개 감소)

**결과: ✓ 통과 (감소)**

---

## WORKSTATE 발췌

```json
{"diId":"DI-R-01","status":"verifying","measureViolationsBefore":4,"measureViolationsAfter":3}
```

게이트 기록: `{"gate":"dev-pack","violations":3,"attempt":1}`

---

## 구현 경계 메모 (추측 진행 없음)

- **직접 경로 사용**: 지시서에 `docs/verification/` 보고서 작성이 명시됐으므로 직접 경로 허용(관례·가이드 문서).
- **설계 결정**: C# top-level static 함수는 Program.cs의 `<Main>` 로컬 함수로 컴파일되므로 외부 클래스에서 직접 호출 불가. 이를 해결하기 위해:
  - `RunMeasureCore`: `Func<>` 위임자 주입 (`CliRouter.MeasureCore`)
  - `TryEscalateInsufficientRefeedback`: `delegate` 주입 (`CliRouter.EscalateRefeedback`)
  - `Number()`: 1줄 순수 함수이므로 CliRouter 내부 private 복사본 사용
  - 위 접근이 "함수 본문 로직 불변" 제약을 만족하며 빌드 통과.
- **공유 함수 무이동**: `RunMeasureCore`, `TryEscalateInsufficientRefeedback`, `FindInsufficientRefeedback`, `RefeedbackInsufficientLog`는 Program.cs 원위치 유지(DI-R-04 몫).

## 참조한 스킬

없음 (별도 도메인 스킬 해당 없음).

## 후속

DI-R-02 (InboxBuilder → server/InboxBuilder.cs)
