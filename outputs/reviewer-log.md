# reviewer-log — 검수자 세션 전용 기록 (다른 주체는 읽기만)

> 2026-07-11 신설. 검수자가 `outputs/review-log.md`(조율자 전용)에 쓰다가 동시 쓰기로 기록 3건이 소실됐다. 이제 검수자는 여기에만 쓴다.

## 2026-07-11 20:2x — 소실된 기록 3건 복원

1. **기준 변경(maxLoopIterations 10→100)**: 사람 승인·근거·되돌리는 법 → `docs/handoff/BASELINE-CHANGES.md` BC-001로 이관(정본).
2. **claim-check MISMATCH = 오탐 확정**: 정규식 `server/[A-Za-z0-9_/\.]+\.cs`에 종단 경계가 없어 `.csproj`를 `.cs` 파일 주장으로 오인. → **코덱스가 H-6(a941177)로 수정 완료. 재실행 결과 claimCount 12 / mismatch 0 / MATCH.** 조율자가 ACTOR-01을 커밋(6929406). **해소됨.**
3. **하네스 오탐 override 규칙**: 사람 승인 + 실체 입증 + 하네스 수정 과제 등재 + `[harness-override]` 커밋 메시지, 4개 전부 충족 시에만. → 조율자 프롬프트에 반영됨. **이번 건은 override 없이 코덱스가 고쳐서 자연 해소.**

## 2026-07-11 20:2x — FIX-04 검수 (measure 4 → 1)

- 하네스: build exit 0 / verify-behavior exit 0(behaviorEqual:true) / measure exit 1(violationCount **1**, 직전 4).
- 해소: smallTouchTargets 0 · skillDomainViolations 0 · functionsWithoutComment 0 · renderApprovalPanel 159→78줄(appJsLines 2692 상한 유지).
- 잔존: maxFunctionLength 115 (server/BalanceTuner.cs:43-157) — 가장 긴 함수가 해소되자 그 뒤 사전 위반이 드러남(두더지 잡기 구조). 실행자는 allowlist 밖이라 중단·보고 = 지시서 준수. → FIX-05 발사(PID 24408).
- **ACK 미검출 3번째**(ORCH-01·ACTOR-01·FIX-04 전부). `claude -p`는 stdout에 최종 메시지만 내보내므로 "맨 첫 줄 ACK" 규약은 **구조적으로 작동하지 않는다.** launch-check(H-00)가 이걸 반영하지 않으면 정상 산출물을 전부 오탐 처리한다 — 코덱스에 전달 필요.
## 2026-07-11 23:5x — P0-04 검수 + WORKSTATE 정정

- **P0-04 PASS**: projection exit 0 / **handoff-integrity exit 1 → exit 0(PASS)** / idempotent(2회 해시 동일) / measure 비악화(1, 코덱스 몫) / GENERATED 경고 삽입 확인.
- **ADR-005 자진신고 절이 첫 회에 작동**: 실행자가 `## 지표는 만족했으나 목적은 미달인 부분`에 **"WORKSTATE diId를 P0-04로 갱신하지 못함"**을 스스로 신고했다. 검수자가 잡기 전에 실행자가 먼저 말했다. **자리를 주면 쓴다.**
- **검수자 정정**: WORKSTATE의 phaseId/wpId/diId를 FIX-07 → **P0-04**, status → verifying 으로 수정하고 `projection` 재실행해 L0 갱신. 상태 원본이 낡은 채로 남는 것이 P0-04의 존재 이유와 정면 충돌하므로 즉시 정정했다.
- 이사(ADR-007): 코덱스 `target_thread_id` 제거 완료(TOML 파서 검증). 검수자용 `docs/handoff/REVIEWER-HANDOFF.md` 신설.

## 2026-07-12 00:0x — 새 검수자 세션: P0-04 재판정 + 하네스 반증 시험

인수인계 4문서(REVIEWER-HANDOFF / RUNTIME-INDEX / review-log / SESSION-051)로 상태 재구성. 문서 대신 실체로 재판정했다.

**실측 (전부 exit code)**

