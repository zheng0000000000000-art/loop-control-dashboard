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
   `apply: "in_progress"`, `changeReview: "approved"` 그대로 유지** — 새 proposal 객체는 다시
   만들어지지만(기존에도 매 측정마다 생성되던 동작, 변경 없음) `changeReview` 상태가
   `pending_review`로 안 열리므로 프런트의 `canReview`(단계 상태 기준)가 계속 false — 사용자에게
   재승인을 요구하지 않는다.
4. `dotnet run --project server -- measure dev-pack` CLI 경로도 동일하게 확인(HTTP·CLI가
   같은 `RunMeasureCore`를 타므로 Tier2Approver의 자체 측정 호출에도 동일하게 적용됨을 확인).

## 부수 효과 고지

위 실측은 실제 저장소의 실제 dev-pack 루프를 대상으로 했다 — 재현을 위해 실제로 두 번의
승인(approve)을 수행했고, 이는 loopIteration을 두 번 진행시켰다. 파괴적이지 않으며, 사람이
실제로 그 버튼을 눌렀을 때와 동일한 정상 진행이다.

## 게이트

```json
{"gate":"dev-pack","violations":4,"attempt":4}
```

기존과 동일한 4건(`smallTouchTargets`/`skillDomainViolations`/`maxFunctionLength`은 순수 기존 위반,
`programCsLines`는 이번 수정으로 2689 → 2741(+52줄) 악화 — 버그 수정 로직 자체가
`Program.cs`(Approve 핸들러·`ApplyMeasurementStagePatch`)에 있어 불가피하게 그 파일에 추가됨).
새로 생긴 위반 카테고리는 없다(`violations` 개수 4로 불변) — `functionsWithoutComment` 등은
계속 0.

## 참조한 스킬

- `skills/common/verification.md`

## 추측 진행

- "위반 집합이 완전히 같다"의 판정 단위를 `metricId=value` 문자열 집합 동등 비교로 잡았다 —
  값이 조금이라도 바뀌면(악화든 개선이든) "무언가 시도됐다"로 보고 새 검토를 연다. 값은
  그대로인데 evidence(파일 위치 등)만 바뀐 경우는 여전히 "unchanged"로 본다 — 지시서 없이
  진행한 판단이라 여기 남긴다.
- 매 측정마다 proposal 객체 자체는 여전히 다시 생성된다(단계가 안 열려 있어도) — 이는
  이번 버그와 별개의 기존 동작이라 범위를 넘는다고 보고 손대지 않았다.
