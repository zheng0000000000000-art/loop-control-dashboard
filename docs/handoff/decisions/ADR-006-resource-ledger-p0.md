# ADR-006 — Resource Ledger(토큰 계측)를 P1이 아니라 **P0**로 올린다

- 상태: **승인됨 (사람 choi, 2026-07-11 23:5x)** — 승인 즉시 `LEDGER-01` 지시서 발행·발사(PID 20896). 발사 기록: `outputs/reviewer-log.md` 2026-07-11 23:50 항목.
- 일시: 2026-07-11 (승인 반영: 2026-07-12 00:0x, 검수자 세션. 조율자가 "승인상태 불일치"로 관측한 건을 해소)
- **실측 정정**: 아래 §1의 "664건"은 낡았다. 2026-07-12 호스트 실측 = `entries` **938건**, `cost` 필드 보유 938/938, **토큰 값 채워진 것 0건**. ollama 데몬 **UP**(qwen3:8b·llama3.1:8b·qwen3:14b). 결론은 그대로다 — **재고 있지 않다.**
- 제안: 검수자 세션(Claude) — 사람(choi)이 "토큰을 안 쟀었다, 이제 잴 만한 것 같다"고 제기
- 근거 문서: `Business_Aligned_Technical_Roadmap_Supplement` ("경제 시스템보다 먼저 계측 시스템이 필요하다") · `docs/plan/INTENT-DIGEST.md` §4

## 1. 상황 (실측)

로드맵의 **P0 성공 기준은 "토큰 절감·실행 속도·재사용성"**이다. 그런데:

- `dashboard/data/dev-pack/run-log.json`의 모든 이벤트에 `cost: {inputTokens, outputTokens, estimatedUSD, subscriptionCalls, role}` 필드가 **이미 존재한다.**
- **전체 664건 중 값이 채워진 것은 0건이다.** 스키마만 있고 아무도 안 쓴다.
- 서버는 ollama를 세 곳에서 호출한다: `server/OllamaExecutor.cs:487` · `server/OllamaReviewer.cs:152` · `server/Tier2Approver.cs:223` (모두 `POST /api/generate`).
- **ollama의 `/api/generate` 응답은 `prompt_eval_count`(입력 토큰)와 `eval_count`(출력 토큰), `total_duration`을 항상 포함한다.** 우리는 그 필드를 읽지 않고 버린다.

**즉 "토큰을 줄이자"는 프로젝트가 토큰을 재지 않고 있다.** 그리고 잴 데이터는 이미 문 앞에 와 있다.

## 2. 선택지

**(A) 지금 잰다 (P0로 승격).** ollama 응답의 `prompt_eval_count`/`eval_count`/`total_duration`을 **기존 `cost` 필드에 기록**한다. 신규 스키마·신규 하네스 **0개**. 헤드리스 실행자(`claude -p`)는 `--output-format json`의 `usage`를 로그에 남긴다.
- 장점: 비용 거의 0(값을 버리던 걸 적기만 함). **P0의 성공/실패를 비로소 판정할 수 있다.**
- 단점: 서버 코드 3곳 수정(Phase 0 중 기능 개발 금지 원칙과 충돌 소지).

**(B) 계획대로 P1로 미룬다.**
- 장점: Phase 0에 집중.
- 단점: **Phase 0을 끝내도 "좋아졌는지" 말할 수 없다.** 6개월 뒤 "느낌상 나아졌다"고 하게 된다 — 오늘 하루 종일 싸운 그 병(프록시로 판정)이다.

**(C) 별도 계측 파이프라인을 구축한다** — 과잉. 예산 위반.

## 3. 선택 (권고)

**(A).** 단 **최소 범위**로 한정한다:

1. ollama 호출 3곳에서 응답의 `prompt_eval_count`·`eval_count`·`total_duration`을 읽어 **기존 `cost` 필드에 기록**한다. (신규 필드·신규 스키마 없음)
2. 헤드리스 실행자 발사 시 `claude -p --output-format json`으로 `usage`(input/output tokens)를 받아 `outputs/` 로그에 남긴다. **발사 방식은 바꾸지 않는다**(FAIL-2026-013 재발 방지 — 인자 전달 방식은 그대로).
3. 하네스는 **만들지 않는다.** 값이 쌓인 뒤 "토큰이 줄었는가"를 판정할 하네스가 필요해지면 그때 HS-GATE로 승격 심사한다(계획서 §0.4 — **볼 데이터가 실재하는가**가 관문인데, 지금은 데이터가 없다. **먼저 데이터를 만드는 것이 정답이다.**)

## 4. 판단 기준

- **계측 없이는 판정할 수 없다.** 오늘의 제1교훈(판정은 실체로)을 프로젝트 목표 자체에 적용한 것.
- **비용이 거의 0이다.** 이미 오는 숫자를 이미 있는 필드에 적는 일이다. Phase 0의 예산을 잠식하지 않는다.
- **하네스를 먼저 만들지 않는다.** `gate-audit`이 "감사할 데이터가 없는데" 만들어졌다가 철회된 실패(FAIL-2026-012)의 역방향 교훈 — **데이터가 먼저다.**

## 5. 결과 (승인 시)

- SONNET-QUEUE에 `LEDGER-01`(ollama 토큰 기록) 지시서 발행. allowlist: `server/OllamaExecutor.cs`·`server/OllamaReviewer.cs`·`server/Tier2Approver.cs`·`server/Engine.cs`(cost 배선부).
- Phase 0(P0-03~07)과 **병행 가능**하다 — 영역이 겹치지 않는다(코덱스=`server/Harness/`, sonnet=`server/` 나머지).
- 값이 30일 쌓이면 "Context Engineering이 실제로 토큰을 줄였는가"를 **숫자로** 답할 수 있다.

## 6. 되돌림 조건

기록이 성능에 영향을 주거나(응답 지연) 로그가 비대해지면 샘플링(N회 중 1회)으로 낮춘다.

## 7. 관련 실패 사례

- FAIL-2026-012 (볼 데이터가 없는데 하네스를 만들어 오보 생산 → 철회). **이 ADR은 그 반대 방향이다: 하네스 이전에 데이터를 만든다.**