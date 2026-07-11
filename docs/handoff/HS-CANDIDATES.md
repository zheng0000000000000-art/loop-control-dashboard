# HS-CANDIDATES — 하네스·스킬 승격 후보 판정 (v9 §0.4 HS-GATE)

> 규약: KNOWLEDGE-PROMOTION.md. 반복(2회 이상)이 승급의 조건. 점수 = 반복성·결정가능성·장애주입·격리·관찰성·유지가치 각 0~2, 총 0~12.
> `0~4 부적합 / 5~7 보류·기존확장 / 8~10 기존확장·기한부 / 11~12 즉시제작`
> **판정 주체는 코덱스**(파이프라인 1단계). 검수자가 올린 후보는 `코덱스 확정 대기`로 표시하고, 코덱스가 HS-GATE 회차에 확정한다.

<!-- hs-scan 이 읽는 메타. HS-GATE 수행 시 갱신할 것. -->
- `lastGate: 2026-07-11 21:45`
- `judgedClasses: unnormalized_gate, self_report_as_truth, config_side_effect, observability, path_escape, executor-orchestration`

## HS-01 `gate-clean` — 트리 clean을 정규화 내용 해시로 판정 (하네스)

- 상태: **즉시제작 제안 (12/12) — 코덱스 확정 대기**
- 올린 주체: 검수자 세션(2026-07-11). 근거 데이터: FAIL-2026-010, FAIL-2026-005, KNOWN-ISSUES I-8.
- 무엇을 검사: 지정 경로(예: `server/`)의 트리가 깨끗한지를 **raw `git status`가 아니라 정규화된 내용 해시**로 판정. 표현만 다른 파일(CRLF/BOM/후행공백)과 실내용이 바뀐 파일을 분해해 리포트하고, 게이트는 후자만 본다.

| 항목 | 점수 | 근거 |
| --- | --- | --- |
| 반복성 | 2 | 같은 실패 계열 2회: FAIL-005(실행 판정을 StartTime/"launched" 프록시에 의존 → 오판·데드락), FAIL-010(트리 판정을 줄바꿈 표현에 의존 → 교착). 게다가 FAIL-010은 07-09부터 **매 5분 회차마다** 오판이 반복됐다. |
| 결정가능성 | 2 | 완전 기계 판정. 정규화 후 SHA 비교 → PASS/FAIL 이분. 사람 해석 여지 없음. |
| 장애주입 | 2 | 재현 자명: 파일을 CRLF로 되쓰면 raw git은 dirty, 하네스는 clean이어야 한다. BOM·후행공백도 동일 방식으로 주입 가능. 실제로 이번에 재현·검증했다. |
| 격리 | 2 | git 읽기 전용(status/show). 상태·인덱스 무변경. 부작용 0. |
| 관찰성 | 2 | "왜 dirty인지"를 분해 출력(표현차 vs 실내용). 기존 `git status`가 못 주던 정보 — 이게 이번 교착을 며칠간 안 보이게 만든 바로 그 정보다. |
| 유지가치 | 2 | 오케스트레이터 **발사 게이트가 이 판정에 직접 의존**. 틀리면 큐가 영구 정지(실측됨). ORCH-01·조율자 발사규칙①이 모두 소비자. |
| **총점** | **12** | → 즉시제작 |

- 산출: SONNET-QUEUE #6 `HARNESS-01` 지시서 + 참조 스캐폴드(`queue/GateCleanCli.reference.cs`).
- 소비자: 조율자 발사규칙①, ORCH-01 관측 스캐폴드, 검수자 VERIFY-PROTOCOL, 코덱스 QA.
- 주의(점수와 무관한 차단 없음): 보안·정책 미확정 사항 없음.

## HS-02 이후 (KNOWLEDGE-PROMOTION 첫 후보 표에서 이월 — 코덱스 판정 대기)

