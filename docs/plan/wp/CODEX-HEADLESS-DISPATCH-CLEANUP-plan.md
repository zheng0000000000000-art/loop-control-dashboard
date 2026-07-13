# Codex Headless Dispatch Cleanup — 정리 계획서

- 상태: **계획 초안. 구현 착수 금지.**
- 작성일: 2026-07-13
- 관련: `ADR-015` · `CODEX-HARNESS-LAUNCHER-minimal-contract.md` · `WP-STATE-INTEGRITY-land-gate.md`
- 목적: "코덱스가 없다"라는 틀린 근거를 제거하고, dispatch의 예약된 외피와 실제 헤드리스 launcher 구현 대상을 분리한다.

---

## 0. 현재 사실

1. `~/.codex`와 MS Store 앱 `OpenAI.Codex_26.707.3748.0`은 실재한다.
2. ~~현재 Windows 환경에는 조율자/서버가 호출할 수 있는 Codex 헤드리스 진입점이 없다.~~ **← 2026-07-13 폐기. §0.2 참조.**
   - Store 앱 번들 `codex.exe` 직접 실행은 `Access denied`. *(여전히 사실)*
   - App Execution Alias `codex.exe`는 없다. *(여전히 사실)*
   - 전역 npm `@openai/codex`도 없다. *(**설치 안 된 상태였을 뿐이다. 2026-07-13 설치 완료.**)*
3. 저장소의 dispatch는 LLM 실행 라우터가 아니다.
   - `OutboxManager`는 `executor: "codex"`를 허용한다.
   - 실제 실행은 `dotnet run --project server -- dispatch-executor <executor> <instruction>`이다.
   - `DispatchExecutorCli`는 결정론 스텁이다. README 한 줄, self-refactor 템플릿, 불일치 보고서만 만든다.
4. 따라서 `executor: "codex"`는 현재 "구현된 실행 통로"가 아니라 향후 `CodexHarnessLauncher`가 채울 예약 슬롯이다.

### 0.2 정정 — **헤드리스 진입점은 존재한다. "없다"가 아니라 "안 깔았다"였다** (검수자 실증, 2026-07-13, 사람 승인 후 설치)

앞선 "진입점 부재" 판정은 **탐색 범위가 PATH였다.** 범위를 넓히니 답이 달라졌다.

| 항목 | 실측 |
| --- | --- |
| node / npm | `v24.14.1` / `11.11.0` — **있었다** |
| `npm view @openai/codex` | `0.144.3`, `bin: { codex: bin/codex.js }` — **레지스트리에 있었다** |
| `~/.codex/auth.json` | 4,519B — **이미 인증돼 있었다** (`config.toml`도 존재) |
| `npm i -g @openai/codex` | exit 0. `codex-cli 0.144.3`, `C:\Users\1\AppData\Roaming\npm\codex.cmd` |

**`codex exec` = 비대화 헤드리스 진입점.** launcher가 필요로 하는 계약 요소를 전부 제공한다:
`--json`(JSONL 이벤트) · `-o/--output-last-message`(최종 산출물 파일) · `-C/--cd`(작업 경로 격리) ·
`-s/--sandbox`(샌드박스 정책) · exit code.

**probe 3종 (임시 디렉터리. 저장소 무접촉):**

| # | 시험 | 결과 |
| --- | --- | --- |
| 1 | 지시 도착·실행 확인 — `canary.txt`를 읽어 내용+표식 반환 | exit 0, `output-last-message` = `REPO-UNTOUCHED / CODEX-PROBE-OK`. 이벤트 6줄(`thread.started`…`turn.completed`) |
| 2 | read-only에서 파일 생성 지시 | `SANDBOX-BLOCKED`, 파일 미생성. **단 모델이 시도조차 안 했다 — 이건 샌드박스 강제의 증거가 아니다** |
| 3 | 쓰기를 **강제** 실행시킴 | 모델이 실제로 시도 → OS가 거부: `UnauthorizedAccessException` / 내부 exit 1. `BREACH.txt` 미생성. **샌드박스가 커널 레벨로 강제된다** |

#### ★ 그러나 `codex exec`의 exit code는 "성공"이 아니다 — **"세션이 끝났다"이다**

