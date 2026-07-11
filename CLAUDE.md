# 이 저장소에서 작업하는 에이전트의 규칙

먼저 [AGENT-GUIDE.md](AGENT-GUIDE.md)를 읽는다 — API·작업 수명주기·금지선·기여 방법이 거기 있다.

## ★ 새 검수자 세션은 이 순서로 읽는다 (이사 규약 — ADR-007)

1. **`docs/handoff/REVIEWER-HANDOFF.md`** — 지금 어디까지 왔고 다음에 뭘 하는가. **검수자의 유일한 인수인계 원본이다.**
2. **`docs/context/RUNTIME-INDEX.md`** — 기계가 WORKSTATE에서 생성한 L0 상태(11줄). **손으로 쓴 문서보다 이걸 믿어라.**
3. **`outputs/review-log.md`**(조율자) + **`docs/handoff/sessions/`**(코덱스 최신) — 독립 관찰자 기록. **안 읽고 추측하면 틀린다.**
4. `docs/plan/INTENT-DIGEST.md` · `docs/handoff/decisions/ADR-*.md` — 왜 이렇게 하기로 했는가.

## ★ 이 프로젝트의 계획서 (2026-07-11 반입)

- **의도 요약(새 세션은 이것부터)**: `docs/plan/INTENT-DIGEST.md` — 이 프로젝트가 무엇을 하려는가. 핵심 철학은 **"LLM은 적게 기억하고 적게 생성한다. 프로그램이 많이 기억하고 조립하고 검증한다."** 판단이 갈리면 이 문장이 심판이다 — **프롬프트로 시키지 말고 코드로 강제하라.**
- **정본 계획서**: `docs/plan/AI-RUNTIME-REFACTOR-MICRO-DIRECTIVES-v9.md` (Phase 0~13, DI 체계, HS-GATE, 인수인계·컨텍스트 규약)
- **정렬 문서**: `docs/plan/ALIGNMENT-v9.md` — 계획서가 요구하는 것 ↔ 저장소의 실재 ↔ 진짜 공백. **동등물 선언이 여기 있다(`server/Harness/`=`harnesses/`, `skills/common/`=`skills/`, allowlist+scope-check=`FILE-CLAIMS`). 이름이 다르다고 새로 만들지 마라 — 재발명 금지.**
- **결정 기록**: `docs/handoff/decisions/ADR-*.md`. 대안 중 하나를 골랐으면 ADR을 남긴다(계획서 §0.5).
- **현재 운영 등급**: `Required Before Multi-model Parallel Work` (ADR-001, 사람 승인). 실행자 4명이 병렬로 돌고 자동 스케줄러가 켜져 있다 — **그 등급의 안전장치(Phase 0)를 세우는 중이다. `HS-GATE-P00` PASS 전까지 기능 개발(Phase 1)로 넘어가지 않는다.**

## 품질 게이트 (필수)
- 코드·데이터 수정 후 커밋 전에 반드시 측정을 실행한다:
  `dotnet run --project server -- measure dev-pack` (아래 CLI, 서버 기동 불필요)
- 위반(deviations)이 0이 될 때까지 수정 후 재측정한다.
- 끝내 통과하지 못하면 남은 위반 목록을 작업 보고에 그대로 포함한다 — 숨기지 않는다.

## 금지 사항
- blueprint·workflow-definition·측정 코드를 수정해서 게이트를 통과하는 것.
  기준 변경은 사람 결재 사항이다.
  - **기준 파일은 `dashboard/data/*/blueprint.json`(목표치)과 `dashboard/data/*/workflow-definition.json`(가드레일·정책) 둘이다.**
    이 둘은 **커밋 대상**이다 — 나머지 `dashboard/data`(measurement·run-log·patch-proposal·review-report·workflow-state)는 런타임 산출물이라 커밋하지 않는다.
    2026-07-11까지 `dashboard/data` 통째 제외 규칙 때문에 **기준 변경이 git 이력에 하나도 남지 않았다.** 결재는 사람만 하는데 그 결과물이 버전관리 밖에 있었다.
  - 기준 파일을 바꿨으면 **`outputs/review-log.md`에 ①주체(누가 승인했는가) ②근거 ③되돌리는 법을 반드시 남긴다.**
    근거 기록 없는 기준 파일 변경은 **커밋되지 않고 HUMAN-INBOX에 '무단 변경 의심'으로 올라간다**(조율자 규칙).
- approve/reject 계열 액션 호출. 결재는 사람 몫이다.
- Engine.cs·Storage.cs·Guardrails.cs에 도메인 지식(게임 용어, metricId, ollama 코드)을 넣는 것.

## 관례
- 주석은 한국어 기능 설명만 (파일 머리 1~2줄 + 함수 위 1줄). 함수 수정 시 주석 갱신.
- 커밋 전 git status로 bin/, obj/, history/ 미포함 확인.
- 코드 변경의 기본 경로는 dispatch/outbox다 — 사본에서 작업하고 diff를 제출하며, 반입은 사람이 한다.
  직접 수정 + 커밋은 예외이며 다음 경우에만 쓴다: ①관례·가이드 문서 자체(CLAUDE.md, AGENTS.md,
  AGENT-GUIDE.md, skills/, docs/) ②지시서에 "직접 경로"가 명시된 경우. 예외를 썼으면 작업 보고에
  사유를 남긴다.
