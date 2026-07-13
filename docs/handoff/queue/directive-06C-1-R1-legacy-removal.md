```context-pack
{
  "diId": "06C-1-R1",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/handoff/queue/directive-06C-1-statetransition-v2.md", "sha256": "b979d4f69abbf290642b6fff740c30a3b0bf8c5577e4191cb980b0130be06b3e" },
    { "path": "outputs/review/06C-1.codex.md", "sha256": "4f705483003e3b02941d620b74597d009a75d1703ea600c143e593bdedbb6d60" },
    { "path": "docs/handoff/RECOVERY.md", "sha256": "f7bcf9eccb5cad4cf37b61fc45f5682d5665c090bd074597f0e10d44d56dd069" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "outputs/review/06C-1.codex.md",
    "docs/handoff/queue/directive-06C-1-statetransition-v2.md",
    "docs/handoff/queue/directive-06C-1-R1-legacy-removal.md",
    "docs/directives/_header.md"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

> **06C-1 반려에 따른 재작업.** 원 지시서 `directive-06C-1-statetransition-v2.md`는 **그대로 유효하다.**
> 이 문서는 그 위의 정정분이다.
> **절대 수정 금지**: `WORKSTATE.json` · `WORKSTATE.applier-log.jsonl` · `HandoffIntegrityChecker.cs`·`HandoffIntegrityCli.cs`(05H-R2 영역).
> **단일 land gate**: 05H·06C-1·06C-2·06H는 통합 branch에서 함께 넘긴다. **조각 land 금지.**

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.

---

# 06C-1-R1 — legacy 단일-샷 경로 삭제 + 결속·seam·범위 결함 5건

- actor: **CORE_INFRA_EXECUTOR (sonnet)** — ADR-015 한시 예외 유지
- 발견: **코덱스 독립 검수**(`outputs/review/06C-1.codex.md`, 11건) + 검수자 코드 대조(핵심 3건 확인)
- **사람 결재 완료**: legacy 경로 **삭제**한다(choi, 2026-07-13. `HUMAN-INBOX.md` 참조).

---

## 0. 왜 반려됐나 — **새 경로를 만들고 옛 경로를 지우지 않았다**

06C-1은 prepare/apply를 훌륭하게 만들었다. 기본 게이트도 전부 초록이었다:
`build 0` · `state-transition-callsite-check 0`(`legacyCallsiteCount=0`) · `measure 0` · `at-rest 1`(설계된 참 양성).

**그런데 `StateApplierCli.cs`의 legacy 단일-샷 경로가 코드에 그대로 살아 있고, 결함이 전부 그 안에 있다.**

> **`legacyCallsiteCount=0`은 "옛 호출부가 없다"는 뜻이지 "옛 경로가 없다"는 뜻이 아니다.**
> **하네스가 다른 것을 쟀다.** 이 저장소에서 여섯 번 반복된 실패 양식이다.

## 1. 고칠 것 — 다섯 가지

### ★ 1-1. legacy 단일-샷 경로를 **삭제한다** (사람 결재 완료)

`state-transition --transition-id <id> --expected-workstate-sha256 <sha> --request <f> [--human-decision <f>]` 형태의
**옛 경로 전체를 제거한다.** 남기지 말고 지운다.

- **`--human-decision` 옵션을 완전히 제거한다.** `ValidateHumanDecision` 및 그 호출부(`:847` 등)도 함께 지운다.
  - **이 구멍으로 검수자가 실제로 자기 승인을 위조했다**(SESSION-BRIEF 2026-07-13). AI가 임의 JSON 파일을 써서 phase 전이를 승인했다.
  - PHASE_CHANGE/RECOVERY/REPLAY는 **prepare/apply 경로에서 `trusted-human-receipt-required` exit 1**이다(원 지시서 §6). 그것만 남는다.
- legacy 경로의 rollback 부재(`:560-612`)·가짜 멱등(`:695-715`)도 **경로와 함께 사라진다.** 별도로 고치지 마라 — **지우면 된다.**
- 남은 CLI 표면은 **`state-transition prepare`와 `state-transition apply` 둘뿐**이다. 그 외 인자 조합은 **exit 2 + usage**.

> **⚠ `docs/handoff/RECOVERY.md:51`이 legacy 형태를 운영 복구 절차로 쓴다. 그 문서는 06H가 prepare/apply로 다시 쓴다.**
> **너는 `RECOVERY.md`를 건드리지 마라**(allowlist 밖). 다만 **검증 문서에 "RECOVERY.md가 지금 깨졌다. 06H 필요"를 명시하라.**
> 06H 없이 land하면 복구 절차 문서가 실체와 어긋난다 — land gate가 그걸 잡아야 한다.

### ★ 1-2. contract hash를 **재계산**하라 — envelope의 자기신고를 믿지 마라

현재 `:681-685`:

```csharp
var envelopeContractHash = envelope.TransitionContractSha256;              // envelope가 스스로 신고한 값
if (!string.Equals(envelopeContractHash, logInfo.TransitionContractSha256, ...))  // 문자열 비교
```

**계약(원 지시서 §2)은 "envelope로부터 같은 방식으로 계약 hash를 계산한다"를 요구한다.**
지금은 **재계산하지 않고 envelope가 스스로 적은 문자열을 믿는다.**

**반증**: 성공 로그의 `transitionContractSha256`을 복사해 **다른 request의 envelope**에 그대로 써넣으면
→ 문자열이 일치하므로 **`idempotent exit 0`.** **결함 4(ID 결속 없음)가 미수정이다. 결속하는 척한다.**

**고쳐라**: envelope의 **구성 필드로부터** contract hash를 **직접 계산**하고, 그 계산값을 log와 비교한다.

```text
computed = sha256(canonical{ transitionId, transitionKind, requestSha256,
                             preStateSha256(=expectedPre), postStateSha256(=expectedPost), effectiveAt })
