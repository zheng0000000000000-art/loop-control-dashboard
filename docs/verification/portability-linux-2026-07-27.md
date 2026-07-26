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

---

# append — CI 등재 (2026-07-27)

## 무엇을 켰나

`.github/workflows/gates.yml`. push·PR·수동 실행에서 돈다.

- **linux**: `mcr.microsoft.com/dotnet/sdk:<ver>` **컨테이너 안에서** 돈다.
  런타임을 `setup-dotnet`+환경변수로 고르지 않는 이유는 러너에 여러 SDK가 깔려 있어
  **"어느 런타임이 돌았나"가 설정 문제로 남기 때문이다.** 이미지 안에 그 버전 하나뿐이면
  그건 설정이 아니라 사실이 된다.
- **windows / 10.0**: 이 저장소의 실제 개발 환경을 지킨다.

각 잡: build → 하네스 10종 → LAND → POST-COMMIT. 통합 게이트 앞에서
`git checkout -- . && git clean -fdq` — 앞선 `measure`가 `dashboard/data`를 다시 써서
`gate-clean`(order 1)이 더러운 트리를 보게 되기 때문이다.

**컨테이너에서 워크플로 단계를 그대로 예행했다.** YAML을 믿고 올리지 않았다.

## CI가 즉시 두 번째 사례를 잡았다

앞의 자진 신고 §3("measure 외의 서버 경로는 .NET 8에서 안 돌려봤다")이 **바로 실현됐다.**

```
POST-COMMIT FAIL (linux / .NET 8)
  order 5  hs-scan  기대 1 / 실제 2
  {"error":"hs-scan 실패: JsonSerializerOptions instance must specify a TypeInfoResolver..."}
```

`server/Harness/` 안 **17개 파일이 각자 같은 옵션을 만든다.** 지금 터지는 건 `hs-scan`
하나지만 — 원시 타입이 아닌 `JsonValue`를 트리에 넣는 게 그것뿐이라서다 — 나머지 16개도
같은 지뢰를 밟을 준비가 돼 있다.

## .NET 8 다리를 지금 켜지 않은 이유

**켜면 영구 적색이고, 영구히 빨간 게이트는 무시된다**(FAIL-2026-010).
matrix에서 `"8.0"`을 빼되 **"잊어서 빠진 게 아니다"를 주석으로 박았다.**
`NET8-01` 지시서가 옵션을 한 벌로 모으고 **그 목록에 `"8.0"`을 넣는다** — 다리는 초록으로 도착한다.

## 왜 조율자가 직접 안 고쳤나

`server/Harness/`는 ADR-002상 코덱스 영토다. 오늘 이미 세 번 침범했고
(`TERR-01`이 그걸 막으려는 지시서다), **네 번째를 하면서 그 지시서를 쓰는 것은 앞뒤가 안 맞는다.**
`Storage.cs`는 `server/` 루트라 조율자 몫이 맞아 직접 고쳤다.

## 지표는 만족했으나 목적은 미달인 부분

1. **CI가 실제로 GitHub에서 도는 것을 아직 못 봤다.** 컨테이너 예행은 했지만
   Actions 실행은 push 이후에나 확인된다. **"YAML이 유효하고 단계가 로컬에서 통과한다"까지가
   지금의 증거다.**
2. **windows 잡은 예행하지 않았다.** 리눅스 컨테이너로만 돌려봤다. Windows 러너에서
   `shell: bash`·`git clean` 동작은 미검증이다.
3. **CI가 게이트 매니페스트에 없다.** CI 자체가 깨져도 로컬 게이트는 초록이다 —
   `gate-witness-check`가 CI를 모른다. **CI를 지키는 것은 여전히 사람이다.**

## 정정 — windows 잡은 이름과 다른 런타임에서 돌고 있었다

첫 CI 실행 결과: **linux / 10.0 ✓ (1m25s)**, **windows / 10.0 ✗** — `hs-scan` 기대 1 / 실제 2,
사유는 같은 `TypeInfoResolver`다.

`setup-dotnet 10.0.x`를 깔았는데도 **.NET 8에서 돌았다.** 러너 로그:

```
Microsoft.NETCore.App 8.0.6 / 8.0.22 / 8.0.28 / 9.0.6 / 9.0.17 / 10.0.8 / 10.0.9 / 10.0.10
```

이 프로젝트는 `net8.0`을 겨냥하므로 **정확히 맞는 8이 있으면 그것을 고른다.** `RollForward=Major`는
없을 때만 올라간다. 로컬이 10에서 돌던 이유는 **로컬에 `Microsoft.AspNetCore.App` 8이 없어서**다
(`NETCore.App` 8.0.27은 있지만 Web 앱이 요구하는 AspNetCore 8이 없다). 실측:

```
기본                              hs-scan=1
DOTNET_ROLL_FORWARD=LatestPatch   실행 실패 150 (AspNetCore.App 8.0.0 없음)
DOTNET_ROLL_FORWARD=LatestMajor   hs-scan=1
```

**즉 "이 저장소는 .NET 10에서 돈다"는 로컬 환경의 우연이었다.** 8이 깔린 기계라면 어디서든
`hs-scan`이 깨진다 — 리눅스만의 문제가 아니었다. 앞 절에서 "linux / .NET 8"이라고 좁게 쓴 것을
여기서 넓힌다.

windows 잡에 `DOTNET_ROLL_FORWARD: LatestMajor`를 준다. **이름과 실재를 맞추는 것이지
버그를 덮는 게 아니다** — .NET 8 검증은 linux 컨테이너 다리가 맡고, 그건 `NET8-01`이 켠다.

## global.json — SDK 하한선을 선언한다

앞 절 자진 신고 §2("어느 SDK가 정본인지 저장소가 말하지 않는다")를 닫는다.

```json
{ "sdk": { "version": "8.0.100", "rollForward": "latestMajor" } }
```

**정확한 버전을 못 박지 않았다.** 못 박으면 CI의 `sdk:8.0` 컨테이너가 **빌드조차 못 한다** —
그 다리는 `NET8-01`이 켤 예정이라 지금 막아두면 안 된다. `latestMajor`는
"설치된 것 중 가장 높은 것을 쓰되 8.0.100보다 낮으면 거부한다"는 뜻이다.

**이건 SDK(빌드 도구) 선언이지 런타임 선언이 아니다.** 어느 런타임에서 도는지는
`TargetFramework` + `RollForward` + 그 기계에 깔린 런타임이 정한다 —
windows CI가 이름은 `10.0`인데 .NET 8에서 돌던 것이 정확히 그 차이였다. **혼동하지 마라.**

### 실측 — 세 환경 + 반증

| 환경 | SDK | build |
| --- | --- | --- |
| 로컬 Windows | 10.0.301 | **0** |
| `sdk:10.0` 컨테이너 | 10.0.302 | **0** |
| `sdk:8.0` 컨테이너 | 8.0.423 | **0** |

**하한선이 실제로 거부하는지 반증했다.** 하한을 `99.0.100`으로 위조하면
`build exit=155`, 메시지에 `global.json`과 `not found`가 들어간다. 원복하면 `0`.
*(첫 시도에서 `dotnet ... | head -4` 뒤의 `$?`를 읽어 "exit 0"으로 잘못 봤다.
파이프라인 뒤의 `$?`는 `head`의 것이다 — exit code로 판정하라는 규칙을 내가 어겼고, 다시 쟀다.)*

`_comment` 키는 SDK 파서가 무시한다 — 세 환경 모두 build 0으로 확인했다.
