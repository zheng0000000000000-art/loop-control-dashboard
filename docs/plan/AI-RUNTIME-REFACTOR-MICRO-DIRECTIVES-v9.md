# AI Runtime Refactor 소단위 작업 지시서 v9 — 점진적 운영 강도·다중 AI·컨텍스트 효율 통합판

> 기반 문서: `AI-RUNTIME-REFACTOR-WORK-PLAN-v2-restructured.md`  
> 목적: WP 단위를 한 가지 책임만 갖는 실행 지시서(DI)로 세분화한다.  
> 핵심 지표: **중복 부작용 0 / 복구 불가능한 상태 0 / 승인되지 않은 원본 변경 0**

> v9 변경 원칙: **초기에는 최소 통제 / 자동화 전 안전 강화 / 병렬 작업 전 협업 통제 / Release 전체 검증**
> authoritative 상태: `WORKSTATE.json`; `HANDOFF.md`와 `RUNTIME-INDEX.md`는 생성 가능한 Projection이다.
> 운영 원칙: 규칙은 필요 시 단계적으로 활성화하며, 초기에는 제품 구현보다 관리 체계가 커지지 않도록 한다.


## 0-A. 점진적 운영 강도와 활성화 등급

이 문서의 모든 운영 규칙을 처음부터 동일한 강도로 적용하지 않는다. 각 규칙은 다음 네 등급 중 하나를 가진다.

```text
Required Now
Required Before Automation
Required Before Multi-model Parallel Work
Optional
```

### 0-A.1 등급별 목적

| 등급 | 적용 시점 | 목적 |
|---|---|---|
| Required Now | Phase 0부터 | 상태 소유권, 재실행 안전성, 기본 인수인계 보장 |
| Required Before Automation | Scheduler·retry·recovery 자동화 활성화 전 | 장애 복구와 자동 반복 실행의 위험 통제 |
| Required Before Multi-model Parallel Work | 둘 이상의 AI·사람이 병렬로 작업하기 전 | 파일 충돌, stale context, 인수인계 불일치 통제 |
| Optional | 비용 대비 효과가 확인될 때 | 생산성 향상과 운영 편의 제공 |

### 0-A.2 Required Now

다음 항목만 초기 단계부터 필수다.

1. `WORKSTATE.json`을 현재 작업 상태의 유일한 원본으로 사용한다.
2. WP 하나를 브랜치 하나로 관리하고 DI 하나를 commit 또는 검증 checkpoint 하나로 관리한다.
3. DI 종료 시 상태, 변경 파일, 실행한 검증, 잔여 위험, 다음 작업만 기록한다.
4. WP 종료 시 `handoff-integrity --mode full` 또는 동등한 전체 검증을 수행한다.
5. 새로운 중대한 실패, 불변식 위반, 운영자 개입 사례만 실패 위키에 기록한다.
6. Phase 종료 시 Harness·Skill 후보를 판정하되, gate-critical 항목이 아니면 즉시 제작을 강제하지 않는다.
7. 원본 변경, 중복 부작용, 복구 불가능 상태, stale 승인 사용을 항상 차단한다.

초기 단계에서는 다음을 기본적으로 강제하지 않는다.

- DI마다 전체 Context Receipt 생성
- DI마다 불변 Snapshot 생성
- 모든 문서와 산출물의 전면 hash 관리
- `FILE-CLAIMS.json` 기반 파일 lease
- Phase마다 독립 실행자 재개 시험
- Phase마다 신규 Skill 제작
- 사소한 expected rejection의 실패 위키 등록

### 0-A.3 Required Before Automation

다음 기능을 활성화하기 전 적용한다.

```text
자동 Scheduler
lease 만료 후 자동 재큐잉
자동 retry
RecoveryScanner 자동 복구
자동 Approval·Commit 흐름
```

필수 항목:

- 장애 주입 Harness
- `handoff-integrity --mode full`의 WP·Phase 경계 실행
- stable section ID
- Context Pack source hash 검증
- failureClass별 retry matrix
- 실패 위키 색인
- Commit·복구 관련 Harness와 rollback 검증
- Runtime Evidence와 Engineering Verification Record 분리

### 0-A.4 Required Before Multi-model Parallel Work

둘 이상의 실행자가 동시에 서로 다른 DI 또는 WP를 수행하기 전에 적용한다.

필수 항목:

- `FILE-CLAIMS.json` 또는 동등한 파일 소유권 예약
- 불변 handoff snapshot
- Context Receipt strict mode
- 외부 실행자 또는 미참여 모델의 재개 시험
- stale Context Pack 탐지
- 공통 Projection 파일의 자동 생성
- 충돌 시 authoritative source 우선순위

병렬 작업을 하지 않는 동안 이 규칙들은 비활성 상태로 둘 수 있다.

### 0-A.5 Optional

다음은 효과가 확인되었을 때만 활성화한다.

- 모든 DI에 대한 상세 Context Receipt
- 모든 산출물의 전면 hash inventory
- non-critical Skill의 실행 자동화
- Phase별 신규 Harness·Skill 최대치 소진
- 장문의 자동 요약과 전체 위키 자동 색인
- 세밀한 Context Budget 경보

### 0-A.6 운영 강도 승격 조건

다음 조건 중 두 개 이상이 발생하면 한 단계 높은 운영 등급으로 승격한다.

- 동시에 작업하는 실행자가 3명 이상이다.
- 동일 파일 충돌 또는 잘못된 병합이 두 번 이상 발생했다.
- 인수인계 실패가 두 번 이상 발생했다.
- stale 문서 또는 stale Context Pack 때문에 잘못된 구현이 발생했다.
- 같은 복구 절차를 세 번 이상 수동 수행했다.
- 같은 검증을 세 번 이상 반복했다.
- 자동 retry 또는 Scheduler가 실제 실행되기 시작했다.
- Commit 경계가 canonical repository에 실제 영향을 주기 시작했다.

승격은 ADR 또는 운영 Decision으로 기록한다. 하향 조정은 안전 요구가 줄었다는 근거와 함께 명시적으로 승인한다.

### 0-A.7 규칙 우선순위

이 문서의 다른 절에서 더 강한 의무 표현이 있더라도, 현재 활성화된 운영 등급이 우선한다. 단, 다음 불변식은 등급과 관계없이 항상 필수다.

```text
중복 부작용 0
복구 불가능한 상태 0
승인되지 않은 원본 변경 0
stale 승인 재사용 0
sealed Artifact 변조 0
```

## 0. 사용 규칙

- 실행 책임 단위는 `DI-XX-YY`다. 한 DI는 한 가지 책임과 한 개의 검증 가능한 checkpoint를 가진다.
- **브랜치는 WP당 하나**, DI는 해당 브랜치 안의 commit 또는 검증 checkpoint 하나를 기본으로 한다.
- DI별 격리가 반드시 필요한 경우에만 `task/WP-XX/DI-XX-YY-{name}` 하위 브랜치를 허용한다.
- 선행 DI와 직전 Phase의 최종 `HS-GATE-PXX`가 완료되지 않으면 다음 범위를 시작하지 않는다.
- DI 종료에서는 최소 상태·검증 기록을, WP 종료에서는 전체 인수인계·검증을 수행한다. Phase 종료의 독립 재개 시험은 `Required Before Multi-model Parallel Work` 활성화 후 필수다.
- 상태·승인·Artifact·Decision 파일을 직접 편집해 테스트를 통과시키지 않는다.
- Runtime 사건과 개발 검증 기록을 분리한다.

```text
.runtime/evidence/                   # 실제 Runtime 사건
 docs/verification/evidence/         # DI·Harness·인수인계·개발 검증 결과
```

### 0.1 DI 유형과 완료 프로필

모든 DI는 시작 전에 다음 유형 중 하나를 선언한다.

```text
implementation | schema | documentation | harness |
skill | migration | verification | operations
```

#### 모든 DI의 공통 완료 조건

- [ ] 선언한 DI 유형의 완료 프로필 충족
- [ ] 관련 계약·스키마·문서 갱신
- [ ] 발견된 실패·위험·미확정 사항 기록
- [ ] `WORKSTATE.json` 갱신; Context Receipt는 현재 운영 등급에서 요구될 때만 갱신
- [ ] 변경 범위 준수; 파일 claim 규칙은 parallel-work 등급 활성화 시 준수
- [ ] 원본 저장소 무단 변경 없음

#### 유형별 필수 검증

| DI 유형 | 필수 검증 |
|---|---|
| implementation | 정상·실패·재실행 테스트, 개발 검증 Evidence |
| schema | 정상·비정상 fixture, unknown field·호환성 검사 |
| documentation | 링크·필수 항목·안정 section ID lint |
| harness | positive·negative·결정성·격리 테스트 |
| skill | dry-run, 입력 누락, 중단 조건, 부작용 범위 테스트 |
| migration | dry-run, 반복 실행, 수량·hash 비교, rollback 계획 |
| verification | 독립 재현, 결과 manifest, 근거 링크 |
| operations | 권한, 복구, audit, 재실행 안전성 검사 |

### 0.2 지시서 완료 보고 형식

```text
DI ID / DI 유형:
WP 브랜치 / DI commit 또는 checkpoint:
변경 파일:
실행한 검증:
실패 주입 지점:
실패 분류: expected_rejection | known_failure | new_failure | incident | design_learning | 없음
발견된 실패 사례 ID 또는 기존 위키 링크:
생성된 Engineering Verification Record:
관련 Runtime Evidence ID(해당 시에만):
잔여 위험:
현재 가정과 미확정 사항:
다음 실행자가 가장 먼저 확인할 항목:
WORKSTATE snapshot ID(현재 등급에서 생성한 경우):
Context Pack ID / Receipt ID / 추가 조회 문서(해당 시):
Context Budget 사용 결과와 초과 사유(해당 시):
완료 판정: PASS | FAIL | BLOCKED
```

### 0.3 실패 사례 지식화 기본 규칙

- 실패 결과는 먼저 `expected_rejection | known_failure | new_failure | incident | design_learning`으로 분류한다.
- `expected_rejection`은 계약이 의도대로 입력을 거부한 정상적인 negative test이며 새 위키 문서를 만들지 않는다.
- `new_failure`, `incident`, `design_learning`, 운영자 개입이 필요한 `known_failure`는 **실패 사례 문서화가 기본 동작**이다.
- 실패 사례는 코드 수정이나 테스트 통과만으로 종료하지 않는다. 원인과 판단 과정을 재사용할 수 있도록 위키에 등록한 뒤 DI를 완료한다.
- 같은 원인의 반복 사례는 새 문서를 무조건 만들지 않고 기존 문서에 발생 이력을 추가한다. 원인이 다르거나 판단 기준이 달라졌다면 새 문서를 만들고 상호 연결한다.
- 실패 문서에는 개인의 추측과 확인된 사실을 구분하여 기록한다. 원인이 아직 확정되지 않았다면 `원인 상태: 미확정`으로 남기고 후속 조사 항목을 명시한다.
- 실패 사례 문서의 변경도 리뷰 대상이며, 관련 Evidence·테스트·Decision·Artifact를 추적 가능한 ID로 연결한다.

#### 실패 사례 위키 저장 구조

```text
docs/wiki/failures/
├─ README.md                     # 분류·검색·작성 규칙
├─ index.md                      # 전체 색인
├─ by-component/                 # Scheduler, Transition, Sandbox 등
├─ by-failure-class/             # transient, stale_input 등
└─ cases/
   └─ FAIL-YYYY-NNN-{slug}.md
```

#### 실패 사례 문서 필수 항목

```markdown
# FAIL-YYYY-NNN — 제목

- 상태: 조사중 | 해결됨 | 완화됨 | 재발 | 폐기
- 최초 발생일:
- 최근 발생일:
- 관련 DI/WP:
- 관련 구성요소:
- failureClass:
- 심각도:
- Evidence / Transition / Job / Artifact / Decision ID:

## 1. 발생 상황
어떤 입력, 상태, 정책 버전, 실행 모드, 환경에서 발생했는지 기록한다.

## 2. 관찰된 증상과 영향
기대 결과와 실제 결과, 사용자·데이터·원본 저장소에 미친 영향을 기록한다.

## 3. 발생 이유
직접 원인, 근본 원인, 기여 요인을 구분한다. 확정되지 않은 내용은 가설로 표시한다.

## 4. 검토한 해결 방법
고려한 대안과 각각의 장점, 위험, 비용, 배제 이유를 기록한다.

## 5. 선택한 해결 방법
실제로 적용한 수정, 완화책, 복구 절차를 기록한다.

## 6. 판단 기준
정확성, 원자성, idempotency, 보안, 운영 복구성, 구현 비용, 호환성 등 선택에 사용한 기준과 우선순위를 기록한다.

## 7. 검증 결과
재현 테스트, 실패 주입, 재실행, 회귀 테스트 결과와 Evidence를 연결한다.

## 8. 재발 방지
추가한 불변식, 테스트, 경보, 운영 규칙, 계약 변경을 기록한다.

## 9. 후속 작업과 잔여 위험
미해결 위험, 담당 DI/WP, 재검토 조건을 기록한다.

## 10. 발생 이력
동일 원인의 재발 날짜와 차이점을 누적한다.
```

#### 실패 사례 등록 판정과 문서 생성 임계값

다음 중 하나라도 해당하면 새 문서 생성 또는 기존 사례 갱신이 필수다.

- 새로운 failureClass 또는 기존 분류로 설명되지 않는 실패
- 같은 입력 재실행에서 결과가 달라진 사례
- 중복 부작용, 상태·Evidence 불일치, lease 이중 소유
- stale 승인·정책·base 사용 시도
- Sandbox 경계 또는 Commit 경계 위반
- 자동 복구가 실패하거나 운영자 개입이 필요했던 사례
- 기존 해결 방법이 재발을 막지 못한 사례
- 해결 방법을 선택할 때 의미 있는 대안을 비교한 사례

다음은 새 실패 문서 생성 대상이 아니다.

- 의도된 schema·권한·상태 전이 거부(`expected_rejection`)
- 단순 입력 오타나 이미 문서화된 사용 오류
- 기존 실패 사례와 원인·판단 기준·해결 방식이 동일한 반복 사례

이 경우 테스트 결과 또는 기존 실패 사례의 발생 이력에만 기록한다.

### 0.4 Phase 경계 Harness·Skill 판정 기본 규칙

- Phase 진행 중 후보 검토는 `HS-REVIEW-PXX-RN`으로 기록한다. 이는 Phase 종료 게이트가 아니다.
- 각 Phase의 마지막 DI가 PASS된 뒤 **한 번만** 최종 `HS-GATE-PXX`를 수행한다.
- `HS-GATE`는 단순 검토 메모가 아니라 **판정 → 근거 기록 → 적용 또는 보류 → 검증**까지 포함하는 필수 작업이다.
- Harness와 Skill은 서로 다른 기준으로 독립 판정한다. 하나가 부적합해도 다른 하나는 제작할 수 있다.
- 판정은 `즉시 제작 필수 | 기한부 제작 | 기존 항목 확장 | 보류 | 부적합` 중 하나로 기록한다.
- `즉시 제작 필수`는 gate-critical 항목에만 적용하고 다음 Phase 전에 검증을 완료한다.
- `기한부 제작`은 다음 두 Phase 안의 목표 Phase와 담당 WP를 기록한다.
- `보류`는 반드시 해소 조건과 재판정 Phase를 기록한다. 이유 없이 보류할 수 없다.
- 새 Harness 또는 Skill에서 실패가 발견되면 §0.3에 따라 실패 위키를 작성하거나 기존 사례에 연결한다.

#### Harness 판정 기준

다음 항목을 평가한다.

