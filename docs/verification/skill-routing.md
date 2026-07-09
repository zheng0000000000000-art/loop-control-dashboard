# 스킬 도메인 분리 + 지시/재지시 게이트 검증

검증일: 2026-07-09

## 참조한 스킬

- skills/common/directive-writing.md
- skills/common/verification.md
- skills/domains/dev/file-navigation.md

## 변경 경로

- AGENTS.md
- CLAUDE.md
- dashboard/data/dev-pack/blueprint.json
- dashboard/data/lang/ko.json
- dashboard/data/lang/en.json
- docs/DECISIONS.md
- docs/verification/skill-routing.md
- server/DevPackMeasures.cs
- server/OllamaExecutor.cs
- server/Program.cs
- skills/**

## 지시 게이트 자가 검사

| 항목 | 판정 | 기록 |
| --- | --- | --- |
| 완료 기준 검증 가능성 | O | 스킬 이동, 양쪽 관례 파일 동일화, 재지시 가드, 측정 주입/복귀, dev-pack 게이트로 확인 가능했다. |
| 대상 파일·범위 특정 | O | `/skills`, `AGENTS.md`, `CLAUDE.md`, `Program.cs`, `DevPackMeasures.cs`, lang, blueprint, DECISIONS, verification 문서로 특정됐다. |
| 기존 원칙·blueprint 충돌 | O | blueprint에는 신규 오염 측정 지표만 추가했다. 기준 완화는 하지 않았다. |
| 추측 진행 | 없음 | 모호한 범위는 없었고, 결재/기준 변경 액션은 호출하지 않았다. |

## A. 스킬 도메인 분리

`git mv`로 스킬을 아래 구조로 재배치했다.

| 경로 | 내용 |
| --- | --- |
| `skills/common/` | `directive-writing.md`, `verification.md` |
| `skills/domains/dev/` | `file-navigation.md` |
| `skills/domains/design/` | `design.md` |
| `skills/domains/game/` | `balance-tuning.md` |
| `skills/domains/docs/` | `README.md` |

각 스킬의 `버전:` 줄을 `도메인:`과 `트리거:`를 포함하도록 확장했다. `skills/domains/docs/README.md`는 "스킬이 쌓이면 채운다"는 자리 표시 문서로 추가했다.

## B. 라우팅 + 지시 게이트

`AGENTS.md`와 `CLAUDE.md`의 스킬 참조 문단을 같은 내용으로 교체했다.

- `/skills/common/`은 모든 작업에서 읽는다.
- `/skills/domains/`는 변경 파일 경로가 `트리거:`와 일치하는 스킬만 읽는다.
- verification 문서에 "참조한 스킬" 목록을 기록한다.
- 착수 전 완료 기준, 대상 파일·범위, 원칙 충돌 여부를 검사한다.
- 부족하면 선택지 딸린 질문으로 되묻고 대기한다.

양쪽 파일은 해당 섹션 내용이 동일하다.

## C. 재지시 가드

`Program.cs`의 `needs_changes` 재생성 루프 앞에 `TryEscalateInsufficientRefeedback` 검사를 추가했다. 실패 finding의 `note`/`comment`가 공백 제거 후 10자 미만이면 재생성하지 않고 verdict를 `uncertain`으로 격상하며 `review.refeedback_insufficient` 로그를 남긴다.

재지시 self-test:

```powershell
dotnet run --project server --no-build -- refeedbacktest
```

결과:

```json
{
  "escalated": true,
  "verdict": "uncertain",
  "reasonCode": "review.refeedback_insufficient",
  "reportVerdict": "uncertain",
  "event": "review.refeedback_insufficient",
  "checkIds": ["mock-check"]
}
```

판정: O. 빈 note finding은 재생성으로 가지 않고 사람 검토 경로로 격상됐다.

재생성 프롬프트에 들어가는 이전 지적은 `checkId: note` 형태로 구조화했다. `OllamaExecutor.BuildFeedbackByMetric`에서 실패 finding의 `checkId`와 note를 함께 전달한다.

## D. 오염 게이트

`dev-pack/blueprint.json`에 `skillDomainViolations` 지표를 추가했다. `DevPackMeasures.cs`는 verification 문서의 "참조한 스킬" 섹션과 "변경 경로" 섹션을 비교한다. "참조한 스킬" 섹션이 없는 과거 문서는 검사에서 제외한다.

기본 측정:

```powershell
dotnet run --project server --no-build -- measure dev-pack
```

초기 결과:

```json
{
  "skillsWithoutVersion": 0,
  "skillDomainViolations": 0
}
```

위반 주입:

임시 문서 `docs/verification/__tmp-skill-routing-injection.md`에 아래 내용을 넣었다.

```markdown
## 참조한 스킬
- skills/domains/design/design.md

## 변경 경로
- server/Program.cs
```

측정 결과:

```json
{
  "metricId": "skillDomainViolations",
  "value": 1,
  "evidence": [
    "docs/verification/__tmp-skill-routing-injection.md -> skills/domains/design/design.md"
  ]
}
```

임시 문서를 삭제하고 재측정했다.

```json
[
  { "metricId": "skillsWithoutVersion", "value": 0, "evidence": [] },
  { "metricId": "skillDomainViolations", "value": 0, "evidence": [] }
]
```

판정: O. 주입 → 감지 → 삭제 → 0 복귀가 확인됐다.

## E. 결정 기록

`docs/DECISIONS.md`에 다음 결정을 추가했다.

> 재지시(검토 지적)도 지시다 — 정보가 부족하면 재생성(추측) 대신 사람으로 올린다. 지시 게이트의 내부 대칭.

## F. 최종 게이트

실행:

```powershell
dotnet build server/LocalFirstWorkflowDashboard.Server.csproj
node --check dashboard/app.js
dotnet run --project server --no-build -- measure dev-pack
```

결과:

- 서버 빌드: 경고 0, 오류 0
- 프런트 문법: 오류 없음
- dev-pack: `violationCount=0`, `overallStatus=completed`

## 완료 기준 판정

| 기준 | 판정 |
| --- | --- |
| common/domains 물리 분리, 트리거 경로 패턴, 양쪽 관례 파일 동일화 | O |
| 재생성 가드: 정보 부족 → uncertain, DECISIONS 1줄 | O |
| skillDomainViolations 측정: 주입→감지→해소, 소급 위반 없음 | O |
| 이 작업 보고의 스킬 기록·게이트 관례 준수 | O |
