# 이식성 실측 — 리눅스 컨테이너 (2026-07-27)

## 주체

**조율자(Claude).** 사용자가 "잰다"를 골랐다. 코덱스 영토를 건드리지 않는 작업이라 직접 경로.

## 왜 쟀나

`.github/workflows`가 **없다 — CI가 아예 없다.** 지금까지의 초록은 **전부 이 컴퓨터에서 난 값**이고,
"다른 데서도 된다"는 **주장이지 실측이 아니었다.**

## 어떻게

`mcr.microsoft.com/dotnet/sdk` 컨테이너에서 로컬 저장소를 **clone**해 build + 게이트를 돌렸다.
mount가 아니라 clone인 이유: 그래야 **줄 끝이 실제로 LF로 체크아웃**된다. 그게 첫 의심 지점이었다.

## 결과 — OS가 아니라 런타임이었다

| 환경 | build | measure | self-test 3종 | doc/handoff-integrity | LAND |
| --- | --- | --- | --- | --- | --- |
| Windows / .NET 10 (로컬) | 0 | 0 | 0 | 0 | 0 |
| Linux / .NET 10 | 0 | **0** | 0 | 0 | **0** |
| Linux / .NET 8 (수정 전) | 0 | **2** | 0 | 0 | **1** |
| Linux / .NET 8 (수정 후) | 0 | **0** | 0 | 0 | **0** |

**CRLF는 문제가 아니었다.** clone 직후 `WORKSTATE.json`·`TrustOriginCli.cs` 모두 LF인데
`handoff-integrity`가 exit 0이다 — `NHASH-01`이 이미 해시를 작업 트리 줄바꿈에서 떼어놨다.
**의심은 맞았고 답은 틀렸다. 재보길 잘했다.**

## 진짜 원인

```
System.InvalidOperationException: JsonSerializerOptions instance must specify a
TypeInfoResolver setting before being marked as read-only.
  at System.Text.Json.Nodes.JsonValueCustomized`1.WriteTo(...)
  ...
  at Storage.WriteJson(String path, JsonNode node) in server/Storage.cs:line 259
  at Storage.WriteBundle(...) → MeasurementService.RunMeasureCore(...)
```

스택을 얻으려고 **컨테이너 안 클론에서만** `CliError(error.Message)`를 `error.ToString()`으로
바꿔 다시 돌렸다. 저장소는 안 건드렸다. 메시지만 보고는 어느 줄인지 알 수 없었다.

원시 타입이 아닌 값으로 만들어진 `JsonValue`가 `measurement.json`의 `evidence` 배열에 섞인다.
쓸 때 resolver가 필요한데 **.NET 8은 없으면 던지고 .NET 10은 기본값으로 넘어간다.**

**`TargetFramework`는 `net8.0`인데 `RollForward=Major`라 로컬에선 .NET 10에서 돈다.**
그래서 선언과 실재가 갈라져 있었고, 아무도 몰랐다.

## 고친 것

`Storage.JsonOptions`에 `TypeInfoResolver = new DefaultJsonTypeInfoResolver()` 한 줄.
**대상 프레임워크를 올리지 않았다** — 올리면 .NET 8 환경을 잘라내는 것이라 이식성이 **좁아진다.**
에러 메시지가 요구한 그대로를 준 것이 맞는 방향이다.

동작 보존: 로컬 재측정에서 산출물 형식 변화 없음(run-log 추가분·타임스탬프만 달라짐),
`measure=0` · 세 self-test=0 · `verify-behavior`·`doc-integrity`·`gate-witness-check`·
`handoff-integrity`=0 · **LAND 18/18 PASS.**

## 참조한 스킬

`skills/common/` 전부. 도메인 스킬은 이번 변경 경로(`server/Storage.cs`)와 맞는 트리거가 없었다.

## 지표는 만족했으나 목적은 미달인 부분

1. **CI를 만들지 않았다.** 이 실측은 **오늘 한 번**이다. 내일 누가 또 `JsonValue.Create`로
   커스텀 값을 넣으면 **아무도 못 잡는다.** 이식성은 여전히 **게이트가 아니라 기억**이다.
   사용자가 "잰다"만 골랐으므로 CI 등재는 하지 않았다 — **다음 결정 대상으로 남긴다.**
2. **`net8.0` 선언과 실제 요구가 여전히 어긋나 있다.** 이제 .NET 8에서 돌지만,
   `global.json`이 없어 어느 SDK가 정본인지 저장소가 말하지 않는다. 손대지 않았다.
3. **`measure` 외의 서버 경로(HTTP API·BalanceTuner·Tier2Approver)는 .NET 8에서 안 돌려봤다.**
   같은 `JsonValue.Create` 패턴이 `OutboxManager`·`Tier2Approver`에도 있다 —
   **그 경로들은 미검증이다.** 게이트가 도는 경로만 쟀다.
4. **`docker`가 있는 환경을 전제했다.** 다른 형태(맥·순수 리눅스 호스트)는 안 쟀다.
