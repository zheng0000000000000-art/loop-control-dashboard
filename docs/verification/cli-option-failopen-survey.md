# 오타 옵션이 조용히 무시되는가 — CLI 전면 조사

- **주체(actor)**: 조율 세션(Claude Opus 5). 사람 지시.
- **날짜**: 2026-07-26 · 계기: `state-transition apply --dry-run-flag`가 실제로 상태를 쓴 사고

## 구조적 사실

```
미지 옵션(unknown-option)을 검증하는 CLI:  server/StateApplierCli.cs  ← 하나뿐
```

**나머지 CLI는 모르는 옵션을 조용히 무시한다.** 값 추출은 대부분
`for (i; i + 1 < args.Length) if (args[i] == "--" + name) return args[i+1]` 형태여서,
이름이 틀리면 **"옵션이 없는 것"** 으로 처리된다. 결과는 CLI마다 다르다.

## 실측

| # | 명령 | 오타 | exit | 실제로 일어난 일 |
| --- | --- | --- | --- | --- |
| A | `di-completion-check --gate POST-COMMIT --manifest <픽스처>` | `--manifestt` | **0** | **production 매니페스트를 쟀다.** checkCount 1 대신 **14**, verdict **PASS** |
| B | `trust-origin evidence --gate-report <보고>` | `--gate-reportt` | **0** | 증거가 조용히 격하 — `releaseBuild`·`docIntegrity`·`reconciliationFixtures` 전부 **`NOT_RUN`** |
| C | `codex-launch launch --manual` | `--manul` | — | **측정 못 함** (§아래) |
| D | `state-transition apply --dry-run` | `--dry-run-flag` | **2** | 오늘 고쳤다. 고치기 전에는 **실제 apply** |

### A가 가장 위험하다

**픽스처를 쟀다고 믿는데 production을 쟀고, exit 0으로 통과했다.**
오늘 하루 종일 픽스처로 반증 시험을 돌렸는데, 그 명령들이 **한 글자만 틀렸어도
"production이 통과했다"를 픽스처 결과로 오독했을 것**이다. fail-open이다.

### B는 방향은 안전하나 조용하다

`NOT_RUN` 증거는 `declare`가 거절하므로 **최종적으로는 막힌다.**
그러나 **exit 0**이고, 만든 사람은 PASS 증거를 만들었다고 생각한다.

### C — 측정하지 못했다. 이유를 적는다

`--manual` 가드는 **요청 검증 뒤**에 있다(`CodexHarnessLauncherCli.cs:56` → `:60`).
탐침 요청(없는 지시서 경로)은 `directive-missing`으로 **가드 전에** 거부돼 도달하지 못했다.
가드에 도달하려면 **모든 검증을 통과하는 요청**이 필요한데, 그때 가드가 fail-open이면
**코덱스가 실제로 발사된다.**

**코드상으로는 fail-closed다**: `HasFlag`는 `args.Contains("--manual")`이라 오타면 false이고,
기록의 `automatedExecutionReady`는 **false**(실측)이므로 조건이 참이 되어
`automated-execution-not-ready`로 거부된다. **이것은 코드 판독이지 실측이 아니다.**

## 영역

| 대상 | 위치 | 영역 |
| --- | --- | --- |
| **`di-completion-check`** (A) | `server/Harness/` | **코덱스 배타** → 지시서 필요 |
| `trust-origin` (B) | `server/` 루트 | 조율자 |
| `codex-launch` (C) | `server/` 루트 | 조율자 |
| `handoff-integrity`·`scope-check` | `server/Harness/` | 코덱스 · 읽기 전용이라 위험 낮음 |

## 지표는 만족했으나 목적은 미달인 부분

1. **아무것도 고치지 않았다.** 이 문서는 조사다. A는 코덱스 영역이라 지시서가 필요하고,
   B·C는 내 영역이지만 **세션 끝에 반쯤 고치는 것보다 정확히 보고하는 쪽**을 골랐다.
2. **C를 측정하지 못했다.** 측정 비용이 "잘못되면 코덱스 발사"라서 하지 않았다.
   **코드 판독을 실측으로 적지 않는다.**
3. **모든 CLI를 다 훑지 않았다.** `i + 1` 경계 패턴이 있는 곳을 grep으로 찾아
   **결과가 위험한 넷만** 실측했다. `measure`·`build-verify` 등은 안 봤다.

---

# 조치 (append, 2026-07-26) — 세 CLI 전부 오타를 거부한다

조사 당시 *"아무것도 고치지 않았다"* 고 적은 것을 닫는다.

| CLI | 영역 | 조치 |
| --- | --- | --- |
| `state-transition` | `server/` 루트 | 직접(사고 직후) — 값 없는 후행 옵션·내부 키 이름 거부 |
| `di-completion-check` | `server/Harness/` | **`DICC-04` + `R1`** (코덱스) |
| `trust-origin` · `codex-launch` | `server/` 루트 | 직접 — `CliOptions.Validate` 공용 검증기 |

**검증 규칙의 정의는 `server/CliOptions.cs` 하나다.** 오늘 같은 종류의 중복을 네 번 없앴고
(`BuiltInCommands`·`BinaryFreshness`·`NormalizedContentHash`·게이트 보고 수용 규칙),
다섯 번째를 만들지 않으려고 공용으로 뽑았다.

## 실측

| # | 명령 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1 | `trust-origin evidence --gate-reportt <보고>` | 거부 | **exit 2 · `unknown-option: --gate-reportt`** |
| 2 | `trust-origin evidence --outt <파일>` | 거부 | **exit 2 · `unknown-option`** |
| 3 | `trust-origin evidence --out` (값 없이) | 거부 | **exit 2 · `missing-option-value: --out`** |
| 4 | `trust-origin evidence --out <파일> --gate-report <보고>` (대조군) | **0** | **0** (깨끗한 트리에서 재측정) |
| 5 | `trust-origin --self-test` · `inspect` | 0 | **0 / 0** |
| 6 | `codex-launch validate --requestt <파일>` | 거부 | **exit 2 · `unknown-option`** |
| **7** | **`codex-launch launch --request <유효> --manul`** | **거부** | **exit 2 · `unknown-option: --manul`** |

### 7이 이 조치의 값이다

조사 때 *"측정하지 못했다"* 고 적은 항목이다. `--manual` 가드는 **요청 검증 뒤**에 있어,
가드에 도달하려면 모든 검증을 통과하는 요청이 필요했고 **가드가 fail-open이면 코덱스가
실제로 발사**되므로 잴 수 없었다.

**이제 옵션 검증이 요청 검증보다 앞선다.** 안전장치를 닮은 오타가 조용히 무시되지 않고,
그 사실을 **발사 없이 확인할 수 있다.** *"재려면 위험을 감수해야 하는 안전장치"* 는
안전장치가 아니었다.

## 지표는 만족했으나 목적은 미달인 부분

1. **`--manual` 가드 자체의 fail-closed는 여전히 실측이 아니다.** 오타는 이제 막히지만,
   *"가드가 실제로 발사를 막는가"* 를 재려면 유효한 요청으로 가드에 도달해야 한다.
   **막힌 것은 오타이지 가드가 아니다.** 구분해서 적는다.
2. **값이 `--`로 시작하는 경우를 값 없음으로 본다.** 경로가 `--`로 시작할 일은 없다고
   판단했지만, 그런 입력이 있으면 거부된다.
3. **다른 CLI는 여전히 검증하지 않는다.** `measure`·`build-verify`·`gate-clean` 등은
   조사에서 *"결과가 위험한 넷"* 에 들지 않아 손대지 않았다.