probe 3에서 **내부 명령이 실패(exit 1)했는데도 `codex exec` 프로세스는 exit 0**을 냈다.
이것은 `DispatchExecutorCli`가 exit 0을 주는 것과 **정확히 같은 함정**이다(§0.1).
**launcher가 `codex exec`의 exit code를 성공 신호로 읽으면 그것이 일곱 번째 거짓말이 된다.**
판정은 exit code가 아니라 **산출물(diff·artifact hash)과 Program Verifier의 독립 재실행**으로 한다.

부수: probe 3의 오류 문구가 콘솔에서 한글이 깨졌다(`寃쎈줈…`). launcher는 자식 프로세스의
stdout/stderr 인코딩을 **UTF-8로 명시 고정**해야 한다 — `run-executor.ps1`의 BOM 결함과 같은 계열이다.

#### 이것이 ADR-015를 끝내지는 않는다

종료 조건은 "진입점 존재"가 아니라 **발사 규약 3요소의 실측 통과 + `CodexHarnessLauncher`의 Program Verifier 결속**이다(ADR-015 §종료).
지금 실증된 것은 **1·2요소(지시 도착 확인·실행 확인)와 범위 격리 수단**이다. **3요소(범위 대조)는 launcher가 diff를 allowlist와 맞춰봐야 성립한다.**
그리고 `TRUSTED_BASELINE` 전에는 실제 DI를 발사하지 않는다(§2). **05H·06H 대행은 그대로 유지된다.**

### 0.1 정정 — 예약 슬롯이 아니라 **성공을 보고하는 무동작 슬롯**이다 (검수자 실측, 2026-07-13)

위 4항은 부족했다. 빈 슬롯이면 아무 일도 안 일어나야 하는데, 실제로는 **성공처럼 보이는 결과를 만든다.**

| # | 실체 | 근거(코드 위치) |
| --- | --- | --- |
| 1 | 어떤 결정론 규칙에도 맞지 않는 지시는 `EXECUTOR_REPORT.md`를 쓰고 **exit 0**을 낸다 | `server/DispatchExecutorCli.cs:51-53` |
| 2 | dispatch는 `!hasChanges`일 때만 `failed`로 본다. 그 보고서 파일이 변경으로 잡히므로 `hasChanges=true` → 상태는 **`import_pending`** | `server/OutboxManager.cs:87-88` |
| 3 | tier-2 자동 승인이 켜져 있으면 그 `import_pending`이 자동 승인 경로로 들어간다 | `server/OutboxManager.cs:102` |
| 4 | `SubscriptionCalls`가 `codex`를 **1**로 센다 → 일어나지 않은 구독 호출이 비용 meta에 기록된다 | `server/OutboxManager.cs:339-345` |

즉 지금 `executor: "codex"`로 dispatch하면 **"실행 성공 · 반입 대기"** 항목이 생긴다. 이것은 세션 브리프가 모은
"게이트가 거짓말한 다섯 사례"와 **같은 종류의 여섯 번째**다 — 검사가 없는 게 아니라, **대상이 없는데 PASS를 준다.**

## 1. 정리 목표

1. 문서·큐·계약에서 "코덱스 부재"와 "dispatch가 codex를 실제 실행한다"는 표현을 제거한다.
2. `ADR-015`의 결론은 유지한다.
   - 05H·06H는 코덱스 소유 영역이다.
   - 단, 호출 가능한 Codex 헤드리스 경로가 없으므로 `CORE_INFRA_EXECUTOR(sonnet)`가 한시 대행한다.
3. `CODEX-HARNESS-LAUNCHER`는 `TRUSTED_BASELINE` 이후 구현 대상으로 유지한다.
4. Claude/Responses 호환 프록시 같은 새 실행 통로는 지금 만들지 않는다.

## 2. 하지 않을 일

- `DispatchExecutorCli`를 지금 실제 LLM launcher로 바꾸지 않는다.
- `OutboxManager`의 executor 정책을 지금 **확장**하지 않는다(새 실행자·새 실행 통로 추가 금지).
- Claude API 프록시, Responses 호환 gateway, Codex 앱 우회 실행기를 만들지 않는다.
- `WP-STATE-INTEGRITY`가 `TRUSTED_BASELINE`을 만들기 전 자동 발사 경로를 추가하지 않는다.
- `approve-import`·`reject-import`·상태 전이 승인 경로를 대행하지 않는다.

### 2.1 다만 **fail-closed는 "하지 않을 일"이 아니다**