computed != log의 hash  → exit 1 transition-id-collision
envelope.transitionContractSha256 가 computed와 다르면 → exit 1 envelope-contract-mismatch  (자기신고 위조 탐지)
```

**envelope의 `transitionContractSha256` 필드는 이제 "신뢰 입력"이 아니라 "대조 대상"이다.**

### 1-3. test seam에서 **환경변수를 제거하라**

`:357-361`:

```csharp
// 결정적 test seam — atomic replace 직후 실패 주입 (production 노출 플래그 아님).   ← 주석이 거짓말이다
var seamFail = Environment.GetEnvironmentVariable("_ST_SEAM_FAIL_AFTER_WRITE") == "1" ? ... : null;
```

**`#if DEBUG` 가드가 없어 Release 바이너리에 그대로 들어간다.** 누구든 환경변수를 켜면 모든 apply가 rollback 경로로 빠진다.
환경변수는 **자식 프로세스로 상속된다** — 발사기가 실수로 전파할 수도 있다.
**주석이 "production 노출 플래그 아님"이라고 주장하는데 사실과 반대다. 주석으로 계약을 만족시킬 수 없다.**

**고쳐라**: 환경변수를 읽지 마라. **in-process 테스트 훅**으로 바꾼다 —
`internal static Func<bool>? FailAfterWriteHook` 같은 형태로, **자기 시험 코드(같은 프로세스)만 설정할 수 있게** 한다.
production 진입점(CLI 인자·환경변수·설정 파일) 어디에서도 켤 수 없어야 한다.

### 1-4. `--root`를 제거하라 — 사본 시험은 **cwd로 한다**

내 프롬프트가 명시적으로 금지했는데(`06C-1.prompt.txt:63`) `--root`가 만들어졌다.
**그리고 `canonicalMode = string.IsNullOrWhiteSpace(opts.Root)`(`:214`)는 `Root`를 canonical 루트와 대조하지 않는다.**
→ **`state-transition apply --root <canonical repo>`** 로 부르면
**WORKSTATE는 쓰면서 `canonicalMode=false`가 되어 projection을 건너뛴다.**
**`RUNTIME-INDEX.md`(L0)가 조용히 낡는다** — CLAUDE.md가 "손으로 쓴 문서보다 이걸 믿어라"라고 한 그 파일이다.