1. 반복 검증 가치: 이후 Phase 또는 회귀 테스트에서 같은 검증을 반복할 가능성이 높은가?
2. 결정 가능성: 입력, 기대 결과, PASS/FAIL 조건을 기계적으로 판정할 수 있는가?
3. 장애 주입 가치: crash, retry, stale state, 권한 위반 등을 자동 재현할 수 있는가?
4. 격리 가능성: 원본 저장소와 외부 시스템에 안전한 부작용 없이 실행할 수 있는가?
5. 관찰 가능성: Evidence, Transition, Job, Artifact 등 결과를 구조적으로 수집할 수 있는가?
6. 유지 비용: Harness 유지 비용보다 반복 수동 검증 비용이 큰가?

다음 조건 중 하나라도 충족하면 Harness 제작을 우선한다.

- 동일 검증을 2회 이상 반복할 가능성이 있다.
- 게이트 또는 출시 차단 조건을 자동 판정할 수 있다.
- 장애 주입이나 동시성 재현이 사람의 수동 절차보다 신뢰도가 높다.
- 회귀 시 중복 부작용, 상태 불일치, 권한 위반을 조기에 탐지할 수 있다.

#### Skill 판정 기준

여기서 Skill은 실행자가 반복적으로 수행하는 절차를 **명확한 입력·순서·출력·안전 규칙을 가진 재사용 작업 단위**로 만든 것을 뜻한다.

다음 항목을 평가한다.

1. 반복성: 여러 WP, DI, 운영 상황에서 같은 절차가 반복되는가?
2. 절차 안정성: 작업 순서와 필수 확인 항목이 충분히 고정됐는가?
3. 입력·출력 명확성: 필요한 입력과 생성 산출물을 명시할 수 있는가?
4. 안전 경계: 금지사항, 권한, 원본 변경 범위, 중단 조건을 정의할 수 있는가?
5. 판단 재사용성: 개인 경험이 아니라 정책, 체크리스트, Decision 기준으로 일반화할 수 있는가?
6. 도구화 가능성: 스크립트, 명령, 템플릿 또는 Runtime API로 지원할 수 있는가?

다음 유형은 Skill 후보로 우선 검토한다.

- Transition 복구와 reconcile
- 실패 사례 등록과 색인 갱신
- Artifact sealing·hash 검증
- Sandbox 생성·정리
- stale Approval 판정
- Shadow Artifact 승격
- Commit 전 안전성 검사와 rollback
- Legacy migration dry-run

#### 판정 점수와 결과

각 기준은 `0=해당 없음`, `1=부분 충족`, `2=충족`으로 평가한다.

```text
총점 0~4   → 부적합
총점 5~7   → 보류 또는 기존 항목 확장 검토
총점 8~10  → 기존 항목 확장 또는 기한부 제작
총점 11~12 + gate-critical → 즉시 제작 필수
총점 11~12 + non-critical → 다음 두 Phase 내 기한부 제작
```

점수만으로 최종 결정하지 않는다. 보안 위험, 원본 변경 가능성, 정책 미확정처럼 점수와 무관한 차단 사유가 있으면 `보류` 또는 `부적합`으로 판정하고 이유를 기록한다.


#### Harness·Skill 제작 예산과 유형

- Phase당 신규 Harness는 기본 최대 2개, 신규 Skill은 기본 최대 2개다. 이는 제작 의무가 아니라 상한이며, Required Now에서는 gate-critical 항목 외 신규 제작을 강제하지 않는다.
- 출시 차단 불변식, 보안 경계, Commit 안전성, 복구 불가능 상태 방지는 예산 예외가 될 수 있다.
- 신규 제작보다 기존 Harness·Skill 확장을 우선 검토한다.

Skill은 다음 유형 중 하나를 선언한다.

```text
procedural | assisted | executable
```

Skill manifest에는 `skillType`, `automationLevel`, `humanApprovalPoints`, `sideEffectScope`, `requiredCapabilities`를 포함한다.

#### HS-GATE 운영 등급 적용

- Required Now: 후보 판정과 gate-critical 여부 기록만 필수다.
- Required Before Automation: 자동화 경계 관련 Harness는 검증 완료까지 필수다.
- Required Before Multi-model Parallel Work: 인수인계·Context 무결성 Harness와 재개 시험이 필수다.
- Optional: non-critical Skill과 편의 Harness는 backlog로 남길 수 있다.

#### HS-GATE 필수 산출물

```text
docs/verification/phase-gates/HS-GATE-PXX.md
harnesses/<name>/                 # 제작 또는 확장 판정 시
skills/<name>/                    # 제작 또는 확장 판정 시
docs/wiki/harnesses/<name>.md     # 목적·입력·판정·한계
docs/wiki/skills/<name>.md        # 사용 조건·절차·안전 경계
```

#### HS-GATE 판정 문서 형식

```markdown
# HS-GATE-PXX — Phase X 경계 판정

## 1. Phase에서 새로 안정된 계약과 반복 절차

## 2. Harness 후보
| 후보 | 반복성 | 결정 가능성 | 장애 주입 | 격리 | 관찰성 | 유지 가치 | 총점 | 판정 |

## 3. Skill 후보
| 후보 | 반복성 | 절차 안정성 | 입출력 | 안전 경계 | 판단 재사용 | 도구화 | 총점 | 판정 |

## 4. 선택한 적용
제작·확장한 Harness와 Skill, 선택 이유, 배제한 대안을 기록한다.

## 5. 판단 기준
정확성, 회귀 탐지력, 운영 복구성, 보안, 구현·유지 비용의 우선순위를 기록한다.

## 6. 검증 결과
정상, 실패, 재실행, 격리, 권한 테스트 결과와 Evidence를 연결한다.

## 7. 보류·부적합 항목
차단 이유, 해소 조건, 재판정 Phase를 기록한다.

## 8. 실패 위키 연결
새 실패 사례 ID 또는 기존 문서 링크를 기록한다.

## 9. 다음 Phase 진입 판정
PASS | FAIL
```

#### Harness 최소 계약

- 목적과 탐지하려는 불변식
- 입력 fixture와 환경 조건
- 실행 격리 방식
- 장애 주입 지점
- PASS/FAIL의 기계적 기준
- 결과 manifest와 Evidence 연결
- 재실행 시 동일한 판정
- false positive·false negative 한계

#### Skill 최소 계약

- Skill 이름과 적용 조건
- 필요한 입력과 사전 확인
- 순서가 고정된 실행 단계
- 생성·변경 가능한 범위
- 금지사항과 즉시 중단 조건
- 완료 산출물과 검증 방법
- 실패 시 복구·위키 등록 절차
- 버전과 적용 가능한 schema·policy 범위

#### Phase 경계 실행 순서

```text
Phase 마지막 DI PASS
→ 새 계약·반복 절차 목록화
→ Harness 후보별 점수·차단 사유 판정
→ Skill 후보별 점수·차단 사유 판정
→ 제작/확장 항목 구현
→ 정상·실패·재실행 검증
→ 위키·Evidence·검증 문서 연결
→ HS-GATE-PXX PASS
→ 다음 Phase 시작
```

### 0.5 다중 AI 모델 작업 연속성 기본 규칙

#### 목표

어떤 AI 모델이나 사람이 작업을 이어받더라도 이전 대화, 개인 메모, 모델별 숨은 문맥에 의존하지 않고 저장소의 공식 기록만으로 현재 상태를 재구성하고 안전하게 다음 작업을 수행할 수 있어야 한다.

#### 기본 원칙

- **저장소가 유일한 인수인계 원본이다.** 채팅 내용, 모델의 기억, 임시 scratchpad는 공식 상태로 간주하지 않는다.
- `WORKSTATE.json`이 현재 작업 상태의 유일한 authoritative 원본이다.
- `HANDOFF.md`와 `RUNTIME-INDEX.md`는 WORKSTATE와 Context Pack에서 생성되는 Projection이며 직접 상태 원본으로 편집하지 않는다.
- 구현 의도와 판단은 코드만으로 추론하게 두지 않는다. 중요한 선택은 Decision Record 또는 실패 위키에 기록한다.
- 다음 모델은 기존 작업을 신뢰만 하지 않고, 변경 파일·테스트·hash·브랜치 상태를 기계적으로 재검증한 뒤 작업을 재개한다.
- 모델별 문체, 도구, 추론 방식이 달라도 결과가 같도록 입력, 출력, 불변식, PASS/FAIL 기준을 명시한다.
- 완료되지 않은 작업을 완료된 것처럼 요약하지 않는다. `완료`, `부분 완료`, `차단`, `조사중`을 명확히 구분한다.
- 토큰이나 문맥 제한으로 생략한 정보가 있다면 생략 사실과 원본 경로를 기록한다.

#### 공식 인수인계 저장 구조

```text
docs/handoff/
├─ HANDOFF.md                    # WORKSTATE에서 생성되는 사람용 Projection
├─ WORKSTATE.json                # 유일한 현재 작업 상태 원본
├─ CONTEXT-MANIFEST.json         # 입력 참조 목록; 메타 파일 자기 hash 금지
├─ decisions/                    # 중요한 설계·구현 판단
│  └─ ADR-NNN-{slug}.md
├─ sessions/                     # 모델·작업 세션별 종료 기록
│  └─ SESSION-YYYYMMDD-NNN.md
└─ snapshots/                    # Phase/WP 경계의 불변 인수인계 snapshot
   └─ HANDOFF-PXX-WPXX.json
```

#### `HANDOFF.md` 필수 항목

```markdown
# Current Handoff

## 1. 현재 위치
현재 Phase, WP, DI, 브랜치, 상태를 기록한다.

## 2. 이번 작업에서 완료한 것
검증이 끝난 변경만 기록한다.

## 3. 아직 완료되지 않은 것
부분 구현, 임시 코드, 실패 테스트, 미확정 계약을 기록한다.

## 4. 현재 불변식과 안전 경계
다음 실행자가 절대 깨뜨리면 안 되는 규칙을 기록한다.

## 5. 중요 결정과 근거
ADR, Decision, 실패 위키 링크를 기록한다.

## 6. 실행한 명령과 테스트
재현 가능한 명령, 결과 요약, 로그 경로를 기록한다.

## 7. 변경 파일과 산출물
파일 경로, 역할, content hash를 기록한다.

## 8. 알려진 실패와 잔여 위험
failure ID, 차단 사유, 임시 완화책을 기록한다.

## 9. 다음 작업 순서
다음 실행자가 수행할 첫 작업부터 순서대로 기록한다.

## 10. 재개 전 검증
반드시 다시 실행할 검사와 기대 결과를 기록한다.
```

#### `WORKSTATE.json` 최소 계약

```json
{
  "schemaVersion": 1,
  "phaseId": "P00",
  "wpId": "WP-00",
  "diId": "DI-00-05",
  "status": "waiting|in_progress|verifying|completed|blocked",
  "branch": "task/WP-00-multi-model-handoff",
  "baseCommit": "...",
  "headCommit": "...",
  "policyVersions": [],
  "schemaVersions": [],
  "completedOutputs": [],
  "partialOutputs": [],
  "changedFiles": [{"path": "src/...", "sha256": "..."}],
  "snapshotId": "SNAPSHOT-PXX-WPXX-DIXX",
  "tests": [{"command": "...", "result": "pass|fail|not_run", "logRef": "..."}],
  "openFailures": [],
  "decisions": [],
  "assumptions": [],
  "blockers": [],
  "nextActions": [],
  "contextPackId": "CTX-DI-XX-YY",
  "updatedAt": "...",
  "updatedBy": {"type": "ai|human", "model": "unknown-allowed", "sessionId": "..."}
}
```

`updatedBy.model`은 감사 정보일 뿐 구현 판정에 사용하지 않는다. 특정 모델 이름이나 능력을 전제로 다음 작업을 배정해서는 안 된다.

#### Hash·Snapshot 규칙

- `WORKSTATE.json`, `HANDOFF.md`, `RUNTIME-INDEX.md`, `CONTEXT-MANIFEST.json`, Context Receipt는 자신의 hash 목록에 포함하지 않는다.
- `WORKSTATE.changedFiles`에는 구현 코드, schema, fixture, 테스트 산출물, 검증 문서만 기록한다.
- DI 또는 WP 경계에서 불변 `SNAPSHOT-PXX-WPXX-DIXX.json`을 생성하고, 상위 문서는 snapshot ID와 hash만 참조한다.
- CONTEXT-MANIFEST와 WORKSTATE가 서로의 hash를 참조하는 순환 구조를 금지한다.

#### Context Manifest 규칙

- 다음 DI 수행에 필요한 문서, schema, fixture, policy, Harness, Skill만 명시한다.
- 각 항목에 경로, 버전, SHA-256, 필수 여부, 읽는 순서를 기록한다.
- “이전 대화를 참고” 같은 비파일 의존성을 넣지 않는다.
- manifest hash가 달라졌다면 다음 모델은 변경 이유를 확인하고 stale 인수인계 여부를 판정한다.
- 너무 큰 로그나 결과는 요약본과 원본 경로를 함께 제공하고, 요약만을 근거로 안전 결정을 내리지 않는다.

#### 인수 전 검증 절차

```text
현재 branch·commit 확인
→ WORKSTATE schema 검증
→ CONTEXT-MANIFEST hash 검증
→ 변경 파일 hash 확인
→ 선행 DI와 게이트 PASS 확인
→ open failure·blocker 확인
→ 지정된 smoke test 재실행
→ 결과가 기록과 일치할 때만 작업 재개
```

불일치가 있으면 새 구현을 시작하지 않고 `handoff_mismatch` 실패 사례를 등록하거나 기존 사례에 연결한다.

#### 인계 전 종료 절차

```text
작업 중지 지점 확정
→ 임시·부분 산출물 구분
→ 테스트와 명령 기록
→ 변경 파일 hash 생성
→ 가정·결정·실패·잔여 위험 기록
→ 다음 작업을 순서형 명령으로 작성
→ WORKSTATE.tmp 작성 후 atomic replace
→ HANDOFF와 RUNTIME-INDEX Projection 재생성
→ 불변 snapshot 생성
→ handoff Harness 실행
→ SESSION 문서 생성
```

#### 모델 독립적 지시서 작성 기준

모든 DI와 Skill은 다음 정보를 포함해야 한다.

1. 필요한 입력 파일과 버전
2. 시작 상태와 선행 불변식
3. 순서가 고정된 작업
4. 허용되는 변경 범위
5. 금지되는 변경과 즉시 중단 조건
6. 기계적으로 판정 가능한 완료 기준
7. 정상·실패·재실행 테스트 명령
8. 생성할 Evidence와 문서 경로
9. 작업이 중단될 때 안전한 저장 지점
10. 다음 실행자에게 넘겨야 하는 정보

“적절히 수정”, “필요하면 처리”, “알아서 판단”처럼 모델마다 다르게 해석될 표현은 사용하지 않는다. 판단이 필요한 경우 후보, 평가 기준, 우선순위, 승인 요구 조건을 함께 제공한다.

#### 중요 판단 기록 규칙

다음 중 하나라도 해당하면 ADR을 생성한다.

- 두 개 이상의 구현 대안 중 하나를 선택한 경우
- schema, 상태 소유권, idempotency, 보안 경계를 변경한 경우
- 기존 DI 지시를 해석하거나 예외 처리한 경우
- 다음 모델이 같은 논의를 반복할 가능성이 높은 경우
- 선택 결과가 이후 Phase의 Harness·Skill·Commit에 영향을 주는 경우

ADR에는 `상황 / 선택지 / 선택 / 판단 기준 / 결과 / 되돌림 조건 / 관련 실패 사례`를 기록한다.

#### 다중 모델 충돌 방지 규칙

- 동일 DI를 둘 이상의 실행자가 동시에 수정하지 않는다. 필요하면 DI를 더 작은 하위 작업으로 분할한다.
- 병렬 작업 등급이 활성화된 경우에만 파일 소유 범위와 예상 변경 파일을 `docs/handoff/FILE-CLAIMS.json`에 먼저 예약한다.
- 서로 다른 DI라도 동일 파일 또는 겹치는 path pattern을 exclusive claim할 수 없다.
- STATUS, HANDOFF, RUNTIME-INDEX 같은 공통 Projection은 생성기만 갱신한다.
- 인계 시 uncommitted 변경을 숨기지 않는다. commit하지 않은 이유와 안전성을 기록한다.
- 생성물은 deterministic format을 사용하고, 정렬·직렬화·줄바꿈 규칙을 고정한다.
- 한 모델이 만든 요약을 다른 모델이 사실로 승격하려면 원본 Evidence나 테스트로 확인한다.
- 모델 교체를 이유로 이미 통과한 검증을 생략하지 않으며, 반대로 모든 전체 테스트를 무조건 반복하지 않고 Context Manifest의 재개 검사를 따른다.

