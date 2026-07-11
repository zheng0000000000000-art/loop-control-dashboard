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
> **작업 보고 의무**: ①주체(actor) ②사용한 하네스와 결과 ③참조 스킬 — 없으면 조율자가 반려한다.

| 순번 | 작업 | 근거 | 영역 | 상태 |
| --- | --- | --- | --- | --- |
| **H-0** | **`scope-check` 하네스 ★최우선** — 지시서의 `## 허용 파일 (allowlist)` glob을 파싱해 `git status` 변경파일과 대조. 허용 밖이면 exit 1 + 목록 출력. **되돌리지 말고 검출·보고만.** | HS-CANDIDATES **HS-06 (12/12)**. I-1 지시서 이탈 반복 + 오늘 2건(Tier2Approver.cs, Tier2ApproverTestCli.cs). 격리·화이트리스트 **프롬프트가 둘 다 실패**했다 — 말이 아니라 사후 검출로 강제해야 한다 | `server/Harness/` | 대기(HOOK-01 후) |
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
