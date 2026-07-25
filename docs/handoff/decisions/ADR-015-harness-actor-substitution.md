# ADR-015 — 코덱스 헤드리스 진입점 부재 기간의 harness 조각 대행 (ADR-002 한시 예외)

- 상태: **종료됨 (2026-07-26). 승인은 사람 choi, 2026-07-13.** 종료 근거와 남은 문제는 §6.
- 범위: **`WP-STATE-INTEGRITY`의 harness 조각(`05H`·`06H`)에 한정.** 통합 branch `wp/state-integrity` 안에서만.
- 관련: `ADR-002`(영역 소유권) · `ADR-014`(1회 예외, `blockers[]`) · `docs/plan/wp/WP-STATE-INTEGRITY-land-gate.md`

## 1. 상황 (실측)

- `05H`는 `server/Harness/**` → **ADR-002상 코덱스 배타 영역**이다.
- **코덱스는 설치되어 있다.** `~/.codex`와 MS Store 앱 `OpenAI.Codex_26.707.3748.0`은 실재하며, 데스크톱 앱의 `app-server` 프로세스도 관찰됐다. 앞선 "코덱스 CLI가 없다"는 표현은 부정확했다.
- **문제는 호출 가능한 헤드리스 진입점 부재다.** Store 앱 번들 `codex.exe`는 직접 실행이 `Access denied`이고, App Execution Alias `codex.exe`도 없으며, 전역 npm `@openai/codex`도 없다. 따라서 조율자/서버가 안전하게 발사·ACK·exit code를 기록할 CLI 경로가 없다.
- **저장소 dispatch도 실제 LLM 라우터가 아니다.** `OutboxManager`는 `executor: "codex"`를 허용하지만, 실제 실행은 `dotnet run --project server -- dispatch-executor ...`이고 `DispatchExecutorCli`는 README 한 줄 추가·self-refactor 템플릿·불일치 보고서만 처리하는 결정론 스텁이다. `codex`나 `claude-code` 모델 프로세스를 호출하지 않는다.
- **코덱스는 2026-07-12 19:18(세션 055) 이후 실체 활동이 없다.** `server/Harness/`의 이후 변경(23:17)은 전부 **GUARD-03 sonnet 실행자**가 한 것이다. **원인은 주체 미상**(할당량인지 창이 닫힌 것인지 확인되지 않았다 — 침묵을 고장으로 단정하지 않는다).
- 사람 결정(2026-07-13)으로 **자동 스케줄러를 전부 중단**했다. 코덱스 15분 루틴도 멈췄다.
- **`06C-1`은 `05H`가 만드는 내부 `HandoffIntegrityChecker`를 선행으로 요구한다.** 05H가 안 되면 WP 전체가 막힌다.

## 2. 결정

**`ADR-002`의 한시 예외를 승인한다.** `CORE_INFRA_EXECUTOR(sonnet)`가 `05H`와 `06H`를 수행한다.

**예외의 경계 (넘으면 반려):**

- **`WP-STATE-INTEGRITY`의 조각(`05H`·`06H`)에 한정.** 다른 하네스 작업으로 확장하지 않는다.
- **통합 branch `wp/state-integrity` 안에서만.** `main`에 직접 land 금지.
- **`CODEX-GATE-04`는 예외에 포함되지 않는다** — 코덱스가 복귀하면 코덱스가 한다. 급하면 별도 결재.
- **코덱스가 검증 가능한 헤드리스 경로로 복귀하는 즉시 이 예외는 종료된다.** GUI 앱 존재만으로는 종료 조건이 아니다. 최소 조건은 실행자 발사 규약(지시 도착 확인, 실행 확인, 범위 대조)을 만족하는 Codex 발사 경로다.

## 3. 근거 — 왜 정당한가

- `ADR-002`의 목적은 **"만든 사람이 검증하면 같은 착각을 코드에 새긴다"**를 막는 것이다. 그 목적은 **주체 분리**로 지켜진다 — **생산은 sonnet, 검증은 검수자(claude-opus)가 반증 시험을 직접 재실행**한다. **주체 분리는 유지된다.**
- 대안은 **WP 전체 정지**다. 상태 원본(`WORKSTATE`)의 무결성이 증명되지 않은 채로 멈추는 것은 더 위험하다 — **지금 손 위조 transition-id가 통과한다**(검수자 실증).

## 4. 위험 (숨기지 않는다)

- **sonnet이 하네스와 그 하네스가 검사할 코드를 같은 세션에서 만들 수 있다.** `05H`(하네스)와 `06C-1`(StateApplier)이 **같은 actor**가 된다 → **"자기가 만든 하네스에 맞춰 제품 코드를 고치는"** 위험.
- **완화**: ①05H와 06C-1을 **별도 발사**로 분리(같은 세션에서 둘 다 하지 않는다) ②**검수자가 land gate 12개를 직접 재실행**한다 ③fixture는 **05H가 먼저 고정**하고 06C-1이 그것에 맞춘다(역방향 금지).
- **이 위험은 완화되는 것이지 제거되지 않는다.** 코덱스 복귀가 최선이다.