| 검사 | 명령 | exit | 결과 |
| --- | --- | ---: | --- |
| P0-04 성공 기준 | `-- handoff-integrity` | **0** | PASS, changedFileCount=3, failureCount=0 |
| projection 멱등성 | `-- projection` ×2 | 0 | RUNTIME-INDEX 해시 2회 동일(`83746EDF…`) → 멱등 |
| **하네스 반증 시험(신규)** | FIX07 지시서에 1줄 주입 → `handoff-integrity` | **1** | FAIL, `hashMatches:false` 정확히 그 파일만 지목 |
| 원복 후 재실행 | `git checkout --` → `handoff-integrity` | **0** | PASS 복귀 |

→ **게이트는 공허하지 않다.** 통과를 믿기 전에 **실패시킬 수 있음**을 먼저 증명했다. exit 0은 "검사가 없어서"가 아니라 "검사를 통과해서"다. **P0-04 성공 기준 충족.**

**지표는 만족했으나 목적은 미달인 부분 (ADR-005)**

1. **WORKSTATE가 P0-04를 선언하면서 `changedFiles`는 여전히 FIX-07의 것**(`dashboard/app.js`, `docs/verification/fix07-*`, `docs/directives/FIX07-*`). 앞 세션이 diId만 P0-04로 고치고 `changedFiles`를 `history`로 회전시키지 않았다. 결과: **하네스는 P0-04의 산출물이 아니라 남의 DI의 파일 3건을 검증하고 green이다.** 해시 검증 자체는 진짜지만, **"핸드오프가 현실을 기술한다"는 목적은 미달.** ← P0-04의 잔여 결함. (수정 주체: 실행자/코덱스. 검수자는 WORKSTATE에 쓰지 않는다.)
2. **스탬핑 주체 = 검증 주체.** `projection`이 sha256을 쓰고 `handoff-integrity`가 그걸 검증한다. 탬퍼(파일 변경)는 잡지만 **재스탬핑(projection 재실행)으로 언제든 green을 제조할 수 있다.** 구조적 한계 → `HS-GATE-P00` 판정 시 사람이 알고 있어야 할 항목. 게이트 자체를 폐기할 사유는 아니다(현재 목적은 드리프트 탐지).

**정정된 낡은 정보**

- SESSION-051 "P0-03 하네스 코드가 워크트리에 미커밋" → **틀렸다(지금은).** `a7068ad`로 projection CLI + handoff-integrity 하네스 **커밋 완료**, `server/` 워크트리 clean.
- P0-04 실행자 **PID 9804 = DEAD**(종료). 산출물 커밋됨. 실행 중 sonnet 없음.
- 샌드박스 git의 `fatal: unknown index entry format 0xffff0000` → **저장소 손상 아님.** 호스트 git은 정상 동작. 리눅스 샌드박스 git 버전이 Windows가 쓴 index 포맷을 못 읽는 것뿐. **프록시를 원인으로 단정하지 않았다.**

## 2026-07-11 23:50 — LEDGER-01 발사 (사람 승인, ADR-006)

사람(choi)이 명시 승인 → 발사. **발사는 사람 게이트다.**

**지시서 근거를 실측으로 다시 깔았다** (ADR-006의 숫자는 낡아 있었다 — "664건"이 실제로는 938건):

| 사실 | 실측(호스트) |
| --- | --- |
| `run-log.json` `entries` | **938건** |
| `cost` 필드 보유 항목 | 938/938 |
| **토큰 값이 채워진 항목** | **0건** |
| ollama 데몬 | **UP** (`/api/tags` 200, qwen3:8b·llama3.1:8b·qwen3:14b) |

→ ollama가 살아 있으므로 **"실제 호출로 토큰이 기록되는가"를 검수 기준으로 삼을 수 있다.** 코드 설명이 아니라 **기록된 숫자**를 증거로 요구했다.

**발사 전 게이트 4개 (실측)**

| 게이트 | 결과 |
| --- | --- |
| `gate-clean server` | **exit 0**, contentDirtyCount=0 |
| 실행 중 sonnet 실행자 없음 | ✅ 살아있던 claude 2건은 `--input-format stream-json` **데스크톱 세션**(헤드리스 `-p` 실행자 아님). PID 9804(P0-04)는 DEAD, sonnet-P004 로그 23:38 이후 정지 |
| 이전 항목 커밋됨 | ✅ P0-04 = `a7068ad`, `server/` clean |
| 다음 대기 존재 | ✅ `queue/directive-LEDGER01-token-ledger.md` 신규 발행 |

