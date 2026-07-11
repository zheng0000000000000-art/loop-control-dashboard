# 지식 승격 파이프라인 — 데이터를 하네스·스킬로 (Axis2 Promotion)

> 목적: 축적 데이터(FAIL 위키·QA 리포트·세션·run-log)에서 **반복 검증된 것**을 하네스(실행 가능한 검사)·스킬(재사용 절차)로 승급해 프로그램(server/·skills/)에 반영하고, 실행자들이 쓰게 한다. 반복되지 않은 것은 승급하지 않는다(v9 §0.4 HS-GATE, 로드맵 Axis2 Promotion).

## 원칙
- **반복이 자산의 조건**: 같은 검사·절차가 2회 이상 반복돼야 승급 후보(v9 §0.4). 1회성은 리포트에만.
- **데이터는 코덱스가, 승급 구현은 sonnet이**: 코덱스가 판정·후보 작성, 하네스 CLI 코드는 sonnet(server/), 검수는 검수자.
- **하네스 > 스킬 > 문서**: 기계 판정 가능하면 하네스(CLI), 절차면 스킬, 나머지는 위키.

## 주기·트리거 (기계 탐지 — 재량 아님)
**`dotnet run --project server -- hs-scan`(HARNESS-02)이 트리거를 탐지하고, 코덱스는 exit code에 복종한다.**
exit 1이면 `skills/common/hs-gate.md` 절차로 **반드시** 심사한다. 이 절이 재량이던 동안 승격은 한 번도 실행되지 않았다(HS-CANDIDATES.md 부재가 증거).

탐지 신호(hs-scan이 기계 판정):
- 같은 QA 패턴이 2회 이상 반복됨.
- FAIL 위키가 직전 판정 이후 3건 이상 늘어남.
- 마지막 HS-GATE 이후 하루 경과(정기).

## 판정 (v9 §0.4 점수)
각 후보를 반복성·결정가능성·장애주입·격리·관찰성·유지가치로 0~2 평가. 총점:
`0~4 부적합 / 5~7 보류·기존확장 / 8~10 기존확장·기한부 / 11~12 즉시제작`.
점수와 무관한 차단(보안·정책 미확정)은 보류. 결과를 `docs/handoff/HS-CANDIDATES.md`에 후보·점수·판정·근거 데이터로 기록.

## 파이프라인
1. **코덱스**: 축적 데이터 리뷰 → HS-CANDIDATES.md에 하네스/스킬 후보 판정.
2. **검수자/조율자**: '즉시제작'·'기한부' 후보를
   - 하네스 → `SONNET-QUEUE`에 "하네스 CLI 구현" 지시서(server/, CliRouter에 CLI 등록, PASS/FAIL 기계 판정).
   - 스킬 → `skills/` 초안(관련 도메인) + contributions 경유 or 직접.
3. **sonnet**: 하네스 CLI 구현 → server 반영(measure·verify-behavior 옆에).
4. **사용**: 하네스는 CLI로 실행자 전원이 사용(sonnet 자가검증·코덱스 QA·조율자 검수 프로토콜·검수자). 스킬은 작업 시작 시 참조.

## 첫 후보 (이미 반복 검증됨 — 코덱스가 HS-GATE로 확정할 것)
| 후보 | 유형 | 근거 데이터 | 무엇을 검사 |
| --- | --- | --- | --- |
| `path-guard-check` | 하네스 | FAIL-006/007 | 경로 경계가 separator-bounded인지(sibling-prefix escape 회귀) |
| `call-integrity-check` | 하네스 | refactor-call-integrity(R당 반복) | 이동 함수의 호출부 누락·시그니처 불일치 |
| `template-sync-check` | 하네스 | FAIL-008 | dispatch-templates가 현행 코드와 동기화됐는지 |
| `e2e-usage` | 하네스 | FEAT-02(큐 진행) | 실사용 시나리오(이미 큐에 있음) |
| `path-escape-qa` | 스킬 | 경로검증 재현 절차 | 경로 escape 판정 체크리스트(재사용) |

이 하네스들이 서면 코덱스는 매번 수동 QA 대신 CLI 실행으로 검증한다 — 토큰 절감 + 결정론 + 회귀 방지. "프로그램은 많이 기억·검증, LLM은 적게 생성"의 실물.
