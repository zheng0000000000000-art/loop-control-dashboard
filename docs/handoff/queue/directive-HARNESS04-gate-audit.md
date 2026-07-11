# HARNESS-04 — gate-audit: 사람 전용 게이트 위반 감사 (dotnet run -- gate-audit)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: harness. 승격: HS-CANDIDATES HS-04 (12/12 즉시제작, 최우선).

## 왜 — 원인
이 프로젝트의 북극성은 **"사람은 기준만 정한다. 결재·반입·기준변경·이양 결정은 항상 사람"**이다. 그런데 `git log`에는 `[loop] dev-pack 회차N: approve proposal-...` 커밋이 **12건 실측**된다(회차5~10). 즉 **고정점이 12번 깨졌다.**

원인은 두 겹이다:
1. **강제 부재** — 고정점이 문서 규칙(honor system)으로만 존재한다. 코드가 approve 호출 주체를 검증하지 않아 어떤 자동 프로세스든 부를 수 있다.
2. **감지 부재** — 위반이 나도 아무도 모른다. HUMAN-INBOX가 1건(34f5116)을 관측하고 "출처 미확정"으로 남긴 사이 6건 이상 재발했다.

**이 하네스는 ②(감지)만 맡는다.** ①(코드 강제)은 기준 변경이라 **사람 결재 사항**이며 하네스가 대행하지 않는다. 검출 → 보고 → 사람이 판단, 이 순서를 지킨다.

## 전제 조건
server/ clean. 순차.

## 목표
읽기 전용 CLI `gate-audit [--since <commit>]`. 사람 전용 게이트(결재·반입·기준변경·push)가 **사람이 아닌 주체**에 의해 수행된 흔적을 기계 검출한다.

## 작업
1. `server/GateAuditCli.cs` 신설 + CliRouter에 `gate-audit` 분기 등록.
2. 검출 대상(전부 읽기 전용):
   - **A. 무인 결재 커밋**: `git log`에서 커밋 메시지가 자동 서명 패턴(`[loop]`, `[auto]` 등)이면서 결재 액션(`approve`, `reject`, `acknowledge-guardrail`, `approve-import`, `reject-import`)을 포함하는 것.
   - **B. 무인 결재 이벤트**: run-log·이벤트 로그의 결재 액션 중 **사람 승인 근거(actor=human 등)가 없는** 것.
   - **C. 기준 파일 무단 변경**: `blueprint.json`·`workflow-definition.json` 변경 커밋의 주체가 자동 서명인 것. (기준 변경은 사람 전용)
   - **D. 미배치 push**(선택): 사람 배치 기록 없는 push 흔적.
3. JSON 출력: `{harness:"gate-audit", since, violations:[{kind:"A|B|C|D", commit, author, when, action, target, evidence}], violationCount, verdict:"CLEAN"|"VIOLATION"}`.
4. exit: **0=위반 없음, 1=위반 검출, 2=오류.**
5. **자동 서명 패턴은 코드 상수 + 설정 가능**하게 두고, 왜 그 패턴이 '비사람'인지 주석으로 남긴다.
6. **안전 불변**: 검출만 한다. 되돌리기·revert·결재·정책 변경 금지. git 읽기 전용(log/show). 상태 무변경.

## 검수 기준 (검증 가능 6개)
1. `dotnet run --project server -c Release -- gate-audit`가 위 스키마 JSON 출력.
2. **회귀 검증(핵심)**: 현재 저장소에서 실행하면 **기존 12건의 `[loop] ... approve/acknowledge` 커밋을 kind A 위반으로 전부 검출**하고 `verdict:"VIOLATION"`, exit 1. (회차5·6·7·8·9·10 — 34f5116, 94050a6, dc0005c, 1bb76ea, 84e81ea, 5cecb41, 78a4c8b, c210a48, 713f4ca, 538a916, 614a39e, c3f37a0)
3. **장애주입**: 가짜 `[loop] ... approve proposal-x` 커밋을 임시 브랜치에 만들면 검출된다. 정상 사람 커밋은 검출되지 않는다(오탐 0).
4. `--since <commit>` 로 범위 제한이 동작한다.
5. 실행이 어떤 파일·git 상태도 바꾸지 않는다(전후 `git status` 동일). build 0/0, `verify-behavior` true.
6. 코어 3파일 무접촉.

## v9 산출물
WORKSTATE(diId HARNESS-04), `docs/verification/harness04-gate-audit.md`(6기준 + 12건 검출 실측), `docs/directives/HARNESS04-gate-audit.md`.

## 후속 (이번 범위 아님 — 사람 결재 필요)
- 검출된 12건을 어떻게 처리할지(되돌림·인정·정책 예외)는 **사람 판단**. HUMAN-INBOX에 이미 올라가 있다.
- loop의 approve 호출 자체를 코드로 차단하는 것은 **기준 변경** → 사람 결재. 별도 지시서.
- `gate-audit`를 조율자·코덱스 루틴에 상시 편입(위반 시 즉시 HUMAN-INBOX 등재).

## 경계 / 보고
server/ + 위 문서만. dashboard/·docs/qa/·docs/wiki/ 무접촉. git commit/push 금지. 결재 액션 호출 금지. `-c Release`. stdout에 수행요약·자가점검표·검출 결과 JSON. rate limit 시 마지막 줄 QUOTA_SIGNAL.