`executor: "codex"`를 거절하도록 막는 것은 **정책 확장이 아니라 거짓 PASS 제거**다. 위 금지선과 충돌하지 않는다.
없는 실행 통로가 `import_pending`을 만드는 상태를 그대로 두면, 지금 진행 중인 `WP-STATE-INTEGRITY`가 회복하려는
**바로 그 신뢰**(상태가 실체를 반영한다)를 dispatch 쪽에서 다시 깬다.

- 요구: launcher 배선 전까지 `executor: "codex"` dispatch는 **명시적 실패**(예: `dispatch.executor_not_implemented`)로 거절한다.
- 요구: `SubscriptionCalls`가 codex를 세지 않는다 — 호출하지 않은 구독을 과금 meta에 남기지 않는다.
- 구현은 **코드 변경**이므로 이 계획서가 아니라 **지시서 경로**로 간다: `docs/handoff/queue/directive-DISPATCH-01-codex-failclosed.md`.
- **`TRUSTED_BASELINE` 이후에 넣는다.** 지금 넣으면 land gate 중인 통합 branch에 조각을 얹는 것이다(§3 Phase B의 "조각 land 금지").

## 3. 단계별 계획

### Phase A — 사실 정리 완료

완료 기준:

- `ADR-015`가 "코덱스 설치됨 / 헤드리스 진입점 없음 / dispatch 스텁"을 모두 명시한다.
- `CODEX-QUEUE`가 `codex`를 소유권 표기로만 사용하고, 실행자는 ADR-015 예외에 따라 sonnet 대행이라고 쓴다.
- `CODEX-HARNESS-LAUNCHER-minimal-contract.md`가 현 dispatch를 실제 LLM 라우팅으로 오해하지 않게 한다.
- 검증 문서에 `measure dev-pack` 결과 JSON 한 줄을 남긴다.

상태: **완료됨** (`docs/verification/adr015-codex-headless-correction.md`).

### Phase B — TRUSTED_BASELINE 전 유지보수

수행 조건:

- `WP-STATE-INTEGRITY` land 전.
- 자동 발사 중단 상태 유지.

해야 할 일:

1. 05H·06H는 ADR-015 경계 안에서만 sonnet이 수행한다.
2. 검수자는 05H/06H 산출물의 반증 시험을 직접 재실행한다.
3. `CODEX-GATE-04`는 예외에 포함하지 않는다. 급하면 별도 ADR과 사람 결재가 필요하다.
4. 다음 세션이 `executor: "codex"`를 실제 실행 통로로 오해하면 이 계획서와 ADR-015를 먼저 읽게 한다.
5. **`DISPATCH-01`(§2.1 fail-closed) 지시서를 큐에 대기시킨다. 발사는 `TRUSTED_BASELINE` 이후다** — 통합 branch에 조각을 얹지 않는다.
   그때까지의 방어는 코드가 아니라 규칙이다: **`executor: "codex"`로 dispatch하지 마라. 성공처럼 보이는 무동작 결과가 나온다(§0.1).**

> **주의 — 상태표를 믿지 마라.** `docs/handoff/CODEX-QUEUE.md`의 `C-05H` 행은 "대기"라고 쓰여 있지만
> 05H는 이미 실행 중이었다(2026-07-13 검수자 실측). Phase B 진행도는 **`docs/context/RUNTIME-INDEX.md`(L0)와
> 실제 프로세스·산출물**로 센다. 손으로 쓴 표는 지연된다.

완료 기준:

- 05H·06C-1·06C-2·06H가 통합 branch에서 단일 land gate로 통과한다.
- clean replay 또는 trust-origin bootstrap 의식을 사람이 직접 수행한다.
- `TRUSTED_BASELINE`이 선언된다.

### Phase C — Launcher 구현 착수 전 점검

수행 조건:

- `TRUSTED_BASELINE` 선언 이후.
- Program Verifier가 최소 검사(scope·preimage·build·target oracle·regression·evidence)를 실제 실행할 수 있음.

착수 전 질문:

1. ~~호출 가능한 Codex 헤드리스 진입점이 생겼는가.~~ **✅ 답함 — `codex exec` (codex-cli 0.144.3). 실증: §0.2**
2. 실행자 발사 규약을 만족하는가.
   - 지시 도착 확인 — **✅ probe 1** (`-o/--output-last-message`로 회수)
   - 실행 확인 — **✅ probe 1** (`--json` JSONL 이벤트 스트림)
   - 범위 대조 — **⬜ 미충족.** 샌드박스 격리 수단(`-C`·`-s`)은 실증됐으나(probe 3, 커널이 쓰기 거부),
     **launcher가 산출 diff를 allowlist와 대조하는 부분은 아직 없다.** 이것이 Phase D의 핵심이다.
   - stdout/stderr/exit code/artifact hash 기록 — **⚠ exit code를 성공 신호로 쓰지 마라(§0.2).**
     `codex exec`는 내부 명령이 실패해도 exit 0을 준다. 판정은 **artifact hash + Program Verifier 재실행.**