**발사**: PID **20896**. §6 방식(단일 인용 문자열 argline) 준수 → `cmdlen=1191`로 **프롬프트 온전 도착 확인**(FAIL-2026-013 회피). I-1 완화: 다른 큐/지시서 열람 금지 + task ID 결속. SONNET-QUEUE #20에 **진행(PID)** 표기 → **조율자 이중 발사 차단.**

**지시서에 실은 안전장치**: 신규 스키마·필드·하네스 0개 / `estimatedUSD`·`subscriptionCalls` 무접촉(로컬 ollama는 과금 없음, 0을 "모름"으로 바꾸는 것도 스키마 변경) / **토큰 필드가 없으면 추정치를 넣지 말고 0 유지 후 보고** / **dev-pack json 손 편집 = 위조 = 즉시 반려**(값은 서버가 실행되며 스스로 적어야 한다) / `handoff-integrity` exit 0 유지(P0-04에서 막 세운 게이트를 깨지 마라).

## 2026-07-12 00:1x — 도구 주의: **샌드박스 마운트는 실체가 아니다**

리눅스 샌드박스에서 `run-log.json`을 읽으니 11,379줄에서 잘려 **JSON 파싱 실패**했다. "런타임 파일 손상"으로 보고할 뻔했다. **호스트에서 재측정: 22,626줄, JSON 유효, 해시 2회 안정.** 파일은 멀쩡했고 **마운트가 절반만 보여준 것**이다.

같은 계열로, 샌드박스 `git status`는 `fatal: unknown index entry format 0xffff0000`을 냈지만 **호스트 git은 정상**이다(리눅스 git이 Windows가 쓴 index 포맷을 못 읽음). **저장소 손상 아님.**

→ **규칙: 이 저장소의 실체 판정은 호스트(PowerShell)에서 한다. 샌드박스 뷰로 결함을 단정하지 마라.** 오늘 하마터면 다섯 번째로 프록시에 속을 뻔했다.

**다음 수**

- LEDGER-01 검수(PID 20896 종료 후): 위 6개 하네스 exit code + **토큰이 실제로 기록된 run-log 항목 원문**. 자기보고 신뢰 금지 — 하네스 직접 재실행.
- WORKSTATE `changedFiles` 회전(FIX-07 → history) — LEDGER-01 지시서에 실어 실행자가 처리하게 했다.
- P0-05는 여전히 data gate 블록(`requiredInputs`/`readOrder` 스키마 부재).

## 2026-07-12 00:2x — 조율자 23:52 회차 검수 (자기보고 ↔ 실체 대조)

조율자가 **자기 오염을 스스로 신고**했다(`review-log.md` 전체 재작성 → 기존 깨진 글자 재인코딩 → `git checkout`으로 되돌리고 append로 재작성 → 커밋 교체). **자기보고가 실체와 일치하는지 직접 확인했다:**

| 확인 | 실측 |
| --- | --- |
| 이력 재작성 흔적 | reflog: `fcb085b`(오염) → `reset fcb085b~1` → `7dab8f2`(정상). **로컬 전용** — origin은 12커밋 뒤라 **공유 이력 훼손 0** |
| `7dab8f2`가 실제로 깨끗한가 | diff = `outputs/review-log.md` **1파일, +19/-1**. 전체 재작성이 아니라 **순수 추가** → 되돌림이 실제로 먹혔다 |
| 파일 오염 잔존 | U+FFFD **0개** |
| 내 기록 손실 | 없음(4개 항목 전부 생존) |

→ **조율자 자기보고 = 실체. 이 회차 통과.** 결재 대행 없음, sonnet 발사 없음, 기준 파일 무접촉 — 전부 규칙대로다.

### 그러나 조율자가 한 번 프록시로 추측했다 (FAIL-2026-012 재발)

조율자 보고: *"PID 20896이 `server/OllamaReviewer.cs`와 **`docs/handoff/SONNET-QUEUE.md`**를 편집하기 시작했다."*

**틀렸다.** `git diff -- docs/handoff/SONNET-QUEUE.md` = **row 20 한 줄, 전부 검수자(나)의 편집**이다. 실행자는 큐를 건드리지 않았다 — **지시서가 큐 열람을 금지했고, 실행자는 그걸 지켰다.** 조율자는 "같은 시각에 바뀐 파일 = 그 프로세스가 썼다"고 **타이밍 상관으로 주체를 단정**했다. 실체(diff 내용)를 보면 1초면 알 수 있었다.