#### 다중 모델 연속성 Harness·Skill

다음 두 항목은 Phase 0에서 우선 제작한다.

**`handoff-integrity` Harness**

- `quick` 모드: DI 종료 시 WORKSTATE schema, 현재 상태, 필수 참조, blocker·nextAction 일관성 검사
- `full` 모드: WP·Phase 종료 시 snapshot hash, commit, 전체 참조, 산출물, smoke test까지 검사
- HANDOFF, WORKSTATE, CONTEXT-MANIFEST schema와 상호 참조 검사
- 파일 hash, branch, commit, 선행 DI, 테스트 기록 일치 검사
- 완료로 표시된 산출물의 실제 존재와 검증 문서 확인
- blocker가 있는데 다음 DI가 진행 상태인지 검사
- stale handoff와 누락된 실패 위키 링크 탐지
- PASS/FAIL 결과와 Evidence 생성

**`prepare-model-handoff` Skill**

- 현재 작업 상태 수집
- 완료·부분·미착수 구분
- 변경 파일과 hash 기록
- 테스트 결과 및 재현 명령 정리
- ADR·실패 위키·Evidence 연결
- 다음 작업 순서 작성
- atomic handoff 갱신
- `handoff-integrity` Harness 실행

#### Phase·WP·DI 경계 적용

- **DI 종료:** WORKSTATE를 atomic replace하고 Projection을 재생성한 뒤 `handoff-integrity --mode quick`을 실행한다.
- **WP 종료:** 해당 WP의 결정·실패·산출물을 snapshot으로 고정하고 `handoff-integrity --mode full`을 실행한다.
- **Phase 종료:** 병렬 작업 등급이 활성화된 경우에만 HS-GATE 전에 다중 모델 재개 시험을 수행한다. 기존 작업에 참여하지 않은 실행자가 Context Manifest만으로 smoke test와 다음 DI 착수 준비를 성공해야 한다.
- 병렬 작업 등급이 활성화된 경우 재개 시험 실패 시 HS-GATE를 PASS로 판정할 수 없다.

#### 추가 완료 조건

- [ ] 다음 모델이 이전 채팅 없이 현재 상태를 설명할 수 있다.
- [ ] 완료·부분 완료·차단 상태가 파일에서 구분된다.
- [ ] 모든 중요 결정이 ADR 또는 Decision으로 추적된다.
- [ ] 재현 명령과 기대 결과가 기록되어 있다.
- [ ] 변경 파일 hash와 실제 파일이 일치한다.
- [ ] handoff-integrity Harness가 PASS한다.

### 0.6 컨텍스트 절약과 단계적 로딩 기본 규칙

#### 목표

실행자가 전체 설계·전체 위키·전체 로그를 매번 읽지 않고도 현재 DI를 안전하게 수행하도록 한다. 컨텍스트 절약은 정보 삭제가 아니라 **요약 계층, 참조 키, 필요 시 원문 확장**으로 구현한다.

#### 핵심 원칙

- **최소 충분 컨텍스트**를 기본값으로 한다. 현재 DI 수행과 안전 판정에 필요한 문서만 먼저 읽는다.
- 요약본은 탐색용이며 최종 근거가 아니다. 상태 변경, 승인, Commit, 보안 판단 전에는 원본 계약이나 Evidence를 확인한다.
- 같은 사실을 HANDOFF, STATUS, 위키, 검증 문서에 장문으로 복제하지 않는다. 한 곳을 원본으로 두고 나머지는 ID와 짧은 요약만 기록한다.
- 큰 로그, 테스트 출력, diff, Evidence는 전문을 프롬프트에 넣지 않고 경로·hash·핵심 구간·조회 조건을 남긴다.
- Context Receipt는 기본적으로 WP 경계에서 남긴다. DI별 Receipt는 automation 또는 parallel-work 등급이 활성화되었거나 해당 DI가 고위험일 때만 필수다.
- 컨텍스트 한도를 이유로 안전 경계, 미해결 실패, 금지사항, 승인 조건을 생략하지 않는다.

#### 컨텍스트 4계층

```text
L0 — Runtime Index
     현재 Phase/WP/DI, 상태, 차단 사유, 다음 작업만 포함
L1 — DI Context Pack
     현재 DI 수행에 필요한 계약·입력·테스트·실패 요약
L2 — Referenced Detail
     관련 ADR, 실패 위키, schema, Harness/Skill 문서의 필요한 절
L3 — Raw Evidence
     전체 로그, JSONL, 전체 diff, 원본 실행 결과
```

기본 로딩 순서는 `L0 → L1`이다. 모호성, 실패, 계약 충돌, 안전 판정이 있을 때만 `L2`, `L3`를 조회한다.

#### 저장 구조

```text
docs/context/
├─ RUNTIME-INDEX.md
├─ packs/
│  └─ CTX-DI-XX-YY.json
├─ receipts/
│  └─ RECEIPT-DI-XX-YY-YYYYMMDD-NNN.json
├─ summaries/
│  ├─ architecture.md
│  ├─ invariants.md
│  ├─ open-failures.md
│  └─ phase-PXX.md
└─ schemas/
   ├─ context-pack.schema.json
   └─ context-receipt.schema.json
```

#### DI Context Pack 최소 계약

```json
{
  "schemaVersion": 1,
  "contextPackId": "CTX-DI-00-06",
  "diId": "DI-00-06",
  "objective": "한 문장 목표",
  "readOrder": [],
  "requiredInputs": [{"path": "...", "sha256": "...", "sectionIds": ["CONTRACT-...-001"]}],
  "optionalInputs": [],
  "invariants": [],
  "forbiddenActions": [],
  "openFailures": [],
  "decisions": [],
  "expectedOutputs": [],
  "verificationCommands": [],
  "escalationTriggers": [],
  "budget": {
    "maxRequiredDocuments": 8,
    "maxRequiredSummaryChars": 16000,
    "rawEvidenceDefault": "not_loaded"
  }
}
```

숫자는 기본값이며 DI 위험도에 따라 조정할 수 있다. 조정 시 이유를 Context Pack에 기록한다.

#### Context Receipt 최소 계약

```json
{
  "schemaVersion": 1,
  "contextPackId": "CTX-DI-00-06",
  "diId": "DI-00-06",
  "loadedRequired": [],
  "loadedOptional": [],
  "loadedRawEvidence": [],
  "unresolvedQuestions": [],
  "newReferences": [],
  "budgetExceeded": false,
  "budgetExceptionReason": null,
  "result": "pass|fail|blocked"
}
```

Receipt는 실행자가 실제로 무엇을 읽고 판단했는지 남긴다. 특정 모델의 내부 추론은 기록하지 않고, 확인한 근거와 미확정 항목만 기록한다.

#### 문서 작성 규칙

- 각 문서는 시작 부분에 `TL;DR`, 적용 범위, authoritative 여부, 마지막 검증 버전을 둔다.
- 장문 문서는 제목과 분리된 안정 `sectionId`를 사용한다. 제목 변경으로 sectionId를 변경하지 않는다.
- Context Pack은 문서 제목이 아니라 sectionId를 참조한다.
- 중복 설명 대신 `ADR-NNN`, `FAIL-YYYY-NNN`, `INV-NNN`, `DECISION-ID`를 사용한다.
- 요약에는 `확정 사실 / 가정 / 미확정 / 폐기된 내용`을 구분한다.
- 200줄을 넘는 로그와 diff는 원문 파일로 저장하고, 요약·관련 줄 범위·hash만 문서에 넣는다.
- Phase 요약은 이전 Phase의 세부 작업을 반복하지 않고 새로 확정된 계약, 변경된 불변식, 열린 실패, 다음 Phase 영향만 기록한다.

#### 요약 freshness 계약

모든 요약은 다음 metadata를 가진다.

```yaml
summaryId: SUMMARY-PXX-NNN
sourceRefs:
  - path: path/to/source
    sha256: ...
generatedFromCommit: ...
validatedAt: ...
status: current | stale | superseded
```

Integrity Harness는 요약 파일 자체 hash가 아니라 `sourceRefs`의 현재 hash와 기록된 hash를 비교한다.

#### 문서 Redaction 정책

- 환경변수 값, API key, credential 내용, 민감 사용자 데이터는 HANDOFF·위키·Context Pack에 저장하지 않는다.
- credential은 식별자나 이름만 기록하고 값은 기록하지 않는다.
- 원본 로그 대신 redacted log를 사용하며 민감 입력은 content hash와 안전한 요약만 남긴다.
- 실패 위키는 `visibility = public | internal | restricted`를 선언한다.
- 문서·snapshot 생성 전 secret scanner를 실행한다.

#### 조회 확대 조건

다음 중 하나가 발생하면 상위 계층의 원문을 추가로 읽는다.

- Context Pack과 실제 schema·코드·hash가 불일치
- 상태 소유권, 권한, idempotency, Commit 경계 판단
- 새로운 failureClass 또는 기존 실패와 다른 증상
- 테스트 결과가 요약과 불일치
- ADR 간 충돌 또는 policy version 변경
- stale base, stale Approval, 외부 부작용 불명 상태

추가 조회를 수행하면 Receipt에 경로와 이유를 기록한다.

#### Context Budget 판정

각 DI는 시작 전에 다음을 기록한다.

```text
필수 문서 수
필수 요약 문자 수
선택 문서 수
Raw Evidence 로딩 여부
예상 추가 조회 조건
```

Budget 초과 자체는 실패가 아니다. 다만 다음 중 하나이면 DI를 분할하거나 Context Pack을 개선한다.

- 필수 문서가 기본값 8개를 반복적으로 초과
- 동일 원문을 세 개 이상의 DI가 매번 전체 로딩
- Raw Evidence를 정상 경로에서 항상 로딩
- Context Pack만으로 시작 상태와 안전 경계를 설명할 수 없음
- 요약 불일치가 반복되어 원문 재확인이 상시 필요

#### Context Harness·Skill

**`context-pack-integrity` Harness**

- pack schema, 경로, hash, 절 ID, 선행 DI, policy/schema version을 검사한다.
- 필수 문서 누락, 폐기된 ADR 참조, 해결된 실패를 open으로 유지한 경우를 탐지한다.
- Budget 수치와 실제 필수 입력을 비교한다.
- 요약이 authoritative 원본보다 최신인 척하는 stale summary를 탐지한다.

**`build-di-context-pack` Skill**

- DI 목표와 선행조건을 읽는다.
- 필수 불변식, 금지사항, 열린 실패, 관련 Decision을 추출한다.
- 필요한 절만 readOrder에 배치한다.
- 중복 설명을 ID 참조로 치환한다.
- integrity Harness를 실행하고 Context Pack을 확정한다.

**`compact-phase-context` Skill**

- Phase 완료 산출물에서 새로 확정된 계약과 변경점만 추출한다.
- 폐기된 가정과 superseded 문서를 표시한다.
- 다음 Phase용 요약과 Context Manifest를 갱신한다.
- 원문 hash와 역참조를 유지한다.

#### 경계 적용

- **DI 시작:** L0와 해당 Context Pack만 먼저 읽고 integrity Harness를 통과한다.
- **DI 종료:** WORKSTATE에 최소 상태를 기록한다. Context Receipt와 RUNTIME-INDEX 갱신은 현재 운영 등급에서 요구될 때 수행한다.
- **WP 종료:** 반복 참조되는 내용을 WP 요약으로 압축하고 중복 문서를 정리한다.
- **Phase 종료:** `compact-phase-context`를 실행한 뒤 HS-GATE에서 다음 Phase의 예상 Context Budget을 판정한다.
- **모델 인수인계:** HANDOFF는 전체 배경을 복제하지 않고 RUNTIME-INDEX와 Context Pack ID를 안내한다.

#### 추가 완료 조건

- [ ] 현재 DI는 L0와 L1만으로 안전하게 착수할 수 있다.
- [ ] 원문을 읽어야 하는 조건이 명시되어 있다.
- [ ] Context Pack의 모든 필수 참조와 hash가 유효하다.
- [ ] Context Receipt가 실제 조회 범위를 기록한다.
- [ ] 장문 로그·diff·Evidence가 요약과 원문으로 분리되어 있다.
- [ ] Context Budget 초과 시 이유 또는 DI 분할 결정이 기록된다.

## 1. 전체 실행 지도

| Phase | 범위 | DI 수 | 시작 조건 | 종료 조건 |
|---|---|---:|---|---|
| Phase 0 | 작업 착수와 공통 기반 | 7 | `없음` | `DI-00-07`에서 `HS-GATE-P00` PASS |
| Phase 1 | 저장 구조와 공통 계약 고정 | 11 | `HS-GATE-P00` | 마지막 DI PASS + `HS-GATE-P01` PASS |
| Phase 2 | 상태 소유권과 전이 엔진 | 13 | `HS-GATE-P01` 및 기존 선행 DI | 마지막 DI PASS + `HS-GATE-P02` PASS |
| Phase 3 | Job Queue, Lease, Retry | 11 | `HS-GATE-P02` 및 기존 선행 DI | 마지막 DI PASS + `HS-GATE-P03` PASS |
| Phase 4 | Artifact와 Shadow 실행 기반 | 12 | `HS-GATE-P03` 및 기존 선행 DI | 마지막 DI PASS + `HS-GATE-P04` PASS |
| Phase 5 | LegacyCycleJob 이관과 Idempotency | 14 | `HS-GATE-P04` 및 기존 선행 DI | `DI-12-04` PASS + `HS-GATE-P05` PASS |
| Phase 6 | 제한된 Scheduler 자동화 | 7 | `HS-GATE-P05` | `DI-13-07` PASS + `HS-GATE-P06` PASS |
| Phase 7 | Blueprint Admission과 Policy 버전 | 5 | `HS-GATE-P06` | `DI-14-05` PASS + `HS-GATE-P07` PASS |
| Phase 8 | Authority와 비차단 Approval | 10 | `HS-GATE-P07` | `DI-16-06` PASS + `HS-GATE-P08` PASS |
| Phase 9 | Shadow Artifact 승격 | 5 | `HS-GATE-P08` | `DI-17-05` PASS + `HS-GATE-P09` PASS |
| Phase 10 | Commit 경계와 Rollback | 10 | `HS-GATE-P09` | `DI-18-10` PASS + `HS-GATE-P10` PASS |
| Phase 11 | Projection과 운영 복구 도구 | 8 | `HS-GATE-P10` | `DI-19-08` PASS + `HS-GATE-P11` PASS |
| Phase 12 | Legacy 데이터 마이그레이션 | 6 | `HS-GATE-P11` | `DI-20-06` PASS + `HS-GATE-P12` PASS |
| Phase 13 | 최종 통합과 출시 판정 | 7 | `HS-GATE-P12` | `DI-21-07` PASS + `HS-GATE-P13` PASS |

> **Phase 진입 추가 조건:** Phase 1 이후 모든 Phase는 직전 Phase의 `HS-GATE-PXX`가 PASS되어야 시작할 수 있다. 표의 기존 DI 선행조건과 HS-GATE 조건을 모두 만족해야 한다.

# Phase 0. 작업 착수와 공통 기반

## DI-00-01 — 작업 추적 파일 초기화

`선행: 없음`

### 지시

1. `STATUS.md`를 생성하고 모든 WP를 `대기`로 등록한다.
2. 현재 작업의 브랜치·담당자·시작일·검증 문서 경로 필드를 정의한다.
3. 상태 변경 규칙을 `대기 → 진행 → 검증 → 완료`로 제한한다.

