# 06H 검증 — RECOVERY fixture

## DI 유형 선언

- 선언한 유형: `documentation`

## 주체

- 누가: Codex
- 경로: 직접 docs/fixture 경로 수정

## 사용한 하네스

| 하네스 | 명령 | 기대 exit | 실제 exit | 결과 |
| --- | --- | --- | --- | --- |
| context-pack-integrity | `dotnet run --project server -c Release -- context-pack-integrity docs/handoff/queue/directive-06H-recovery-fixture.md` | 0 | 0 | PASS |
| handoff-integrity fixture A | `dotnet run --project server -c Release -- handoff-integrity --workstate docs/qa/fixtures/reconciliation/A/WORKSTATE.json --applier-log docs/qa/fixtures/reconciliation/A/WORKSTATE.applier-log.jsonl` | 1 | 1 | PASS |
| di-completion-check fixture A | `dotnet run --project server -c Release -- di-completion-check --gate POST-COMMIT --manifest docs/qa/fixtures/reconciliation/A/GATE-MANIFEST.json --task recon-postcommit-A` | 1 | 1 | PASS |
| doc-integrity | `dotnet run --project server -c Release -- doc-integrity` | 0 | 0 | PASS |
| measure dev-pack | `dotnet run --project server -- measure dev-pack` | 0 | 0 | PASS |

## 유형별 필수 검증

| DI 유형 | 필수 검증 | 수행 결과 |
| --- | --- | --- |
| documentation | 필수 절, 금지선, RECOVERY 시대 구분, fixture negative evidence | RECOVERY 문서와 fixture manifest로 검증 |

## 참조한 스킬

- `skills/common/hs-gate.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/verification.md`
- `skills/domains/docs/README.md`

## 변경 경로

- docs/handoff/RECOVERY.md
- docs/qa/fixtures/reconciliation/A/WORKSTATE.json
- docs/qa/fixtures/reconciliation/A/WORKSTATE.applier-log.jsonl
- docs/qa/fixtures/reconciliation/A/GATE-MANIFEST.json
- docs/qa/06h-recovery.md
- docs/verification/06h-recovery-fixture.md

## 변경 내용

| 파일 | 종류 | 요약 |
| --- | --- | --- |
| `docs/handoff/RECOVERY.md` | 수정 | 현재 fail-closed 절차와 post-provenance RECOVERY 절차를 분리 |
| `docs/qa/fixtures/reconciliation/A/WORKSTATE.json` | 신규 | 누락 전이 fixture A 상태 파일 |
| `docs/qa/fixtures/reconciliation/A/WORKSTATE.applier-log.jsonl` | 신규 | fixture A 성공 전이 로그 |
| `docs/qa/fixtures/reconciliation/A/GATE-MANIFEST.json` | 신규 | POST-COMMIT manifest negative check |
| `docs/qa/06h-recovery.md` | 신규 | fixture A QA 설명 |
| `docs/verification/06h-recovery-fixture.md` | 신규 | 06H 검증 기록 |

## 반증 시험

| # | 시험 | 기대 exit | 실제 exit | 판정 |
| --- | --- | --- | --- | --- |
| 1 | fixture A 직접 `handoff-integrity` | 1 | 1 | PASS |
| 2 | fixture A POST-COMMIT `di-completion-check` | 1 | 1 | PASS |

## 실패 분류와 실패 사례

- 실패 분류: `expected_rejection`
- 실패 사례 ID: 신규 실패 사례 없음
- 분류 근거: fixture A는 성공 로그에 있는 `TEST-DI0001-2`가 WORKSTATE에 없는 사고 재현 입력이다. 실패는 의도된 반증 결과다.

## 게이트 기록

`{"gate":"dev-pack","violations":0,"attempt":1}`

중간 실행에서 `skillDomainViolations=1`이 한 번 발생했다. 원인은 이 검증 문서가 `skills/domains/docs/README.md`를 참조하면서 변경 경로를 표로만 적어 측정기가 `docs/**` 변경을 bullet 항목으로 읽지 못한 것이다. `## 변경 경로` bullet 목록을 추가한 뒤 `measure dev-pack`은 `violationCount=0`으로 복귀했다.

## 잔여 위험 · 미확정 사항

- 06H는 RECOVERY 운영 문서와 fixture manifest만 다룬다. 실제 StateApplier provenance RECOVERY 구현은 TRUSTED_BASELINE 이후 별도 작업이다.
- 본 세션은 approve/reject/import/push/merge를 수행하지 않는다.

## 직접 경로 사용 사유

06H 지시서 allowlist가 `docs/handoff/RECOVERY.md`, `docs/qa/fixtures/reconciliation/**`, `docs/qa/06h-recovery.md`, `docs/verification/06h-recovery-fixture.md` 직접 수정을 허용한다.

## 지표는 만족했으나 목적은 미달인 부분

- 없음. 단, 06H 자체는 운영 문서와 사고 fixture를 준비한 것이며 실제 provenance RECOVERY 구현은 TRUSTED_BASELINE 이후 별도 작업이다.

## 완료 판정

`PASS | FAIL | BLOCKED` 중 최종 판정은 별도 검수자가 적는다.
