# 이 저장소에서 작업하는 에이전트의 규칙

**철학**: LLM은 적게 기억하고 적게 생성한다. 프로그램이 많이 기억하고 조립하고 검증한다.
판단이 갈리면 이 문장이 심판이다 — **프롬프트로 시키지 말고 코드로 강제하라.**

> 이 파일은 **모든 세션에 자동으로, 매 턴 다시** 실린다. 그래서 **규칙만** 둔다.
> **근거·사고 사례는 [`docs/handoff/RULES-RATIONALE.md`](docs/handoff/RULES-RATIONALE.md)에 있다 — 필요할 때만 읽어라.**

## 읽기 순서 (새 세션)

1. `docs/context/RUNTIME-INDEX.md` — 기계가 생성한 L0 상태. **손으로 쓴 문서보다 이걸 믿어라.**
2. 역할별 인수인계: 검수자 `docs/handoff/REVIEWER-HANDOFF.md` · 조율자 `outputs/review-log.md` · 코덱스 `docs/handoff/sessions/`
3. `docs/plan/INTENT-DIGEST.md`(의도) · `docs/plan/ALIGNMENT-v9.md`(요구↔실재↔공백, **재발명 금지**) · `docs/handoff/decisions/ADR-*.md`(왜)
4. 지시서를 받았으면 `docs/directives/_header.md`(불변 제약 + Context Pack 형식).

**현재**: Phase 0(안전장치 구축). 운영 등급 `Required Before Multi-model Parallel Work`(ADR-001).
**`HS-GATE-P00` PASS 전까지 Phase 1(기능 개발)로 넘어가지 않는다.**

## 규칙은 대부분 하네스가 강제한다 — 목록: `docs/handoff/HARNESSES.md`

| 규칙 | 강제하는 것 |
| --- | --- |
| 커밋 전 측정 통과 | `measure dev-pack` (위반 0까지 재측정. 못 넘기면 남은 위반을 보고에 그대로 적는다 — 숨기지 않는다) |
| 지시서 allowlist 밖 수정 금지 | `scope-check` |
| 함수마다 한국어 기능 주석 1줄 | `measure`의 `functionsWithoutComment` |
| 인수인계가 실체와 일치 | `handoff-integrity` (**파일을 다 쓴 뒤 마지막에 `projection` 실행**) |
| 지시서의 참조 입력이 stale하지 않음 | `context-pack-integrity` |
| 동작 보존 | `verify-behavior` |
| 트리 clean 판정 | `gate-clean` (raw `git status`로 판정하지 마라) |

**판정은 exit code로 한다.** 출력 문자열·정규식으로 성패를 세지 마라.

## 금지 사항 (코드가 아직 안 잡는다 — 사람과 규칙이 지킨다)

- **기준 파일(`dashboard/data/*/blueprint.json`·`workflow-definition.json`)이나 측정 코드를 고쳐 게이트를 통과하는 것.**
  기준 변경은 **사람 결재**다. 바꿨으면 `docs/handoff/BASELINE-CHANGES.md`에 **①주체 ②근거 ③되돌리는 법**을 남긴다. 근거 없으면 커밋 금지 + HUMAN-INBOX.
- **approve/reject/import 대행.** 결재는 사람 몫이다.
- **발사(sonnet spawn)와 push.** 사람 게이트다.
- **`Engine.cs`·`Storage.cs`·`Guardrails.cs`에 도메인 지식**(게임 용어·metricId·ollama 코드)을 넣는 것.
- **기록 파일을 통째로 읽어 통째로 다시 쓰는 것.** `outputs/review-log.md`·`outputs/reviewer-log.md`·`docs/handoff/sessions/*`·`HUMAN-INBOX.md`·`BASELINE-CHANGES.md`는 **append만.**
  고칠 게 있으면 **새 항목으로 정정을 덧붙인다.** (이유: 저장소에 이미 깨진 한글이 박혀 있어 재작성하면 조용히 오염된다 — RULES-RATIONALE 참조)

## 관례

- 주석은 한국어 기능 설명만(파일 머리 1~2줄 + 함수 위 1줄). 함수 수정 시 주석 갱신.
- 코드 변경의 기본 경로는 dispatch/outbox다. **직접 수정+커밋은 예외** — ①관례·가이드 문서 자체(CLAUDE.md·AGENTS.md·AGENT-GUIDE.md·skills/·docs/) ②지시서에 "직접 경로" 명시. 예외를 썼으면 보고에 사유를 남긴다.
- `outputs/reviewer-log.md`는 **검수자가 직접 커밋한다**(ADR-003 단일 기록자. 다른 주체는 쓰지 않는다).
- 대안 중 하나를 골랐으면 **ADR을 남긴다**(`docs/handoff/decisions/`).
- 커밋 전 `git status`로 bin/·obj/·history/ 미포함 확인.

## 작업 보고(verification 문서)에 반드시 적는 것

1. **주체(actor)** — 누가 했는가. *없으면 다음 사람이 프록시로 추측하고, 그러면 틀린다(FAIL-2026-012).*
2. **사용한 하네스** — 명령·exit code·핵심 수치. *조율자가 직접 재실행해 대조한다. **자기보고는 증거가 아니다.***
3. **참조한 스킬.**
4. **`## 지표는 만족했으나 목적은 미달인 부분`** — 자진 신고(ADR-005). 없으면 "없음"이라고 쓰고 근거를 대라.
   *신고하면 감점이 아니다. 숨기면 반려다.* 게이트 결과는 JSON 한 줄로: `{"gate":"dev-pack","violations":0,"attempt":1}`

## 지시 게이트 (착수 전 자가 검사)

①완료 기준이 검증 가능한가 ②대상 파일·범위가 특정되는가 ③기존 원칙·blueprint와 충돌하지 않는가.
부족하면 **추측으로 진행하지 말고 선택지 딸린 질문으로 되묻고 대기한다.** 추측 진행한 부분은 보고에 '추측 진행'으로 명시한다.
원칙과 충돌하면 **"기준 변경입니까?"**를 확인한다 — 기준 변경은 사람 결재다.

## 스킬 라우팅

- `skills/common/` 은 모든 작업에서 읽는다.
- `skills/domains/` 는 **이번 작업이 바꿀 파일 경로가 그 스킬의 '트리거:'와 일치할 때만** 읽는다. 애매하면 읽지 않는다.

## 원인을 지목할 때

**프록시로 단정하지 마라.** 커밋 접두사·타임스탬프 상관·에러 문구·정규식 매치는 **증거가 아니다.**
판정은 **exit code**로, 원인은 **실체**로(호출부·실제 입력·diff 내용·주체에게 직접 묻기). 모르면 **"주체 미상"이라고 쓴다.**
