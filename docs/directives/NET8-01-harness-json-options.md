```context-pack
{
  "diId": "NET8-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/portability-linux-2026-07-27.md", "sha256": "534c8299b89bd754dfcabf32ce80a16418c2dbb47216e7fb29528b44ca899238" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/directives/NET8-01-harness-json-options.md",
    "docs/verification/portability-linux-2026-07-27.md"
  ],
  "forbiddenActions": ["git commit", "git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

# NET8-01 — 하네스 JSON 옵션을 한 벌로 모으고 .NET 8 다리를 켠다

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.

## 1. 왜 (실측 근거)

**2026-07-27 실측**: 리눅스 + .NET 8 컨테이너에서

```
POST-COMMIT FAIL
  order 5  hs-scan  기대 1 / 실제 2
  stderr: {"error":"hs-scan 실패: JsonSerializerOptions instance must specify
           a TypeInfoResolver setting before being marked as read-only."}
```

`Storage.JsonOptions`는 이미 고쳤다(`46c1d5f`). 그런데 **`server/Harness/` 안 17개 파일이
각자 같은 옵션을 다시 만든다.**

```
BuildVerifyCli · CallIntegrityCheckCli · ClaimCheckCli · ContextPackIntegrityCli
DiCompletionCheckCli · DocIntegrityCli · E2EUsageCli · GateCleanCli · GateWitnessCheckCli
HandoffIntegrityCli · HsScanCli · LaunchCheckCli · LaunchDispositionCli · PathGuardCheckCli
ProjectApiEdgeCheckCli · ScopeCheckCli · TemplateSyncCheckCli
```

지금 터지는 것은 `hs-scan` 하나지만 — 원시 타입이 아닌 값으로 만든 `JsonValue`를 트리에 넣는
하네스가 그것뿐이라서다 — **나머지 16개도 같은 지뢰를 밟을 준비가 돼 있다.**

**한 벌로 모으는 이유는 편의가 아니다.** 이 저장소는 정의를 두 벌 둔 대가를 이미 두 번 치렀다:
`RequiredGateCommands`가 이름 교체 뒤 낡아 `--gate-report` 경로가 통째로 죽었고,
`SelfTestGateCounts`는 생산자와 검사자가 같은 표를 읽어 대조가 공회전이었다.
**17벌은 17번 틀릴 수 있다.**

## 2. 무엇을 하나

### 2-1. 옵션 정의를 하나로

`server/Harness/`에 공용 정의를 만든다(예: `HarnessJson.cs`).

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

17개 파일의 `private static readonly JsonSerializerOptions JsonOptions = new() {...}`를
**전부 이것으로 갈아탄다.** 값이 다른 것이 있으면(들여쓰기 끔 등) **갈아타지 말고 그 사유를
verification에 적어라** — 다르게 만들 이유가 있으면 그건 통합 대상이 아니다.

`CliOptions.cs`·`GateReportReader.cs`·`SelfTestCensus.cs`가 같은 전례다. 그 모양을 따르라.

### 2-2. .NET 8 다리를 켠다

`.github/workflows/gates.yml`의 linux matrix를 `sdk: ["8.0", "10.0"]`으로 바꾸고,
"8.0이 빠져 있는 이유"를 적은 주석을 **지운다**(더 이상 사실이 아니게 되므로).

**고치기 전에 켜지 마라.** 영구 적색 다리는 무시된다(FAIL-2026-010).

## 3. 반증 시험 — 무엇이 이 수정을 증명하나

**컨테이너에서 직접 재라. 로컬 결과는 증거가 아니다** — 로컬은 `RollForward`로 .NET 10에서
돈다. 그래서 이 버그가 지금까지 안 보였다.

```
docker run --rm -v <repo>:/src:ro mcr.microsoft.com/dotnet/sdk:8.0 bash -c '
  git clone -q /src /work && cd /work
  git config --global --add safe.directory /work
  dotnet build server -v q --nologo
  dotnet run --project server --no-build -- hs-scan; echo "hs-scan=$?"
  dotnet run --project server --no-build -- di-completion-check --gate POST-COMMIT --task t'
```

- 수정 전: `hs-scan` **exit 2**, POST-COMMIT **FAIL**
- 수정 후: `hs-scan` **exit 1**(매니페스트 기대값), POST-COMMIT **PASS 18/18**

**나머지 16개도 .NET 8에서 한 번씩 돌려 exit code를 verification에 표로 남겨라.**
"고쳤으니 될 것이다"는 증거가 아니다. 인자가 필요해 exit 2가 정상인 것들은 그렇게 적어라.

## 4. 허용 파일 (allowlist)

- server/Harness/**
- .github/workflows/gates.yml
- docs/verification/net8-01.md

## 5. 검수 기준

지표 기준(기계 판정). `_header.md`의 공통 항목에 아래를 더한다.

- [ ] `build-verify` exit 0
- [ ] `verify-behavior` → `behaviorEqual: true`
- [ ] `measure dev-pack` **violations 0**
- [ ] `scope-check` — 변경 파일이 위 allowlist 안
- [ ] **컨테이너 .NET 8**에서 `hs-scan` exit **1**
- [ ] **컨테이너 .NET 8**에서 `di-completion-check --gate POST-COMMIT` **PASS 18/18**
- [ ] **컨테이너 .NET 8**에서 `--gate WP-STATE-INTEGRITY-LAND` **PASS 18/18**
- [ ] 컨테이너 .NET 10에서도 위 둘 PASS (회귀 없음)
- [ ] `gates.yml`의 linux matrix에 `"8.0"`이 들어가고 유예 주석이 지워졌다
- [ ] verification 문서에 주체·하네스 결과·참조 스킬·자진 신고 기록

### 목적 기준 (사람 판정)

- **정의가 한 벌인가.** `server/Harness/`에 `new JsonSerializerOptions`가 **공용 정의 한 곳
  말고 남아 있으면 미달**이다(값이 달라야 할 사유를 적은 것은 예외).
- **다리가 초록으로 도착했는가.** `"8.0"`을 넣은 채 빨간 상태로 제출하면 반려다.
- **16개를 실제로 돌려봤는가.** 표가 없으면 미달이다.

## 6. verification 문서 (`docs/verification/net8-01.md`)에 반드시 적을 것

1. **주체** 2. **사용한 하네스와 결과**(명령·exit code·핵심 수치) 3. **참조한 스킬**
4. **`## 지표는 만족했으나 목적은 미달인 부분`** — 없으면 "없음"과 근거.

게이트 결과는 JSON 한 줄로: `{"gate":"dev-pack","violations":0,"attempt":1}`