- 지시서(#12부터)는 불변 제약을 인라인으로 싣지 않고 "이 지시서는 docs/directives/_header.md의
  불변 제약을 따른다" 1줄로 참조한다 — 전문은 docs/directives/_header.md에 있다(#11까지는 인라인).

### ★ 기록 파일은 append만 한다 — 통째로 읽어 통째로 쓰지 마라 (2026-07-12 신설)

**대상**: `outputs/review-log.md`(조율자) · `outputs/reviewer-log.md`(검수자) · `docs/handoff/sessions/*`(코덱스) ·
`docs/handoff/HUMAN-INBOX.md` · `docs/handoff/BASELINE-CHANGES.md` — **누적 기록 파일 전부.**

- **금지**: 파일 전체를 읽어(read-all) 전체를 다시 쓰는(write-all) 방식. **끝에 덧붙이기(append)만 한다.**
- **이유(실제 사고)**: 조율자가 2026-07-11 회차에 `review-log.md`를 통째로 재작성하다가 **원래 깨져 있던 글자들을
  다시 인코딩해 파일을 오염**시켰다. 스스로 발견해 `git checkout`으로 이전 blob을 정확히 되돌리고 append로 다시 붙여
  커밋을 교체했다(`fcb085b` 폐기 → `7dab8f2`). **이번엔 잡았지만 다음엔 못 잡는다.**
- **근본 원인은 부주의가 아니다.** 이 저장소에는 **이미 깨진 한글이 박혀 있다**(예: `docs/handoff/WORKSTATE.json`의
  note 필드 — `"IsWithinRoot(separator-bounded) ?ы띁"`). 그 바이트를 통째로 다시 쓰면 **조용히 바뀐다.**
  깨진 글자는 U+FFFD가 아니라 **이중 디코딩된 정상 유니코드**라서 자동 검출도 안 된다.
- **자기 기록을 고쳐야 할 때**: 앞 내용을 수정하지 말고 **새 항목으로 정정을 덧붙인다**(기록은 이력이지 현재 상태가 아니다).
  파일을 반드시 재작성해야 하면 **작업 전 해시를 남기고, 작업 후 `git diff`가 의도한 줄만 바뀌었는지 확인한다.**

### 검수자 기록의 버전관리 (ADR-003 보강, 2026-07-12)

- `outputs/reviewer-log.md`는 **검수자가 직접 커밋한다**(관례 예외 ①에 준한다 — 자기 소유 기록 파일).
  조율자 커밋 레인에 없어서 **오늘까지 git 밖에 있었다.** 판정 근거가 버전관리 밖에 있으면
  기준 파일 변경이 이력에 안 남았던 그 병(위 '금지 사항' 첫 항목)과 같은 형태가 된다.
- 다른 주체는 이 파일에 **쓰지 않는다**(ADR-003 단일 기록자). 커밋도 검수자만 한다.

## 스킬 라우팅
- /skills/common/ 은 모든 작업에서 읽는다.
- /skills/domains/ 는 이번 작업이 변경할 파일 경로가 스킬의 '트리거:'와 일치하는
  것만 읽는다. 일치하지 않는 도메인 폴더는 열지 않는다. 애매하면 읽지 않는다.
- 작업 보고(verification 문서)에 **다음 세 가지를 반드시 기록한다**:
  ① **주체(actor)** — 누가 이 작업을 했는가(sonnet/코덱스/조율자/검수자, 식별자).
     *같은 오류가 반복될 때 어느 주체 탓인지 추적하기 위함. 기록이 없으면 프록시로 추측하게 되고, 그러면 틀린다(FAIL-2026-012).*
  ② **사용한 하네스** — 이번 작업에서 실행한 하네스와 그 결과(명령·exit code·핵심 수치).
     *조율자가 이 목록을 보고 직접 재실행해 대조한다. 기록하지 않으면 검사할 수 없다.*
  ③ **참조한 스킬** 목록.

## 지시 게이트 (착수 전 자가 검사)
- 지시를 받으면 착수 전에 검사한다: ① 완료 기준이 검증 가능한가
  ② 대상 파일·범위가 특정되는가 ③ 기존 원칙·blueprint와 충돌하지 않는가.
- 부족하면 추측으로 진행하지 말고, 부족 항목을 **선택지 딸린 질문**으로
  되물은 뒤 대기한다. 질문 없이 추측 진행한 부분은 보고에 '추측 진행'으로 명시한다.
- 원칙과 충돌하면 "기준 변경입니까?"를 확인한다 — 기준 변경은 사람 결재 사항.

## 게이트 기록 형식
- 커밋 전 `dotnet run --project server -- measure dev-pack` 결과를 verification 문서에 JSON 한 줄로 기록한다.
- 형식: `{"gate":"dev-pack","violations":0,"attempt":1}`
- 위반이 남으면 `violations`에 실제 개수를 적고, 이어서 남은 위반 목록을 적는다.