| 후보 | 유형 | 근거 데이터 | 상태 |
| --- | --- | --- | --- |
| `path-guard-check` | 하네스 | FAIL-006/007 | 판정 대기 |
| `call-integrity-check` | 하네스 | refactor-call-integrity(R당 반복) | 판정 대기 |
| `template-sync-check` | 하네스 | FAIL-008 | 판정 대기 |
| `e2e-usage` | 하네스 | FEAT-02 | **큐 진행 중**(SONNET-QUEUE #3) |
| `path-escape-qa` | 스킬 | 경로검증 재현 절차 | 판정 대기 |

## 일반화된 교훈 (이번 승격이 코드로 박는 것)

> **게이트 판정을 표현·프록시에 의존시키지 말 것. 정규화된 실체(내용 해시·산출물)로 판정할 것.**

FAIL-005는 "실행 중인가"를 StartTime으로, FAIL-010은 "깨끗한가"를 바이트로 물었다. 둘 다 프록시가 흔들리자 게이트가 잠겼다. `gate-clean`은 이 교훈 중 트리 판정 부분을 실행 가능한 검사로 고정한다. 실행 판정 부분은 ORCH-03(자식 프로세스 핸들 직접 소유)이 맡는다.


## HS-03 `claim-check` — 자기보고가 아니라 실체로 완료를 판정 (하네스)

- 상태: **즉시제작 제안 (11/12) — 코덱스 확정 대기**
- 근거 데이터: FIX-01 "완료 주장 vs 코드 미반영" 3회차(조율자 13:2x·13:36·14:09, HUMAN-INBOX 기록) / 조율자 15:35 "WORKSTATE.json이 FIX-02/verifying인데 실제 완료 상태와 불일치" / FAIL-2026-005("launched" 문자열로 실행 판정).

### 원인 분석 (증상 아니라 원인을 올린다)
- **증상**: WORKSTATE·verification 문서가 "완료/PASS"라는데 코드가 없다.
- **얕은 원인**: 실행자가 문서를 잘못 썼다. ← 여기서 멈추면 "실행자를 더 잘 훈련시켜라"가 되고, 이건 하네스화 불가능하다.
- **진짜 원인**: **완료 판정의 근거가 실행자의 자기보고다.** 실행자가 "완료"라고 쓰면 그게 진실로 취급된다. 파이프라인에 **주장과 실체를 대조하는 단계가 없다.** 조율자는 이걸 매번 손으로 `git grep`해서 잡아냈다 — **그 반복 수작업이 정확히 하네스화 대상이다.**
- **같은 뿌리**: FAIL-005는 실행 여부를 "launched" 문자열(자기보고)로 판정했다. FAIL-010은 트리 상태를 바이트(표현)로 판정했다. 전부 **프록시로 판정**이다.
- **실시간 증거**: 이번 FEAT-02 실행자도 자가점검표를 전부 PASS로 썼는데, 잔존 위반을 "Codex concurrent activity"로 **오귀인**했다(실제로는 검수자 세션이 만든 파일). 자기보고는 악의 없이도 틀린다.

| 항목 | 점수 | 근거 |
| --- | --- | --- |
| 반복성 | 2 | FIX-01 3회 + FIX-02 상태 불일치 1회 + FAIL-005. 매 DI마다 조율자가 수작업 대조 중. |
| 결정가능성 | 2 | 검수기준 항목 → 코드 grep·빌드 exit·커밋 존재로 기계 대조. PASS/FAIL 이분. |
| 장애주입 | 2 | verification에 허위 "완료" 주장을 넣고 코드는 안 넣으면 FAIL이 떠야 한다. 재현 자명. |
| 격리 | 2 | 읽기 전용(문서 파싱 + git grep + build). 상태 무변경. |
| 관찰성 | 2 | "무엇을 주장했고, 실체는 무엇인가"를 항목별로 대조 출력. |
| 유지가치 | 1 | 조율자 검수 프로토콜이 직접 의존. 다만 검수기준 파싱이 지시서 형식에 결합돼 유지비가 있다. |
| **총점** | **11** | → 즉시제작 |

- 산출: SONNET-QUEUE #8 `HARNESS-03`.
- 검사 내용: `claim-check <diId>` — verification 문서/WORKSTATE가 주장하는 검수기준 항목을 파싱 → ①주장된 심볼·파일이 실제 코드에 존재하는가(git grep) ②주장된 빌드/게이트 결과가 재현되는가 ③주장된 커밋이 로그에 있는가. 불일치 목록 출력, 있으면 exit 1.

## ~~HS-04 `gate-audit`~~ — **철회됨 (2026-07-11 16:5x)** ※승격 자체가 오판이었다

- 상태: **철회.** 승격 근거였던 "무인 결재 22건"이 **오판**이었다. 하네스 삭제·CliRouter 등록 해제 완료.
- **철회 사유**: `[loop]`는 자동 주체의 서명이 **아니다**. `GitDataCommitter.CommitHumanAction`이 붙이는 커밋 메시지 형식이며 **사람 승인에도 붙는다**. `Approve()` 호출자는 HTTP 엔드포인트 하나뿐이고 서버에 proposal 자동승인 경로는 없다. 상세: **FAIL-2026-012**.
- **교훈(HS-GATE 절차 보강)**: 점수표의 '결정가능성'은 *데이터가 있을 때* 기계 판정 가능한지를 묻는 것이다. **하네스를 승격하기 전에 "이 하네스가 볼 데이터가 실제로 존재하는가"를 먼저 확인해야 한다.** gate-audit이 볼 actor 데이터는 애초에 존재하지 않았다. 이 확인 단계를 `skills/common/hs-gate.md`에 추가했다.
- **대체 과제**: ACTOR-01(결재 액션 actor 기록). 그게 서면 gate-audit을 재심사한다.
- (철회 전 원 근거 — 이력 보존) `git log`에 `[loop] dev-pack 회차N: approve proposal-...` / `acknowledge-guardrail ...` 커밋 **12건 실측**(회차5·6·7·8·9·10, 두 계열). HUMAN-INBOX가 34f5116 1건을 관측하고 "출처 미확정"으로 남긴 뒤 **6건 이상 추가 재발**.

### 원인 분석 (증상 아니라 원인을 올린다)
- **증상**: 대시보드 loop이 proposal을 무인 승인하고 자동 커밋한다.
- **얕은 원인**: loop 코드에 approve 호출이 있다. ← 여기서 멈추면 "그 호출을 지워라"가 되고, 다음 자동화가 또 부른다.
- **진짜 원인은 두 겹**:
  1. **강제 부재** — "결재·반입·기준변경은 항상 사람"이라는 **북극성 고정점이 문서 규칙(honor system)으로만 존재한다.** 코드에 호출 주체를 검증하는 장치가 없어 어떤 자동 프로세스든 approve를 부를 수 있다.
  2. **감지 부재** — 위반이 일어나도 **아무도 모른다.** HUMAN-INBOX가 1회 관측하고 "출처 미확정"으로 방치하는 사이 6회 더 반복됐다. **감사가 없으니 재발을 막을 수 없었다.**
- **하네스가 맡을 것은 ②(감지)**다. ①(코드 강제)은 **기준 변경이라 사람 결재 사항** — 하네스가 대행하지 않는다.
- **같은 계열**: HS-02(hs-scan)의 원인과 동일하다. HS-02는 "규칙이 문서·재량이라 **실행되지 않았다**", 여기는 "규칙이 문서라 **위반됐다**". 둘 다 **문서 규칙 ≠ 강제**.
- **왜 최우선인가**: 이 프로젝트의 북극성은 "사람은 기준만 정한다. 결재·반입·기준변경·이양은 항상 사람"이다. 그 **단 하나의 불변식이 12회 깨지는 동안 시스템은 몰랐다.** 이건 기능 버그가 아니라 **북극성의 회귀 테스트 부재**다.

| 항목 | 점수 | 근거 |
| --- | --- | --- |
| 반복성 | 2 | 12회 실측(회차5~10). 관측 후에도 재발. |
| 결정가능성 | 2 | git log author·메시지 패턴(`[loop]`, approve/acknowledge/import) + 이벤트 로그 스캔 → 기계 검출. |
| 장애주입 | 2 | 가짜 `[loop] ... approve ...` 커밋을 만들면 검출돼야 한다. 자명. |
| 격리 | 2 | 읽기 전용(git log·이벤트 로그). 되돌리거나 결재하지 않는다. |
| 관찰성 | 2 | 위반 커밋·주체·시각·대상 proposal을 목록으로 출력. |
| 유지가치 | 2 | **프로젝트 고정점의 유일한 자동 방어선.** 이양·자율화가 진행될수록 가치가 커진다. |
| **총점** | **12** | → 즉시제작 |

- 산출: SONNET-QUEUE #9 `HARNESS-04`.
- 검사 내용: `gate-audit [--since <commit>]` — ①git log에서 approve/reject/approve-import/reject-import/acknowledge가 **사람이 아닌 주체**에 의해 수행된 흔적(`[loop]` 등 자동 커밋 서명) ②이벤트 로그의 결재 액션 중 사람 승인 근거가 없는 것 ③사람 배치 없는 push. 위반 있으면 목록 + exit 1.
- **주의**: 이 하네스는 위반을 **검출만** 한다. 되돌리기·결재·정책 변경은 사람.

## 기각 기록 (승격하지 않음 — 기각도 근거로 남긴다)

| 후보 | 판정 | 사유 |
| --- | --- | --- |
| `doc-integrity` | ~~보류(5/12)~~ → **승격 12/12 (아래 HS-05에서 뒤집음)** | KNOWN-ISSUES I-9. STATUS.md·SONNET-QUEUE.md가 잘린 채 발견됐으나 **단일 사건 1회**(2파일). KNOWLEDGE-PROMOTION의 "반복이 자산의 조건(2회+)" 미충족 → 새 하네스를 만들지 않는다. 대신 **기존 확장**: `gate-clean`/`e2e-usage`에 핵심 상태파일(WORKSTATE.json·큐 문서) 파싱 가능성·끝 마커 검사를 얹는다. 재발하면 그때 승격. |
| `hs-scan` 메타태그 노이즈 | **기각** | 검수자 참조본의 내부 결함(design_learning 8회가 노이즈 후보를 생성). 발견 즉시 제외 필터로 수정. 1회성 구현 버그이지 시스템 실패 패턴이 아니다. |

## 이번 심사가 드러낸 상위 원칙

앞선 승격들과 이번 둘을 묶으면 하나의 규칙이 남는다:

> **문서·보고·표현은 전부 프록시다. 판정은 실체로 하고, 규칙은 코드로 강제하라.**

| 하네스 | 프록시(틀린 판정 근거) | 실체(옳은 판정 근거) |
| --- | --- | --- |
| `gate-clean` (HS-01) | 바이트/줄바꿈 | 정규화된 내용 해시 |
| `hs-scan` (HS-02) | "해당하면 판정하라"(재량) | exit code(의무) |
| `claim-check` (HS-03) | 실행자의 "완료" 자기보고 | 코드·빌드·커밋의 실재 |
| `gate-audit` (HS-04) | "결재는 사람"이라는 문서 규칙 | git log·이벤트의 실제 주체 |

네 건 모두 같은 병이다. 시스템이 자기 자신에 대해 **말한 것**을 믿고, **한 것**을 확인하지 않았다.

## HS-05 `doc-integrity` — 조용히 잘린 문서 검출 (하네스) ※기각을 뒤집은 건

- 상태: **즉시제작 → 구현 완료** (기각 → 승격 번복)
- **번복 사유**: 같은 세션에서 `doc-integrity`를 "1회성"이라며 **보류(5/12)로 기각했는데, 그 직후 3회차가 실시간 재현됐다.** 검수자가 `server/Cli/CliRouter.cs`를 쓰다가 `retur`에서 잘렸고, 빌드가 CS1002로 잡았다. 반복성 점수가 1 → 2로 바뀌면서 총점이 5 → 12가 됐다.
- **이 번복 자체가 기록 가치가 있다**: 기각도 점수와 근거로 남겨뒀기 때문에, 새 증거가 나왔을 때 **무엇을 다시 열어야 하는지 즉시 알 수 있었다.** 기각을 기록하지 않았다면 이 3회차는 그냥 또 한 번의 사고로 지나갔을 것이다.

### 원인 분석
- **증상**: STATUS.md(4번 항목 중간), SONNET-QUEUE.md("QUOTA_"), CliRouter.cs("retur")가 각각 끝이 잘린 채 발견.
- **진짜 원인**: **쓰기가 원자적이지 않다.** 쓰기 중 프로세스가 끊기거나 버퍼가 플러시되지 않으면 파일이 **조용히** 잘린다. 오류가 나지 않는다는 게 핵심이다.
- **왜 위험한가**: `.cs`는 **빌드가 잡아준다**(CliRouter가 그렇게 걸렸다). 그러나 `.md`·`.json`은 잡아주는 것이 **아무것도 없다.** `WORKSTATE.json`(단일 상태 원본)이 같은 식으로 잘리면 전체 상태가 조용히 소실된다. **빌드가 없는 파일에는 빌드를 대신할 검사가 필요하다** — 그게 이 하네스다.

| 항목 | 점수 | 근거 |
| --- | --- | --- |
| 반복성 | 2 | 3회(STATUS.md, SONNET-QUEUE.md, CliRouter.cs — 마지막은 실시간 재현) |
| 결정가능성 | 2 | JSON 파싱 / 코드펜스 균형 / 마지막 줄 토큰 완결성 — 기계 판정 |
| 장애주입 | 2 | 파일 끝을 잘라내면 검출. 실측 확인 |
| 격리 | 2 | 읽기 전용 |
| 관찰성 | 2 | 어느 파일이 어디서 끊겼는지 마지막 30자 출력 |
| 유지가치 | 2 | WORKSTATE.json 소실 = 상태 원본 소실. 빌드가 없는 파일의 유일한 방어선 |
| **총점** | **12** | → 즉시제작 (구현 완료) |

- **오탐 교훈**: 첫 구현이 "끝 개행 없음 = 잘림"으로 판정해 HUMAN-INBOX를 오탐했다. 정상 파일도 끝 개행이 없을 수 있다. 진짜 표식은 **마지막 줄이 토큰 중간에서 끊긴 것**("retur", "QUOTA_")이다. 규칙을 조여 오탐 0 확인.

## 실행 결과 정정 — gate-audit이 사람 집계를 넘어섰다

`gate-audit` 구현 후 실행하니 고정점 위반이 **12건이 아니라 22건**이었다(git log와 22/22 일치, 오탐 0).

- 내가 손으로 센 12건은 `dashboard/data/dev-pack/patch-proposal.json`을 건드린 커밋만 본 것이었다.
- 하네스는 전체 로그를 봤고, **ruined-lab 프로젝트에서 4건**과 **dev-pack 회차1~4**를 추가로 찾았다. 나는 프로젝트가 둘이라는 것조차 감사 범위에 넣지 않았다.
- **이것이 하네스의 존재 이유다**: 사람의 집계는 자기가 본 범위까지만 정확하다. 기계는 범위를 잊지 않는다. 승격 근거였던 "감지 부재"가 승격 직후 스스로 입증됐다.

## HS-06 `scope-check` — 산출물이 지시서 허용 범위를 벗어났는지 (하네스) ★코덱스 제작

- 상태: **즉시제작 (12/12)** — 데이터 존재 관문 **통과**. `server/Harness/` 소유자인 **코덱스**가 만든다(CODEX-QUEUE H-0).

### 데이터 존재 확인 (점수화 전 필수 관문 — gate-audit의 교훈)
| 필요 데이터 | 존재? |
| --- | --- |
| 실제 변경 파일 목록 | ✅ `git status/diff` — **실체**(프록시 아님) |
| 지시서의 허용 파일 목록 | ❌ **없었다** → 검수자가 선행 조치로 **`## 허용 파일 (allowlist)` 절을 지시서 형식에 신설**(`docs/directives/_header.md`)하고 큐의 지시서 4건에 백필. **이제 존재한다.** |

> **이것이 gate-audit과 갈린 지점이다.** gate-audit은 actor 데이터가 없는데 커밋 접두사라는 프록시로 대신하려다 오보를 냈다(FAIL-2026-012). scope-check도 처음엔 데이터가 없었지만, **선행조건이 작아서(지시서 형식) 먼저 깔았다.** 없으면 만들거나, 못 만들면 하네스를 포기한다 — 프록시로 대신하지 않는다.

### 원인 분석
- **증상**: 실행자가 지시서와 무관한 파일을 고친다. HOOK-01 발사했더니 `Tier2Approver.cs`에 FEAT-01이 109줄 들어와 있었고, 다른 세션도 `Tier2ApproverTestCli.cs`를 건드렸다.
- **얕은 원인**: 실행자가 지시를 안 지킨다. ← 여기서 멈추면 "더 강하게 말하라"가 되고, 실제로 **격리 프롬프트도 화이트리스트 프롬프트도 실패했다**(I-1, I-10).
- **진짜 원인**: **범위 위반을 기계가 검출하지 못한다.** 지시서의 경계가 산문이라 파싱 불가였고, 산출물과 대조하는 단계가 파이프라인에 없다. 사람이 눈으로 `git status`를 봐야만 잡힌다 — 이번에 검수자가 우연히 봐서 잡았다. **안 봤으면 안전 보류 항목(FEAT-01)이 조용히 반입될 뻔했다.**
- **교훈**: 지시 준수는 **말로 강제할 수 없다. 사후 검출로 강제한다.**

| 항목 | 점수 | 근거 |
| --- | --- | --- |
| 반복성 | 2 | I-1(지시서 이탈) 다회 + 오늘만 2건(Tier2Approver.cs, Tier2ApproverTestCli.cs) |
| 결정가능성 | 2 | allowlist(glob) vs `git status` 파일 목록 — 집합 연산. PASS/FAIL 이분 |
| 장애주입 | 2 | 허용 밖 파일을 하나 고치면 FAIL이 떠야 한다. 자명 |
| 격리 | 2 | 읽기 전용(git status + 지시서 파싱) |
| 관찰성 | 2 | 어느 파일이 어느 지시서 범위를 벗어났는지 목록 출력 |
| 유지가치 | 2 | **모든 발사의 사후 게이트.** 이게 없으면 실행자가 무엇을 하든 통제 불가 |
| **총점** | **12** | → 즉시제작 |

- 검사 내용: `scope-check <diId>` — 지시서의 `## 허용 파일 (allowlist)` glob 목록을 파싱 → `git status --porcelain`의 변경 파일과 대조 → 허용 밖 파일 목록 출력, 있으면 exit 1.
- 소비자: **조율자**가 커밋 전 필수 실행(범위 밖이면 커밋 거부·되돌림). 검수자가 발사 후 확인.
- 주의: **되돌리기는 하지 않는다.** 검출·보고만. 되돌림은 조율자/사람 판단.

## HS-07 `build-verify` — 빌드 판정을 exit code로 (하네스) ★코덱스 제작

- 상태: **즉시제작 (11/12)** — 데이터 존재 관문 통과(**exit code는 실체다**).
- 근거: 검수자가 `dotnet build` 출력에서 `error CS` 정규식 매치 수를 세어 **"build 0/0 성공"을 여러 커밋·검증 문서에 기록했다.** 실제로는 **exit code = 1(실패)**였다 — 실행 중 서버(PID 14252)가 Release exe를 잠가 apphost 복사가 실패했고, 그건 `error CS`가 아니라 MSBuild 에러라 정규식에 안 걸렸다.
- **코덱스가 5주기 연속(SESSION codex-027~031) "build FAIL"이라 보고했는데 검수자가 안 읽었다.**

### 원인 분석
- **증상**: 검증 문서의 "build 0/0"이 거짓이다.
- **얕은 원인**: 검수자가 부주의했다. ← 하네스화 불가.
- **진짜 원인**: **빌드 성공 판정이 출력 텍스트 파싱(프록시)에 의존한다.** 프로세스는 exit code로 말하는데 아무도 안 들었다. 게다가 **서버 실행 중이면 빌드가 락으로 실패하는 것이 정상 동작**(I-3)이라, 이 둘을 구분하지 못하면 "코드가 깨졌다"는 오보도, "빌드 성공"이라는 오보도 둘 다 난다.
- 이건 `claim-check`(자기보고 vs 실체)의 **빌드 특화판**이다.

| 항목 | 점수 | 근거 |
| --- | --- | --- |
| 반복성 | 2 | 검수자의 모든 하네스 커밋(6건+)에 거짓 "build 0/0"이 실렸다. 코덱스가 5주기 연속 지적. |
| 결정가능성 | 2 | exit code 이분 판정 |
| 장애주입 | 2 | 서버를 띄우고 빌드하면 락 실패가 재현된다 |
| 격리 | 2 | 빌드는 임시 출력경로(`-o`)로 하면 부작용 0 |
| 관찰성 | 2 | 실패 원인을 **"코드 오류" / "exe 락(I-3)"** 으로 분해 출력 — 이 구분이 핵심 |
| 유지가치 | 1 | 모든 검수의 전제. 다만 claim-check에 흡수될 수도 있다 |
| **총점** | **11** | → 즉시제작 |

- 검사 내용: `build-verify` — ①`dotnet build -c Release -o <임시경로>`로 락을 우회해 **코드 자체의 exit code**를 얻는다 ②락이 걸린 상태였는지(실행 중 서버 PID) 별도 보고 ③출력: `{exitCode, verdict:"PASS"|"CODE-ERROR"|"LOCKED", lockingPid, warnings, errors}`.
- **핵심**: "락 때문에 실패"와 "코드가 깨져서 실패"를 **절대 섞지 않는다.** 검수자는 이 둘을 구분 못해 정규식으로 때웠다.

## 미심사 반복 계열 (hs-scan S1이 다음 회차에 제기할 것)

| failureClass | 반복 | 케이스 | 비고 |
| --- | --- | --- | --- |
| `path_escape` | 2회 | FAIL-2026-006, FAIL-2026-007 | **미심사** — KNOWLEDGE-PROMOTION 첫 후보표의 `path-guard-check`와 대응. 다음 HS-GATE에서 점수화 대상. |

> `judgedClasses`에 없는 2회+ 계열은 hs-scan이 자동으로 후보로 올린다. 이 표는 그 예고이며, 코덱스가 심사하면 위 HS-XX 절로 승격 기록된다.

## 2026-07-11 19:15 codex hs-scan follow-up

- actor: codex
- command: `dotnet run --project server -c Release -- hs-scan`
- exitCode: 1
- observed: `failureCaseCount=14`; candidates=`config_side_effect(2)`, `observability(2)`, `path_escape(2)`, `executor-orchestration(6)`.
- data-existence gate:
  - `observability`: PASS. Data exists in `FAIL-2026-005`, `FAIL-2026-013`, launch logs under `outputs/sonnet-*.out.log`, and queue/directive text requiring `ACK-<taskId>`.
  - `path_escape`: PASS. Data exists in `FAIL-2026-006`, `FAIL-2026-007`, and prior reproduction docs.
  - `executor-orchestration`: PASS. Data exists across `FAIL-2026-004/005/008/010/012/013`.
  - `config_side_effect`: PASS but already partly covered by `gate-clean`; keep as existing-extension candidate.
- promoted this cycle: H-00 `launch-check` for the `observability`/`executor-orchestration` intersection. Score: 12/12. Reason: repeated, mechanically decidable by exit code, role-injection is a log with/without `ACK-<taskId>`, read-only, observable JSON fields, directly prevents accepting untied executor output.
- next candidates remain: H-0 `scope-check`, H-01 `build-verify`, H-1 `path-guard-check`.

## 2026-07-11 19:30 codex hs-scan follow-up

- actor: codex
- command: `dotnet run --project server -c Release -- hs-scan`
- exitCode: 1
- observed: `failureCaseCount=14`; candidate=`executor-orchestration(6)` only, because prior judged classes are now recorded.
- data-existence gate: PASS. `scope-check` uses directive `## 허용 파일 (allowlist)` sections and `git status --porcelain`; both exist in the repository now. Evidence: `docs/directives/_header.md`, `docs/handoff/queue/directive-*.md`, `docs/handoff/HS-CANDIDATES.md` HS-06.
- promoted this cycle: H-0 `scope-check`. Score: 12/12. Reason: repeated executor-orchestration failure, fully mechanical set comparison, synthetic role injection is an out-of-allowlist changed file, read-only, outputs exact offending paths, and directly blocks the known directive-scope drift class.
- next candidates remain: H-01 `build-verify`, H-1 `path-guard-check`.

## 2026-07-11 19:45 codex hs-scan follow-up

- actor: codex
- command: `dotnet run --project server -c Release -- hs-scan`
- exitCode: 1
- observed: `failureCaseCount=14`; candidate=`executor-orchestration(6)`.
- data-existence gate: PASS. `build-verify` can observe actual `dotnet build` exit codes. This cycle also reproduced the concrete data: direct `dotnet build server -c Release` exited 1 because `server/obj/Release/net8.0/rjsmrazor.dswa.cache.json` was locked, while a copied temp-source Release build exited 0.
- promoted this cycle: H-01 `build-verify`. Score: 11/12. Reason: repeated false build judgment, fully mechanical exit-code decision, read-only/temp-copy isolation, diagnostic output separates locked/stale build artifacts from code errors, and directly prevents text-regex build claims.
- next candidates remain: H-1 `path-guard-check`, H-2 `call-integrity-check`.

## 2026-07-11 20:15 codex hs-scan follow-up / H-6 claim-check precision fix

- actor: codex
- command: `dotnet run --project server -c Release -- hs-scan`
- exitCode: 1
- observed: `failureCaseCount=14`; candidate=`executor-orchestration(6)`.
- data-existence gate: PASS. The false-positive input exists as concrete text in `docs/verification/actor01-actor-provenance.md`: the build command mentions `server/LocalFirstWorkflowDashboard.Server.csproj`. Before H-6, `claim-check ACTOR-01` parsed that as a file claim for `server/LocalFirstWorkflowDashboard.Server.cs`, producing `claimCount=13`, `mismatchCount=1`, exit 1.
- fixed this cycle: H-6 `claim-check` path-boundary precision. Score: existing HS-03 maintenance, not a new promotion. The harness had real data but the extraction regex used a proxy boundary; adding an extension boundary prevents `.csproj` and `.csv` from being treated as `.cs` files.
- post-fix proof: `claim-check ACTOR-01` exit 0, `claimCount=12`, `mismatchCount=0`, `verdict=MATCH`; regex probe matched `server/Foo.cs`, `server/Bar.cs`, `server/Baz.cs` and ignored `server/Foo.csproj` / `server/Foo.csv`.
- next candidates remain: H-1 `path-guard-check`, H-7 quota/root-cause skill hardening, H-2 `call-integrity-check`.

## 2026-07-11 20:30 codex hs-scan follow-up / H-7 quota diagnosis hardening

- actor: codex
- command: `dotnet run --project server -c Release -- hs-scan`
- exitCode: 1
- observed: `failureCaseCount=14`; candidate=`executor-orchestration(6)`.
- data-existence gate: PASS. Quota diagnosis can use concrete executor logs (`hit your limit`, `rate limit`, `QUOTA_SIGNAL`) and process/exit-code observations. The immediate queue evidence records the real quota phrase: `You've hit your limit · resets 5:40pm`.
- fixed this cycle: H-7 `root-cause-diagnosis` skill hardening plus `launch-check` quota signal split. Score: existing executor-orchestration extension, not a new promotion. The change separates usage-limit exhaustion from prompt delivery/scope failures so "actor did not work" is not diagnosed from silence alone.
- post-fix proof: `launch-check H7 <pass.log>` exit 0 / `verdict=PASS`; `launch-check H7 <quota.log>` exit 1 / `verdict=QUOTA`; `launch-check H7 <quota-ack.log>` exit 1 / `verdict=QUOTA`.
- next candidates remain: H-1 `path-guard-check`, H-2 `call-integrity-check`, H-3 `template-sync-check`.

## 2026-07-11 20:45 codex hs-scan follow-up / H-1 path-guard-check

- actor: codex
- command: `dotnet run --project server -c Release -- hs-scan`
- exitCode: 1
- observed: `failureCaseCount=14`; candidate=`executor-orchestration(6)`.
- data-existence gate: PASS. `path_escape` has concrete cases `FAIL-2026-006` and `FAIL-2026-007`, plus FIX-01 verification data for separator-bounded behavior. The check uses concrete full paths and exit code, not a proxy.
- fixed this cycle: H-1 `path-guard-check`. Score: existing 11/12 `path_escape` immediate-production candidate now implemented. The harness checks root containment as equality or root plus directory separator and rejects sibling-prefix paths such as `data-escape` and `outbox-escape`.
- post-fix proof: default regression mode exit 0 with 6/6 cases matching expected results; input child path exit 0; input sibling-prefix path exit 1.
- next candidates remain: H-2 `call-integrity-check`, H-3 `template-sync-check`, H-4 `path-escape-qa` skill.

## 2026-07-11 21:00 codex hs-scan follow-up / H-2 call-integrity-check

- actor: codex
- command: `dotnet run --project server -c Release -- hs-scan`
- exitCode: 1
- observed: `failureCaseCount=14`; candidate=`executor-orchestration(6)`.
- data-existence gate: PASS. DI-R-01~04 verification docs and current C# source contain concrete moved symbols and call sites: `CliRouter.TryRun`, `InboxBuilder.BuildInboxItems`, `InboxBuilder.AddProjectInboxItems`, `CycleSummaryBuilder.BuildCycleSummary`, `MeasurementService.RunMeasureCore`.
- fixed this cycle: H-2 `call-integrity-check`. Score: existing refactor-call-integrity candidate now implemented. The harness checks each moved method has exactly one definition in the expected file, enough qualified call sites, and no stale unqualified call in `server/Program.cs`.
- post-fix proof: default rule set exit 0 with 5/5 rules PASS; bad definition-file injection for `MeasurementService.RunMeasureCore` exit 1.
- next candidates remain: H-3 `template-sync-check`, H-4 `path-escape-qa` skill, H-5 inherited harness review.

## 2026-07-11 21:15 codex hs-scan follow-up / H-3 template-sync-check

- actor: codex
- command: `dotnet run --project server -c Release -- hs-scan`
- exitCode: 1
- observed: `failureCaseCount=14`; candidate=`executor-orchestration(6)`.
- data-existence gate: PASS. FAIL-2026-008 and `docs/verification/fail-2026-008-template-sync.md` identify concrete dispatch templates, and `DispatchExecutorCli` can apply them in a temp copy.
- fixed this cycle: H-3 `template-sync-check`. Score: existing template-sync candidate now implemented. The harness copies the needed workspace subset to temp, runs `dispatch-executor claude-code "Program.cs Orchestrator.cs ProposalFlow.cs"`, then builds the temp server with Release and uses exit code as the source of truth.
- post-fix proof: default mode exit 0 with `dispatchExitCode=0`, `buildExitCode=0`; missing-template injection exit 1 with `dispatchExitCode=1`.
- next candidates remain: H-4 `path-escape-qa` skill, H-5 inherited harness review.

## 2026-07-11 21:30 codex hs-scan follow-up / H-4 path-escape-qa skill

- actor: codex
- command: `dotnet run --project server -c Release -- hs-scan`
- exitCode: 1
- observed: `failureCaseCount=14`; candidate=`executor-orchestration(6)`.
- data-existence gate: PASS. `FAIL-2026-006`, `FAIL-2026-007`, and `docs/qa/path-escape-repro-2026-07-10.md` contain concrete sibling-prefix and encoded-backslash evidence.
- fixed this cycle: H-4 `path-escape-qa` skill. Score: existing `path_escape` skill candidate now implemented as `skills/domains/dev/path-escape-qa.md`.
- post-fix proof: skill file created with trigger metadata, static checks, required cases, `path-guard-check` commands, dynamic PoC safety rules, and reporting format.
- next candidates remain: H-5 inherited harness review, 검수 위임 시범, 신규 sonnet 커밋 QA.

## 2026-07-11 21:45 codex hs-scan follow-up / H-5 inherited harness review

- actor: codex
- command: `dotnet run --project server -c Release -- hs-scan`
- exitCode: 1
- observed: `failureCaseCount=14`; candidate=`executor-orchestration(6)`.
- data-existence gate: PASS. The inherited harnesses consume concrete data: git normalized content (`gate-clean`), failure wiki index plus HS-CANDIDATES metadata (`hs-scan`), verification docs plus git/code reality (`claim-check`), and parsed JSON/markdown completeness (`doc-integrity`).
- completed this cycle: H-5 inherited harness review. No code change. Smoke results: `gate-clean server/Harness` exit 0, `hs-scan` exit 1 by design with current S4 candidate, `claim-check ACTOR-01` exit 0, `doc-integrity` exit 0.
- residual risk: `hs-scan` continues to trigger on broad `executor-orchestration`; future work should either split that component into narrower classes or add a judged component metadata mechanism.
- next candidates: 검수 위임 시범, 신규 sonnet 커밋 QA, broader E2E usage QA.