**작은 오판이지만 그냥 넘기지 않는다** — 오늘 우리를 네 번 틀리게 한 바로 그 사고방식이고, 여기서는 무해했지만 **"실행자가 지시서를 어겼다"는 누명**이 될 수 있었다. 주체 판정은 **diff 내용**으로 한다.

### 이번 회차로 고친 것 (CLAUDE.md 직접 경로, 사유 기록)

1. **`## 관례 > 기록 파일은 append만 한다`** 신설 — 근본 원인은 조율자의 부주의가 아니라 **저장소에 이미 박혀 있는 깨진 한글**이다(`WORKSTATE.json`의 note: `"IsWithinRoot(separator-bounded) ?ы띁"`). 그 바이트를 통째로 다시 쓰면 조용히 바뀐다. 게다가 **U+FFFD가 아니라 이중 디코딩된 정상 유니코드라 자동 검출도 안 된다.** 그래서 **규칙으로 막는다** — 프롬프트로 시키지 말고 코드/규칙으로 강제하라(INTENT-DIGEST).
2. **`outputs/reviewer-log.md`를 git에 넣었다.** 오늘까지 **untracked**였다 — 조율자 로그는 커밋되는데 검수자 로그만 버전관리 밖이었다. **판정 근거가 git 밖에 있는 것**은 CLAUDE.md가 지적한 "기준 변경이 이력에 하나도 안 남았다"와 같은 병이다. ADR-003 보강으로 **검수자가 직접 커밋**한다고 명시했다.
3. **ADR-006 상태를 `승인됨`으로 갱신**(사람 choi, 2026-07-11 23:5x) + §1의 낡은 수치(664건) → 실측(938건)으로 정정. 조율자가 관측한 "승인상태 불일치" 해소.

## 2026-07-12 00:4x — P0-05 데이터 관문 해제 (형식 + 실데이터)

코덱스가 **두 회차 연속(세션 050·051) "볼 데이터가 없다"며 하네스 제작을 거부**했다. **옳은 거부였다** — `hs-gate.md` 2항(볼 데이터가 실재하는가)을 지킨 것이고, `gate-audit`이 그걸 안 물어서 철회됐다. 막힌 쪽은 코덱스가 아니라 **데이터를 만들 책임이 있는 검수자(나)였다**(ALIGNMENT §4: P0-05 형식 = 검수자).

**만든 것**

1. **형식 정본** — `docs/directives/_header.md`에 「Context Pack」 절 신설. 펜스 언어 태그 `context-pack` + JSON: `diId` · `requiredInputs[{path, sha256}]` · `readOrder[]` · `forbiddenActions[]`.
2. **실데이터 1건** — `directive-FEAT01-conditional-delegation.md` 머리에 실제 블록. **sha256은 `Get-FileHash`(프로그램)가 계산했다.** LLM이 적은 값이 하나도 없다.
3. **코덱스 큐 P0-05를 `착수 가능`으로 전환** + 판정 규칙표(`missing`/`stale` → exit 1, 블록 없음 → `skipped`) + 장애 주입 시험 의무 명시.

**핵심 설계 결정 두 가지 (근거 있음)**

- **`requiredInputs`(읽기 참조)와 `allowlist`(쓰기 대상)는 겹치지 않는다.** 작업 중 바뀌는 파일에 해시를 걸면 **게이트가 자기 작업에 걸려 넘어진다.**
- **`context-pack-integrity`에 스탬핑 기능을 넣지 마라 — 검사만 한다.** P0-04에서 배운 것: `handoff-integrity`는 **스탬핑 주체(`projection`)와 검증 주체가 같아서** 재실행으로 green을 제조할 수 있다. 이번엔 **고치는 자와 검사하는 자를 분리**한다(ADR-002와 같은 원리).

**실측 (프로토타입 검사기로 3케이스 확인)**