### 산출물

- `STATUS.md`
- `상태 변경 규칙 문서`

### 검증

- [ ] 누락된 WP가 없다.
- [ ] 허용되지 않은 역방향 상태 변경이 문서상 차단된다.

### 금지사항

- 구현 코드를 작성하지 않는다.
- WP 완료 전 상태를 `완료`로 바꾸지 않는다.

## DI-00-02 — 공통 검증 템플릿 생성

`선행: DI-00-01`

### 지시

1. `docs/verification/_template.md`를 생성한다.
2. 목표, 변경 파일, 정상 테스트, 실패 테스트, 재실행 테스트, Evidence 예시, 잔여 위험, 완료 판정 항목을 넣는다.
3. 모든 WP 검증 문서가 이 템플릿을 사용하도록 규칙을 명시한다.

### 산출물

- `검증 문서 템플릿`

### 검증

- [ ] DI 유형별 완료 프로필과 공통 완료 조건이 모두 템플릿에 존재한다.

### 금지사항

- 검증 결과를 코드 주석으로만 남기지 않는다.

## DI-00-03 — 실패 사례 위키 기반 생성

`선행: DI-00-02`

### 지시

1. `docs/wiki/failures/` 저장 구조와 `README.md`, `index.md`, 사례 템플릿을 생성한다.
2. 실패 사례 ID를 `FAIL-YYYY-NNN` 형식으로 발급하는 규칙을 정의한다.
3. 구성요소별·failureClass별 색인을 갱신하는 절차를 정의한다.
4. DI 완료 보고와 WP 검증 문서에서 실패 사례 ID 또는 기존 위키 링크를 필수로 참조하도록 템플릿을 갱신한다.
5. 실패 사례가 없는 DI는 `신규 실패 사례 없음`을 명시하도록 한다.
6. 해결되지 않은 사례는 상태를 `조사중` 또는 `완화됨`으로 유지하고 완료된 것으로 위장하지 않는다.

### 산출물

- `docs/wiki/failures/README.md`
- `docs/wiki/failures/index.md`
- `docs/wiki/failures/_template.md`
- `실패 사례 ID·색인 관리 규칙`
- 갱신된 `docs/verification/_template.md`

### 검증

- [ ] 템플릿에 발생 상황, 선택한 해결 방법, 판단 기준, 발생 이유가 모두 포함된다.
- [ ] 하나의 예시 실패 사례가 색인에서 구성요소와 failureClass 양쪽으로 조회된다.
- [ ] 동일 원인의 재발을 기존 문서에 누적할 수 있다.
- [ ] 미확정 원인을 사실처럼 기록하지 않도록 상태와 가설 표기가 존재한다.

### 금지사항

- 실패 원인을 확인하지 않고 개인 또는 모듈의 책임으로 단정하지 않는다.
- 테스트 로그 원문만 붙이고 판단 과정과 재발 방지를 생략하지 않는다.
- 실패 문서 작성을 코드 수정 이후의 선택 작업으로 취급하지 않는다.

## DI-00-04 — Harness·Skill 판정 기반 생성

`선행: DI-00-03`

### 지시

1. `docs/verification/phase-gates/`, `docs/wiki/harnesses/`, `docs/wiki/skills/`, `harnesses/`, `skills/` 기본 구조를 생성한다.
2. §0.4의 HS-GATE 판정 템플릿을 `docs/verification/phase-gates/_template.md`로 만든다.
3. Harness manifest와 Skill manifest의 최소 schema 또는 문서 계약을 정의한다.
4. Phase 종료 시 HS-GATE 누락을 탐지하는 STATUS 또는 CI 검사를 추가한다.
5. Phase 0 산출물을 대상으로 `HS-REVIEW-P00-R1`을 수행한다.
6. 실패 위키 등록 절차를 Skill 후보로 평가하고, 기준을 충족하면 `failure-case-wiki` Skill의 최소 버전을 제작한다.
7. 공통 완료 조건을 검사하는 Harness 후보를 평가하고, 기준을 충족하면 `di-completion-check` Harness의 최소 버전을 제작한다.

### 산출물

- `docs/verification/phase-gates/_template.md`
- `docs/verification/phase-gates/HS-REVIEW-P00-R1.md`
- `Harness manifest 계약`
- `Skill manifest 계약`
- `HS-GATE 누락 검사`
- 판정에 따라 생성된 초기 Harness·Skill

### 검증

- [ ] Harness와 Skill을 독립적으로 판정할 수 있다.
- [ ] 제작, 확장, 보류, 부적합 결과를 모두 표현할 수 있다.
- [ ] 보류 항목에 해소 조건과 재판정 Phase가 존재한다.
- [ ] 제작된 초기 Harness는 PASS/FAIL을 기계적으로 반환한다.
- [ ] 제작된 초기 Skill은 입력, 절차, 산출물, 금지사항, 실패 처리를 포함한다.
- [ ] HS-GATE가 없으면 다음 Phase 진입이 차단된다.

### 금지사항

- 반복 가능성만으로 자동화 가치를 과장하지 않는다.
- 판정 기준이 불명확한 검사를 Harness로 포장하지 않는다.
- 정책과 안전 경계가 확정되지 않은 절차를 Skill로 고정하지 않는다.
- 문서만 만들고 실제 적용·검증 없이 `제작 완료`로 판정하지 않는다.


## DI-00-05 — 다중 AI 모델 인수인계 기반 생성

`선행: DI-00-04`

### 지시

1. `docs/handoff/` 구조와 HANDOFF, WORKSTATE, CONTEXT-MANIFEST schema·템플릿을 생성한다.
2. 중요 판단용 ADR 템플릿과 세션 종료 기록 템플릿을 생성한다.
3. 현재 Phase 0 산출물의 경로·버전·hash를 Context Manifest에 등록한다.
4. `handoff-integrity` Harness 최소 버전을 제작한다.
5. `prepare-model-handoff` Skill 최소 버전을 제작한다.
6. 기존 작업에 참여하지 않았다고 가정한 새 실행 세션에서 Context Manifest만 읽고 현재 상태, 완료 산출물, 다음 작업을 재구성한다.
7. 재구성 결과와 실제 상태가 다르면 `handoff_mismatch` 실패 사례를 등록하고 계약을 수정한다.
8. 인수인계 후보를 `HS-REVIEW-P00-R2`에 반영한다. 최종 Phase 게이트로 판정하지 않는다.

### 산출물

- `docs/handoff/HANDOFF.md`
- `docs/handoff/WORKSTATE.json`
- `docs/handoff/CONTEXT-MANIFEST.json`
- `docs/handoff/decisions/_template.md`
- `docs/handoff/sessions/_template.md`
- `harnesses/handoff-integrity/`
- `skills/prepare-model-handoff/`
- `docs/verification/phase-gates/HS-REVIEW-P00-R2.md`

### 검증

- [ ] 이전 대화 없이 현재 Phase, DI, branch, 완료·미완료 작업을 재구성할 수 있다.
- [ ] 변경 파일 hash, commit, 테스트 기록의 불일치를 Harness가 탐지한다.
- [ ] blocker가 존재하면 다음 DI 진행을 차단한다.
- [ ] 중요 결정이 ADR 없이 HANDOFF 요약에만 존재하는 경우 실패한다.
- [ ] 새 실행자가 지정된 smoke test를 재현하고 같은 결과를 얻는다.
- [ ] HANDOFF Projection을 반복 생성해도 형식과 참조가 깨지지 않는다.

### 금지사항

- 채팅 기록이나 특정 모델의 기억을 필수 입력으로 사용하지 않는다.
- 미검증 요약을 완료 사실로 기록하지 않는다.
- 부분 구현과 임시 파일을 숨기지 않는다.
- 특정 AI 모델만 이해할 수 있는 프롬프트나 암묵적 약어에 의존하지 않는다.
- handoff-integrity FAIL 상태에서 Phase 1을 시작하지 않는다.


## DI-00-06 — 컨텍스트 절약 기반 생성

`선행: DI-00-05`

### 지시

1. `docs/context/` 구조와 Context Pack·Receipt schema 및 템플릿을 생성한다.
2. 현재 Phase, WP, DI, blocker, 다음 작업만 담는 `RUNTIME-INDEX.md`를 생성한다.
3. Phase 0과 Phase 1 첫 DI를 위한 Context Pack을 작성한다.
4. `context-pack-integrity` Harness 최소 버전을 제작한다.
5. `build-di-context-pack`과 `compact-phase-context` Skill 최소 버전을 제작한다.
6. 기존 HANDOFF와 CONTEXT-MANIFEST가 전체 문서를 중복 복제하지 않고 Context Pack ID와 authoritative 원본을 참조하도록 갱신한다.
7. 새 실행 세션에서 L0와 L1만 읽고 현재 상태, 안전 경계, 다음 작업을 설명한 뒤 지정 smoke test를 실행한다.
8. 불충분한 경우 누락 정보를 분류하고 Context Pack을 보완하되 전체 문서를 기본 입력으로 추가하지 않는다.
9. 컨텍스트 후보를 `HS-REVIEW-P00-R3`에 반영한다. 최종 Phase 게이트로 판정하지 않는다.

### 산출물

- `docs/context/RUNTIME-INDEX.md`
- `docs/context/schemas/context-pack.schema.json`
- `docs/context/schemas/context-receipt.schema.json`
- `docs/context/packs/CTX-DI-00-06.json`
- `docs/context/packs/CTX-DI-01-01.json`
- `harnesses/context-pack-integrity/`
- `skills/build-di-context-pack/`
- `skills/compact-phase-context/`
- 갱신된 HANDOFF Projection, Context Pack, `HS-REVIEW-P00-R3`

### 검증

- [ ] L0와 L1만으로 Phase 1 첫 DI의 목표, 입력, 금지사항, 완료 기준을 설명할 수 있다.
- [ ] 잘못된 hash, 누락 절 ID, stale summary를 Harness가 탐지한다.
- [ ] Context Receipt에 실제 조회한 문서와 추가 조회 이유가 남는다.
- [ ] 큰 로그와 diff가 기본 Context Pack에 포함되지 않는다.
- [ ] 같은 사실의 장문 복제가 HANDOFF, STATUS, 위키에 동시에 존재하지 않는다.
- [ ] Context Budget 초과 시 자동 PASS하지 않고 이유 또는 DI 분할 결정을 요구한다.

### 금지사항

- 컨텍스트 절약을 이유로 미해결 실패, 권한 조건, 안전 경계를 생략하지 않는다.
- 요약본을 authoritative state나 Evidence로 취급하지 않는다.
- 모든 문서를 하나의 거대한 요약 파일로 합치지 않는다.
- 특정 모델의 최대 토큰 수를 계약의 전제로 삼지 않는다.
- context-pack-integrity FAIL 상태에서 Phase 1을 시작하지 않는다.

## DI-00-07 — Phase 0 최종 경계 판정

`선행: DI-00-06`

### DI 유형

`verification`

### 지시

1. `HS-REVIEW-P00-R1~R3`의 후보와 보류 조건을 통합한다.
2. `handoff-integrity --mode full`, `context-pack-integrity`, secret scanner를 실행한다.
3. 기존 작업에 참여하지 않은 실행자가 L0·L1만으로 현재 상태를 재구성하고 Phase 1 smoke test를 수행한다.
4. Phase 0의 Harness·Skill 후보를 예산·gate-critical 규칙에 따라 최종 판정한다.
5. 즉시 제작 필수 항목만 구현·검증하고, 기한부 제작은 목표 Phase와 담당 WP를 기록한다.
6. 최종 `HS-GATE-P00.md`를 한 번 생성한다.
7. PASS일 때만 WORKSTATE의 다음 Phase 진입 상태를 갱신한다.

### 산출물

- `docs/verification/phase-gates/HS-GATE-P00.md`
- `docs/handoff/snapshots/SNAPSHOT-P00-WP00-DI00-07.json`
- Phase 1 진입용 WORKSTATE 및 Projection

### 검증

- [ ] Phase 0에서 최종 HS-GATE가 정확히 한 번만 생성된다.
- [ ] WORKSTATE와 Projection의 상태가 일치한다.
- [ ] hash 자기 참조와 순환 참조가 없다.
- [ ] 독립 재개 시험과 full integrity가 PASS한다.
- [ ] 다음 Phase에 필요한 gate-critical Harness·Skill만 즉시 제작됐다.

### 금지사항

- HS-REVIEW를 최종 게이트 PASS로 간주하지 않는다.
- unresolved blocker가 있는 상태에서 PASS하지 않는다.
- HANDOFF Projection을 직접 편집해 불일치를 숨기지 않는다.

# Phase 1. 저장 구조와 공통 계약 고정

## DI-01-01 — Runtime 데이터 루트와 디렉터리 정의

`선행: DI-00-07, HS-GATE-P00`

### 지시

1. Runtime 데이터 루트를 설정 값 하나로 정의한다.
2. state, transitions, evidence, idempotency, model-calls, sandboxes, restore-points, dead-letter 디렉터리를 생성하는 초기화 함수를 구현한다.
3. 초기화는 여러 번 실행해도 동일 결과가 되도록 한다.

### 산출물

- `RuntimePaths`
- `저장 구조 문서`
- `초기화 테스트`

### 검증

- [ ] 모든 파일이 단일 루트 아래 생성된다.
- [ ] 초기화 재실행 시 오류나 중복이 없다.

### 금지사항

- 경로를 각 모듈에 하드코딩하지 않는다.

## DI-01-02 — 임시 파일과 atomic replace 규칙 구현

`선행: DI-01-01`

### 지시

1. 동일 볼륨에서 임시 파일을 생성한다.
2. flush와 fsync 가능 범위를 정의한다.
3. 완료 파일로 atomic rename/replace하는 유틸리티를 구현한다.
4. 실패 시 임시 파일 정리 규칙을 구현한다.

### 산출물

- `AtomicFileWriter`
- `강제 종료 테스트`

### 검증

- [ ] 부분 파일이 완료 파일로 오인되지 않는다.
- [ ] 교체 실패 시 기존 완료 파일이 보존된다.

### 금지사항

- 다른 볼륨 간 rename에 원자성을 가정하지 않는다.

## DI-01-03 — Windows 경로 정규화 구현

`선행: DI-01-01`

### 지시

1. separator, trailing separator, drive letter, 대소문자를 정규화한다.
2. realpath를 구하고 junction/symlink 해석 결과를 사용한다.
3. 상대 경로와 절대 경로 검증 함수를 분리한다.

### 산출물

- `PathNormalizer`
- `경로 충돌 fixture`

### 검증

- [ ] 표기만 다른 같은 경로가 동일 key가 된다.
- [ ] `..`로 루트 밖을 가리키는 경로가 거부된다.

### 금지사항

- 문자열 lower-case만으로 동일 경로를 판정하지 않는다.

## DI-01-04 — ID 접두사와 생성기 구현

`선행: DI-01-01`

### 지시

1. Aggregate별 접두사를 중앙 enum으로 정의한다.
2. 결정적 ID와 비결정적 ID API를 분리한다.
3. 결정적 ID는 canonical input hash에서 생성한다.
4. 충돌과 잘못된 접두사를 검증한다.

### 산출물

- `IdFactory`
- `ID 계약 테스트`

### 검증

- [ ] 같은 입력은 같은 ID를 만든다.
- [ ] 서로 다른 Aggregate 접두사를 혼용할 수 없다.

### 금지사항

- timestamp만으로 결정적 ID를 만들지 않는다.

## DI-01-05 — Canonical serialization과 hash 구현

`선행: DI-01-04`

### 지시

1. JSON key 정렬, 숫자·null·문자열 표기 규칙을 확정한다.
2. canonical byte serialization을 구현한다.
3. content hash와 normalized input hash API를 분리한다.
4. field order가 다른 fixture로 동일 hash를 검증한다.

### 산출물

- `CanonicalSerializer`
- `CanonicalHasher`
- `hash fixture`

### 검증

