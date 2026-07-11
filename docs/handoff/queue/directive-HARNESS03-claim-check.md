# HARNESS-03 — claim-check: 자기보고가 아니라 실체로 완료를 판정 (dotnet run -- claim-check)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: harness. 승격: HS-CANDIDATES HS-03 (11/12 즉시제작).

## 왜 — 원인
FIX-01은 WORKSTATE·verification 문서가 **"완료(behaviorEqual:true, build 0/0)"를 주장하는데 코드에 `IsWithinRoot`가 존재하지 않는** 상태로 **3회차** 반복됐다. 조율자는 매번 손으로 `git grep`해서 허위를 잡아냈다.

- **얕은 원인**: 실행자가 문서를 잘못 썼다. ← 여기서 멈추면 "더 잘 쓰게 하라"가 되고 하네스화가 불가능하다.
- **진짜 원인**: **완료 판정의 근거가 실행자의 자기보고다.** "완료"라고 쓰면 완료로 취급된다. 파이프라인에 **주장과 실체를 대조하는 단계가 없다.** 조율자의 반복 수작업(git grep 대조)이 곧 하네스화 대상이다.
- **같은 뿌리**: FAIL-2026-005는 실행 여부를 "launched" 문자열로 판정했다. 자기보고를 진실로 취급하는 같은 병이다.
- **실시간 증거**: FEAT-02 실행자도 자가점검표를 전부 PASS로 쓰면서 잔존 위반을 "Codex concurrent activity"로 **오귀인**했다(실제 출처는 검수자 세션 파일). 자기보고는 악의 없이도 틀린다.

## 전제 조건
server/ clean. 순차.

## 목표
읽기 전용 CLI `claim-check <diId>`. 지정 DI의 verification 문서/WORKSTATE가 **주장하는 것**을 파싱해 **실체와 대조**한다.

## 작업
1. `server/ClaimCheckCli.cs` 신설 + CliRouter에 `claim-check` 분기 등록.
2. 입력: `docs/verification/<di>*.md`(검수기준 자가점검표 표), `docs/handoff/WORKSTATE.json`(diId·상태).
3. 대조 항목(주장 → 실체):
   - **심볼·파일 주장**: 문서가 "X 적용/신설"이라 주장하면 → 실제 코드에 심볼/파일이 존재하는가(`git grep`, 파일 존재). *FIX-01의 `IsWithinRoot`가 이 검사에 걸렸어야 했다.*
   - **빌드 주장**: "build 0/0" 주장 → 실제 `dotnet build -c Release` 재현(경고·오류 수).
   - **게이트 주장**: "violations N" 주장 → 실제 `measure` 재실행 결과와 비교.
   - **커밋 주장**: 커밋 해시를 주장하면 → `git log`에 실재하는가.
   - **상태 정합**: WORKSTATE의 diId·상태가 실제 커밋·산출물과 모순되지 않는가. *"FIX-02/verifying인데 실제로는 완료" 같은 불일치.*
4. JSON 출력: `{harness:"claim-check", diId, claims:[{claim, kind, asserted, actual, verdict:"match"|"MISMATCH"}], mismatchCount, verdict}`.
5. exit: **0=주장과 실체 일치, 1=불일치 존재, 2=오류.**
6. **안전**: 읽기 전용(문서 파싱·git grep·build). 문서를 고치지 않는다 — 불일치는 **보고**만 하고, 정정은 실행자·조율자·사람의 몫.

## 검수 기준 (검증 가능 6개)
1. `dotnet run --project server -c Release -- claim-check FEAT-02`가 위 스키마 JSON 출력.
2. **회귀 검증(핵심)**: 임시 사본에서 verification 문서에 **허위 주장**(존재하지 않는 심볼 "완료" 주장)을 넣으면 → `MISMATCH` 검출, exit 1. *FIX-01의 3회 허위 완료 주장을 이 하네스가 잡았어야 했음을 증명한다.*
3. 정상 DI(FEAT-02, 실제 코드 존재)에서는 `match`, exit 0 (오탐 0).
4. 커밋 해시 허위 주장 시 검출된다.
5. 실행이 문서·상태를 바꾸지 않는다. build 0/0, `verify-behavior` true.
6. 코어 3파일 무접촉. 검수기준 표 파싱은 지시서 형식(마크다운 표)에 맞춘다.

## v9 산출물
WORKSTATE(diId HARNESS-03), `docs/verification/harness03-claim-check.md`(6기준 + 허위주장 주입 실측), `docs/directives/HARNESS03-claim-check.md`.

## 소비자 (후속)
- **조율자 VERIFY-PROTOCOL**: 커밋 전 `claim-check <di>` exit 0을 필수 게이트로. 지금 손으로 하는 `git grep` 대조를 대체한다.
- ORCH-01/03의 "발사↔완료 task ID 결속" 요구와 결합.

## 경계 / 보고
server/ + 위 문서만. dashboard/·docs/qa/·docs/wiki/ 무접촉. git commit/push 금지. `-c Release`. stdout에 수행요약·자가점검표·claim-check 출력. rate limit 시 마지막 줄 QUOTA_SIGNAL.
