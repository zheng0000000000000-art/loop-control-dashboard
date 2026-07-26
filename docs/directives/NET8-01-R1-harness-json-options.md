```context-pack
{
  "diId": "NET8-01-R1",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/portability-linux-2026-07-27.md", "sha256": "a9e60b10962ed17b77109ea5f4452c3f00451525bca142ba2b5ae073e9233d22" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/directives/NET8-01-R1-harness-json-options.md",
    "docs/verification/portability-linux-2026-07-27.md"
  ],
  "forbiddenActions": ["git commit", "git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

# NET8-01-R1 — 하네스 JSON 옵션 통합 (재시도)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.

## 0. 앞 시도(`LAUNCH-NET8-01`)가 왜 반려됐나 — 그리고 무엇이 내 잘못이었나

**반려 사유는 하나다: 패치가 컴파일되지 않았다.**

```
LaunchDispositionCli.cs(40)  JsonSerializer 없음
LaunchDispositionCli.cs(63,66,137,139)  JsonException 없음
LaunchCheckCli.cs(65,67)     JsonException 없음
→ 로컬 7건, 리눅스 .NET 8 컨테이너 7건. 양쪽 BUILD FAIL.
```

옵션 선언을 지우면서 **`using System.Text.Json;`을 같이 지웠는데**, 그 파일들은 여전히
`JsonException`·`JsonSerializer`를 쓴다. `using System.Text.Encodings.Web;`는 지워도 되지만
**`System.Text.Json`은 파일마다 다르다.**

**방향은 맞았다.** 19개 파일을 `HarnessJson.Options`로 정확히 갈아탔고 `scopeViolations`도 없었다.
**틀린 것은 마감이다.**

### 조율자 잘못 두 가지 (이번에 고쳤다)

1. **작업 보고를 쓸 자리를 안 줬다.** `allowedPaths`를 `server/Harness/**`로만 열었는데
   지시서는 보고를 `docs/verification/`(영토 밖)에 쓰라고 했다. **네가 어디에도 보고를 남길 수
   없었고**, 그래서 자진 신고도 게이트 결과도 없었다. 이번엔 `docs/qa/gate-witness/**`를 연다.
2. **`build-verify`가 격리 사본에서 못 도는 문제를 방치했다.** 앞선 두 발사(TERR-01·TERR-02)에서
   네가 `NU1301`(NuGet 인증서)로 `build-verify`가 exit 1이라고 **정직하게 보고했는데**,
   나는 "환경 탓"으로 넘겼다. **그 결과 이번엔 컴파일조차 안 되는 산출물이 나왔다.**
   §3에 대체 수단을 명시한다.

## 1. 무엇을 하나 (앞과 동일)

`server/Harness/`의 17개 파일이 각자 만드는 `JsonSerializerOptions`를 **한 벌로 모은다.**
`HarnessJson.Options`에 `TypeInfoResolver = new DefaultJsonTypeInfoResolver()`를 준다 —
그것이 없어 **.NET 8에서 `hs-scan`이 exit 2로 죽고 POST-COMMIT이 FAIL**이다.

앞 시도의 `HarnessJson.cs`는 그대로 쓸 만하다:

```csharp
internal static class HarnessJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };
}
```

값이 다른 옵션이 있으면(들여쓰기 끔 등) **갈아타지 말고 사유를 보고에 적어라.**

`CliOptions.cs`·`GateReportReader.cs`·`SelfTestCensus.cs`가 같은 전례다.

## 2. `using` 정리 규칙 — 이번 반려의 핵심

**옵션 선언을 지웠다고 `using`을 같이 지우지 마라.** 파일마다 따로 판단한다.

- `using System.Text.Encodings.Web;` — `JavaScriptEncoder`를 그 파일이 더 이상 안 쓰면 지운다.
- **`using System.Text.Json;` — `JsonSerializer`·`JsonException`·`JsonSerializerOptions` 중
  하나라도 남아 있으면 지우지 마라.** 최소 두 파일이 여기 걸린다
  (`LaunchDispositionCli.cs`, `LaunchCheckCli.cs`). **다른 파일도 직접 확인하라 — 이 둘만이라고
  가정하지 마라.**

**판단 근거는 컴파일러다.** 지운 뒤 빌드해서 확인해라. 눈으로 훑지 마라.

## 3. 빌드를 반드시 통과시켜라 — 못 하면 제출하지 마라

**`build-verify`가 격리 사본에서 `NU1301`로 실패하면, 실패했다고 적고 넘어가지 마라.**
아래 중 하나로 **컴파일이 통과하는 것을 실제로 보여라.**

```
# (가) 오프라인 복원
dotnet build server -v q --nologo --no-restore

# (나) 이미 있는 패키지로 복원
dotnet restore server --source server/obj --ignore-failed-sources
dotnet build server -v q --nologo --no-restore

# (다) 타깃 대체
dotnet build server -p:TargetFramework=net10.0 -v q --nologo
```

**어느 것도 안 되면 그 사실을 보고 맨 위에 쓰고 산출물을 내지 마라.**
컴파일되지 않는 패치는 반입할 수 없고, 조율자가 다시 재는 데 시간이 든다.

보고에 **`오류 0개`가 나온 명령과 그 출력 꼬리를 그대로 붙여라.** "빌드했다"는 증거가 아니다.

## 4. 반증 시험

**컨테이너에서 재라. 로컬 결과는 증거가 아니다** — 로컬은 `RollForward`로 .NET 10에서 돈다.

```
docker run --rm -v <repo>:/src:ro mcr.microsoft.com/dotnet/sdk:8.0 bash -c '
  git clone -q /src /work && cd /work
  git config --global --add safe.directory /work
  dotnet build server -v q --nologo
  dotnet run --project server --no-build -- hs-scan; echo "hs-scan=$?"
  dotnet run --project server --no-build -- di-completion-check --gate POST-COMMIT --task t'
```

> **주의**: `git clone`은 **커밋된 내용만** 가져간다. 격리 사본에서 커밋을 못 하면
> `cp -r`로 복사해서 재라. 조율자가 이 함정에 한 번 빠져 옛 코드를 새 코드로 착각했다.

| | 수정 전 | 기대 |
| --- | --- | --- |
| `hs-scan` (.NET 8) | exit **2** | exit **1** (매니페스트 기대값) |
| POST-COMMIT (.NET 8) | **FAIL** | **PASS 22/22** |
| LAND (.NET 8) | PASS | PASS |
| .NET 10 (회귀) | PASS | PASS |

**나머지 하네스도 .NET 8에서 한 번씩 돌려 exit code를 표로 남겨라.** 인자가 필요해 exit 2가
정상인 것은 그렇게 적어라. "고쳤으니 될 것이다"는 증거가 아니다.

## 5. `.github/workflows/gates.yml`은 건드리지 마라

영토 밖이라 쓸 수 없다. **`.NET 8` 다리 켜기는 조율자가 반입 때 한다.**
네 몫은 **컨테이너에서 초록임을 보이는 것**이다 — 그 증거로 조율자가 matrix에 `"8.0"`을 넣는다.

## 6. 허용 파일 (allowlist)

- server/Harness/**
- docs/qa/gate-witness/**

**둘 다 코덱스 영토 안이다.** 작업 보고는 **`docs/qa/gate-witness/NET8-01-R1.md`**에 남겨라.
`docs/verification/net8-01-r1.md`는 조율자가 반입 때 만든다.

## 7. 검수 기준

지표 기준(기계 판정). `_header.md`의 공통 항목에 아래를 더한다.

- [ ] **컴파일 통과** — §3의 명령 중 하나가 `오류 0개`. **출력 꼬리를 보고에 붙였다**
- [ ] `verify-behavior` → `behaviorEqual: true`
- [ ] `measure dev-pack` **violations 0**
- [ ] `scope-check` — 변경 파일이 위 allowlist 안
- [ ] **컨테이너 .NET 8**에서 `hs-scan` exit **1**
- [ ] **컨테이너 .NET 8**에서 POST-COMMIT **PASS 22/22**
- [ ] **컨테이너 .NET 8**에서 LAND **PASS**
- [ ] **컨테이너 .NET 10**에서도 위 셋 PASS (회귀 없음)
- [ ] `server/Harness/`에 `new JsonSerializerOptions`가 `HarnessJson` 말고 남아 있지 않다
- [ ] `docs/qa/gate-witness/NET8-01-R1.md`에 주체·하네스 결과·참조 스킬·자진 신고 기록

### 목적 기준 (사람 판정)

- **컴파일되는가.** 이것이 앞 시도의 반려 사유다. 다른 무엇도 이것을 대신하지 못한다.
- **정의가 한 벌인가.**
- **다리가 초록으로 도착할 준비가 됐는가.** 컨테이너 .NET 8이 빨간 채로 제출하면 반려다.

## 8. 작업 보고 (`docs/qa/gate-witness/NET8-01-R1.md`)에 반드시 적을 것

1. **주체** 2. **사용한 하네스와 결과**(명령·exit code·핵심 수치) 3. **참조한 스킬**
4. **`## 지표는 만족했으나 목적은 미달인 부분`** — 없으면 "없음"과 근거.

게이트 결과는 JSON 한 줄로: `{"gate":"dev-pack","violations":0,"attempt":1}`
