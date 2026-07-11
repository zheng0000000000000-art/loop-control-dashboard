# CODEX-QUEUE — 코덱스 작업 큐

> 오케스트레이터/검수자가 채우고, 코덱스가 위에서부터 픽업한다(CODEX-AUTO-15min 루틴 2단계).
> 코덱스는 완료 시 상태를 갱신하지 말고(커밋 안 함) SESSION 문서에 "픽업·완료"를 남긴다 — 큐 상태 갱신은 조율자/검수자가 한다.

| 순번 | 작업 | 지시서/근거 | 영역 | 상태 |
| --- | --- | --- | --- | --- |
| 1 | S-01/S-02 경로 escape 재현 (의심→확정/기각) | directive-CODEX-3-reproduce-path-escape | docs/qa + docs/wiki | 진행 |
| 2 | 리팩토링 호출부 정합성 헌트 — CliRouter/InboxBuilder/CycleSummaryBuilder/MeasurementService의 모든 호출처가 새 위치를 가리키는지, 누락·시그니처 불일치 없는지 | CODEX-AUTO-15min §3 | docs/qa | 대기 |
| 3 | 검수 위임 시범 — 다음 sonnet DI 커밋 1건을 VERIFY-PROTOCOL로 코덱스가 1차 검수 → docs/qa/review-<커밋>.md | VERIFY-PROTOCOL-universal | docs/qa | 대기 |
| 4 | R-04(MeasurementService) 완료 후 maxFunctionLength 해소 검증 — ApplyMeasurementResult 분할이 동작 보존했는지 verify-behavior 교차확인 | — | docs/qa | R-04 완료 대기 |

## 하네스·스킬 제작 (2026-07-11 신설 — 코덱스 소유)

> **왜 코덱스인가**: 검수자(Claude)가 하네스를 직접 만들다 같은 실패계열을 3회 반복했다(FAIL-2026-010, FAIL-2026-012 — 프록시로 판정). **만든 사람이 검증하면 같은 착각을 코드에 새긴다.** 코덱스는 FAIL 위키를 가진 주체이므로, **자기가 등록한 실패를 자기가 하네스로 굳힌다.** 검사는 조율자가 한다.
>
> **쓰기 영역**: `server/Harness/` + `skills/` (배타적). **`server/Cli/CliRouter.cs`는 건드리지 않는다** — HOOK-01이 레지스트리를 한 번만 훅한다.
>
> **제작 전 필수 관문** (`skills/common/hs-gate.md` 2항): **"이 하네스가 볼 데이터가 실제로 존재하는가?"** 없으면 만들지 말고 "그 데이터를 만드는 선행 과제"를 올린다. `gate-audit`이 이걸 안 물어서 철회됐다.
>
> **작업 보고 의무**: ①주체(actor) ②사용한 하네스와 결과 ③참조 스킬 — 없으면 조율자가 반려한다. 템플릿 `docs/verification/_template.md`.
>
> **필독 스킬 (2026-07-11 신설, 하네스 만들기 전에 반드시)**:
> - `skills/common/root-cause-diagnosis.md` — **프록시로 원인을 단정하지 마라.** 검수자가 하루에 세 번 틀렸다. 하네스는 원인 위에 세우는 것이라, 원인이 틀리면 하네스가 오보 증폭기가 된다(gate-audit 철회 사례).
> - `skills/common/hs-gate.md` 2항 — **"이 하네스가 볼 데이터가 실제로 존재하는가?"** 없으면 만들지 말고 선행 과제를 올려라.
> - `skills/common/executor-launch.md` — 발사·검증 규약.
>
> **지시 주체**: 당분간 **검수자(Claude 세션)가 이 큐를 직접 채운다.** 조율자는 전달하지 않는다 — 너(코덱스)가 15분 루틴 §2에서 스스로 픽업한다.

