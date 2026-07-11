# 결재 브리핑 — 2026-07-11 (사람 판단용)

> 작성: 검토 세션. 아래는 **자료 정리·추천**이며 approve/reject 실행은 사람 전용 게이트. 나는 대행하지 않는다.
> 기준 HEAD: 리팩토링 완료본(Program.cs 4분할). 신규 CLI 등록 지점은 이제 `server/Cli/CliRouter.cs`의 `TryRun`이다(Program.cs 아님).

## 1) 반입 승인 대기 — task-20260710070612000 (directive-draft CLI, #12)

- 산출: `server/DirectiveDraftCli.cs`(신규 338줄) + `server/Program.cs`(수정).
- baseCommit `b3e9d2a` — 현재 HEAD보다 **69커밋 뒤**.
- 문제: 이 diff의 `Program.cs`는 `originalFileHash 34ab5293…`(리팩토링 **이전** Program.cs)를 전제. 그 뒤 Program.cs는 327 insert / 715 delete로 4분할됨 → **stale-guard가 반입 거절**(해시 불일치). Program.cs 조각은 충돌.
- 추천: **거절(reject-import)**. 단 directive-draft 기능 자체는 유효 → 현재 HEAD 기준 재제출. 재제출 시 `Program.cs` 대신 `server/Cli/CliRouter.cs TryRun`에 `directive-draft` 분기를 추가하고 `DirectiveDraftCli.cs`만 신규로. (STATUS.md도 이미 #12를 "stale 거절 대상"으로 표기함.)

## 2) 반입 승인 대기 — task-20260710090000000 (retrospect CLI, #7)

- 산출: `server/RetrospectCli.cs`(신규) + `server/Program.cs`(수정).
- baseCommit `7df4bde`, executor claude-code. **같은 문제**: `Program.cs` originalFileHash가 리팩토링 이전(34ab5293…) → stale 충돌.
- 추천: **거절(reject-import)** 후 현재 HEAD 기준 재제출. 등록은 CliRouter.TryRun에 `retrospect` 분기. RetrospectCli.cs 본체는 대체로 재사용 가능(코어 3파일 무접촉이면).
- 참고: 완료기준(run-log 인용 전수 대조·환각 인용 폐기)은 재제출본에서 실측 재확인 필요.

### 공통 결론
두 건 모두 **기능 결함이 아니라 base drift**가 사유. 반입 창구를 "Program.cs 직접수정"에서 "CliRouter 분기 추가"로 바꿔 재발 방지. 두 CLI를 SONNET-QUEUE에 재지시서로 넣는 것도 방법(단 발사는 사람 게이트).

## 3) proposal 결재 대기 — dev-pack patch-proposal.json

- 현재 파일: `proposal-1783750546584`("UI/UX 개선 및 코드 품질 향상", revisionOf 1783750066352), lifecycle submitted, createdBy ollama/qwen3:8b.
- 변경 3건(측정 목표): smallTouchTargets 1→0, skillDomainViolations 2→0, maxFunctionLength 159→[0,80].
- **주의 — ID가 계속 바뀜**: HUMAN-INBOX엔 …747077098로 적혔으나 실제 파일은 …750546584. qwen3:8b 루프가 수분마다 self-revision 중(리비전 체인 churn). 결재 대상이 움직이는 표적.
- 추천: 현 리비전은 **보류(hold)**. 측정 기준을 바꾸는 변경이라 사람이 "이 품질 목표를 채택할지"를 판단해야 하고, 자동 churn이 멈춘 안정 리비전에 대해서만 결재하는 게 안전. churn 자체를 이슈로 분리(아래 4번).

## 4) 별건 경보 — dashboard loop의 무인 approve+commit 재발 (안전게이트 위반)

- `git log -- dashboard/data/dev-pack/patch-proposal.json`에 `[loop] dev-pack 회차7~10: approve proposal-…` / `acknowledge-guardrail …` 커밋이 연속 존재.
- 즉 대시보드 loop 프로세스가 **proposal을 무인 자동 승인하고 자동 커밋** 중 — "결재는 사람 전용" 고정점 위반. HUMAN-INBOX가 1회 관측했던 것이 **회차7·8·9·10로 반복**되고 있음.
- 추천: 사람이 ① 이 dashboard loop 자동커밋/자동approve 프로세스를 **중단**할지, ② loop 정책 자체(무인 approve 금지)를 코드로 강제할지 결정. 이게 4번 중 가장 시급.

## 실행 시 (사람이 결정한 뒤)
- 반입 거절: 서버 켜고 `reject-import <taskId>` (서버는 Windows .NET, 내 샌드박스에서 못 켬).
- 재제출: SONNET-QUEUE에 지시서 추가 → 발사는 사람 게이트.
