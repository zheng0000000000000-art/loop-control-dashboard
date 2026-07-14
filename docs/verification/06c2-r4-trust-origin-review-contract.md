# 06C-2-R4 검수 보고 — trust-origin review contract closure

## 주체 (actor)

CORE_INFRA_EXECUTOR (claude-sonnet-4-6, 06C-2-R4)

## 참조한 스킬

- `skills/common/directive-writing.md` — 지시서 해석 절차, 완료 판정 기준 확인
- `skills/common/verification.md` — 실행 검증 절차, 검증 문서 작성 기준

## R3 FAIL 사유가 닫힌 방법

### 1. skills/common/ 참조 의무 미기록 (AGENTS.md 계약 위반)

**R3 위반:** `docs/verification/06c2-r3-trust-origin-reason-and-hooks.md:85`에 "없음 (skills/common/ 경로 없이 직접 지시서 수행)"이라고 기록했다. `skills/common/`은 모든 작업에서 읽어야 하는 필수 참조다.

**R4 처리:** R3 문서를 수정하지 않고, 본 R4 문서를 새로 작성하며 실제 참조한 스킬 목록을 위 `## 참조한 스킬` 항목에 기록했다.

### 2. high-risk reason 검사가 JSON 필드 정확 매칭이 아님

**R3 구현:** `stderrText.Contains("trusted-human-receipt-required")` — 문자열 포함 여부만 확인. 우회 가능: `{"code":"other","detail":"contains trusted-human-receipt-required in detail"}` 같은 응답에서도 true.

**R4 수정 (`server/TrustOriginCli.cs` — `RunHighRiskEnvelopeCheck`):**
```csharp
var reasonMatched = stderrText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
    .Any(line =>
    {
        try
        {
            var node = JsonNode.Parse(line.Trim())?.AsObject();
            if (node is null) return false;
            var reason = node["reason"]?.GetValue<string>();
            var code = node["code"]?.GetValue<string>();
            return string.Equals(reason, "trusted-human-receipt-required", StringComparison.Ordinal)
                || string.Equals(code, "trusted-human-receipt-required", StringComparison.Ordinal);
        }
        catch { return false; }
    });
```
- stderr 각 줄을 독립적으로 JSON 파싱한다.
- `reason` 또는 `code` 필드가 정확히 `"trusted-human-receipt-required"`이어야 `reasonMatched=true`.
- 파싱 실패 줄은 무시. 어떤 줄에서도 정확 매칭이 없으면 `false`.

### 3. high-risk self-test의 canonical WORKSTATE 의존성이 숨겨져 있었음

**R3 보고의 부정확:** "self-test는 `$TEMP` 사본에서 실행하므로 canonical 저장소에 record를 생성하지 않았다" / "self-test는 tmpRoot의 fixture를 사용한다"라고 적었다. 하지만 high-risk 3종 case는 tmpRoot fixture와 무관하다.

**실제 실행 경로(코드 근거):**
1. `RunHighRiskEnvelopeCheck(kind)` → `StateApplierCli.Run(["state-transition", "apply", "--envelope", tmpEnv])`
2. `StateApplierCli.RunApply` → `LoadWorkstateContextFromRoot(GitTools.FindRepoRoot())` ← **canonical repo WORKSTATE 로드**
3. `ApplyEnvelopeCore`: `HighRiskKinds.Contains(envelope.TransitionKind)` → `Fail(1, ..., "trusted-human-receipt-required", ...)` ← **request/candidate 파일 읽기 전에 반환**

따라서 high-risk 3종 검사는:
- canonical `docs/handoff/WORKSTATE.json` 로드에 **의존한다**.
- placeholder `requestPath`·`candidatePath`에는 의존하지 않는다 (high-risk 분기가 그 전에 종료).

**이 의존성이 현재 검증에서 문제되지 않는 이유:**
- `LoadWorkstateContextFromRoot`가 null 반환하면 exit 2를 돌려주며, `CheckHighRiskClosed`는 `exit != 1`로 `false`를 반환한다. 즉 canonical WORKSTATE가 유효하지 않으면 trust-origin `declare` 자체가 차단된다 — fail-closed.
- 현재 canonical WORKSTATE.json이 정상이므로 self-test에서 exit 1 + reasonMatched=true가 정확히 나온다.

**R4 조치:** 코드에 주석 추가, self-test case note 수정, 본 문서에 명시.

## 사용한 하네스 및 결과