**코덱스 지적이 옳다: `--root`는 요구 결함이 아니라 구현 우회다. 사본을 process cwd로 두면 된다.**
`GitTools.FindRepoRoot()`와 `ProjectionCli.cs:32`는 **cwd 기준**이다. cwd가 사본이면 **전부 사본에서 돈다.**

**고쳐라**: `--root` 옵션과 `canonicalMode` 개념을 **삭제한다.** root는 항상 `FindRepoRoot()`. **projection은 항상 실행한다.**

> **⚠ 사본에서 시험하는 법 (함정)**: PowerShell의 `Set-Location`은 **.NET의 CurrentDirectory를 바꾸지 않는다**(실측 함정).
> 진짜 cwd를 바꿔라: `Start-Process -WorkingDirectory <copy>` 또는 `cmd /c "cd /d <copy> && dotnet run --project server -c Release -- ..."`.
> **이걸 몰라서 전임자가 `--root`를 만들었을 가능성이 높다. 이 문단이 그 답이다.**

### 1-5. `transitionKind` 미지값을 **fail-closed** 하라

`:629-650`·`:202-205` — unknown kind(예: `"EVIL"`) 검증이 없다. **high-risk가 아니라고 판단해 NORMAL처럼 진행한다.** fail-open이다.

**고쳐라**: `transitionKind ∉ {NORMAL, PHASE_CHANGE, RECOVERY, REPLAY}` → **exit 1 `unknown-transition-kind`.**

### 1-6. `state-transition-callsite-check`의 **범위 결손**

`legacyCallsiteCount=0`이 나온 이유의 절반이다:

- `outputs/launch/**` **전체를 historical allowlist로 빼버렸다**(`:22-34`). **거긴 활성 범위다** — 발사 프롬프트가 산다.
- `.md`(운영 prompts/docs)·`.github/workflows/**`·루트 JSON/YAML manifest를 **스캔하지 않는다**(`:16-20`).

**고쳐라**: 원 지시서 §7의 범위대로 되돌린다.
**historical allowlist는 경로 접두사가 아니라 명시 파일 목록**이어야 한다(`docs/handoff/sessions/**`, `outputs/*.out.log` 등 **증거 파일만**).
**지시서 문서(`docs/handoff/queue/**`)와 과거 인수인계는 historical이 맞다** — 거기 legacy 문자열이 남는 건 정상이다.
**`legacyCallsiteCount`가 0이 되게 만들려고 범위를 줄이지 마라. 그게 이번 반려 사유의 핵심이다.**

## 2. 하지 않을 일 (하면 반려)

- **`legacyCallsiteCount=0`을 맞추려고 스캔 범위를 줄이는 것.** ← 반려 사유 그 자체다.
- `RECOVERY.md` 수정 (**06H 영역**). 깨졌다는 사실만 **보고**하라.
- `HandoffIntegrityChecker.cs`·`HandoffIntegrityCli.cs` 접촉 (**05H-R2가 동시에 그 파일에서 작업 중이다**).
- `ProjectionCli.cs` 수정 — 지금은 cwd 기준으로 충분하다. 필요하다고 판단되면 **고치지 말고 보고하라.**
- `WORKSTATE.json`·`WORKSTATE.applier-log.jsonl` 수정. **`DI0004-BLOCKED-CODEX`를 지우지 마라.**
- **at-rest를 exit 0으로 만들려는 모든 시도.** at-rest exit 1은 정상이다.
- production에서 켤 수 있는 실패 주입 통로(환경변수·CLI 플래그·설정 파일)를 **다시 만드는 것.**

## 3. 완료 기준 (exit code)