| 케이스 | 결과 |
| --- | --- |
| FEAT-01 정상 | 파싱 OK, requiredInputs 5건 전부 `ok` → **exit 0** |
| 장애 주입: 참조를 유령 경로로 교체(사본) | `MISSING docs/handoff/DELETED-GHOST.md` → **exit 1** ← **ORCH-01 유령 참조 사건이 이제 기계에 잡힌다** |
| 블록 없는 지시서(P0-04) | `PARSE_FAIL` = `skipped` → 게이트 잠기지 않음 |

### 내 실수 두 건 (숨기지 않는다)

1. **첫 검사가 진짜 stale을 잡았는데 그게 내 것이었다.** `_header.md` 해시를 뜬 **뒤에** 그 파일을 편집해서 해시가 어긋났다. → 메커니즘이 의도대로 작동한다는 증거였지만, **동시에 "해시는 쓰는 순간 낡는다"는 구조적 사실**이다. 그래서 코덱스에게 **stale 검출이 이 하네스의 존재 이유**라고 명시했다.
2. **`git checkout -- docs/directives/_header.md`로 장애 주입을 원복하다가, 아직 커밋 안 한 내 Context Pack 절까지 통째로 날렸다.** 되살렸고, 이번엔 **검증 직후 즉시 커밋**했다. 교훈: **미커밋 작업물 위에서 `git checkout`으로 장애 주입 시험을 하지 마라 — 사본에서 해라.** (2차 시험은 `$env:TEMP` 사본으로 했다.)

## 2026-07-12 00:5x — LEDGER-01 검수 (독립 재실행) — **지표 PASS, 목적 1/3**

조율자가 먼저 검수·커밋했다(`9d4aac5` server / `8a982d4` docs / `174be5f` WORKSTATE+projection / `430b307` 큐). **실행자 자기보고도 조율자 보고도 프록시다 — 전부 직접 재실행했다.**

**하네스 독립 재실행 (검수자, 2026-07-12)**

| 하네스 | exit | 수치 |
| --- | ---: | --- |
| `build server -c Release` | 0 | **경고 0 / 오류 0** |
| `measure dev-pack` | 0 | **violationCount 0** (직전 1 → 0. 비악화 아니라 개선) |
| `verify-behavior` | 0 | `behaviorEqual: true` |
| `handoff-integrity` | 0 | PASS, failureCount 0, **`diId: LEDGER-01`** ← changedFiles 회전이 실제로 됐다 |
| `gate-clean server` | 0 | contentDirtyCount 0 |

**실체 증명**: `run-log.json`에 토큰이 진짜 찍혔다 — `review.tier1_completed`, `inputTokens: 1541`, `outputTokens: 144`. `cost` 스키마 키는 그대로(`inputTokens, outputTokens, estimatedUSD, subscriptionCalls, role`), `estimatedUSD`·`subscriptionCalls`는 0 유지. **신규 필드 0개 요구 준수.**

### 그런데 958건 중 토큰이 찍힌 항목은 **1건뿐이다**

| 호출부 | 토큰을 읽는가 | run-log 도달 |
| --- | --- | --- |
| `OllamaReviewer.cs` | ✅ | **✅ 유일** |
| `OllamaExecutor.cs` | ✅ (`totalIn`/`totalOut` → `ExecutorGenerateResult`) | ❌ **0으로 기록** — run-log를 쓰는 곳은 `Program.cs`인데 **allowlist 밖이었다** |
| `Tier2Approver.cs` | ✅ | ❌ **run-log 항목 자체가 없음** |

실측: `proposal.generated` 항목의 `cost.inputTokens = 0`. **토큰을 가장 많이 쓰는 경로(제안 생성)가 여전히 0이다.**

### 이건 실행자 잘못이 아니라 **내 잘못이다**

ADR-006이 배선부를 "`Engine.cs`"라고 적었고 **내가 그대로 allowlist에 옮겼다.** 실제 배선부는 **`Program.cs`**였다(`RuntimeCost()` 호출 21곳). 실행자는 **allowlist를 지키고 막힌 사실을 정확히 신고했다** — 지표를 green으로 만들려고 범위를 넘지 않았다. **ADR-005 자진신고 절이 두 회 연속 작동했다(P0-04, LEDGER-01). 자리를 주면 쓴다.**

**판정: LEDGER-01 = PASS.** 지표 전부 통과 + 실체 증명 1건. **목적은 1/3** — 잔여는 내 allowlist 오류이며 `LEDGER-02`로 이어받는다.