| 명령 | exit code | 핵심 수치 |
| --- | --- | --- |
| `dotnet build server -c Release -nologo` | 0 | 경고 0, 오류 0 |
| `dotnet run --project server -c Release -- trust-origin --self-test` | 0 | casesRun=18, verdict=PASS |
| `dotnet run --project server -c Release -- verify-behavior` | 0 | behaviorEqual:true |
| `dotnet run --project server -c Release -- measure dev-pack` | 0 | violationCount=0 |

## high-risk 3종 실제 exit와 JSON 정확 reason/code 매칭

| case | kind | exit | reasonMatched (JSON exact) | pass |
| --- | --- | --- | --- | --- |
| high-risk-full-envelope-phase-change | PHASE_CHANGE | 1 | true | true |
| high-risk-full-envelope-recovery | RECOVERY | 1 | true | true |
| high-risk-full-envelope-replay | REPLAY | 1 | true | true |

`StateApplierCli.Fail`이 출력하는 stderr JSON: `{"ok":false,"transitionId":"trust-origin-high-risk-check","reason":"trusted-human-receipt-required","detail":"..."}` — `reason` 필드가 정확히 `"trusted-human-receipt-required"`.

## high-risk 검사의 canonical WORKSTATE 의존성

- `StateApplierCli.RunApply`는 `GitTools.FindRepoRoot()` → canonical `docs/handoff/WORKSTATE.json`을 로드한다.
- high-risk 분기는 그 이후 `ApplyEnvelopeCore` 진입 직후에 실행되며, request/candidate 파일 읽기(Step 1 `VerifyApplyEvidence`)보다 먼저 반환된다.
- canonical WORKSTATE 로드 실패 시 exit 2 → `reasonMatched=false` → `CheckHighRiskClosed` false → 선행조건 10 미충족 → declare 차단(fail-closed).
- 현재 검증에서 문제되지 않는 이유: 현 저장소 WORKSTATE가 유효하고, WORKSTATE 로드는 high-risk 분기에 영향을 주지 않는다.

## hooks 형태별 self-test 결과

| case | settingsJson | CheckAutoLauncherOffResult | expectedResult | pass |
| --- | --- | --- | --- | --- |
| launcher-settings-malformed | (malformed JSON) | false | false | true |
| launcher-settings-hooks-empty-object | `{"hooks":{}}` | false | false | true |
| launcher-settings-hooks-array | `{"hooks":[]}` | false | false | true |
| launcher-settings-hooks-string | `{"hooks":"x"}` | false | false | true |
| launcher-settings-hooks-null | `{"hooks":null}` | false | false | true |
| launcher-settings-no-hooks | `{}` | true | true | true |

## canonical record 미생성 및 WORKSTATE/applier-log 무결성

- `docs/handoff/trust-origin/TO-2026-001.json`: 미생성 확인 (`test -f` → NOT EXISTS).
- `docs/handoff/WORKSTATE.json`: 변경 없음 (`git status` — nothing to commit).
- `docs/handoff/WORKSTATE.applier-log.jsonl`: 변경 없음 (동일 확인).

## 변경 파일 (allowlist 내)

- `server/TrustOriginCli.cs` — allowlist 허용. 직접 경로: 지시서 allowlist 명시.
- `docs/verification/06c2-r4-trust-origin-review-contract.md` — allowlist 허용. 직접 경로: docs/ 관례 허용.

`server/StateApplierCli.cs`는 읽기만 했고 수정하지 않았다.

## 직접 경로 사용 사유

`docs/verification/` 문서는 CLAUDE.md 관례상 직접 경로 허용.
`server/TrustOriginCli.cs`는 지시서 allowlist에 명시.

## 게이트 결과

`{"gate":"dev-pack","violations":0,"attempt":1}`

## 지표는 만족했으나 목적은 미달인 부분

없음. 고위험 reason 검사가 JSON 정확 매칭으로 강화됐고, canonical WORKSTATE 의존성이 코드·문서에 모두 명시됐다. R3의 reporting 계약 위반도 본 문서에서 닫혔다.

단, 지시서 C에서 언급한 추가 개선 가능성 중 하나를 명시한다: high-risk self-test case를 tmpRoot 격리 fixture에서 실행하려면 `RunHighRiskEnvelopeCheck`가 `root` 파라미터를 받아 `StateApplierCli.RunApplyCore` 같은 분리된 경로로 진입해야 한다. 이는 이번 지시서 scope 밖(StateApplierCli.cs 수정 금지)이며, 현재 구현이 fail-closed 방향으로 동작하므로 즉각적 안전 문제는 없다.
