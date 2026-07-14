# ACTOR-01 — 결재 액션에 주체(actor) 기록 ※사람 결재 선행 필요

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
유형: feature. **상태: 사람 결재 대기 — 결재 게이트의 의미를 코드에 새기는 일이므로 기준 변경에 가깝다. 승인 전 발사 금지.**
근거: FAIL-2026-012(검수자가 주체를 프록시로 오판해 위반 22건 날조), HUMAN-INBOX "출처 미확정" 원 기록.

## 문제 — 한 줄
**시스템은 "이 결재를 누가 했는가"를 기록하지 않는다.** 그래서 고정점("결재는 항상 사람")이 지켜졌는지 **아무도 검증할 수 없다.**

## 왜 이게 드러났나
검수자가 `[loop]` 커밋 접두사를 "자동 주체"로 해석해 위반 22건을 보고했다. 오판이었다 — `[loop]`는 `CommitHumanAction`이 붙이는 형식이고 **사람 승인에도 붙는다**. 하지만 오판의 **근본 원인은 검수자가 아니라 시스템**이다: 주체를 알려주는 데이터가 없으니 프록시에 기댈 수밖에 없었다. HUMAN-INBOX가 이 건을 오래도록 **"출처 미확정"**으로 남겨둔 것도 같은 이유다. **사람도 알 수 없었다.**

## 목표
결재·반입 액션에 **주체(actor)를 기록**한다. 사후에 "누가 했는가"를 데이터로 답할 수 있게 만든다.

## 작업 (제안 — 사람 승인 후 확정)
1. 액션 요청에 actor 정보를 싣는다: 주체 유형(`human` | `agent`), 식별자, 호출 경로(`ui` | `api` | `cli`).
   - 대상 엔드포인트: `/actions/approve`·`reject`·`acknowledge`, `outbox/{id}/approve-import`·`reject-import`.
   - 대시보드 UI 호출은 `human`으로, 그 외 경로는 명시 없으면 `unknown`으로 기록(위조 방지는 별도 과제 — 여기서는 **기록**이 목적).
2. `run-log` 항목과 **커밋 메시지**에 actor를 남긴다.
   - 현재: `[loop] dev-pack 회차5: approve proposal-x`
   - 제안: `[loop] dev-pack 회차5: approve proposal-x (actor: human/ui)`
   - **`[loop]` 접두사가 주체를 뜻하지 않는다는 점을 주석과 문서에 명시**한다 — 이 오해가 FAIL-012를 낳았다.
3. `docs/verification/auto-data-commit.md`의 설명을 보강해 `[loop]`의 의미(루프 이터레이션, 주체 아님)를 눈에 띄게 적는다.

## 검수 기준 (검증 가능)
1. 대시보드에서 승인 → run-log·커밋 메시지에 `actor: human/ui` 기록.
2. API 직접 호출(actor 미지정) → `unknown`으로 기록되고, 그 자체가 감사 신호가 된다.
3. 기존 22건처럼 actor 없는 과거 기록은 `unknown`으로 남는다 — **소급 날조하지 않는다.**
4. build 0/0, verify-behavior true, measure 비악화, 코어 3파일 무접촉.

## 후속 (이번 범위 아님)
- actor가 기록되면 그때 **`gate-audit` 재승격 심사**(현재는 근거 데이터가 없어 철회됨, FAIL-012).
- actor **위조 방지**(인증·서명)는 별도 과제이자 기준 변경 — 사람 결재.

## 허용 파일 (allowlist)

- server/Program.cs
- server/GitDataCommitter.cs
- server/OutboxManager.cs
- docs/verification/actor01-actor-provenance.md
- docs/handoff/WORKSTATE.json

> 이 목록 밖의 파일을 수정하면 산출물 전체가 반려된다. 필요하면 고치지 말고 중단하고 보고하라.

## 경계 / 보고
사람 승인 전 착수 금지. 승인 시 server/ + 위 문서만. git commit/push 금지. 결재 액션 호출 금지.