- [ ] 같은 의미의 JSON은 동일 hash를 가진다.
- [ ] hash 알고리즘과 schemaVersion이 문서화된다.

### 금지사항

- 일반 pretty JSON 문자열을 그대로 hash하지 않는다.

## DI-01-06 — Aggregate version과 transition sequence 구현

`선행: DI-01-05`

### 지시

1. Aggregate version을 0 또는 1에서 시작하는 규칙을 하나로 확정한다.
2. 성공한 transition마다 정확히 1 증가하도록 한다.
3. timestamp와 무관한 transition sequence를 정의한다.
4. overflow와 잘못된 version 감소를 차단한다.

### 산출물

- `Version 타입`
- `순서 판정 테스트`

### 검증

- [ ] 동일 timestamp에서도 version으로 순서가 결정된다.
- [ ] version 건너뛰기와 감소가 거부된다.

### 금지사항

- wall clock을 authoritative ordering으로 사용하지 않는다.

## DI-02-01 — 중앙 enum과 공통 타입 정의

`선행: DI-01-06`

### 지시

1. Revision, Job, Approval, Artifact, Transition, failureClass enum을 중앙 정의한다.
2. ID, version, hash, relative path를 별도 타입으로 정의한다.
3. 직렬화 이름을 고정한다.

### 산출물

- `공통 계약 모듈`

### 검증

- [ ] 스키마와 런타임 enum 값이 일치한다.

### 금지사항

- 모듈마다 같은 enum을 중복 정의하지 않는다.

## DI-02-02 — 핵심 Aggregate 스키마 작성

`선행: DI-02-01`

### 지시

1. Project, Blueprint, WorkItem, Revision, Job 스키마를 작성한다.
2. 각 스키마에 schemaVersion, id, version을 넣는다.
3. unknown field 정책과 필수 필드를 명시한다.

### 산출물

- `5개 JSON schema`
- `정상·비정상 fixture`

### 검증

- [ ] 필수 필드 누락과 잘못된 enum이 거부된다.

### 금지사항

- Draft와 Active Blueprint를 같은 상태로 취급하지 않는다.

## DI-02-03 — 통제 Aggregate 스키마 작성

`선행: DI-02-01`

### 지시

1. ApprovalRequest, Decision, Artifact, TransitionRecord, EvidenceEvent 스키마를 작성한다.
2. subject/version/base/policy 결합 필드를 포함한다.
3. 불변 데이터와 변경 가능한 상태 필드를 구분한다.

### 산출물

- `5개 JSON schema`
- `fixture`

### 검증

- [ ] stale 판정에 필요한 필드가 모두 존재한다.
- [ ] sealed Artifact 변경을 표현하는 스키마가 없다.

### 금지사항

- Approval subject를 단순 문자열 설명으로만 저장하지 않는다.

## DI-02-04 — 실행 Manifest 스키마 작성

`선행: DI-02-01`

### 지시

1. ShadowRun manifest와 Commit manifest 스키마를 작성한다.
2. 입력 hash, base fingerprint, actor/session, policy version, capability, artifact refs를 포함한다.
3. 상대 경로만 허용한다.

### 산출물

- `Manifest schema 2개`
- `fixture`

### 검증

- [ ] 절대 경로와 루트 이탈 경로가 거부된다.

### 금지사항

- 실행 환경 capability를 단일 과장된 level 값으로만 기록하지 않는다.

## DI-02-05 — Schema validator 구현

`선행: DI-02-02, DI-02-03, DI-02-04`

### 지시

1. schemaVersion으로 validator를 선택한다.
2. UTF-8 without BOM을 검증한다.
3. 파일 크기 제한과 unknown field 정책을 적용한다.
4. 오류 위치와 원인을 구조화해 반환한다.

### 산출물

- `SchemaValidator`
- `검증 CLI 또는 테스트 helper`

### 검증

- [ ] 모든 fixture가 기대 결과와 일치한다.
- [ ] 운영자가 원인을 식별 가능한 오류가 나온다.

### 금지사항

- 검증 실패 데이터를 자동 보정해 저장하지 않는다.

# Phase 2. 상태 소유권과 전이 엔진

## DI-03-01 — Revision 상태 enum과 terminal 판정 구현

`선행: DI-02-05`

### 지시

1. Revision lifecycle enum을 구현한다.
2. completed, failed, superseded, cancelled 등 terminal 상태를 중앙 함수로 판정한다.
3. WorkItem에 실행 상태 필드가 존재하지 않는지 검사한다.

### 산출물

- `Revision 상태 모듈`

### 검증

- [ ] terminal 상태에서 실행 Job 생성이 거부된다.

### 금지사항

- WorkItem status로 Revision 실행 단계를 대체하지 않는다.

## DI-03-02 — 허용 전이 표 구현

`선행: DI-03-01`

### 지시

1. 허용 transition table을 데이터로 정의한다.
2. transition reason 요구 규칙을 정의한다.
3. 전이 함수가 표 밖 이동을 거부하도록 한다.

### 산출물

- `RevisionStateMachine`
- `전이 표 테스트`

### 검증

- [ ] 모든 허용 전이와 주요 금지 전이에 테스트가 있다.

### 금지사항

- 상태 값을 직접 대입하는 공개 API를 제공하지 않는다.

## DI-03-03 — WorkItem activeRevision 관리 구현

`선행: DI-03-02`

### 지시

1. activeRevisionIds 추가·제거 명령을 구현한다.
2. terminal Revision을 active 목록에서 제거하는 규칙을 구현한다.
3. 중복 ID와 다른 WorkItem 소속 Revision을 거부한다.

### 산출물

- `WorkItem revision 관리 명령`

### 검증

- [ ] activeRevisionIds가 실제 Revision 소유권과 일치한다.

### 금지사항

- 배열을 직접 덮어쓰는 저장 API를 노출하지 않는다.

## DI-03-04 — Revision 선택 정책 구현

`선행: DI-03-03`

### 지시

1. explicit_human, authority_ranked, deterministic_score 정책을 구현한다.
2. 선택 명령은 Decision ID를 요구한다.
3. stale Revision과 terminal 부적합 Revision 선택을 차단한다.

### 산출물

- `SelectRevisionCommand`
- `RevisionSelectionDecision`

### 검증

- [ ] 동시에 selectedRevisionId는 최대 하나다.
- [ ] Decision 없이 선택 변경이 불가능하다.

### 금지사항

- 마지막 쓰기 승자 방식으로 선택하지 않는다.

## DI-04-01 — TransitionRecord 준비 단계 구현

`선행: DI-02-05`

### 지시

1. transitionId, aggregateId, expectedVersion, nextVersion, statePatch, evidenceEvents를 생성한다.
2. prepared 파일을 AtomicFileWriter로 저장한다.
3. 같은 transitionId 재요청 시 기존 내용을 비교한다.

### 산출물

- `TransitionPreparer`

### 검증

- [ ] prepared 기록 전에는 state가 바뀌지 않는다.
- [ ] 동일 ID의 다른 내용이 충돌로 처리된다.

### 금지사항

- prepared 없이 state를 직접 변경하지 않는다.

## DI-04-02 — CAS 기반 state 적용 구현

`선행: DI-04-01`

### 지시

1. 현재 Aggregate version을 읽는다.
2. expectedVersion과 비교한다.
3. 일치할 때만 새 state 파일을 atomic replace한다.
4. state_applied 단계와 결과 hash를 기록한다.

### 산출물

- `AggregateStore CAS API`

### 검증

- [ ] CAS 불일치 시 state와 Evidence 모두 바뀌지 않는다.

### 금지사항

- CAS 충돌을 자동 덮어쓰지 않는다.

## DI-04-03 — Evidence append와 중복 제거 구현

`선행: DI-04-01`

### 지시

1. single-writer append 경계를 구현한다.
2. eventId 인덱스 또는 중복 검사 전략을 구현한다.
3. append 후 durable flush 범위를 정의한다.
4. evidence_applied 상태를 기록한다.

### 산출물

- `EvidenceWriter`
- `eventId 중복 테스트`

### 검증

- [ ] 동일 eventId가 두 줄 이상 기록되지 않는다.

### 금지사항

- 여러 writer가 JSONL에 직접 append하지 않는다.

## DI-04-04 — Transition commit 완료 처리 구현

`선행: DI-04-02, DI-04-03`

### 지시

1. state와 Evidence 결과를 최종 검증한다.
2. TransitionRecord를 committed로 이동하거나 상태 전환한다.
3. 결과 state hash와 event IDs를 고정한다.

### 산출물

- `TransitionWriter 전체 흐름`

### 검증

- [ ] 성공한 transition이 prepared에 남지 않는다.

### 금지사항

- 부분 성공을 committed로 표시하지 않는다.

## DI-05-01 — Prepared transition 스캔 구현

`선행: DI-04-04`

### 지시

1. 시작 시 prepared 디렉터리를 열거한다.
2. transition sequence와 aggregate별로 안정적 순서를 만든다.
3. 이미 committed된 transition을 식별한다.

### 산출물

- `RecoveryScanner scan 단계`

### 검증

- [ ] 같은 입력 디렉터리에서 항상 같은 처리 순서가 나온다.

### 금지사항

- timestamp 순서만 신뢰하지 않는다.

## DI-05-02 — State 미적용 전이 복구 구현

`선행: DI-05-01`

### 지시

1. 현재 version이 expectedVersion이면 state 적용부터 재개한다.
2. 현재 version이 nextVersion이고 hash가 일치하면 다음 단계로 이동한다.
3. 그 외는 충돌로 분류한다.

### 산출물

- `Recovery state reconciler`

### 검증

- [ ] prepared 직후 종료 시 정확히 한 번 반영된다.

### 금지사항

- 불일치 state를 임의 덮어쓰지 않는다.

## DI-05-03 — Evidence 미적용 전이 복구 구현

`선행: DI-05-02`

### 지시

1. state_applied이나 Evidence가 없는 상태를 탐지한다.
2. eventId별 존재 여부를 검사한다.
3. 없는 이벤트만 append하고 결과를 검증한다.

### 산출물

- `Recovery evidence reconciler`

### 검증

- [ ] state 적용 후 종료 시 이벤트가 한 번만 추가된다.

### 금지사항

- 전체 이벤트 묶음을 무조건 재append하지 않는다.

## DI-05-04 — 충돌·aborted·복구 Evidence 구현

`선행: DI-05-03`

### 지시

1. 복구 불가능한 충돌을 aborted로 이동한다.
2. 원인, 현재 version/hash, 기대 version/hash를 진단 파일에 기록한다.
3. 복구 성공과 실패 모두 새 Evidence를 생성한다.

### 산출물

- `Recovery diagnostic`
- `aborted transition 처리`

### 검증

- [ ] 운영자가 파일 직접 수정 없이 원인을 확인할 수 있다.

### 금지사항

- 충돌을 조용히 무시하지 않는다.

## DI-05-05 — 전이 장애 주입 테스트 완성

`선행: DI-05-04`

### 지시

1. prepared 직후부터 evidence_applied 직후까지 각 지점 강제 종료를 주입한다.
2. 같은 transition을 두 번 복구한다.
3. CAS 충돌과 event 중복을 함께 검증한다.

### 산출물

- `장애 주입 테스트 보고서`

### 검증

- [ ] 모든 종료 지점에서 최종 state와 Evidence가 정확히 한 번 반영된다.

### 금지사항

- happy path 테스트만으로 완료 처리하지 않는다.

# Phase 3. Job Queue, Lease, Retry

## DI-06-01 — Job 명령 API와 상태 전이 구현

`선행: DI-04-04`

### 지시

1. enqueue, claim, renew, complete, fail, requeue, dead-letter 명령을 정의한다.
2. Job 상태 전이 표를 구현한다.
3. 직접 CRUD update API를 차단한다.

### 산출물

- `QueueStore 명령 인터페이스`

### 검증

- [ ] 허용되지 않은 Job 상태 전이가 거부된다.

### 금지사항

- 외부에서 Job JSON을 직접 덮어쓰지 않는다.

## DI-06-02 — Runnable Job 조회와 정렬 구현

`선행: DI-06-01`

### 지시

1. queued이며 의존성이 충족된 Job만 조회한다.
2. priority 후 생성 sequence로 안정 정렬한다.
3. terminal Job과 미래 retryAt Job을 제외한다.

### 산출물

- `Runnable query`

### 검증

- [ ] 동일 입력에서 정렬 결과가 결정적이다.

### 금지사항

- wall clock 동률을 임의 순서로 처리하지 않는다.

## DI-06-03 — Job 완료와 resultRef 검증 구현

`선행: DI-06-01`

### 지시

1. complete 시 result manifest 존재와 schema를 검증한다.
2. owner token과 lease 유효성을 검사한다.
3. 성공 transition과 Evidence를 생성한다.

### 산출물

- `completeJob`

### 검증

- [ ] resultRef 없는 succeeded Job이 존재하지 않는다.

### 금지사항

- Handler 반환값만 믿고 succeeded 처리하지 않는다.

## DI-06-04 — Dead-letter 저장 구현

`선행: DI-06-01`

### 지시

1. terminal failure 사유와 마지막 입력·attempt를 구조화한다.
2. 원본 Job ID를 유지한다.
3. 수동 재처리 시 새 Job을 만들고 관계를 기록한다.

### 산출물

- `DeadLetterStore`

### 검증

- [ ] 사유 없는 dead-letter가 없다.

### 금지사항

- dead-letter Job을 동일 ID로 되살리지 않는다.

## DI-06-05 — Lease claim CAS 구현

`선행: DI-06-01`

### 지시

1. claim 시 owner token과 expiry를 설정한다.
2. version CAS로 동시 claim을 제어한다.
3. 두 worker 경합 테스트를 구현한다.

### 산출물

- `LeaseManager claim`

### 검증

- [ ] 동시 claim에서 정확히 하나만 성공한다.

### 금지사항

- 파일 존재 여부만으로 lock을 판단하지 않는다.

## DI-06-06 — Lease heartbeat와 expiry 구현

`선행: DI-06-05`

### 지시

1. 정상 owner만 renew할 수 있게 한다.
2. expiry 비교 기준과 clock skew 허용 범위를 정의한다.
3. 만료 owner의 complete를 차단한다.

### 산출물

- `renewLease`
- `expiry 판정`

### 검증

- [ ] 만료 직전 갱신과 만료 직후 거부가 재현 가능하다.

### 금지사항

- 만료된 owner 요청을 관대하게 성공 처리하지 않는다.

## DI-06-07 — Expired Job 재큐잉 구현

`선행: DI-06-06`

### 지시

1. RecoveryScanner가 만료 lease를 찾는다.
2. 현재 result manifest와 idempotency 상태를 확인한다.
3. 재큐잉 transition을 정확히 한 번 수행한다.

### 산출물

- `requeueExpiredJob`

### 검증

- [ ] 만료 후 정확히 하나의 runnable Job 상태가 존재한다.

### 금지사항

- 결과가 이미 완료된 Job을 다시 실행하지 않는다.

## DI-07-01 — FailureClass 판별기 구현

`선행: DI-06-07`

### 지시

1. transient, deterministic, policy_denied, stale_input, invalid_contract, resource_exhausted, sandbox_violation, external_side_effect_unknown, unknown을 정의한다.
2. 예외·결과 코드에서 failureClass로 매핑한다.
3. 매핑 불가 항목은 unknown으로 명시한다.

### 산출물

- `FailureClassifier`
- `분류 fixture`

### 검증

- [ ] 실패가 분류 없이 retry 경로로 들어가지 않는다.

### 금지사항

- unknown을 transient로 간주하지 않는다.

## DI-07-02 — Retry matrix 구현

`선행: DI-07-01`

### 지시

1. failureClass별 retry 여부, maxAttempts, backoff를 표로 정의한다.
2. deterministic과 invalid_contract를 terminal로 처리한다.
3. stale_input은 새 Job/Revision 생성 경로로 보낸다.

### 산출물

- `RetryPolicy`
- `retry matrix 테스트`

