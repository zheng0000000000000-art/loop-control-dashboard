# GWIT-04 검증 — **실행자가 범위를 근거로 착수를 거부했다** (설계대로)

- **주체(actor)**: 코덱스(`LAUNCH-GWIT-04`). 검증·기록은 조율 세션.
- **날짜**: 2026-07-26

## 결과: 아무것도 바꾸지 않았다 — 그리고 그것이 옳다

```
exitCode 0 · changedPaths [] · scopeViolations [] · pathsMissingFromPatch []
```

실행자의 답:

> 음성 여부를 전달받지 못합니다. 따라서 허용된 `HarnessRegistry.cs`나 `GateWitnessCheckCli.cs`만
> 수정하면 **이름 프록시 또는 수동 목록이 필요해져 §1-A를 위반**합니다.
> 세 루트 CLI를 대상으로 하는 후속 지시서가 필요합니다.

**지시서 §5가 요구한 그대로다** — *"allowlist 밖이면 고치지 말고 위치만 보고하라."*
조율자가 케이스 정의 위치를 **일부러 확인하지 않고** 쐈고, 실행자가 스스로 범위를 지켰다.

**오늘 두 번째 범위 거부이고 두 번 다 옳았다.** 첫 번째는 `CODEX-GATE-04`의 첫 실사격이었다.

## 조율자의 후속 확인 (실측)

케이스 정의는 `server/` 루트에 있다 — **`ADR-002`상 코덱스 영역이 아니라 조율자 영역이다.**

```
server/TrustOriginCli.cs    24 케이스
server/RecoveryCli.cs        8 케이스
server/StateApplierCli.cs   19 케이스 (다른 등록 형태)
```

등록은 `Add(cases, "name", RunCase(CaseX))` 형태이고, 각 `CaseX`의 마지막 `return`이
**무엇을 단언하는지 기계적으로 말해준다**:

```
return ... .ExitCode == 0    → 양성 (성공을 단언)
return ... .ExitCode == 1    → 음성 (거부를 단언)
return ... .ExitCode != 0    → 음성
```

즉 §1-A가 요구한 *"케이스 정의부에서 파생"*이 가능하다. 이름을 보지 않아도 된다.

## 지표는 만족했으나 목적은 미달인 부분

1. **`LAND`의 self-test 3건은 여전히 반증 없음이다.** 이 발사로 줄지 않았다.
   `totalUnwitnessed`는 **8 그대로**다.
2. **구현은 조율자 몫으로 남는다.** `server/` 루트는 코덱스 영역이 아니므로 `CodexHarnessLauncher`로
   쏠 수 없다(`allowed-paths-outside-codex-territory`). 직접 경로로 해야 하며 사유를 남겨야 한다.
3. 실행자 로그에 NuGet 서명 인덱스 TLS 실패(`NU1301`)가 함께 찍혔다. 격리 워크트리의 환경
   문제이며 위 판단과는 독립이다 — **다만 그 상태에서 빌드가 중단됐으므로, 이 발사는 범위
   판단만 검증했고 코드 검증은 하지 않았다.**