3. `executor: "codex"` dispatch 토큰·권한 설정이 금지선과 충돌하지 않는가.
4. 실패 시 fallback이 자동으로 상태를 바꾸지 않도록 막혀 있는가.
5. **`DISPATCH-01`(§2.1)이 land되어 `executor: "codex"`가 fail-closed 상태인가.** launcher는 **거절되던 통로를 여는 것**이지,
   무동작 exit 0 위에 얹는 것이 아니다. fail-closed가 먼저 서야 launcher의 negative fixture(지시 미도착·exit 1)가 의미를 가진다.

산출물:

- `CodexHarnessLauncher` 구현 지시서.
- Launch Request schema.
- Program Verifier 결속 지시서.
- negative fixture: 지시 미도착, 범위 이탈, exit 1, artifact hash 불일치.

### Phase D — 최소 launcher 구현

범위:

- 단일 `CodexHarnessLauncher`.
- actorRole은 `HARNESS_EXECUTOR`만 허용.
- allowedPaths는 `server/Harness/**`와 승인된 fixture 경로만 허용.
- 산출물은 candidate patch와 execution report뿐이다.

금지:

- 상태 판정 금지.
- PASS 선언 금지.
- WORKSTATE 직접 수정 금지.
- commit/push 금지.
- 제품 코드 일반 수정 금지.
- 실패 후 임의 fallback 금지.

완료 기준:

- Program Verifier가 launcher 산출물을 독립 재실행해 판정한다.
- 통과한 경우에도 canonical state 변경은 StateApplier 전이와 사람 결재를 거친다.
- launcher 자체 negative test가 모두 실패를 실패로 보고한다.

### Phase E — ADR-015 종료 판정

종료 조건:

- 호출 가능한 Codex 헤드리스 경로가 있다.
- 발사 규약 3요소가 실측으로 통과했다.
- `CodexHarnessLauncher`가 Program Verifier와 결속되어 있다.
- 자동 발사는 별도 `WP-STATE-LAUNCH-GATE` 이후에만 허용된다.

종료 작업:

1. ADR-015를 "종료됨"으로 갱신할 새 ADR 또는 종료 기록을 만든다.
2. CODEX-QUEUE의 05H/06H 대행 문구를 제거한다.
3. `executor: "codex"`가 실제 launcher를 호출하는 경로와 증거 문서를 연결한다.
4. `measure dev-pack`과 launcher negative fixture 결과를 검증 문서에 기록한다.

## 4. 검증 게이트

모든 단계의 문서·코드 변경 후:

```powershell
dotnet run --project server -c Release -- measure dev-pack
```

> **`-c Release`를 빼지 마라.** 이 계획서는 원래 `-c Release` 없이 적혀 있었다 — 그건 **낡은 Debug 바이너리를 검사하라는 지시**다.
> 실측 근거: `server/Harness/DiCompletionCheckCli.cs:142-157`이 `run --no-build --project server`를 `-c Release` 없이 부른다.
> Release만 빌드한 저장소에서 그 하네스는 **직전 Debug 빌드**를 검사하고 PASS를 준다(세션 브리프 함정 #5, `CODEX-GATE-04`가 고칠 대상).
> `server/ProjectionCli.cs:365-368`이 찍는 하네스 명령은 이미 `-c Release`다 — 그쪽이 정본이다.

기록 형식:

```json
{"gate":"dev-pack","violations":0,"attempt":1}
```

Launcher 구현 단계에서는 추가로 다음을 요구한다.

- 지시 미도착 negative test.
- 범위 이탈 negative test.
- 실행 실패 exit code negative test.
- artifact hash 불일치 negative test.
- Program Verifier 독립 재실행.

## 5. 다음 실행자에게 남기는 판단

지금 할 일은 Claude를 Codex에 꽂는 것이 아니다. 먼저 상태 원본 신뢰도를 회복하고, 그 다음에 Codex 실행 통로를 최소 launcher로 구현해야 한다. dispatch의 `codex` 값은 그때까지 실제 라우팅이 아니라 이름표다.