### 검증

- [ ] 같은 deterministic 입력이 자동 재시도되지 않는다.

### 금지사항

- 모든 실패에 공통 maxAttempts만 적용하지 않는다.

## DI-07-03 — 외부 부작용 불명 상태 차단 구현

`선행: DI-07-02`

### 지시

1. external_side_effect_unknown 상태를 terminal 운영 검토 대상으로 만든다.
2. 자동 requeue와 자동 Handler 호출을 차단한다.
3. 진단과 수동 reconcile 명령 연결점을 만든다.

### 산출물

- `UnknownSideEffect guard`

### 검증

- [ ] 불명 상태에서 자동 retry가 0회다.

### 금지사항

- 성공 여부를 추측해 succeeded 또는 failed로 확정하지 않는다.

## DI-07-04 — Retry 재실행 테스트 구현

`선행: DI-07-03`

### 지시

1. 각 failureClass의 첫 실패와 재시도 결과를 검증한다.
2. maxAttempts 도달과 dead-letter 이동을 검증한다.
3. 재실행 시 idempotency key 보존 여부를 확인한다.

### 산출물

- `Retry 통합 테스트`

### 검증

- [ ] 무한 retry 경로가 없다.

### 금지사항

- sleep 기반 불안정 테스트만 사용하지 않는다.

# Phase 4. Artifact와 Shadow 실행 기반

## DI-08-01 — Staging Artifact 생성 구현

`선행: DI-02-05`

### 지시

1. Artifact ID와 staging 디렉터리를 생성한다.
2. source Job, Revision, base fingerprint를 기록한다.
3. staging만 파일 추가·수정을 허용한다.

### 산출물

- `ArtifactStore createStaging`

### 검증

- [ ] sealed/rejected 경로에는 staging 쓰기가 불가능하다.

### 금지사항

- 실행 결과를 곧바로 sealed로 만들지 않는다.

## DI-08-02 — Artifact manifest와 content hash 구현

`선행: DI-08-01`

### 지시

1. 파일 트리를 상대 경로 기준으로 정렬한다.
2. 각 파일 hash와 전체 manifest hash를 계산한다.
3. 금지 경로와 symlink 탈출을 검사한다.

### 산출물

- `ArtifactManifest builder`

### 검증

- [ ] 같은 파일 트리는 항상 같은 hash다.

### 금지사항

- mtime을 content hash 입력으로 사용하지 않는다.

## DI-08-03 — Harness 결과 연결과 sealing 구현

`선행: DI-08-02`

### 지시

1. 검증 결과를 Artifact에 연결한다.
2. 성공 조건을 만족하면 atomic하게 sealed로 전환한다.
3. sealed 후 파일 권한과 저장 API를 읽기 전용으로 만든다.

### 산출물

- `sealArtifact`

### 검증

- [ ] provenance 또는 Harness 결과가 없으면 seal이 거부된다.

### 금지사항

- `report.status=completed`만으로 seal하지 않는다.

## DI-08-04 — Artifact reject와 supersedes 구현

`선행: DI-08-03`

### 지시

1. 검증 실패 Artifact를 rejected로 전환한다.
2. 수정 결과는 새 Artifact로 만들고 supersedes 관계를 기록한다.
3. lineage 순환을 차단한다.

### 산출물

- `rejectArtifact`
- `supersedes graph`

### 검증

- [ ] sealed Artifact를 수정하는 경로가 없다.

### 금지사항

- 기존 Artifact ID를 재사용해 내용을 교체하지 않는다.

## DI-09-01 — SnapshotProvider 구현

`선행: DI-08-04`

### 지시

1. base fingerprint를 고정한다.
2. Git worktree 또는 source snapshot은 SandboxManager 전용으로 만든다.
3. Executor용 부분 사본의 원천을 제공한다.

### 산출물

- `SnapshotProvider`

### 검증

- [ ] 실행 중 base 변경이 snapshot 내용에 섞이지 않는다.

### 금지사항

- Executor에 canonical repository 경로를 전달하지 않는다.

## DI-09-02 — WorkspaceFactory 구현

`선행: DI-09-01`

### 지시

1. Executor root를 별도 디렉터리에 생성한다.
2. 필요 파일만 부분 복사한다.
3. `.git`, credential, 비허용 파일을 제외한다.
4. 정리 가능한 workspace ID를 부여한다.

### 산출물

- `WorkspaceFactory`

### 검증

- [ ] Executor root에 `.git`과 credential이 없다.

### 금지사항

- 원본 경로에 직접 작업 디렉터리를 만들지 않는다.

## DI-09-03 — PermissionProfileBuilder 구현

`선행: DI-09-02`

### 지시

1. 환경변수 allowlist를 만든다.
2. 실행 파일 allowlist와 작업 디렉터리 ACL을 정의한다.
3. 시간·메모리·프로세스 수 제한을 구조화한다.
4. 미지원 네트워크 제한은 not_enforced로 기록한다.

### 산출물

- `PermissionProfile`

### 검증

- [ ] 실제 적용 여부가 capability별로 기록된다.

### 금지사항

- 지원하지 않는 통제를 enforced로 표시하지 않는다.

## DI-09-04 — SandboxLauncher 구현

`선행: DI-09-03`

### 지시

1. 제한된 환경으로 프로세스를 시작한다.
2. stdout/stderr 크기 제한과 timeout을 적용한다.
3. 종료 코드, resource usage, violation을 수집한다.

### 산출물

- `SandboxLauncher`

### 검증

- [ ] 프로세스 제한 위반이 구조화된 실패로 나온다.

### 금지사항

- 무제한 자식 프로세스를 허용하지 않는다.

## DI-09-05 — ArtifactCollector 구현

`선행: DI-09-04`

### 지시

1. Executor root의 실제 변경 파일을 수집한다.
2. base와 비교해 diff manifest를 만든다.
3. `_task` 보고서와 실제 파일 결과가 일치하는지 검증한다.
4. staging Artifact로 복사한다.

### 산출물

- `ArtifactCollector`

### 검증

- [ ] 보고서가 completed여도 실제 결과가 없으면 성공하지 않는다.

### 금지사항

- Executor 자기보고만 신뢰하지 않는다.

## DI-09-06 — SandboxCleaner 구현

`선행: DI-09-05`

### 지시

1. 성공·실패·보존 정책별 정리 규칙을 구현한다.
2. 잠긴 파일과 잔여 프로세스를 처리한다.
3. 정리 실패를 Evidence와 운영 대상에 남긴다.

### 산출물

- `SandboxCleaner`

### 검증

- [ ] 정리 실패가 조용히 무시되지 않는다.

### 금지사항

- 조사 대상 workspace를 무조건 삭제하지 않는다.

## DI-09-07 — ShadowRun manifest 구현

`선행: DI-09-05`

### 지시

1. inputHash, baseFingerprint, actor/session, permissions, capabilities, measurements, resourceUsage, outputHash, artifactRefs를 기록한다.
2. schema validator를 적용한다.
3. provenance 누락 시 sealing을 차단한다.

### 산출물

- `Shadow Contract Lite 구현`

### 검증

- [ ] 모든 Shadow 결과가 입력과 실행 환경까지 추적된다.

### 금지사항

- Shadow manifest에서 canonical state patch를 허용하지 않는다.

## DI-09-08 — Shadow 비부작용 통합 테스트

`선행: DI-09-07`

### 지시

1. 원본 경로 접근, `.git` 접근, credential 접근, 비허용 executable 실행을 시도한다.
2. 네트워크가 미통제이면 manifest에 정확히 기록되는지 확인한다.
3. Shadow 실행 후 Canonical State와 원본 hash가 불변인지 확인한다.

### 산출물

- `Sandbox/Shadow 검증 보고서`

### 검증

- [ ] 승인되지 않은 원본 변경이 0이다.

### 금지사항

- 통제되지 않은 기능을 테스트에서 생략하지 않는다.

# Phase 5. LegacyCycleJob 이관과 Idempotency

## DI-10-01 — Legacy input adapter 구현

`선행: DI-06-07, DI-09-08`

### 지시

1. Job·Blueprint·Revision 입력을 legacy handler 입력으로 변환한다.
2. 정규화된 입력 manifest를 저장한다.
3. 원본 경로와 직접 쓰기 권한을 제거한다.

### 산출물

- `LegacyInputAdapter`

### 검증

- [ ] 동일 Runtime 입력이 동일 normalized input을 만든다.

### 금지사항

- legacy handler에 Canonical State 객체를 직접 넘기지 않는다.

## DI-10-02 — Legacy handler sandbox 실행 구현

`선행: DI-10-01`

### 지시

1. Job claim 후 최신 정책을 재검증한다.
2. sandbox를 생성하고 handler를 실행한다.
3. timeout, violation, 비정상 종료를 failureClass로 변환한다.

### 산출물

- `LegacyCycleJob 실행기`

### 검증

- [ ] legacy handler가 원본에서 실행되지 않는다.

### 금지사항

- 정책 재검증 전에 모델 호출이나 파일 변경을 시작하지 않는다.

## DI-10-03 — Legacy output adapter 구현

`선행: DI-10-02`

### 지시

1. proposal, review, run-log, 생성 파일을 추출한다.
2. legacy 내부 상태 변경 시도를 탐지한다.
3. 구조화 result manifest를 생성한다.

### 산출물

- `LegacyOutputAdapter`
- `result manifest`

### 검증

- [ ] 부분 결과의 상태가 명확히 표시된다.

### 금지사항

- 누락된 필드를 임의 기본값으로 성공 처리하지 않는다.

## DI-10-04 — Artifact/Harness/Job 완료 흐름 연결

`선행: DI-10-03`

### 지시

1. output을 staging Artifact로 만든다.
2. Harness를 실행하고 seal 또는 reject한다.
3. TransitionRecord로 Runtime state와 Evidence를 반영한다.
4. 유효한 resultRef가 있을 때만 Job을 완료한다.

### 산출물

- `LegacyCycleJob end-to-end`

### 검증

- [ ] 실패 위치별 state가 복구 가능하다.

### 금지사항

- Artifact seal 전에 Job succeeded 처리하지 않는다.

## DI-11-01 — normalizedInputHash와 cycleKey 구현

`선행: DI-10-04`

### 지시

1. 정규화 대상 필드와 제외 필드를 문서화한다.
2. projectId, blueprintVersion, baseFingerprint, requestedAction, policyVersionAtExecution, normalizedInputHash로 cycleKey를 계산한다.
3. hash fixture를 만든다.

### 산출물

- `CycleKeyCalculator`

### 검증

- [ ] 의미가 같은 입력은 같은 cycleKey다.

### 금지사항

- timestamp, 임시 경로를 cycleKey에 넣지 않는다.

## DI-11-02 — Idempotency reservation 구현

`선행: DI-11-01`

### 지시

1. reserved, running, completed, failed_retriable, failed_terminal 상태를 정의한다.
2. cycleKey 단위 CAS reservation을 구현한다.
3. completed 결과는 기존 resultRef를 반환한다.

### 산출물

- `IdempotencyStore`

### 검증

- [ ] 동시 동일 cycleKey 요청 중 하나만 handler를 시작한다.

### 금지사항

- reservation 없이 handler를 호출하지 않는다.

## DI-11-03 — 재개와 terminal 처리 구현

`선행: DI-11-02`

### 지시

1. failed_retriable의 재개 기준점을 기록한다.
2. failed_terminal 자동 재실행을 차단한다.
3. 부분 Artifact와 model call 존재 여부를 확인해 재사용한다.

### 산출물

- `Idempotent resume flow`

### 검증

- [ ] 재개가 이미 끝난 단계를 반복하지 않는다.

### 금지사항

- 실패마다 처음부터 무조건 재실행하지 않는다.

## DI-11-04 — ModelCallKey와 캐시 구현

`선행: DI-11-01`

### 지시

1. provider, model, parameters, normalizedPromptHash, context hash, experimentNonce로 key를 만든다.
2. 호출 전 cache lookup을 수행한다.
3. 응답, usage, finish status, raw provenance를 저장한다.

### 산출물

- `ModelCallCache`

### 검증

- [ ] 동일 key는 기존 응답을 재사용한다.

### 금지사항

- 모델 응답 텍스트만 저장하고 요청 조건을 버리지 않는다.

## DI-11-05 — 불완전 모델 호출 복구 구현

`선행: DI-11-04`

### 지시

1. requested, response_received, persisted, failed 상태를 정의한다.
2. 응답 저장 후 Job 종료 시 persisted 결과를 재사용한다.
3. 성공 여부가 불명확한 원격 호출은 정책에 따라 운영 검토로 보낸다.

### 산출물

- `Model call recovery`

### 검증

- [ ] 응답 persisted 이후 재호출이 0회다.

### 금지사항

- 불명 원격 호출을 자동으로 다시 보내지 않는다.

## DI-11-06 — Idempotency 반복 테스트 구현

`선행: DI-11-05, DI-11-03`

### 지시

1. 동일 cycleKey를 10회와 100회 반복한다.
2. proposal, review, Artifact, Evidence, model call 개수를 검증한다.
3. 동시 요청과 강제 종료를 섞는다.

### 산출물

- `Idempotency 시험 보고서`

### 검증

- [ ] 결과 Aggregate 수가 1회 실행과 동일하다.

### 금지사항

- 로그 문자열 비교만으로 중복 여부를 판단하지 않는다.

## DI-12-01 — G1 정상·재실행 시나리오 실행

`선행: DI-11-06, DI-05-05`

### 지시

1. 정상 cycle 1회와 즉시 재실행을 수행한다.
2. 모든 결과 ID와 hash를 비교한다.
3. 중복 카운트를 기록한다.

### 산출물

- `G1 시나리오 A 보고`

### 검증

- [ ] proposal/review/artifact/event 중복이 0이다.

### 금지사항

- 일부 항목만 표본 확인하지 않는다.

## DI-12-02 — G1 강제 종료 시나리오 실행

`선행: DI-12-01`

### 지시

1. 모델 응답 저장 후, Artifact 생성 후, state 적용 후, Evidence 적용 후 종료를 각각 주입한다.
2. RecoveryScanner 후 동일 cycleKey를 재실행한다.
3. 최종 lineage와 수량을 비교한다.

### 산출물

- `G1 crash matrix`

### 검증

- [ ] prepared transition 복구 성공률이 100%다.

### 금지사항

- 한 종료 지점 성공으로 전체를 통과시키지 않는다.

## DI-12-03 — G1 위반·계약 오류 시나리오 실행

`선행: DI-12-02`

### 지시

1. 원본 변경 시도와 invalid contract 입력을 실행한다.
2. sandbox_violation 및 invalid_contract 분류를 확인한다.
3. 원본 hash 불변을 확인한다.

### 산출물

- `G1 negative report`

### 검증

- [ ] 원본 repository 직접 변경이 0이다.

### 금지사항

- 위반을 단순 process exit code로만 기록하지 않는다.

## DI-12-04 — G1 판정과 범위 잠금

`선행: DI-12-03`

### 지시

1. G1 지표를 표로 집계한다.
2. 실패 항목이 있으면 WP-13 시작 차단 상태를 STATUS.md에 기록한다.
3. 전체 통과 시 검증 문서를 승인하고 G1 통과를 Evidence로 남긴다.

### 산출물

- `docs/verification/wp-12-g1-gate.md`

### 검증

- [ ] 모든 G1 기준이 통과 상태다.

### 금지사항

- 조건부 통과로 Scheduler를 활성화하지 않는다.

# Phase 6. 제한된 Scheduler 자동화

## DI-13-01 — PriorityPolicy 구현

`선행: DI-12-04`

### 지시

1. runnable 후보의 priority 계산을 독립 모듈로 구현한다.
2. 동률 tie-breaker를 deterministic sequence로 정의한다.
3. 정책 버전을 결과에 기록한다.

### 산출물

- `PriorityPolicy`

### 검증

- [ ] 동일 후보 집합은 동일 순서를 만든다.

### 금지사항