| 순번 | 작업 | 근거 | 영역 | 상태 |
| --- | --- | --- | --- | --- |
| **H-6** | **`claim-check` 오탐 수정 ★★지금 최우선 — 큐가 이것 때문에 잠겨 있다** — 주장 추출 정규식 `server/[A-Za-z0-9_/\.]+\.cs` 에 **종단 경계가 없어** `server/Foo.csproj` 를 `server/Foo.cs` 존재 주장으로 오인한다. `(?![A-Za-z0-9])` 같은 경계를 붙이고, `.csproj`·`.csv` 등 회귀 테스트를 넣어라. | **실측(2026-07-11 19:5x)**: ACTOR-01 검증문서의 빌드 명령줄(`...Server.csproj`)이 파일 존재 주장으로 오인돼 claim-check MISMATCH(13건 중 1건) → 조율자가 정상 산출물 커밋 보류 → gate-clean FAIL 지속 → 다음 발사 차단. **검수자가 만든 하네스가 오보 증폭기가 된 4번째 사례**(FAIL-2026-012 계열). 실체는 12/13 일치, actor 기록은 코드에 실재. | `server/Harness/` | **최우선** |
| **H-00** | **`launch-check` 하네스 ★최우선** — 발사 로그에서 **ACK-\<taskId\> 에코백**을 확인. 없으면 exit 1(=지시 미도착, 산출물 폐기 대상). 입력: taskId + 로그 경로. | **FAIL-2026-013**. 프롬프트가 인자 경계에서 잘려 실행자가 지시서를 **받은 적이 없었다.** 지시 없이 저장소만 읽고 **안전 보류 항목(FEAT-01)까지 구현**했다. `scope-check`가 사후 검출이라면 이건 **사전 검출**이다 | `server/Harness/` | 대기(HOOK-01 후) |
| **H-0** | **`scope-check` 하네스 ★최우선** — 지시서의 `## 허용 파일 (allowlist)` glob을 파싱해 `git status` 변경파일과 대조. 허용 밖이면 exit 1 + 목록 출력. **되돌리지 말고 검출·보고만.** | HS-CANDIDATES **HS-06 (12/12)**. I-1 지시서 이탈 반복 + 오늘 2건(Tier2Approver.cs, Tier2ApproverTestCli.cs). 격리·화이트리스트 **프롬프트가 둘 다 실패**했다 — 말이 아니라 사후 검출로 강제해야 한다 | `server/Harness/` | 대기(HOOK-01 후) |
| **H-01** | **`build-verify` 하네스** — 빌드 판정을 **exit code**로. `-o <임시경로>`로 exe 락을 우회해 **코드 자체의 성패**를 얻고, "락 때문에 실패(I-3)"와 "코드 오류"를 **분해해서** 출력. | **HS-07 (11/12)**. 검수자가 `error CS` 정규식으로 "build 0/0"을 여러 커밋에 거짓 기록. **네가 5주기 연속 지적했는데 검수자가 안 읽었다.** 네 관측이 옳았다 | `server/Harness/` | 대기(HOOK-01 후) |
| H-1 | **`path-guard-check` 하네스** — 경로 경계가 separator-bounded인지(sibling-prefix escape 회귀) | FAIL-2026-006/007. `hs-scan`이 S1로 자동 검출한 후보(path_escape 2회). 코덱스 자체 HS-GATE도 11점 즉시제작 판정 | `server/Harness/` | 대기(HOOK-01 후) |
| H-2 | **`call-integrity-check` 하네스** — 이동한 함수의 호출부 누락·시그니처 불일치 | 리팩토링 R당 반복된 수작업 QA | `server/Harness/` | 대기 |
| H-3 | **`template-sync-check` 하네스** — dispatch-templates가 현행 코드와 동기화됐는지 | FAIL-2026-008 | `server/Harness/` | 대기 |
| H-4 | **`path-escape-qa` 스킬** — 경로 escape 재현·판정 체크리스트 | 반복 재현 절차 자산화 | `skills/domains/dev/` | 대기 |
| H-5 | 기존 하네스 4종 **인수** — gate-clean·hs-scan·claim-check·doc-integrity를 코덱스가 소유·유지보수. 검수자가 만든 것이라 **오탐·프록시 판정이 더 없는지 코덱스가 재검토**한다 | FAIL-2026-012(검수자 하네스 1종이 이미 철회됨) | `server/Harness/` | 대기(HOOK-01 후) |

## 픽업 규칙

> **전달 방식**: 조율자가 코덱스에게 주지 않는다. **코덱스가 15분 루틴 §2에서 이 파일을 스스로 읽고 위에서부터 픽업한다.** 조율자는 산출물을 검수·커밋할 뿐이다.

- 위에서부터. `진행`이 있으면 그것 우선 마무리.
- 새 sonnet 커밋이 있고 큐에 해당 QA가 없으면 §3(최근 커밋 QA)을 자체 수행하고 SESSION에 기록.
- 코드 수정 필요한 발견은 FAIL 위키에 등록만(수정은 sonnet). 큐에 "sonnet 수정 필요: FAIL-XXX" 후보로 남긴다.

## 코덱스 몫으로 남은 measure 위반 (2026-07-11, 검수자)

- `server/Harness/ScopeCheckCli.cs:156` — 함수 기능 주석 없음(`functionsWithoutComment` 위반 1건). CLAUDE.md 관례: 함수 위 1줄 한국어 기능 주석. **네 영역이라 sonnet이 못 고친다 — H-0 마무리 시 같이 처리하라.**
- 나머지 measure 위반(smallTouchTargets·maxFunctionLength·skillDomainViolations)은 sonnet FIX-04가 맡는다. **dashboard/·docs/verification/tuning-advanced.md 는 건드리지 마라.**


