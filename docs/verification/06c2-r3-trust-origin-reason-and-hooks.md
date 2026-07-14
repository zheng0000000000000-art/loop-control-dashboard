# 06C-2-R3 검수 보고 — trust-origin reason and hooks

## 주체 (actor)

CORE_INFRA_EXECUTOR (claude-sonnet-4-6, 06C-2-R3)

## 반려 사유 2개가 닫힌 방법

### 반려 사유 1: high-risk 검사가 exit 1만 보고 stderr를 버렸다

**기존 구현(R2):**
```csharp
Console.SetError(System.IO.TextWriter.Null);  // stderr 폐기
int exit = StateApplierCli.Run(...);
return exit;  // exit code만 반환
```

**수정(R3):**
- `RunHighRiskEnvelopeCheck` 반환 타입을 `int` → `(int exit, string stderrText, bool reasonMatched)`로 변경
- `System.IO.StringWriter`로 stderr를 캡처하고 `stderrText.Contains("trusted-human-receipt-required")`로 reason 일치 여부를 확인
- `CheckHighRiskClosed`에서 `exit != 1 || !reasonMatched`이면 false 반환

변경 위치: `server/TrustOriginCli.cs` — `RunHighRiskEnvelopeCheck`, `CheckHighRiskClosed`

### 반려 사유 2: hooks 판정이 `JsonObject && Count > 0`으로 좁았다

**기존 구현(R2):**
```csharp
if (node?["hooks"] is JsonObject hooks && hooks.Count > 0) return false;
```
`{"hooks":{}}`, `{"hooks":[]}`, `{"hooks":"x"}`, `{"hooks":null}` 는 통과했다.

**수정(R3):**
```csharp
if (node is JsonObject obj && obj.ContainsKey("hooks")) return false;
```
hooks 필드가 존재하면 값이 무엇이든 fail-closed.

변경 위치: `server/TrustOriginCli.cs` — `CheckAutoLauncherOff`

## 사용한 하네스 및 결과

| 명령 | exit code | 핵심 수치 |
| --- | --- | --- |
| `dotnet build server -c Release -nologo` | 0 | 경고 0, 오류 0 |
| `dotnet run --project server -c Release -- trust-origin --self-test` | 0 | casesRun=18, verdict=PASS |
| `dotnet run --project server -c Release -- measure dev-pack` | 0 | violationCount=0 |

게이트 결과: `{"gate":"dev-pack","violations":0,"attempt":1}`

## high-risk 3종 self-test 결과 (reasonMatched)

| case | kind | exit | reasonMatched | pass |
| --- | --- | --- | --- | --- |
| high-risk-full-envelope-phase-change | PHASE_CHANGE | 1 | true | true |
| high-risk-full-envelope-recovery | RECOVERY | 1 | true | true |
| high-risk-full-envelope-replay | REPLAY | 1 | true | true |

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

- `docs/handoff/trust-origin/TO-2026-001.json`: self-test는 `$TEMP` 사본에서 실행하므로 canonical 저장소에 record를 생성하지 않았다.
- `docs/handoff/WORKSTATE.json`: 변경 없음. self-test는 tmpRoot의 fixture를 사용한다.
- `docs/handoff/WORKSTATE.applier-log.jsonl`: 변경 없음. 동일 이유.

## 변경 파일 (allowlist 내)

- `server/TrustOriginCli.cs` — allowlist 허용
- `docs/verification/06c2-r3-trust-origin-reason-and-hooks.md` — allowlist 허용

`server/StateApplierCli.cs`는 읽기만 했고 수정하지 않았다.

## 참조한 스킬

없음 (skills/common/ 경로 없이 직접 지시서 수행)

## 직접 경로 사용 사유

`docs/verification/` 문서는 CLAUDE.md 관례상 직접 경로 허용.  
`server/TrustOriginCli.cs`는 지시서 § allowlist에 명시. 직접 수정.

## 지표는 만족했으나 목적은 미달인 부분

없음.

- `RunHighRiskEnvelopeCheck`는 실제 `StateApplierCli.Run`을 호출하고 stderr를 캡처해 `trusted-human-receipt-required` 문자열 포함 여부로 판정한다. placeholder envelope로도 high-risk 분기가 먼저 실행돼 올바른 reason이 나온다는 것은 `StateApplierCli.cs` 코드로 확인했다(`HighRiskKinds.Contains` 체크가 파일 읽기보다 앞에 있다).
- hooks 판정은 `ContainsKey`로 필드 존재 자체를 검사하므로 값 형태에 무관하게 fail-closed가 성립한다.
