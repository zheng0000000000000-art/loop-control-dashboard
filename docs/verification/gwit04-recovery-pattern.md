# GWIT-04 패턴 증명 — `RecoveryCli` 8건으로 먼저 확인

- **주체(actor)**: 조율 세션(직접 경로). `server/` 루트는 `ADR-002`상 코덱스 영역이 아니라
  `CodexHarnessLauncher`로 쏠 수 없다 — `GWIT-04` 발사에서 실행자가 정확히 그 이유로 착수를
  거부했고 그 판단이 옳았다.
- **날짜**: 2026-07-26

## 설계 — §1-A의 가장 강한 형태

`Add`에 `negative`를 **필수 인자**로 넣었다.

```csharp
private static void Add(JsonArray cases, string name, bool pass, bool negative)
```

**케이스를 추가하면서 표시를 잊으면 컴파일이 안 된다.** 손으로 유지하는 목록도, 이름 문자열
추정도 없다. `GWIT-04` §1-A가 *"케이스 정의부에서 파생, 별도 목록 금지"*를 요구했고 컴파일러가
그것을 강제한다.

분류 근거는 각 케이스가 무엇을 단언하는가다:

| 케이스 | 단언 | |
| --- | --- | --- |
| `pending-*` 4건 | `HasPendingCode(...)` — 미해결 상태가 보고되는가 | 음성 |
| `state-only-gap`·`conflicting-success` | `HasFailureCode(...)` | 음성 |
| `high-risk-stays-closed` | `recoveryApplyReady == false` — 거부 유지 | 음성 |
| `evidence-package` | 산출물이 생성되는가 | 양성 |

7 음성 / 1 양성. `RecoveryCli` self-test가 **결함 탐지를 확인하는 것**이므로 자연스럽다.

## 반증 시험 (지시서 §4)

| # | 시험 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1 | 실행 | `negativeCaseCount >= 1`, `negative` 합계와 일치 | **7 / 7 일치** |
| 2 | **음성 케이스 하나 비활성화** | 1 감소 | `casesRun` 8→7, `negativeCaseCount` 7→**6** |
| 3 | **양성 케이스 하나 비활성화** | 그대로 | `casesRun` 8→7, `negativeCaseCount` **7 유지** |
| 4 | 연속 실행 후 워크트리 | 변경 0 | (비파괴 — 임시 디렉터리에서만 돈다) |

**2와 3이 함께 상수 구현을 배제한다.** 하나만으로는 부족하다 — 2만 보면 "케이스 수에 연동된
상수"일 수 있고, 3만 보면 "아무것도 안 하는 상수"일 수 있다.

## §1-C 검증도 확인했다 — 거짓 주장이 막힌다

매니페스트에 `internalNegativeCases: 7`을 선언하니 `recovery-selftest`가 반증 없는 목록에서
빠졌다(**LAND 5 → 4**, 전체 **8 → 7**).

**99로 올려 거짓 주장을 만들자 다시 목록에 들어갔다.**

```
internalNegativeCases: 99 (실제 7)
  → LAND unwitnessed: [... 'recovery-selftest' ...]
```

`gate-witness-check`가 **주장을 믿지 않고 실제로 실행해 세어 대조한다.** `GWIT-01` §1-C가
설계한 그대로다.

## 조율자가 저지른 실수 (기록)

시험 2 직후 `git checkout -- server/RecoveryCli.cs`로 되돌렸는데, **그 변경이 아직 커밋되지
않아 구현 전체가 지워졌다.** 시험 3은 원본을 잰 것이었고 `negativeCaseCount`가 `None`으로
나왔다. 재적용하고 **커밋한 뒤에** 시험 3을 다시 돌려 통과를 확인했다.

미커밋 상태에서 `git checkout --`를 임시 되돌림으로 쓴 것이 원인이다.

## 지표는 만족했으나 목적은 미달인 부분

1. **`StateApplierCli`(19건)와 `TrustOriginCli`(24건)는 그대로다.** 패턴만 증명했다.
   남은 반증 없는 검사 7건 중 2건이 그 둘이다.
2. **시험 4(비파괴)를 이번에 따로 재지 않았다.** `HREG-01` 착륙 때 세 self-test 연속 실행 후
   워크트리 변경 0을 확인했고 이번 변경은 출력 필드 추가뿐이라 성질이 유지된다고 보았으나,
   **재측정하지는 않았다.**
3. `LAND`는 여전히 `requireFailureWitness`를 켜지 않았다. 4건이 남아 있다.