- Scheduler 내부 if문에 우선순위 규칙을 숨기지 않는다.

## DI-13-02 — Resource key derivation 구현

`선행: DI-01-03`

### 지시

1. repository_commit, model_endpoint, workspace_dir key 정규화 규칙을 구현한다.
2. endpoint URL, model, runtime instance를 포함한다.
3. 경로 case/junction 충돌 fixture를 작성한다.

### 산출물

- `ResourceKeyFactory`

### 검증

- [ ] 동일 실제 자원이 표기 차이로 다른 key가 되지 않는다.

### 금지사항

- 원시 사용자 문자열을 resource key로 쓰지 않는다.

## DI-13-03 — ResourceArbiter와 lease 구현

`선행: DI-13-02`

### 지시

1. resource claim CAS와 owner token을 구현한다.
2. 복수 resource의 정렬된 획득 순서를 정의한다.
3. 부분 획득 실패 시 이미 얻은 lease를 해제한다.

### 산출물

- `ResourceArbiter`

### 검증

- [ ] 같은 repository Commit Job 두 개가 동시 실행되지 않는다.

### 금지사항

- 교착 가능 순서로 resource를 획득하지 않는다.

## DI-13-04 — JobDispatcher 구현

`선행: DI-13-01, DI-13-03`

### 지시

1. Job type별 handler registry를 구현한다.
2. dispatcher는 handler 내부 상태를 알지 않게 한다.
3. claim과 resource 확보 후에만 handler를 호출한다.

### 산출물

- `JobDispatcher`

### 검증

- [ ] 미등록 handler가 명확한 terminal 오류가 된다.

### 금지사항

- dispatcher가 Aggregate patch를 직접 만들지 않는다.

## DI-13-05 — RuntimePulseService 구현

`선행: DI-13-04`

### 지시

1. RecoveryScanner를 먼저 실행한다.
2. 한 tick에 후보 조회·claim·dispatch를 최대 1건 수행한다.
3. owner token과 종료 signal을 처리한다.

### 산출물

- `RuntimePulseService`

### 검증

- [ ] 한 tick 한 Job 제한이 보장된다.

### 금지사항

- 무한 while 내부에 복구·dispatch를 결합하지 않는다.

## DI-13-06 — 제한적 자동화 설정 적용

`선행: DI-13-05`

### 지시

1. Project 1, Blueprint 1, worker 1, dispatch 1, Commit off, Approval off를 설정한다.
2. 범위 밖 Job을 runnable에서 제외한다.
3. 설정 변경을 Evidence에 남긴다.

### 산출물

- `Limited automation config`

### 검증

- [ ] 초기 제한을 우회하는 코드 경로가 없다.

### 금지사항

- 기본값으로 전체 Project를 활성화하지 않는다.

## DI-13-07 — G2 lease·종료 시험

`선행: DI-13-06`

### 지시

1. worker 종료, heartbeat 중단, lease 만료를 주입한다.
2. 정확히 한 Job만 재개하는지 검증한다.
3. terminal failure와 stale input이 루프에 들어가지 않는지 확인한다.

### 산출물

- `docs/verification/wp-13-scheduler.md`

### 검증

- [ ] G2 기준 전체가 통과한다.

### 금지사항

- G2 실패 상태에서 Blueprint 수를 늘리지 않는다.

# Phase 7. Blueprint Admission과 Policy 버전

## DI-14-01 — BlueprintDraft 저장 구현

`선행: DI-13-07`

### 지시

1. Draft와 Active 저장 경로·상태를 분리한다.
2. Draft 생성·수정은 허용하되 실행 대상 조회에서 제외한다.
3. Draft schema를 검증한다.

### 산출물

- `BlueprintDraftStore`

### 검증

- [ ] Draft가 Job 생성에 사용되지 않는다.

### 금지사항

- Draft 저장을 Active 생성으로 간주하지 않는다.

## DI-14-02 — T0 Admission 검사 구현

`선행: DI-14-01`

### 지시

1. schema, path, fence, 권한 범위를 검사한다.
2. 기존 활성 버전의 범위와 비교한다.
3. 검사 결과를 구조화한다.

### 산출물

- `BlueprintAdmissionValidator`

### 검증

- [ ] 새 외부 접근과 live side effect가 탐지된다.

### 금지사항

- 신뢰된 Actor라는 이유로 검사를 생략하지 않는다.

## DI-14-03 — Admission Decision과 Active version 생성

`선행: DI-14-02`

### 지시

1. 검사 결과에서 required tier를 산출한다.
2. 승인 조건 충족 시 immutable Active version을 생성한다.
3. 기존 version 수정 API를 차단한다.

### 산출물

- `BlueprintAdmission Decision`
- `Active Blueprint version`

### 검증

- [ ] Active version은 수정 불가능하다.

### 금지사항

- 같은 version 번호로 내용을 교체하지 않는다.

## DI-14-04 — Policy creation/execution 버전 기록

`선행: DI-14-03`

### 지시

1. Job 생성 시 policyVersionAtCreation을 고정한다.
2. 실행 직전 최신 정책을 다시 평가한다.
3. policyVersionAtExecution을 기록한다.

### 산출물

- `Policy execution guard`

### 검증

- [ ] 생성 당시 허용됐어도 현재 금지면 실행하지 않는다.

### 금지사항

- 생성 정책만으로 실행을 계속 허용하지 않는다.

## DI-14-05 — Commit 정책 재검증 인터페이스 구현

`선행: DI-14-04`

### 지시

1. Commit 직전 사용할 최신 정책 조회·평가 인터페이스를 만든다.
2. policyVersionAtCommit 기록 계약을 정의한다.
3. 변경 시 policy_denied 또는 stale_input 매핑을 구분한다.

### 산출물

- `Commit policy guard interface`

### 검증

- [ ] 세 정책 버전을 lineage에서 조회할 수 있다.

### 금지사항

- 실행 정책 결과를 Commit에 그대로 재사용하지 않는다.

# Phase 8. Authority와 비차단 Approval

## DI-15-01 — RiskAssessor 구현

`선행: DI-14-05`

### 지시

1. 변경 범위, 외부 부작용, 데이터 민감도, rollback 가능성을 입력으로 위험도를 계산한다.
2. 판정 이유와 policy version을 반환한다.
3. 결정적 fixture를 만든다.

### 산출물

- `RiskAssessor`

### 검증

- [ ] 같은 사실 입력은 같은 위험 등급을 만든다.

### 금지사항

- Actor 권한과 위험도를 한 함수에서 혼합하지 않는다.

## DI-15-02 — JudgementRelation과 IndependenceEvaluator 구현

`선행: DI-15-01`

### 지시

1. subjectActorId와 counterpartActorId 관계를 입력으로 받는다.
2. sameModel, sameSession, contextIsolated, deterministicEvidencePresent, sharedProvenance를 판정한다.
3. Actor manifest에는 상대적 필드를 저장하지 않는다.

### 산출물

- `IndependenceEvaluator`

### 검증

- [ ] 비교 상대 없이 sameModel 판정을 생성하지 않는다.

### 금지사항

- Actor 자체 속성으로 독립성을 고정하지 않는다.

## DI-15-03 — AuthorityPolicyEvaluator 구현

`선행: DI-15-02`

### 지시

1. Risk와 Independence 결과에서 required tier와 조건을 계산한다.
2. 정책 버전과 적용 규칙 ID를 기록한다.
3. 허용·거부·승인 필요 결과를 분리한다.

### 산출물

- `AuthorityPolicyEvaluator`

### 검증

- [ ] 위험도와 독립성 모듈을 독립 테스트할 수 있다.

### 금지사항

- 평가 중 state를 변경하지 않는다.

## DI-15-04 — Immutable DecisionRecorder 구현

`선행: DI-15-03`

### 지시

1. Decision을 새 ID와 version으로 저장한다.
2. 이전 Decision을 supersedes로 연결한다.
3. 수정 API를 제공하지 않는다.

### 산출물

- `DecisionRecorder`

### 검증

- [ ] Decision 내용 변경은 새 Decision으로만 가능하다.

### 금지사항

- 기존 파일 overwrite로 판정을 바꾸지 않는다.

## DI-16-01 — ApprovalRequest 생성 구현

`선행: DI-15-04`

### 지시

1. subjectType, subjectId, subjectVersion, baseFingerprint, authorityPolicyVersion, requiredTier를 필수로 한다.
2. expiresAt과 supersedes를 지원한다.
3. 불명확한 subject를 거부한다.

### 산출물

- `ApprovalRequest factory`

### 검증

- [ ] 승인 범위를 정확히 재구성할 수 있다.

### 금지사항

- 설명 텍스트만으로 승인 대상을 지정하지 않는다.

## DI-16-02 — 승인·거부·만료 처리 구현

`선행: DI-16-01`

### 지시

1. approve, reject, expire transition을 구현한다.
2. 판정자와 Decision ID를 기록한다.
3. terminal Approval 상태 재처리를 차단한다.

### 산출물

- `Approval command handler`

### 검증

- [ ] 중복 approve가 상태를 두 번 변경하지 않는다.

### 금지사항

- 승인 파일을 직접 편집하지 않는다.

## DI-16-03 — Stale·superseded 판정 구현

`선행: DI-16-02`

### 지시

1. subject version, base, policy, required tier, fence/권한 변경을 비교한다.
2. 변경되면 stale 또는 superseded로 전환한다.
3. 기존 승인을 새 입력에 재사용하지 않는다.

### 산출물

- `ApprovalStalenessEvaluator`

### 검증

- [ ] 모든 stale 조건별 테스트가 있다.

### 금지사항

- expiresAt만 stale 기준으로 사용하지 않는다.

## DI-16-04 — Awaiting approval와 Frozen 전이 구현

`선행: DI-16-03`

### 지시

1. Revision deciding에서 awaiting_approval/frozen 전이를 연결한다.
2. 해당 Revision Job 생성을 차단한다.
3. 다른 active shadow Revision은 runnable로 유지한다.

### 산출물

- `NonBlockingApproval flow`

### 검증

- [ ] Revision A가 Frozen이어도 Revision B가 실행된다.

### 금지사항

- WorkItem 전체를 일괄 Frozen 처리하지 않는다.

## DI-16-05 — 승인 후 새 Revision 생성 구현

`선행: DI-16-04`

### 지시

1. 승인 시점의 최신 base·policy·subject를 다시 읽는다.
2. 기존 Revision을 임의 재개하지 않고 새 Revision을 생성한다.
3. supersedes/derivedFrom 관계를 기록한다.

### 산출물

- `ApprovedRevisionFactory`

### 검증

- [ ] 승인 이후 실행이 최신 입력과 정책을 사용한다.

### 금지사항

- 오래된 frozen Revision 상태를 executing으로 되돌리지 않는다.

## DI-16-06 — G3 통합 판정

`선행: DI-16-05`

### 지시

1. Frozen 병렬 실행, stale 승인 거부, 승인 후 새 Revision 시나리오를 실행한다.
2. 결과와 Evidence lineage를 검증한다.
3. G3 통과 여부를 STATUS.md에 기록한다.

### 산출물

- `docs/verification/wp-16-approval.md`

### 검증

- [ ] G3 기준 전체가 통과한다.

### 금지사항

- 실패 시 WP-17을 시작하지 않는다.

# Phase 9. Shadow Artifact 승격

## DI-17-01 — 승격 명령 입력 계약 구현

`선행: DI-16-06`

### 지시

1. promoteShadowArtifact 입력에 artifactId, target WorkItem/Revision, expected base, policy context를 정의한다.
2. 명령 schema와 validator를 만든다.
3. 권한 없는 호출을 거부한다.

### 산출물

- `PromoteShadowArtifactCommand`

### 검증

- [ ] 명시적 명령 없이는 승격이 시작되지 않는다.

### 금지사항

- Artifact 상태를 직접 Full로 바꾸지 않는다.

## DI-17-02 — 승격 전 Artifact·provenance 검증

`선행: DI-17-01`

### 지시

1. sealed 상태와 content hash를 검사한다.
2. ShadowRun provenance, inputHash, baseFingerprint, Harness 결과를 검증한다.
3. staging/rejected를 거부한다.

### 산출물

- `PromotionPreconditionValidator`

### 검증

- [ ] 검증 실패 시 새 Runtime Artifact가 생기지 않는다.

### 금지사항

- Artifact ID만 존재하면 신뢰하지 않는다.

## DI-17-03 — 최신 Authority 재평가 구현

`선행: DI-17-02`

### 지시

1. 승격 시점 최신 정책과 위험도를 평가한다.
2. 필요 시 ApprovalRequest를 생성한다.
3. Decision을 immutable하게 기록한다.

### 산출물

- `PromotionAuthorityDecision`

### 검증

- [ ] 승격 시점 policy version이 기록된다.

### 금지사항

- Shadow 실행 당시 Authority 결과만 재사용하지 않는다.

## DI-17-04 — Full Runtime Artifact 생성·연결

`선행: DI-17-03`

### 지시

1. 원 Shadow Artifact를 변경하지 않고 새 Full Artifact를 생성한다.
2. sourceArtifactId와 lineage를 기록한다.
3. WorkItem/Revision에 transition으로 연결한다.

### 산출물

- `Promoted Runtime Artifact`

### 검증

- [ ] Commit 경로는 Full Artifact만 허용한다.

### 금지사항

- Shadow Artifact를 그대로 Commit 후보 필드에 넣지 않는다.

## DI-17-05 — 승격 재실행·중복 테스트

`선행: DI-17-04`

### 지시

1. 동일 명령 반복과 동시 호출을 실행한다.
2. 동일 source/target/policy 조합의 중복 Full Artifact를 차단한다.
3. stale base 변경 후 재시도를 검증한다.

### 산출물

- `docs/verification/wp-17-promote.md`

### 검증

- [ ] 명시적 승격 lineage가 하나만 생성된다.

### 금지사항

- 중복 승격을 사후 정리로 해결하지 않는다.

# Phase 10. Commit 경계와 Rollback

## DI-18-01 — FreshnessValidator 구현

`선행: DI-17-05`

### 지시

1. selected Revision, sealed Full Artifact, current base fingerprint를 비교한다.
2. stale base와 stale selection을 구분한다.
3. 실패 시 변경 전 안전 종료한다.

### 산출물

- `FreshnessValidator`

### 검증

- [ ] stale base에서는 파일 적용이 0건이다.

### 금지사항

- 검증 후 lease 획득까지 긴 무보호 구간을 두지 않는다.

## DI-18-02 — AuthorityVerifier 구현

`선행: DI-18-01`

### 지시

1. 최신 policy를 조회한다.
2. Decision, Approval, required tier, subject version을 검증한다.
3. policyVersionAtCommit을 기록한다.

### 산출물

- `AuthorityVerifier`

### 검증

- [ ] 최신 정책 미검증 Commit이 불가능하다.

### 금지사항

- stale Approval을 허용하지 않는다.

## DI-18-03 — RestorePointCreator 구현

`선행: DI-18-02`

### 지시

1. 변경 대상과 repository 상태를 캡처한다.
2. restore point hash와 manifest를 저장한다.
3. 복원 가능성을 사전 검증한다.

### 산출물

- `RestorePointCreator`

### 검증

- [ ] restore point 실패 시 apply가 시작되지 않는다.

### 금지사항

- 백업 경로만 만들고 복원 시험을 생략하지 않는다.

## DI-18-04 — ChangeApplier 구현

`선행: DI-18-03`

### 지시

1. repository_commit lease를 확인한다.
2. Artifact manifest의 변경만 적용한다.
3. 적용 단계와 파일별 결과를 기록한다.

### 산출물

- `ChangeApplier`

### 검증

- [ ] CommitCoordinator 외부에서 호출할 수 없는 경계를 둔다.

### 금지사항

- 임의 경로 파일을 Artifact 밖에서 추가하지 않는다.

## DI-18-05 — GitDataCommitter 구현

`선행: DI-18-04`

### 지시