### LEDGER-02 발행 (사람 승인 대기 — 발사는 사람 게이트)

`docs/handoff/queue/directive-LEDGER02-executor-token-wiring.md`. **신설 Context Pack 형식을 처음으로 적용한 지시서다**(자기 밥그릇 먹기). allowlist에 **`server/Program.cs` 포함**. 못박은 것:

- `Program.cs`의 `RuntimeCost()` 호출 21곳 중 **대부분은 LLM 호출이 아니다**(rule-engine·측정·결재). **거기에 토큰을 억지로 채우지 마라 — 0이 정답이다.** 재지 않은 것을 잰 것처럼 적는 것이 우리가 싸우는 병이다.
- **Tier2Approver run-log 경로 신설은 범위 밖** — 새 이벤트 타입 = 스키마 변경 = ADR 필요. `LEDGER-03` 후보로 남겼다.
- **`role` 분화(`executor`/`reviewer`)도 범위 밖** — 스키마 의미 변경이라 사람 결재 사항. LEDGER-01 실행자가 정확히 이 이유로 손대지 않았다.
- 검수 기준에 **"비-LLM 항목이 여전히 0인가"**를 넣었다 — 토큰을 채우는 게 목표가 되면 **안 쓴 곳에도 숫자를 채우게 된다**(ADR-005: 지표를 목표로 삼지 마라).

## 2026-07-12 00:32 — LEDGER-02 발사 (사람 승인)

**발사 전 게이트 4개(실측)**: `gate-clean server` **exit 0**(contentDirty 0) / 헤드리스 `-p` 실행자 **없음**(CommandLine에 `--dangerously-skip-permissions` 포함 프로세스로 판정 — 데스크톱 세션과 구분) / 이전 항목 **커밋됨**(LEDGER-01 = `9d4aac5`, `server/` clean) / 다음 대기 **존재**(LEDGER-02 지시서 + Context Pack 자체검사 exit 0).

**발사**: PID **29060**. `cmdlen=1748` → **프롬프트 온전 도착**(FAIL-2026-013 회피). SONNET-QUEUE #21에 **진행(PID)** 표기 → 조율자 이중 발사 차단.

**이번 지시서의 특이점**: **신설 Context Pack 형식을 실제로 쓴 첫 지시서다**(dogfood). `requiredInputs` 5건 전부 `ok`로 검사 통과한 뒤 발사했다 — **형식을 만들었으면 자기가 먼저 먹어본다.**

## 2026-07-12 00:5x — ★ 런타임이 **작업 중인 파일**에 대해 제안을 만들었다 (P0-06 근거 실측)

조율자 00:39 회차가 신규 제안 1건을 HUMAN-INBOX에 올렸다(결재하지 않음 — 옳다). **실체를 봤다:**

```
proposal: functionsWithoutComment @ server/OllamaExecutor.cs:569
실제 코드(그 순간 스냅샷):
  569: private static bool __TokenProbe(int x) => x >= 0;
```

**이건 LEDGER-02 실행자(PID 29060)가 작업 중에 남긴 임시 디버그 함수다.** 즉 **rule-engine이 반쯤 쓰다 만 파일을 측정해, 그 미완성 코드를 고치라는 제안을 생성했다.**

**⛔ 이 제안은 승인하면 안 된다.** 반입이 **실행자가 지금 쓰고 있는 파일에 겹쳐 쓴다** — FAIL-2026-004(병렬 실행자 워크트리 오염) 계열이다. 조율자는 결재하지 않고 사람에게 올렸다 — **정확한 처신.**

### 이것이 P0-06의 존재 이유다 (근거 사례 추가)

지금은 유령 제안 1건이라 무해하지만, **원리적으로 이 시스템은 아무 때나 남이 작업 중인 파일에 대해 제안을 만들고 결재를 요청할 수 있다.** 런타임이 **"이 파일은 지금 누가 잡고 있다"를 모르기 때문**이다. `FILE-CLAIMS.json`(P0-06)이 정확히 이걸 막는다.

**고아 코드 109줄 사건(주체 규명 불가)에 이어 P0-06의 두 번째 실측 근거다.** 그때는 사후에 몰랐고, 이번엔 **실시간으로 충돌 직전이었다.**

### `__TokenProbe`는 개입하지 않는다 — 게이트가 잡는다

