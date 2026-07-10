# "승인해도 새로고침하면 다시 승인해야 한다" 버그 — 원인과 수정

## 재현

실제 서버(포트 5173)의 dev-pack 프로젝트에서 재현했다.

1. `POST /actions/measure`(측정 실행) → 위반 4건 존재 → 새 proposal 생성, `currentStage: "changeReview"`.
2. `POST /actions/approve` → `currentStage: "apply"`, `stages.apply: "in_progress"`, `changeReview: "approved"`.
   여기까지는 정상.
3. **아무 코드도 고치지 않은 채** `POST /actions/measure`를 다시 호출(사용자가 새로고침하거나
   "측정 실행"을 다시 누르는 것과 동일한 호출) → `currentStage`가 `"changeReview"`로 되돌아가고
   `stages.apply`가 `"blocked"`(`blockInfo.kind: "waiting"`)로 리셋되며, **새 proposal이
   `submitted`로 다시 생성**됐다 — 방금 승인한 결정이 무효화된 것처럼 보인다.

## 원인

`server/Program.cs`의 `ApplyMeasurementStagePatch`가 위반이 하나라도 있으면(`violations.Count > 0`)
**`apply` 단계의 현재 상태와 무관하게** 무조건 `blocked`로 되돌리고 `changeReview`를 다시
`pending_review`로 열었다. dev-pack은 (여러 개의 기존 위반이 항상 남아 있어) 거의 항상
`violations.Count > 0`이므로, 승인 직후 실제로 아무것도 고치기 전에 측정이 다시 실행되면 —
사람이 재측정 버튼을 누르든, 30분 폴링 자동 검수가 돌든 — 곧바로 방금 승인한 `apply:in_progress`가
지워지고 새 검토가 강제로 열렸다. **승인 자체가 상태 전이를 만드는 게 아니라, 다음 측정이
그 전이를 지워버리는 구조였다.**

## 수정

승인이 `apply` 단계로 들어갈 때, 그 시점의 위반 집합(`metricId=value` 서명)을
`state.applyBaselineViolations`에 저장한다(`server/Engine.cs`의 `ApplyStatePatch`에 일반적인
필드 복사 한 줄 추가 — 코어 파일에 도메인 지식을 넣지 않음, `suspendedTracks`와 동일한 방식).

`ApplyMeasurementStagePatch`는 위반이 있어도 ①`apply`가 이미 `in_progress`이고 ②현재 위반
집합이 이 기준선과 **완전히 같으면**(`ViolationSignatureUnchanged`) 아직 아무것도 고쳐지지
않은 것으로 보고 `apply`를 건드리지 않는다 — `changeReview`도 `approved`로 그대로 둔다.
위반 집합이 조금이라도 달라지면(무언가 실제로 바뀌었다는 뜻) 기존 로직 그대로 새 검토를 연다.

기준선이 없는 기존 저장 상태(이 수정 이전에 이미 `apply:in_progress`였던 프로젝트)는
안전 쪽(`false` = 기존 동작 유지)으로 처리된다.

## 실측

실제 서버(포트 5173, dev-pack)에서 순서대로:

1. 수정 전 코드로 떠 있던 기존 in_progress 사이클(기준선 없음)에 측정 재실행 →
   **버그 그대로 재현**: `currentStage: "changeReview"`, `apply: "blocked"`, 새 proposal `submitted`.
   (기존 저장 상태 호환성 확인 겸 재현 증거.)
2. 새로 생성된 proposal을 승인 → `currentStage: "apply"`, `apply: "in_progress"`,
   `applyBaselineViolations: ["smallTouchTargets=1","skillDomainViolations=2","programCsLines=2741","maxFunctionLength=246"]`.
3. 아무 코드도 고치지 않고 측정 재실행(`POST /actions/measure`) → **`currentStage: "apply"`,
   `apply: "in_progress"`, `changeReview: "approved"` 그대로 유지**.
4. `dotnet run --project server -- measure dev-pack` CLI 경로도 동일하게 확인(HTTP·CLI가
   같은 `RunMeasureCore`를 타므로 Tier2Approver의 자체 측정 호출에도 동일하게 적용됨을 확인).

## 추가 발견 — "승인 버튼이 막혀 있다" (사람이 실사용 중 직접 신고)