## H-7 — 스킬 보강: "안 돌면 한도부터 배제하라" (2026-07-11, 사람 지적)

- **문제**: 주체가 일을 안 하면 우리는 곧장 "버그·지시서 이탈·설정 오류"를 의심한다. 그런데 오늘 sonnet이 멈춘 진짜 원인은 **사용량 한도**였다(`You've hit your limit · resets 5:40pm`). 검수자는 그걸 "지시서 이탈"로 오귀인했다(FAIL-2026-012 계열, 네 번째).
- **사람 지적(2026-07-11 20:0x)**: **코덱스도 자기 한도에 걸릴 수 있다.** 그때 "코덱스가 H-6을 안 잡는다 → 큐 인식 버그"로 오진할 위험이 있다.
- **요청**: `skills/common/root-cause-diagnosis.md`에 **감별 0순위** 절을 넣어라 — *"주체가 안 움직이면, 버그를 의심하기 전에 **한도(quota)·인증·프로세스 생존**부터 배제하라. 그 셋은 실체로 즉시 확인된다(로그의 limit 문구, exit code, 프로세스 목록). 배제하지 않고 원인을 지목하면 그것이 프록시다."*
- 가능하면 기계 검출도: 실행 로그에서 한도 문구(`hit your limit`·`rate limit`·`QUOTA_SIGNAL`)를 찾아 **"작업 실패"와 "한도 소진"을 분해**하는 검사를 `launch-check`에 얹어라(별도 하네스 불필요).
- 영역: `skills/` + `server/Harness/`. 우선순위: H-6 다음.

## ★ Phase 0 (P0) — 계획서 흡수 (2026-07-11, ADR-001 사람 승인)

> 정본: `docs/plan/AI-RUNTIME-REFACTOR-MICRO-DIRECTIVES-v9.md` · 정렬: `docs/plan/ALIGNMENT-v9.md` · 결정: `docs/handoff/decisions/`
> **예산: 이 Phase의 신규 하네스는 2개뿐이다**(P0-03, P0-05). 나머지는 기존 확장. 하루에 6개 만든 오늘 같은 과잉 금지.

| 순번 | 작업 | 근거 | 영역 | 상태 |
| --- | --- | --- | --- | --- |
| **P0-03** | **`handoff-integrity` 하네스 (신규 1/2) ★최우선** — `WORKSTATE.json`이 실체와 일치하는지 검사: ①schema ②`changedFiles`의 실제 존재·hash ③`status`와 큐 표의 일치 ④blocker가 있는데 다음 항목이 진행 중인지 ⑤완료 주장 산출물의 실재. exit 0/1. | **가장 큰 구멍**: WORKSTATE가 `phaseId=FEAT-01, status=verifying`으로 며칠간 거짓말했는데 아무도 못 잡았다. 계획서 DI-00-05가 요구하는 항목. | `server/Harness/` | **대기** |
| **P0-05** | **`context-pack-integrity` 하네스 (신규 2/2)** — 지시서(=우리의 Context Pack)의 `requiredInputs` 경로가 **실재하고 hash가 일치하는지** 검사. 없으면 exit 1. | ORCH-01 지시서가 **삭제된 참조 스캐폴드**를 가리키고 있었다(커밋 797e7bc가 지움). 검수자가 손으로 발견했다 — 기계가 잡아야 한다. | `server/Harness/` | 대기(P0-03 후) |
| **P0-06** | **`scope-check` 확장(기존 확장, 신규 아님)** — 발사 시 지시서 allowlist를 **claim으로 등록**하고, 실행 중 다른 주체가 같은 파일을 만지면 검출. 사후 검출 → **사전 경고**. | 계획서 §0-A.4 `FILE-CLAIMS`. 고아 코드 109줄 사건(주체 규명 불가)의 재발 방지. | `server/Harness/` | 대기(P0-05 후) |
| **H-00 수정** | **`launch-check`를 파일 기반 ACK로 바꿔라 (ADR-004)** | **실측: ORCH-01·ACTOR-01·FIX-04·FIX-05·FIX-06 5건 모두 stdout에 ACK를 출력하지 않았다.** `claude -p`는 최종 메시지만 내보내므로 "첫 줄 ACK"는 구조적으로 불가능하다. 그런데 5건 다 지시서를 정확히 지켰다 — **지시는 도착했다.** 현행 stdout 기반 launch-check를 그대로 쓰면 **정상 산출물을 전부 오탐으로 죽인다.** 대신 실행자가 시작 시 `outputs/ack-<taskId>.txt`를 쓰게 하고 그 **파일 존재**로 판정하라. | `server/Harness/` | **긴급** |
