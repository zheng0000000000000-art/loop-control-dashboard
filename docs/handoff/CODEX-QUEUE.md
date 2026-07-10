# CODEX-QUEUE — 코덱스 작업 큐

> 오케스트레이터/검수자가 채우고, 코덱스가 위에서부터 픽업한다(CODEX-AUTO-15min 루틴 2단계).
> 코덱스는 완료 시 상태를 갱신하지 말고(커밋 안 함) SESSION 문서에 "픽업·완료"를 남긴다 — 큐 상태 갱신은 조율자/검수자가 한다.

| 순번 | 작업 | 지시서/근거 | 영역 | 상태 |
| --- | --- | --- | --- | --- |
| 1 | S-01/S-02 경로 escape 재현 (의심→확정/기각) | directive-CODEX-3-reproduce-path-escape | docs/qa + docs/wiki | 진행 |
| 2 | 리팩토링 호출부 정합성 헌트 — CliRouter/InboxBuilder/CycleSummaryBuilder/MeasurementService의 모든 호출처가 새 위치를 가리키는지, 누락·시그니처 불일치 없는지 | CODEX-AUTO-15min §3 | docs/qa | 대기 |
| 3 | 검수 위임 시범 — 다음 sonnet DI 커밋 1건을 VERIFY-PROTOCOL로 코덱스가 1차 검수 → docs/qa/review-<커밋>.md | VERIFY-PROTOCOL-universal | docs/qa | 대기 |
| 4 | R-04(MeasurementService) 완료 후 maxFunctionLength 해소 검증 — ApplyMeasurementResult 분할이 동작 보존했는지 verify-behavior 교차확인 | — | docs/qa | R-04 완료 대기 |

## 픽업 규칙
- 위에서부터. `진행`이 있으면 그것 우선 마무리.
- 새 sonnet 커밋이 있고 큐에 해당 QA가 없으면 §3(최근 커밋 QA)을 자체 수행하고 SESSION에 기록.
- 코드 수정 필요한 발견은 FAIL 위키에 등록만(수정은 sonnet). 큐에 "sonnet 수정 필요: FAIL-XXX" 후보로 남긴다.