1. 변경 검증 후 Git commit 또는 데이터 commit을 수행한다.
2. commit ID와 applied Artifact hash를 연결한다.
3. 실패 위치를 구분한다.

### 산출물

- `GitDataCommitter`

### 검증

- [ ] commit 결과를 lineage에서 추적할 수 있다.

### 금지사항

- Git 성공만으로 Runtime 완료를 확정하지 않는다.

## DI-18-06 — PostCommitVerifier 구현

`선행: DI-18-05`

### 지시

1. 원본 hash, 테스트, 예상 diff, 정책 후조건을 검증한다.
2. 실패 시 RollbackCoordinator를 호출할 수 있는 결과를 반환한다.

### 산출물

- `PostCommitVerifier`

### 검증

- [ ] 검증 실패가 성공으로 숨겨지지 않는다.

### 금지사항

- Commit 전 검사와 동일하다는 이유로 생략하지 않는다.

## DI-18-07 — RollbackCoordinator 구현

`선행: DI-18-06`

### 지시

1. restore point로 복원을 시도한다.
2. rollback 성공, 실패, 결과 불명을 구분한다.
3. 결과 불명은 external_side_effect_unknown으로 전환한다.

### 산출물

- `RollbackCoordinator`

### 검증

- [ ] rollback 실패가 자동 retry되지 않는다.

### 금지사항

- 복원 실패를 일반 failed로 축소하지 않는다.

## DI-18-08 — CommitCoordinator 단일 진입점 구현

`선행: DI-18-07`

### 지시

1. Freshness → Authority → lease → restore point → apply → commit → verify → state/evidence 순서를 조정한다.
2. 각 하위 결과를 manifest에 모은다.
3. lease 해제를 finally와 Recovery 양쪽에서 보장한다.

### 산출물

- `CommitCoordinator`
- `Commit manifest`

### 검증

- [ ] 외부 공개 원본 변경 API가 하나뿐이다.

### 금지사항

- 하위 구성요소 책임을 coordinator에 재구현하지 않는다.

## DI-18-09 — Commit 장애 주입 매트릭스 실행

`선행: DI-18-08`

### 지시

1. lease 후, restore point 후, 일부 적용 후, commit 직전/직후, 검증 실패, rollback 실패를 주입한다.
2. policy/base 변경 직후 요청을 시험한다.
3. 각 경우 state, Evidence, repository 결과를 기록한다.

### 산출물

- `Commit crash matrix`

### 검증

- [ ] 결과가 성공/rollback/unknown 중 하나로 명확하다.

### 금지사항

- 중간 상태를 사람이 추측하도록 남기지 않는다.

## DI-18-10 — G4 판정

`선행: DI-18-09`

### 지시

1. 승인되지 않은 변경, stale base commit, restore point 없는 변경, 불명 상태 은폐 건수를 집계한다.
2. 모두 0인지 확인한다.
3. 통과 Evidence와 검증 문서를 작성한다.

### 산출물

- `docs/verification/wp-18-commit.md`

### 검증

- [ ] G4 기준 전체가 통과한다.

### 금지사항

- rollback 일부 성공을 전체 성공으로 판정하지 않는다.

# Phase 11. Projection과 운영 복구 도구

## DI-19-01 — WorkItem·Revision Projection 구현

`선행: DI-18-10`

### 지시

1. Canonical State에서 목록과 lifecycle view를 생성한다.
2. prepared transition을 완료 상태로 표시하지 않는다.
3. Projection 삭제 후 재생성을 지원한다.

### 산출물

- `WorkItem/Revision projection`

### 검증

- [ ] Projection 장애가 Canonical State를 변경하지 않는다.

### 금지사항

- Projection을 authoritative state로 사용하지 않는다.

## DI-19-02 — Job·Approval Projection 구현

`선행: DI-19-01`

### 지시

1. queue, lease, retry, dead-letter, pending/stale Approval view를 만든다.
2. version과 마지막 transition을 표시한다.

### 산출물

- `Job/Approval projection`

### 검증

- [ ] 운영자가 대기와 막힘 원인을 구분할 수 있다.

### 금지사항

- 로그 문자열 파싱에만 의존하지 않는다.

## DI-19-03 — Artifact lineage·Commit Projection 구현

`선행: DI-19-02`

### 지시

1. ShadowRun → Shadow Artifact → promoted Artifact → Revision → Commit 관계를 표시한다.
2. hash와 policy versions를 포함한다.

### 산출물

- `Artifact/Commit projection`

### 검증

- [ ] Commit에서 원 ShadowRun까지 역추적 가능하다.

### 금지사항

- supersedes 관계를 평면 목록으로 잃지 않는다.

## DI-19-04 — Evidence 감사 검색 구현

`선행: DI-19-03`

### 지시

1. aggregateId, transitionId, cycleKey, approvalRequestId, artifactId, policyVersion, modelCallId 검색을 구현한다.
2. 검색 결과에 source file offset 또는 event identity를 제공한다.

### 산출물

- `Evidence query tool`

### 검증

- [ ] 하나의 결과가 입력·정책·Actor·Job까지 추적된다.

### 금지사항

- 원본 JSONL을 사람이 grep해야만 찾게 두지 않는다.

## DI-19-05 — Transition·lease 복구 명령 구현

`선행: DI-19-04`

### 지시

1. prepared reconcile, aborted 조회, expired lease 조회·복구 명령을 만든다.
2. 각 명령은 dry-run과 apply를 구분한다.
3. apply 결과가 Evidence를 생성한다.

### 산출물

- `Ops recovery CLI 1`

### 검증

- [ ] 운영자가 파일 직접 편집 없이 복구한다.

### 금지사항

- dry-run이 state를 변경하지 않는다.

## DI-19-06 — Dead-letter·Approval·unknown 복구 명령 구현

`선행: DI-19-05`

### 지시

1. dead-letter 재처리, stale Approval 조회, external_side_effect_unknown reconcile 명령을 만든다.
2. 재처리는 새 Job/Decision을 생성한다.
3. 원본 기록은 불변으로 유지한다.

### 산출물

- `Ops recovery CLI 2`

### 검증

- [ ] 수동 조치가 모두 새 Evidence를 남긴다.

### 금지사항

- 기존 terminal 상태를 직접 되돌리지 않는다.

## DI-19-07 — Sandbox·Artifact 유지보수 명령 구현

`선행: DI-19-06`

### 지시

1. 잔여 sandbox 조회·정리와 Artifact hash 재검증을 구현한다.
2. 조사 보존 대상과 안전 삭제 대상을 구분한다.
3. 변조 탐지 시 자동 수정하지 않고 격리한다.

### 산출물

- `Ops maintenance CLI`

### 검증

- [ ] sealed Artifact 변조가 탐지된다.

### 금지사항

- hash 불일치를 새 hash로 덮어써 정상화하지 않는다.

## DI-19-08 — G5 판정

`선행: DI-19-07`

### 지시

1. 대표 복구 시나리오를 UI/CLI만으로 수행한다.
2. 파일 직접 편집이 없었는지 확인한다.
3. 모든 수동 명령의 Evidence를 검증한다.

### 산출물

- `docs/verification/wp-19-projection-ops.md`

### 검증

- [ ] G5 기준 전체가 통과한다.

### 금지사항

- 운영 문서 없이 명령만 제공하지 않는다.

# Phase 12. Legacy 데이터 마이그레이션

## DI-20-01 — Legacy read-only inventory 작성

`선행: DI-19-08`

### 지시

1. 기존 dev-pack, ruined-lab 상태와 파일을 읽기 전용으로 조사한다.
2. 수량, ID, hash, 경로, 참조 관계를 inventory로 만든다.
3. 변환 불가 항목을 별도 분류한다.

### 산출물

- `Legacy inventory report`

### 검증

- [ ] 조사 중 기존 파일 변경이 0이다.

### 금지사항

- 분석 단계에서 신규 Runtime state를 생성하지 않는다.

## DI-20-02 — Legacy ID mapping과 compatibility 규칙 작성

`선행: DI-20-01`

### 지시

1. legacy project를 신규 Project/Blueprint로 매핑한다.
2. 충돌 없는 결정적 신규 ID를 계산한다.
3. 기존 API `{projectId}` 해석과 deprecation 경고를 정의한다.

### 산출물

- `ID mapping file`
- `CompatibilityAdapter 설계`

### 검증

- [ ] 같은 legacy 입력은 같은 신규 ID를 만든다.

### 금지사항

- 문맥에 따라 projectId를 임의로 다르게 해석하지 않는다.

## DI-20-03 — Migration transformer 구현

`선행: DI-20-02`

### 지시

1. Project와 Blueprint version을 생성한다.
2. 기존 실행 기록을 Artifact/Evidence로 변환한다.
3. WorkItem/Revision 관계를 재구성한다.
4. migration Decision을 생성한다.

### 산출물

- `MigrationTransformer`

### 검증

- [ ] 변환 결과가 신규 schema를 모두 통과한다.

### 금지사항

- 기존 데이터를 overwrite하지 않는다.

## DI-20-04 — Migration idempotency 구현

`선행: DI-20-03`

### 지시

1. migration key와 reservation을 정의한다.
2. 이미 변환된 source hash를 재사용한다.
3. 부분 실패 시 완료 단계부터 재개한다.

### 산출물

- `Migration idempotency`

### 검증

- [ ] 반복 실행해도 중복 Aggregate가 생기지 않는다.

### 금지사항

- 파일명만으로 중복을 판정하지 않는다.

## DI-20-05 — Dry-run과 대조 보고서 구현

`선행: DI-20-04`

### 지시

1. 실제 적용 없이 예상 신규 객체와 경고를 출력한다.
2. 전후 수량, hash, 누락, 충돌을 비교한다.
3. 승인 가능한 보고서를 생성한다.

### 산출물

- `Migration dry-run report`

### 검증

- [ ] 변환 전후 차이가 설명 가능하다.

### 금지사항

- 오류 항목을 조용히 건너뛰지 않는다.

## DI-20-06 — Migration 적용과 compatibility 활성화

`선행: DI-20-05`

### 지시

1. 백업 존재를 확인한다.
2. TransitionWriter를 통해 신규 Runtime에 적용한다.
3. compatibility adapter를 활성화한다.
4. 적용 Evidence와 최종 hash 보고서를 만든다.

### 산출물

- `Migrated Runtime data`
- `docs/verification/wp-20-migration.md`

### 검증

- [ ] 기존 호출이 잘못된 Aggregate를 수정하지 않는다.

### 금지사항

- 직접 파일 복사로 Canonical State를 주입하지 않는다.

# Phase 13. 최종 통합과 출시 판정

## DI-21-01 — 정상 Shadow 실행 E2E

`선행: DI-20-06`

### 지시

1. Blueprint 활성화부터 Revision completed까지 실행한다.
2. 각 Aggregate와 Evidence lineage를 검증한다.
3. 동일 실행 재실행 결과를 비교한다.

### 산출물

- `E2E Scenario A report`

### 검증

- [ ] 중복 부작용이 0이다.

### 금지사항

- 중간 수동 state 편집을 사용하지 않는다.

## DI-21-02 — 비차단 Approval E2E

`선행: DI-21-01`

### 지시

1. Revision A를 Frozen 상태로 만들고 Revision B를 실행한다.
2. 승인 후 새 Revision 생성을 확인한다.
3. stale 승인을 재사용 시도한다.

### 산출물

- `E2E Scenario B report`

### 검증

- [ ] Frozen이 WorkItem 전체를 막지 않는다.

### 금지사항

- 기존 Revision을 직접 재개하지 않는다.

## DI-21-03 — 승격과 Commit E2E

`선행: DI-21-02`

### 지시

1. Shadow Artifact를 명시적으로 승격한다.
2. Authority/Approval 후 Commit한다.
3. post-commit 결과와 lineage를 검증한다.

### 산출물

- `E2E Scenario C report`

### 검증

- [ ] 승인되지 않은 원본 변경이 0이다.

### 금지사항

- Shadow Artifact를 직접 Commit하지 않는다.

## DI-21-04 — 장애 복구 E2E

`선행: DI-21-03`

### 지시

1. Job 실행 중 종료와 lease 만료를 주입한다.
2. RecoveryScanner가 model call/result를 재사용하게 한다.
3. 중복 없이 완료되는지 확인한다.

### 산출물

- `E2E Scenario D report`

### 검증

- [ ] 복구 불가능한 상태가 0이다.

### 금지사항

- 새 실행으로 결과를 덮어 문제를 숨기지 않는다.

## DI-21-05 — Stale 입력 E2E

`선행: DI-21-04`

### 지시

1. Job 생성 후 base와 policy를 각각 변경한다.
2. 실행/Commit 재검증 실패를 확인한다.
3. 새 Job 또는 Revision 전환을 검증한다.

### 산출물

- `E2E Scenario E report`

### 검증

- [ ] stale 입력이 기존 실행으로 계속 진행되지 않는다.

### 금지사항

- stale_input을 transient retry로 처리하지 않는다.

## DI-21-06 — 정량 스트레스 기준 실행

`선행: DI-21-05`

### 지시

1. 동일 cycleKey 100회, prepared transition 100건, 동시 claim 1,000회를 실행한다.
2. 결과 Aggregate 수, 복구율, 이중 claim을 측정한다.
3. Commit rollback 전체 시나리오 lineage를 점검한다.

### 산출물

- `Quantitative release report`

### 검증

- [ ] 중복 Aggregate 0, 복구 100%, 이중 claim 0이다.

### 금지사항

- 실패 횟수를 평균으로 희석하지 않는다.

## DI-21-07 — 출시 차단 조건 최종 판정

`선행: DI-21-06`

### 지시

1. 10개 출시 차단 조건을 하나씩 확인한다.
2. 잔여 위험과 미지원 capability를 명시한다.
3. 모든 조건 통과 시 release Decision과 Evidence를 만든다.

### 산출물

- `Release readiness decision`
- `최종 검증 문서`

### 검증

- [ ] 핵심 성공 지표 3개와 정량 기준이 모두 충족된다.

### 금지사항

- 미통과 항목을 known issue로만 남기고 자동화 범위를 확대하지 않는다.

# 부록 C. 컨텍스트 절약 점검표

각 DI 시작과 종료 시 다음을 점검한다.

- [ ] 전체 문서 대신 Context Pack의 지정 절만 읽었는가?
- [ ] 읽지 않은 원문을 읽은 것으로 가정하지 않았는가?
- [ ] 중요한 판단은 요약이 아니라 원본 계약·Evidence로 확인했는가?
- [ ] 새 사실을 authoritative 문서 한 곳에 기록하고 다른 곳에는 참조만 남겼는가?
- [ ] 장문 로그와 diff를 경로·hash·핵심 구간으로 축약했는가?
- [ ] 추가 조회와 Budget 초과 이유를 Receipt에 기록했는가?
- [ ] 다음 실행자가 L0와 L1만으로 안전하게 착수할 수 있는가?

# 부록 A. 게이트 연결

| 게이트 | 판정 DI | 다음 범위 시작 조건 |
|---|---|---|
| G1 | DI-12-04 | DI-13-01 시작 가능 |
| G2 | DI-13-07 | Phase 7 시작 가능 |
| G3 | DI-16-06 | DI-17-01 시작 가능 |
| G4 | DI-18-10 | Phase 11 시작 가능 |
| G5 | DI-19-08 | Migration 및 최종 통합 시작 가능 |

# 부록 B. DI 브랜치와 검증 기록 규칙

```text
branch: task/WP-XX-{short-name}
commit/checkpoint: [DI-XX-YY] <imperative summary>
optional isolated branch: task/WP-XX/DI-XX-YY-{short-name}
verification entry: docs/verification/wp-XX-*.md 내부 DI-XX-YY 절
```

하나의 DI가 지나치게 커져 서로 독립적인 두 실패 원인을 포함하게 되면, 구현 전에 `DI-XX-YY-a`, `DI-XX-YY-b`로 다시 나눈다. 단, 상태 소유권이나 게이트 기준은 변경하지 않는다.
