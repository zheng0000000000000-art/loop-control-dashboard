# GWIT-05 반입 검증 — `build-verify` 픽스처 모드

- **주체(actor)**: 산출은 **코덱스**(`CodexHarnessLauncher`, `LAUNCH-GWIT-05-R2`).
  검증·반입 집행은 **조율 세션(Claude Opus 5)**, 결재는 **사람**(git user `Jaehyuk`).
  검증 문서는 코덱스 영역 밖이라 지시서 allowlist에서 뺐다.
- **날짜**: 2026-07-26

## 사용한 하네스 (명령 · exit code · 수치)

| 명령 | exit | 수치 |
| --- | --- | --- |
| `codex-launch validate` (1차) | 0 | ACCEPTED |
| `codex-launch launch --manual` (1차) | **1** | `scopeViolations` 2 — **요청 형식 오류**(아래) |
| `codex-launch validate` (R2) | 0 | ACCEPTED |
| `codex-launch launch --manual` (R2) | **0** | 변경 3, `scopeViolations` 0, `pathsMissingFromPatch` 0 |
| `build-verify` (패치 후) | 0 | 오류 0 |
| `gate-witness-check` | **0** | `totalUnwitnessed` **0** (13/13, 12/12, 18/18) |
| `measure dev-pack` | 0 | `{"gate":"dev-pack","violations":0,"attempt":1}` |

## 반증 시험 (지시서 §4 — 전부 실측)

| # | 시험 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1 | broken 픽스처 | 1 | **1** |
| 2 | ok 픽스처 | 0 | **0** |
| 3 | **broken의 이름은 그대로, 내용만 컴파일되게** | 0 | **0** |
| 3′ | 되돌린 뒤 재확인 | 1 | **1** |
| 4 | 없는 경로 | 2 | **2** |
| 5 | 인자 없이 실행 | 0 | **0** |
| 6 | 실행 후 트리 잔여 | 0 | **0** |

**시험 3·3′이 목적이다.** 경로 이름을 바꾸지 않고 `Broken.cs` 내용만 뒤집었더니 exit가
1 → 0 → 1로 따라 움직였다. 경로명으로 판정하는 우회 구현이 배제된다.

## 1차 발사가 실패한 이유 — 코덱스가 아니라 내 요청이 틀렸다

`scopeViolations`에 코덱스가 만든 픽스처 디렉터리 2개가 잡혔다. 원인은 **allowlist 표기**다.

```csharp
// CodexHarnessLauncherCli.cs:263 Matches
if (p.EndsWith("**")) return path.StartsWith(p[..^2]);
return string.Equals(path, p);          // ← 끝 슬래시는 "정확 일치"다
```

`docs/qa/gate-witness/`라고 적으면 **그 경로 자체와만 같아야** 하므로 하위 파일이 전부 위반이 된다.
같은 디렉터리에 5개를 쓴 `GWIT-02`는 `**` 형식이라 위반 0이었다 — **관례는 `**`이고 내가 틀렸다.**
지시서와 요청을 `docs/qa/gate-witness/**`로 고쳐 R2로 다시 쐈고 위반 0을 실측했다.

**코덱스는 시킨 곳만 건드렸다.** 1차 증거(`outbox/codex-launch-LAUNCH-GWIT-05`)도 지우지 않고 남겼다.

## 등재

| 게이트 | 추가한 witness |
| --- | --- |
| `POST-EXECUTOR` | `build-verify --fixture …build-verify-broken` (exit 1) |
| `WP-STATE-INTEGRITY-LAND` | 〃 |

세션 시작 `totalUnwitnessed` **17 → 0**.

## ★ 새로 드러난 것 — `gate-witness-check`가 여러 줄 JSON을 못 읽는다

`WP-STATE-INTEGRITY-LAND`에 `requireFailureWitness`를 켜 봤더니
`state-transition-selftest`가 **반증 없음**으로 잡혔다. `internalNegativeCases: 15` 주장은 옳다.

```
recovery-selftest          JSON 객체 수: 1
trust-origin-selftest      JSON 객체 수: 1
state-transition-selftest  JSON 객체 수: 46   ← 케이스마다 한 줄씩 흘린다
```

`CountInternalNegativeCases`는 `JsonNode.Parse(capturedOut)`로 **한 덩어리**를 기대하고,
실패하면 `catch (JsonException) { return 0 }` → 0건으로 세어 15 주장을 검증하지 못한다.
**플래그가 꺼져 있으면 주장을 그냥 믿기 때문에**(`!validateInternalClaims ||`) 지금까지 안 보였다.

fail-closed라 안전 방향이지만, **실재로 음성 15건을 가진 검사를 "반증 없음"으로 보고**한다.
그래서 LAND의 `requireFailureWitness`는 **켜지 않고 되돌렸다** — 켜면 영구 적색이 되고
그게 `FAIL-2026-010`이다. `build-verify` witness 등재는 순수 이득이라 유지했다.

해소 경로는 `server/Harness/GateWitnessCheckCli.cs`(코덱스 영역)에서 JSON Lines를 읽게 하는 것이다.

## 참조한 스킬

`skills/common/directive-authoring.md` §7.

## 지표는 만족했으나 목적은 미달인 부분

1. **LAND의 `requireFailureWitness`는 여전히 꺼져 있다.** 위 결함 때문이며, `totalUnwitnessed 0`은
   **LAND에서는 주장을 검증하지 않고 믿은 결과**다. POST-EXECUTOR·POST-COMMIT은 검증한 결과다.
   숫자만 보면 셋이 같아 보이지만 근거의 강도가 다르다.
2. **1차 발사를 소모했다.** 요청 형식을 관례와 대조하지 않고 만들었다. `Matches`가 `**`만 안다는 것은
   코드에 있었고 `GWIT-02` 요청에도 있었다 — 확인했으면 한 번에 끝났다.
