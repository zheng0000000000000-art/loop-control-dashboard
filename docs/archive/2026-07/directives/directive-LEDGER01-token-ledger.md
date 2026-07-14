# LEDGER-01 — ollama 토큰 계측 (버리던 숫자를 이미 있는 `cost` 필드에 적는다)

이 지시서는 `docs/directives/_header.md`의 불변 제약과 **공통 검수 기준**을 따른다. 작업 시작 전 먼저 읽어라.
유형: implementation. 근거: **ADR-006(사람 승인 2026-07-12)** — "경제 시스템보다 먼저 계측 시스템이 필요하다."

## 왜 하는가 (실측 근거 — 추측 아님)

검수자가 2026-07-12 호스트에서 직접 측정했다:

| 사실 | 실측값 |
| --- | --- |
| `dashboard/data/dev-pack/run-log.json` 항목 수 | **938건** (`entries`, schemaVersion 존재) |
| 모든 항목에 `cost: {inputTokens, outputTokens, estimatedUSD, subscriptionCalls, role}` 필드가 이미 있다 | **938/938** |
| 그중 토큰 값이 채워진 항목 | **0건** |
| ollama 데몬 | **가동 중**(`/api/tags` 200, qwen3:8b·llama3.1:8b·qwen3:14b) |
| ollama 호출 지점 | `server/OllamaExecutor.cs:487` · `server/OllamaReviewer.cs:152` · `server/Tier2Approver.cs:223` (전부 `POST /api/generate`) |

**ollama의 `/api/generate` 응답은 `prompt_eval_count`(입력 토큰)·`eval_count`(출력 토큰)·`total_duration`(ns)을 항상 포함한다. 우리는 그 필드를 읽지 않고 버린다.**

즉 **"토큰을 줄이자"는 프로젝트가 토큰을 재지 않고 있다.** 잴 숫자는 이미 문 앞에 와 있다.

## 목표

**신규 스키마 0개. 신규 하네스 0개. 신규 필드 0개.** 이미 오는 숫자를 이미 있는 필드에 적기만 한다.

1. ollama 호출 3곳에서 응답 JSON의 `prompt_eval_count`·`eval_count`·`total_duration`을 **읽는다**.
2. 그 값을 해당 호출이 남기는 run-log 항목의 **기존 `cost` 필드**에 기록한다:
   - `inputTokens` ← `prompt_eval_count`
   - `outputTokens` ← `eval_count`
   - `role` ← 호출 주체를 구분하는 기존 문자열 규약을 따른다(예: `executor` / `reviewer` / `approver`). **`role`의 의미를 새로 발명하지 마라** — 기존에 쓰이는 값을 그대로 쓴다.
   - `estimatedUSD`·`subscriptionCalls`는 **건드리지 마라**(로컬 ollama는 과금이 없다. 0을 유지한다. **0을 "모름"으로 바꾸지 마라 — 스키마를 바꾸는 일이다**).
3. `total_duration`을 적을 자리가 **기존 스키마에 없으면 적지 마라.** 필드를 새로 만드는 순간 이 작업은 범위를 벗어난다. (기존 항목에 `durationMs` 류 필드가 **이미 있는 경우에만** 채운다.)

## 안전 불변 (어기면 반려)

- **`cost` 스키마의 키를 추가·삭제·개명하지 마라.** 값만 채운다.
- **응답에 `prompt_eval_count`/`eval_count`가 없으면 0을 유지하고 넘어가라.** 추정치를 계산해 넣지 마라. **재지 않은 것을 잰 것처럼 적는 것이 이 프로젝트가 싸우는 바로 그 병이다.**
- **`server/Harness/**` 무접촉**(코덱스 배타 영역). `dashboard/` 무접촉. `docs/STATUS.md` 무접촉.
- **`dashboard/data/dev-pack/*.json`을 손으로 편집하지 마라.** 값은 **서버가 실행되면서 스스로 적어야 한다.** 손으로 채운 숫자는 계측이 아니라 위조다.
- git commit/push 금지. 결재·반입·발사 금지.
- 함수마다 **한국어 기능 주석 1줄**(`functionsWithoutComment`가 올라가면 실패다).

## 검수 기준 (공통 항목 + 아래) — **판정은 exit code와 실제 기록된 숫자로**

1. `dotnet build` → exit 0, warning 0.
2. **실체 증명(핵심)**: ollama를 실제로 호출하는 경로를 한 번 돌려서, **`run-log.json`에 `cost.inputTokens > 0` **이고** `cost.outputTokens > 0` 인 항목이 최소 1건 새로 생기는 것**을 보여라. 그 항목의 JSON 원문을 stdout에 그대로 붙여라.
   - **코드가 그렇게 되어 있다는 설명은 증거가 아니다.** 실제로 기록된 숫자만이 증거다.
   - ollama가 응답하지 않으면 **중단하고 보고하라.** 값을 지어내지 마라.
3. `dotnet run --project server -c Release -- verify-behavior` → `behaviorEqual: true`.
4. `dotnet run --project server -c Release -- measure dev-pack` → **비악화**(현재 위반 1건은 코덱스 몫이며 네 책임이 아니다. **네가 새 위반을 만들지 마라**).
5. `dotnet run --project server -c Release -- gate-clean server` → allowlist 밖 파일이 더럽지 않다.
6. `dotnet run --project server -c Release -- handoff-integrity` → **exit 0 유지**(P0-04에서 막 통과시킨 게이트다. 깨뜨리지 마라).

## v9 산출물

- WORKSTATE 갱신(`diId: LEDGER-01`, `changedFiles`에 **네가 실제로 바꾼 파일만**).
  **주의: 현재 WORKSTATE의 `changedFiles`에는 FIX-07의 파일 3건이 남아 있다. 그건 네 것이 아니다 — `history`로 내리고 네 파일로 교체하라.** 그 뒤 `dotnet run --project server -c Release -- projection`을 실행해 해시를 스탬핑하고 L0를 갱신하라.
- `docs/verification/ledger01-token-ledger.md` — **①주체 ②사용한 하네스와 각각의 exit code ③참조한 스킬** 기록 + 반드시 다음 절을 포함:
  ```
  ## 지표는 만족했으나 목적은 미달인 부분
  ```
  (자진 신고. 비워두지 마라. 정말 없으면 "없다"고 쓰고 근거를 대라. **P0-04 실행자는 이 자리에 스스로 결함을 신고했고, 그게 검수를 통과시켰다.**)
- `docs/directives/LEDGER01-token-ledger.md` — 이 지시서 보관본.

## 허용 파일 (allowlist)

- server/OllamaExecutor.cs
- server/OllamaReviewer.cs
- server/Tier2Approver.cs
- server/Engine.cs (cost 배선부만)
- docs/handoff/WORKSTATE.json
- docs/verification/ledger01-token-ledger.md
- docs/directives/LEDGER01-token-ledger.md
- (실행 산출물) dashboard/data/dev-pack/run-log.json — **서버 실행으로 생성된 변경만 허용. 손 편집 금지.**

> 이 목록 밖을 수정하면 산출물 전체가 반려된다.

## 경계 / 보고

`-c Release`. stdout에 수행요약 · 자가점검표 · 위 6개 하네스의 **실제 exit code** · **토큰이 기록된 run-log 항목 원문**.
**한도·중단이 임박하면 종료 전 마지막 세 줄: `QUOTA_SIGNAL` / `CHANGED: <수정한 파일>` / `NEXT: <다음 할 일 한 줄>`.** 부분 작업물은 되돌리지 말고 verification 문서에 `상태: 미완(한도)`로 적어라(`docs/handoff/QUOTA-POLICY.md`).