실행자가 이 디버그 함수를 지우지 않고 끝내면 `measure violationCount`가 **0 → 1**이 된다. LEDGER-02 지시서에 **"현재 violationCount 0이다. 네가 0을 깨뜨리면 실패다"**를 검수 기준으로 못박아뒀다. **자기 기준에 자기가 걸린다.** 손대지 않고 지켜본다 — **개입하면 게이트가 작동했는지 알 수 없게 된다.**

> **후속(01:1x)**: 실행자가 **스스로 제거했다.** `git grep __TokenProbe` = 0건, `measure dev-pack` violationCount **0** 복귀. **게이트가 작동했다. 개입하지 않은 판단이 옳았다.**

## 2026-07-12 01:1x — LEDGER-02 검수: **지표 PASS, 핵심 실체 증명 미달** + ★ 진짜 병 발견

조율자가 먼저 검수·커밋했다(`040d017` server / `f6bef6b` docs+상태). **직접 재실행해 대조했다.**

| 판정선 | 결과 |
| --- | --- |
| build / verify-behavior / measure / handoff-integrity / gate-clean | 전부 **exit 0** |
| `__TokenProbe` 제거 | ✅ 스스로 제거, measure 0 복귀 |
| **비-LLM 항목에 토큰을 날조했는가** | ✅ **0건** — rule-engine 항목 전부 0 유지. **"안 쓴 곳에도 숫자를 채우지 마라"를 지켰다** |
| **`proposal.generated`에 토큰이 찍혔는가** | ❌ **0** — **배선이 한 번도 실행되지 않았다** |

**실행자가 미달을 자진 신고했다(ADR-005 세 번째 연속 작동).** 12회 시도했으나 전부 실패. 그 실패의 원인 추적이 **이 회차의 진짜 수확이다.**

### ★ 우리는 LLM을 쓴다고 믿었지만 rule-engine 출력을 받고 있었다

```
00:57:50  review.tier1_completed   provider=ollama       in=1541 out=147   ← ollama 호출됨(토큰 있음)
00:57:56  proposal.generated       provider=rule-engine  in=0    out=0     ← 6초 뒤, 산출물은 rule-engine
00:58:02  review.tier1_completed   provider=ollama       in=1541 out=146
00:58:09  proposal.generated       provider=rule-engine  in=0    out=0
```

**원인(실체 확인)**: `server/OllamaExecutor.cs:395` — `metricId == expectedMetricId` **대소문자 완전일치**. `qwen3:8b`가 `functionsWithoutComment`를 **`functionsWithOutComment`(대문자 O)**로 반환 → `ParseNoteResponse` → `null` → **rule-engine 폴백.**

**그리고 그 폴백이 완전히 조용하다.** run-log에 `fallback`·`fail`·`warn`·`error` 이벤트 **0건**.

**회귀 아님(타임라인으로 확인 — 프록시로 단정하지 않았다)**: 마지막 ollama 제안 **2026-07-11 23:40**, LEDGER-01 커밋 **00:22**. **우리 변경 이전부터 그랬다.** 아무도 몰랐을 뿐이다.

### 이것이 ADR-006이 옳았다는 증거다

"토큰을 재자"고 켰더니 **첫 계측에서 "우리가 LLM을 안 쓰고 있었다"가 나왔다.** 계측이 없었으면 영영 몰랐다. **지표는 목적의 프록시지만, 없는 지표는 프록시조차 아니다.**

**판정: LEDGER-02 = 조건부 PASS.** 지표 전부 통과 + 날조 없음. **단 배선은 미검증이다** — 코드는 커밋됐지만(조율자) **실행된 적이 없다. "완료"로 잊지 마라.** LEDGER-03 이후 재검증한다.

### LEDGER-03 발행 (사람 승인: 관측부터 켠다)

`docs/handoff/queue/directive-LEDGER03-fallback-observability.md`. 사람이 4개 선택지 중 **"관측부터 켠다"**를 선택했다.

