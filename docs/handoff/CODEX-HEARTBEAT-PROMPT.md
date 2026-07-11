# 코덱스 heartbeat 프롬프트 (Codex 앱에 붙여넣는 정본)

> 작성: 검수자 세션 2026-07-11 19:1x. **이 파일은 저장소 밖(Codex 앱 설정)에 사는 프롬프트의 정본 사본이다.**
> 앱의 프롬프트를 바꿨으면 이 파일도 같이 갱신한다. 둘이 어긋나면 **앱이 이긴다** — 주체가 실제로 읽는 것이 규칙이다.

## 왜 교체하는가 (실측 근거)

- 코덱스 세션 024~033(10회차, 약 2.5시간) 중 **8회차가 "빌드/verify/measure/하네스 실행: 수행하지 않음 → 검수 불가/보류"**.
- 코덱스 본인의 진술(SESSION-032·033): *"현재 heartbeat 지시는 같은 영역을 다른 실행자가 쓰는 흔적이나 영역 충돌이 있으면 작업하지 말고 보고하라고 한다."*
- 결과: `server/Harness/`·`skills/` 커밋 작성자는 **전부 검수자**. 코덱스 제작 하네스·스킬 **0건**. CODEX-QUEUE의 H-00·H-0·H-01·H-1·H-5는 전부 대기. 매 회차 의무인 `hs-scan`도 **한 번도 실행되지 않음**.
- 즉 **실패 데이터(FAIL 위키 13건 + HS-CANDIDATES) → 하네스·스킬** 파이프라인이 문서에만 있고 가동은 0이었다. 옛 heartbeat의 "충돌이면 보류" 한 줄이 큐 전체를 잠갔다.

## 고친 것 (옛 heartbeat 대비)

1. **정본 지시서를 읽게 한다** — `docs/handoff/CODEX-AUTO-15min-routine.md`. 앱 프롬프트에 규칙을 중복해 싣지 않는다(중복=낡음).
2. **"다른 실행자 있음" ≠ 전면 보류.** 영역은 배타적이다. sonnet이 `server/`를 쓰고 있어도 **`server/Harness/`·`skills/` 제작은 겹치지 않으므로 진행**한다. 보류하는 것은 *그 실행자의 산출물을 검수하는 일*뿐이다.
3. **`hs-scan`은 매 회차 의무** — 재량 아님, exit code에 복종.
4. **큐 픽업을 명령형으로** — CODEX-QUEUE 하네스 섹션 위에서부터.

## 붙여넣을 프롬프트 (한 줄, 그대로 복사)

```
저장소 C:\Users\1\Documents\Local-First Workflow Dashboard 에서 작업한다. 먼저 docs/handoff/CODEX-AUTO-15min-routine.md 를 읽고 그 절차를 그대로 수행하라 — 이 프롬프트가 아니라 그 문서가 정본이다. 핵심 3가지: (1) 다른 실행자(sonnet 등)가 실행 중이거나 server/ 에 미커밋 변경이 있어도 작업을 중단하지 마라. 영역은 배타적이다 — 네 쓰기 영역 server/Harness/ · skills/ · docs/qa/ · docs/wiki/failures/ · docs/handoff/sessions/ 는 그들과 겹치지 않으므로 제작을 계속하라. 보류하는 것은 '그 실행자의 산출물을 검수하는 일' 하나뿐이며, 그 경우에도 큐 작업은 진행한다. (2) 매 회차 dotnet run --project server -c Release -- hs-scan 을 실행하고 exit code에 복종하라(exit 1이면 skills/common/hs-gate.md 절차로 점수화해 HS-CANDIDATES에 append). 재량으로 건너뛰지 마라. (3) docs/handoff/CODEX-QUEUE.md 의 '하네스·스킬 제작' 섹션을 위에서부터 픽업해 실제로 만들어라 — 지금 H-00 launch-check, H-0 scope-check, H-01 build-verify 가 대기 중이고 HOOK-01은 커밋 2e28f7a로 이미 끝났다. 제작 전 skills/common/root-cause-diagnosis.md 와 hs-gate.md 2항(이 하네스가 볼 데이터가 실제로 존재하는가)을 반드시 확인하라. 하네스 등록은 server/Harness/HarnessRegistry.cs 표에 한 줄만 추가하고 server/Cli/CliRouter.cs 는 절대 건드리지 마라. 빌드·CLI는 -c Release, 성패는 exit code로만 판정하라(문자열 정규식 금지). git commit·push 금지, 결재·반입 대행 금지, server/ 나머지와 dashboard/ 무수정. 산출 문서에 ①주체(actor) ②사용한 하네스와 결과(명령·exit code·수치) ③참조한 스킬을 반드시 기록하라. 끝나면 docs/handoff/sessions/SESSION-<날짜>-codex-NNN.md 에 확인한 커밋·수행한 작업·재현/의심/오탐 수·다음 픽업 후보를 남겨라.
```

## 교체 후 검증 (다음 회차에 이걸로 확인)

- 다음 코덱스 SESSION 문서에 **"픽업: H-00"**이 적히는가 (안 적히면 프롬프트가 도착하지 않은 것 — FAIL-2026-013과 동형).
- `hs-scan` 실행 결과(명령·exit code)가 SESSION에 적히는가.
- `server/Harness/LaunchCheckCli.cs` 같은 **코덱스 작성 파일이 실제로 생기는가**. 문서상 완료 주장은 실체가 아니다 — 조율자가 `claim-check`로 대조한다.
