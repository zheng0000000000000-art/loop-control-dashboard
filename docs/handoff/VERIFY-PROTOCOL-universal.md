# 보편 검수 프로토콜 — 어느 실행자든 실행 가능 (코덱스·sonnet·검수자 공통)

> 목적: 커밋·작업 결과의 검수를 특정 실행자에 묶지 않는다. 이 문서만 읽으면 누구든 동일 기준으로 검수하고 PASS/FAIL을 낸다.
> 원칙(불변): **자가보고를 신뢰하지 않는다. 하네스를 독립 재실행해 기계 증명한다.** 저장소 로컬: `C:\Users\1\Documents\Local-First Workflow Dashboard`.

## 0. 전제

- 검수는 읽기·실행 검증이다. 검수 중 코드를 고치지 않는다(고칠 게 있으면 판정에 FAIL 사유로 기록하고 구현 실행자에게 넘긴다).
- 서버가 실행 중이면 빌드·CLI는 항상 `-c Release`로 락을 피한다.
- 한글이 콘솔에서 깨져 보이면 파일 인코딩 표시 문제다 — `[IO.File]::ReadAllText(path,[Text.Encoding]::UTF8)`로 재확인한다.

## 1. 절차 (순서 고정)

1. **동기화**: `git -C <repo> log --oneline -5`로 대상 커밋 확인. 원격 검수면 clone/pull.
2. **검증 문서 전수 독해**: 대상이 만든 `docs/verification/*.md`를 **처음부터 끝까지** 읽는다. 로그 tail만 보고 판단하지 않는다(과거 오판→incident 전례 있음, FAIL-2026-005 참조).
3. **하네스 독립 재실행** (자가보고와 대조):
   - `dotnet build server -c Release` → 경고 0·오류 0 확인.
   - 리팩토링·동작 보존 작업이면 `dotnet run --project server -c Release --no-build -- verify-behavior` → `behaviorEqual: true` 확인. false면 회귀.
   - `dotnet run --project server -c Release --no-build -- measure dev-pack` → 위반 수를 기준선과 대조(증가 없어야 함, 지표 해소 주장이면 실제 사라졌는지).
4. **불변식 확인**:
   - 코어 3파일 무접촉: `rg -in "<작업 도메인 키워드>" server/Engine.cs server/Storage.cs server/Guardrails.cs` → 매치 없음.
   - 기준 파일 무수정: `git diff --stat <base>..<head> -- "**/blueprint.json" "**/workflow-definition.json"` → 빈 결과(사람 승인 없는 변경 없음).
   - 영역 격리: 커밋 diff가 선언한 영역(server/ 또는 docs/…)을 벗어나지 않았는지.
   - 커밋에 비밀 미포함: `server/appsettings.json`의 토큰 등이 커밋에 섞이지 않았는지.
5. **판정 기록**: 아래 형식으로 `docs/verification/` 또는 리뷰 로그에 남긴다.

## 2. 판정 형식

```
검수 대상: <커밋 해시 / 작업 ID>
검수자: <ai model | human>
독립 재실행: build=<0/0?> verify-behavior=<true|false|N/A> measure=<위반수 전/후>
불변식: 코어무접촉=<Y/N> 기준파일무수정=<Y/N> 영역격리=<Y/N> 비밀미포함=<Y/N>
검증문서 주장 vs 실측 불일치: <없음 | 항목별>
판정: PASS | 조건부(사유) | FAIL(사유)
```

## 3. 판정 기준

- **PASS**: build 0/0 + (해당 시)verify-behavior=true + 위반 비악화 + 불변식 전부 Y + 문서 주장이 실측과 일치.
- **조건부**: 기능은 맞으나 문서 산출물 누락 등 경미한 결함(후속 보완 명시).
- **FAIL**: 빌드 실패 / verify-behavior=false / 위반 증가 / 불변식 위반 / 문서 주장과 실측 불일치 중 하나라도.

## 4. 모델 독립 규칙

- 특정 모델의 내부 추론이 아니라 **명령·결과·기준**으로만 판정한다. 다른 실행자가 같은 절차로 같은 결론에 도달해야 한다.
- 검수자 자신이 그 작업의 생성자면(생성=검토 금지 원칙) 판정에 그 사실을 명기하고, 가능하면 다른 실행자의 교차 검수를 요청한다.
- 이 프로토콜로 판정할 수 없는 대상(빌드 불가·하네스 없음)은 "검수 불가" 사유를 기록하고 표준화 대상으로 남긴다.

## 5. 코덱스(QA 역할)용 참고

코덱스는 이 프로토콜의 1차 검수자가 될 수 있다. `skills/common/verification.md`를 함께 읽고, 재현 절차가 반복되면 `POST /api/contributions`(checklist_suggestion)로 검수 체크리스트를 제안한다(승급은 사람).