1차 수정 직후에도 새 proposal 객체는 여전히 매 측정마다 다시 생성됐다(`lifecycle: "submitted"`).
그런데 대시보드의 `getReviewContext()`는 `hasPendingProposal = proposal.lifecycle === "submitted"`만
보고, `canReview`는 별도로 단계 상태(`pending_review`)까지 확인한다 — 즉 **새 proposal이 화면에는
"검토 대상"으로 표시되는데, 승인/거절 버튼은 비활성화**돼 있었다. 사람이 이걸 "승인이 막혀 있다"로
신고했다. `ApplyMeasurementResult`에서 회귀 판정·제안 생성(dev-pack 규칙 기반/ruined-lab 튜닝
공통 경로)이 `apply` 단계가 이미 진행 중이고 위반 집합이 그대로일 때도 무조건 실행됐던 게 원인 —
1차 수정에서는 단계 전이만 막았지 제안 재생성 자체는 막지 않았었다.

`ApplyMeasurementResult`에 동일한 `applyStageAlreadyInProgress` 조건(1차 수정과 같은 함수
`ViolationSignatureUnchanged` 재사용)을 추가해, 그 경우 회귀/제안/1층 검토 로직 전체를
건너뛰도록 `if (applyStageAlreadyInProgress) {} else if (violations.Count > 0) {...} else {...}`
3분기로 재구성했다.

### 실측(실제 서버, 이어서)

5. 위 3번 상태에서 다시 측정 실행 → **`proposalId`가 그대로**(새 proposal 미생성),
   `proposalLifecycle: "decided"` 유지 — 화면에 "검토 대상"이 뜨지 않고 승인 패널은
   빈 상태(`approval.noPendingBody`)로 정상 표시된다.
6. `dotnet run --project server -- measure dev-pack` CLI로도 동일 확인.

(참고: 이 재현·검증 도중 Program.cs를 직접 수정하고 있었으므로, 그 중간 한 번은
`programCsLines`/`maxFunctionLength` 실측값 자체가 내 편집으로 바뀌어 정상적으로 새 검토가
열렸다 — "값이 실제로 달라지면 새 회차를 연다"는 설계가 의도대로 동작한 것이지 결함이 아니다.)

## 부수 효과 고지

위 실측은 실제 저장소의 실제 dev-pack 루프를 대상으로 했다 — 재현을 위해 실제로 두 번의
승인(approve)을 수행했고, 이는 loopIteration을 두 번 진행시켰다. 파괴적이지 않으며, 사람이
실제로 그 버튼을 눌렀을 때와 동일한 정상 진행이다.

## 게이트

```json
{"gate":"dev-pack","violations":4,"attempt":5}
```

기존과 동일한 4건(`smallTouchTargets`/`skillDomainViolations`은 순수 기존 위반,
`programCsLines`=2751(band 0~2661)·`maxFunctionLength`=256(band 0~80)은 두 차례 수정으로
2684 → 2751(+67줄) 악화 — 버그 수정 로직 자체가 `Program.cs`(Approve 핸들러·
`ApplyMeasurementStagePatch`·`ApplyMeasurementResult`)에 있어 불가피하게 그 파일에 추가됨).
새로 생긴 위반 카테고리는 없다(`violations` 개수 4로 불변) — `functionsWithoutComment` 등은
계속 0.

## 참조한 스킬

- `skills/common/verification.md`

## 추측 진행

- "위반 집합이 완전히 같다"의 판정 단위를 `metricId=value` 문자열 집합 동등 비교로 잡았다 —
  값이 조금이라도 바뀌면(악화든 개선이든) "무언가 시도됐다"로 보고 새 검토를 연다. 값은
  그대로인데 evidence(파일 위치 등)만 바뀐 경우는 여전히 "unchanged"로 본다 — 지시서 없이
  진행한 판단이라 여기 남긴다.
- (수정됨) 처음에는 "매 측정마다 proposal이 다시 생성되는 것은 범위 밖"이라고 판단했으나,
  사람이 실사용 중 "승인이 막혀 있다"로 신고해 실제로는 UI에 혼란을 주는 결함임이 확인돼
  위 "추가 발견" 절의 수정으로 함께 해소했다.