## 5. 되돌리는 법

코덱스가 검증 가능한 헤드리스 경로로 복귀하거나, `TRUSTED_BASELINE` 이후 `CodexHarnessLauncher`가 구현되어 발사 규약을 통과하면 이 ADR은 자동 종료된다. 예외를 연장하려면 **새 ADR과 새 사람 결재**가 필요하다. `ADR-002`의 영역 소유권은 **그대로 유효하다.**

## 6. 종료 (2026-07-26)

- 주체: **사람** (저장소 git user `Jaehyuk`). 원 승인자는 `choi`로 기록되어 있다 — **동일인 여부는 확인하지 않았다.** 프록시로 단정하지 않는다.
- 집행: Claude Opus 5(조율 세션). 판정 근거는 아래 실측이다.

### 6.1 종료 근거 (실측 2026-07-26)

**①§1의 전제가 사실로서 소멸했다.** ADR의 핵심 전제는 "호출 가능한 헤드리스 진입점 부재"였다.

- `codex --version` → `codex-cli 0.144.3`, **exit 0**. Store 앱 번들이 아니라 CLI가 PATH에 있다.
- `codex --help`에 `exec  Run Codex non-interactively`, `review  Run a code review non-interactively`가 있다. §1이 "없다"고 적은 바로 그 경로다.
- 별도 저장소(`team-loop-lite-ai-learning`)에서 `codex exec -C <dir> --sandbox read-only -`로 **실제 리뷰를 반복 실행**했다. 종료 코드가 정상적으로 돌아왔고(APPROVE/REJECT 판정 9회), 계약 위반 시 exit 1로 거절됐다. **발사·판정·exit code 기록이 실증됐다.**

**②예외가 덮던 범위의 일이 이미 인도됐다.** 예외는 `05H`·`06H`에 한정이었다.

- `server/Harness/HandoffIntegrityCli.cs`, `server/Harness/HandoffIntegrityChecker.cs` 존재 — `06C-1`이 선행으로 요구하던 내부 checker다.
- `docs/handoff/RECOVERY.md` 존재 — `06H`의 산출물이다.
- 즉 이 예외가 지금 추가로 **허가하고 있는 작업이 없다.** 종료해도 정지되는 일이 없다.

### 6.2 종료가 해결하지 **않는** 것 (숨기지 않는다)

**이 저장소는 여전히 코덱스를 발사할 수 없다.** 원인이 바뀌었을 뿐이다.

- `DispatchExecutorCli`는 **프로세스를 하나도 띄우지 않는다** — §1이 적은 결정론 스텁 그대로다.
- `OutboxManager`에서 `codex`는 **허용 라벨로만** 존재한다(`SubscriptionCalls` 계산 등). 실제 호출부가 없다.
- `CodexHarnessLauncher`는 `docs/plan/wp/CODEX-HARNESS-LAUNCHER-minimal-contract.md`에 **계약만 있고 구현이 없다**(`server/**/*.cs`에 해당 타입 없음).

따라서 §2의 종료 조건 두 갈래 중 **(a) "검증 가능한 헤드리스 경로로 복귀"는 충족**되었고, **(b) "`CodexHarnessLauncher` 구현이 발사 규약을 통과"는 미충족**이다. 이 ADR은 (a)로 종료한다.

**남는 문제**: `ADR-002`의 코덱스 배타 영역(`server/Harness/**`)은 그대로 유효한데, 이 저장소에는 그 영역에 일을 보낼 발사 경로가 없다. **차단 요인이 "CLI 부재"에서 "런처 미구현"으로 이동했다.** 다음에 그 영역의 작업이 생기면 같은 막다름을 만난다.

- 이 종료는 그 문제를 **해결하지 않는다.** 새 예외가 필요하면 §5대로 **새 ADR과 새 사람 결재**가 필요하다 — 이 문서를 근거로 삼을 수 없다.
- 권하는 해소책은 예외 연장이 아니라 **`CodexHarnessLauncher` 구현**이다. CLI가 실재하므로 이제 구현 가능한 일이 되었다.

### 6.3 되돌리는 법

이 절(§6)을 지우고 머리말의 상태 줄을 `승인됨 (사람 choi, 2026-07-13)`으로 되돌리면 예외가 복원된다. 다만 §6.1의 실측이 뒤집히지 않는 한 복원 근거가 없다 — `codex --version`이 실패하게 되면 그때 재검토한다.