```text
 1. dotnet build server -c Release                                      → 0
 2. ★ legacy 인자 조합 (--transition-id / --human-decision)             → 2  usage (옵션이 존재하지 않는다)
 3. NORMAL prepare→apply 왕복 (사본, cwd=사본)                          → 0
 4. 같은 envelope 재적용                                                 → 0 idempotent
 5. ★ 성공 로그의 contract hash를 복사해 다른 request envelope에 삽입     → 1 transition-id-collision  (재계산이 잡는다)
 6. ★ envelope.transitionContractSha256을 손으로 위조                    → 1 envelope-contract-mismatch
 7. 손 위조: state에 가짜 id, log 없음                                   → 1 state-corrupted-preapply
 8. candidate 1바이트 변조                                              → 1 candidate-tampered
 9. in-process 훅으로 atomic write 직후 실패 주입                        → 1 ROLLED_BACK, hash==preimage
10. ★ 환경변수 _ST_SEAM_FAIL_AFTER_WRITE=1 설정 후 정상 apply            → **정상 성공(exit 0)**. 환경변수가 아무 효과 없다
11. transitionKind = "EVIL"                                            → 1 unknown-transition-kind
12. transitionKind ∈ {PHASE_CHANGE, RECOVERY, REPLAY}                  → 1 trusted-human-receipt-required
13. ★ outputs/launch/에 legacy 호출 문자열을 심고 callsite-check          → 1 (legacyCallsiteCount ≥ 1)
14. 심은 것을 지우고 callsite-check                                      → 0
15. at-rest handoff-integrity                                          → 1 · failures 정확히 1건 (정상)
16. dotnet run --project server -c Release -- measure dev-pack          → 0
```

**통과를 믿기 전에 실패시킬 수 있음을 먼저 증명해라.** 특히 5·6·10·13.

## 목적 기준 (사람 판정 — ADR-005)

**"결함이 살 곳을 없앤다. 새 경로를 만드는 것으로는 부족하다 — 옛 경로가 남아 있으면 결함도 남는다."**

우회로: legacy를 "안 쓰이니까" 남겨두기 · 스캔 범위를 줄여 `legacyCallsiteCount=0` 만들기 ·
seam을 다른 이름의 환경변수로 옮기기 · `--root`를 `--workspace` 같은 이름으로 재도입하기.
**전부 목적 미달이다. 자진 신고 없이 하면 반려다. 신고하면 감점이 아니다.**

## 허용 파일 (allowlist)

- server/StateApplierCli.cs
- server/Harness/StateTransitionCallsiteCheckCli.cs
- server/Harness/HarnessRegistry.cs
- outputs/state-transition/**
- docs/verification/06c1-r1-legacy-removal.md
- docs/handoff/queue/directive-06C-1-R1-legacy-removal.md

> `docs/handoff/RECOVERY.md` **무접촉**(06H) · `server/Harness/HandoffIntegrityChecker.cs`·`HandoffIntegrityCli.cs` **무접촉**(05H-R2 동시 작업) ·
> `server/ProjectionCli.cs` **무접촉** · `server/Harness/DiCompletionCheckCli.cs`·`ClaimCheckCli.cs` **무접촉**(CODEX-GATE-04).

## 보고

`docs/verification/06c1-r1-legacy-removal.md` — `_template.md` 형식 그대로.

**반드시 적을 것:**
- **완료 기준 5·6·10·13의 실제 출력** — "뚫렸다"를 보인 다음에만 "막았다"가 성립한다.
- **`RECOVERY.md`가 지금 깨졌다는 사실**과 **06H가 무엇을 고쳐야 하는지** (해당 절·줄 번호).
- **삭제한 legacy 코드의 범위**(함수명·줄 수). 검수자가 "정말 지워졌는가"를 대조한다.

**자기보고는 증거가 아니다.** 검수자가 재실행하고 **코덱스가 read-only로 독립 검수한다**(`outputs/launch/run-codex-review.ps1`).
못 한 시험은 `NOT_VERIFIED`. **숨기면 반려다.**