- **파서를 관대하게 고치지 않는다.** 대소문자 무시로 바꾸면 **모델 출력 오류를 코드가 흡수해 감춘다.** 먼저 **빈도와 원인을 재고**, 그 데이터로 다음을 정한다(hs-gate 2항 — 데이터가 먼저다).
- `proposal.fallback` 이벤트(`level: warn`) + **`reason`을 5종으로 분해**(`parse-rejected-metricid`/`-note`/`-json`/`ollama-unreachable`/`ollama-disabled`, 나머지는 `unknown` + 원문 앞 200자). **"실패했다"로 뭉치면 다음 사람이 프록시로 추측한다.**
- **run-log 항목 스키마 불변** — 사유는 `params`에 담는다.
- **실체 증명의 난점을 지시서에 정면으로 실었다**: 저장소 위반이 0건이라(dev-pack·ruined-lab 둘 다) 제안 생성이 자연 트리거되지 않는다. 그래서 위반 주입을 **조건부 허용**하되 ①즉시 제거 ②measure 0 복귀 확인 ③주입 창에 뜨는 유령 제안은 **승인 금지·보고만**을 못박았다.
- **검수자의 진단을 실행자가 검증하게 했다**: "`actualMetricId`가 진짜 `functionsWithOutComment`인지 네가 확인해라. **틀렸으면 틀렸다고 보고하라 — 그게 더 가치 있다.**"

## 2026-07-12 01:4x — LEDGER-03 검수: **PASS. 침묵이 사라졌다.**

조율자 커밋(`14ad2fc` server / `9ed5732` docs). **직접 재실행해 대조했다.**

**실체 (run-log 원문)**

```json
{ "event": "proposal.fallback", "level": "warn",
  "params": { "reason": "parse-rejected-metricid",
              "expectedMetricId": "functionsWithoutComment",
              "actualMetricId":   "functionsWithOutComment",
              "provider": "ollama", "model": "qwen3:8b" } }
```

| 검수 항목 | 결과 |
| --- | --- |
| `proposal.fallback` 이벤트가 찍히는가 | ✅ **찍힌다.** `level: warn`, `reason` 분해됨 |
| **검수자 진단이 맞았는가** | ✅ **적중.** `actualMetricId = functionsWithOutComment`(대문자 O) — **실체로 확인됐다** |
| **파서를 몰래 완화했는가**(범위 밖 금지) | ✅ **안 했다.** `OllamaExecutor.cs:408` — `if (metricId != expectedMetricId)` **여전히 엄격** |
| run-log 스키마 불변 | ✅ 사유는 전부 `params`에. 최상위 필드 추가 없음 |
| 주입 흔적 제거 + measure 복귀 | ✅ `git grep` 0건, `violationCount 0` |

**판정: LEDGER-03 = PASS.** **이제 우리는 폴백이 일어나면 안다.**

### 이제 데이터가 있다 — 다음은 사람의 결정이다

원인이 **결정적**임이 확인됐다: `qwen3:8b`가 metricId의 대소문자를 일관되게 틀린다. 선택지는 셋이고 **성격이 다르다.**

1. **파서를 대소문자 무시로 완화** — ollama가 즉시 되살아난다. 위험: **모델 오류를 코드가 흡수해 감춘다.** 다음에 모델이 다른 방식으로 틀리면 또 조용해진다.
2. **정규화 + 계속 기록**(권고) — 기대값과 **대소문자 무시로만** 대조해 통과시키되, **정규화가 필요했다는 사실을 `proposal.fallback`이 아닌 `warn` 이벤트로 계속 남긴다.** ollama는 살리고 **모델 품질 저하는 계속 보인다.** 관측을 끄지 않는 복구다.
3. **프롬프트를 고쳐 모델이 정확히 뱉게 한다** — 근본이지만 모델 의존적이고, 다른 모델로 바꾸면 다시 깨진다.

**추천: 2번.** 이 프로젝트의 원칙("프로그램이 검증한다")과 오늘의 교훈("침묵이 병이다")을 둘 다 지킨다. **단 이건 사람 결정이다.**

### LEDGER-02는 여전히 미검증이다 — 잊지 마라

`proposal.generated`에 토큰이 찍히는 배선(LEDGER-02)은 **아직 한 번도 실행되지 않았다.** ollama 제안 경로가 폴백으로 죽어 있었기 때문이다. **위 결정으로 ollama 경로가 되살아나면, 그때 `proposal.generated`의 `cost.inputTokens > 0`을 확인해야 LEDGER-02가 비로소 검증된다.** 두 작업은 이 지점에서 만난다.
