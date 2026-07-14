```context-pack
{
  "diId": "06C-1-R2",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" },
    { "path": "docs/handoff/queue/directive-06C-1-statetransition-v2.md", "sha256": "b979d4f69abbf290642b6fff740c30a3b0bf8c5577e4191cb980b0130be06b3e" },
    { "path": "docs/handoff/queue/directive-06C-1-R1-legacy-removal.md", "sha256": "10ca04164426e8adda9ac0132db3b8496e3a465e74b3289f8c103a014655ce90" },
    { "path": "outputs/review/06C-1-R1.codex.md", "sha256": "ad447596c12b6bf55af5110911e15db5bef44974224131aed28c8d8974a226e5" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "outputs/review/06C-1-R1.codex.md",
    "docs/handoff/queue/directive-06C-1-R1-legacy-removal.md",
    "docs/handoff/queue/directive-06C-1-R2-strict-cli.md",
    "docs/directives/_header.md"
  ],
  "forbiddenActions": ["git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```

> **06C-1-R1 반려에 따른 재작업.** 원 지시서 두 개(`06C-1`, `06C-1-R1`)는 **그대로 유효하다.**
> **legacy 경로 삭제는 성공했다 — 되돌리지 마라.** 이 문서는 그 위의 정정분이다.
> **절대 수정 금지**: `WORKSTATE.json` · `WORKSTATE.applier-log.jsonl` · `HandoffIntegrityChecker.cs`·`HandoffIntegrityCli.cs`(05H-R3 영역).
> **단일 land gate**: 조각 land 금지.

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.

---

# 06C-1-R2 — **"무시"를 "거절"로**, 그리고 **rollback을 시험 가능하게**

- actor: **CORE_INFRA_EXECUTOR (sonnet)** — ADR-015 한시 예외 유지
- 발견: **코덱스 독립 검수**(`outputs/review/06C-1-R1.codex.md`) + 검수자 실증 3건

---

## 0. 전임자는 legacy를 잘 지웠다. 문제는 **지운 뒤에 남은 침묵**이다

`--human-decision` · `ValidateHumanDecision` · `--root` · `canonicalMode` · `_ST_SEAM` 환경변수 →
**소스에서 전부 사라졌다. 1131줄 → 869줄. 잘한 일이다.**

**그런데 그 옵션들을 명령행에 붙이면 여전히 통과한다.**

## 1. 고칠 것 넷 + 하나

### ★ 1-1. 알 수 없는 옵션을 **거절**하라 (검수자 실증)

```
state-transition prepare --transition-id T --request nonexistent.json --human-decision fake.json  → exit 2
state-transition prepare --transition-id T --request nonexistent.json --root C:\somewhere         → exit 2
state-transition prepare --transition-id T --request nonexistent.json --bogus-flag zzz            → exit 2
대조군 (옵션 없음)                                                                                → exit 2
```

**넷 다 exit 2다. 그러나 오류 문구는 전부 `{"error":"request ... nonexistent.json"}`** — **파일이 없다는 뜻이지 usage 거부가 아니다.**
`ParseFlagMap()`(`:806-823`)이 알 수 없는 키를 맵에 담고, `ParsePrepareArgs`/`ParseApplyArgs`(`:826-843`)가 **남은 키를 검사하지 않는다.**

> **`--root C:\copy`를 믿고 붙이면 오류 없이 canonical WORKSTATE에 적용된다.** 사본에 적용됐다고 믿으면서.
> **`--human-decision approved.json`도 마찬가지 — 승인이 먹혔다고 믿는다. 아무 일도 일어나지 않는다.**
> **조용한 거짓말은 시끄러운 거절보다 나쁘다.** 삭제의 목적은 **호출자가 알게 하는 것**이다.

**고쳐라**: `prepare`/`apply`가 **자기가 아는 키만** 허용한다. **알 수 없는 키가 하나라도 있으면 즉시 `exit 2` + usage.**
`--human-decision`·`--root`처럼 **의도적으로 삭제된 옵션**은 일반 usage 대신 **명시적 오류**를 내라:

```json
{"error":"removed-option: --human-decision (06C-1-R1에서 삭제됨. PHASE_CHANGE/RECOVERY/REPLAY는 trusted-human-receipt-required로 fail-closed)"}
{"error":"removed-option: --root (06C-1-R1에서 삭제됨. 사본 시험은 process cwd를 바꿔서 하라)"}
```

**"모르는 옵션"과 "일부러 없앤 옵션"을 구분하라.** 후자는 **왜 없앴는지를 말해야 한다.**

### ★ 1-2. `state-transition --self-test` 신설 — **rollback을 시험 가능하게**

R1의 판정선 9는 `NOT_VERIFIED`였다. 실행자 자진신고: *"새 프로세스에서 in-process 훅을 설정할 수 없다."* **맞는 말이다.**
환경변수 seam을 없앤 것은 **옳다**(`:73 internal static Func<string?>? FailAfterWriteHook`). **그러나 이제 아무도 rollback을 시험할 수 없다.**

> **결함 2(post-apply 실패 시 rollback 없음)는 이 WP의 존재 이유 중 하나다. 고쳤다는 증거가 없다.**
> **이건 검수자 지시서의 공백이었다. 이번에 메운다.**

**답은 이미 저장소에 있다 — `handoff-integrity --self-test`(05H-R2)의 패턴을 그대로 써라:**

```
dotnet run --project server -c Release -- state-transition --self-test
```

- **in-process 단언 실행기.** `FailAfterWriteHook`을 **자기 시험 코드가 직접 설정**하고 apply를 in-process로 돌린다.
- **작업 경로는 `$TEMP` 아래 임시 사본.** canonical 저장소를 **절대 건드리지 마라.**
  (검수자가 반증 시험 중 `docs/wiki` 42파일을 지운 사고가 있었다. **시험은 저장소 밖에서.**)
- **기대 결과를 코드에 하드코딩한다.** 경로·id·플래그를 **인자로 받지 마라** — 받는 순간 production 우회로가 된다.
- 전부 일치 → exit 0. 하나라도 어긋나면 → exit 1 + 어긋난 case·기대·실제를 JSON으로.

**필수 case (최소):**

| case | 주입 | 기대 |
| --- | --- | --- |
| `rollback-after-write` | atomic write 직후 실패 | **exit 1 `ROLLED_BACK`** · WORKSTATE hash **== preimage** |
| `rollback-restores-log` | 같은 조건 | **v2 ok 로그가 append되지 않았다** (성공 경로에서만 쓴다) |
| `normal-apply` | 주입 없음 | **exit 0** · projection 갱신됨 · v2 로그 1줄 append |
| `fatal-restore-failed` | 복원 자체를 실패시킴 | **exit 2 `FATAL_STATE_UNKNOWN`** |

**`--self-test`가 자기를 반증하는지도 보여라**(판정선 8): **기대값을 일부러 틀리게 적으면 exit 1이 나와야 한다.**

### ★ 1-3. callsite 스캔에 **`.txt`를 넣어라** — 발사 프롬프트가 전부 `.txt`다 (검수자 실증)

`StateTransitionCallsiteCheckCli.cs:16-21` `ActiveExtensions` = `.cs .ps1 .sh .cmd .bat .json .yaml .yml .md` — **`.txt` 없음.**

**같은 경로, 확장자만 바꿔 격리한 실측:**

```
outputs/launch/ZZ-bait.ps1 → exit 1  잡음
outputs/launch/ZZ-bait.txt → exit 0  ★ 못 잡음
outputs/launch/ZZ-bait.md  → exit 1  잡음
```

**`outputs/launch/*.prompt.txt` 23개 = 발사 프롬프트 전부.**
원 지시서 §7의 범위는 **`outputs/launch/**`(확장자 무관 모든 파일)**이다.

**발사 프롬프트에 legacy 호출이 남아 있으면 실행자가 삭제된 명령을 실행하려 든다.**
**거기가 가장 위험한 곳인데 유일하게 안 잡힌다.**

**고쳐라**: `.txt` 추가. 더 나은 방법 — **확장자 allowlist 대신 이진 파일만 제외**하고 전부 스캔하라(텍스트면 다 본다).

### ★ 1-4. historical allowlist를 **접두사에서 명시 목록으로** (코덱스 #4, 검수자 실증)

```
docs/wiki/ZZ-live-runbook.md   → exit 0  ★ 숨겨진다
docs/verification/ZZ-bait.md   → exit 0  ★ 숨겨진다
docs/handoff/queue/ZZ-bait.md  → exit 0  ★ 숨겨진다
```

R1 지시서 §1-6이 명시했다: **"historical allowlist는 경로 접두사가 아니라 명시 파일 목록이어야 한다."** **안 고쳐졌다.**
`HistoricalPrefixes`(`:25-37`)가 여전히 접두사다. **그 아래 새로 만든 운영 문서는 legacy 호출을 영원히 숨긴다.**

**고쳐라**: **파일 단위 명시 목록**으로 바꾼다. 목록은 **저장소 안의 데이터 파일**(예: `docs/handoff/CALLSITE-HISTORICAL.json`)에 두고,
**목록에 없는 파일에서 legacy 호출이 나오면 무조건 센다.**
새 historical 항목을 추가하려면 **그 파일을 명시적으로 등재해야 한다** — 조용히 늘어나지 않는다.

### 1-5. 코덱스 #1 — **새 전이의 envelope self-hash 위조가 검사되지 않는다** (미확인. **네가 실증하라**)

코덱스 주장(`:234-235`, `:513-560`):
> *`envelope.transitionContractSha256` 위조 검사는 `transitionId`가 **이미 state에 있을 때만** 실행된다.
> **새 전이**는 `CheckExistingTransition()`이 `null`을 반환해 위조 hash가 검사되지 않는다.*

**반증 절차 (먼저 뚫린 것을 보여라):**

1. 정상 `prepare --transition-id T-NEW --request req.json` (사본에서)
2. 생성된 `T-NEW.envelope.json`에서 **`transitionContractSha256`만** `"aaaa"`로 변경
3. `apply --envelope T-NEW.envelope.json`
4. **기대: exit 1 `envelope-contract-mismatch`.** 현재는 정상 적용될 것으로 코덱스가 예측한다.

**뚫려 있으면 고쳐라**: contract hash 검사는 **기존/신규 전이 무관하게 항상** 수행한다.
**envelope의 `transitionContractSha256`은 언제나 "대조 대상"이지 "신뢰 입력"이 아니다.**

**뚫려 있지 않으면 그렇게 보고하라 — 코덱스 보고도 증거가 아니다.**

## 2. 하지 않을 일 (하면 반려)

- **legacy 삭제를 되돌리는 것.** 잘한 일이다. 그대로 둬라.
- **`--self-test`에 경로·id·플래그 인자를 여는 것.** 그 순간 production 우회로가 된다.
- **`FailAfterWriteHook`을 환경변수·CLI 플래그·설정 파일로 켤 수 있게 만드는 것.**
- **`legacyCallsiteCount`를 0으로 만들려고 스캔 범위를 줄이는 것.** ← R1 반려 사유였다. 반복하지 마라.
- **canonical 저장소에서 시험하는 것.** `state-transition --self-test`도 반증 시험도 **전부 `$TEMP` 사본에서.**
- `HandoffIntegrityChecker.cs`·`HandoffIntegrityCli.cs` 접촉 (**05H-R3 동시 작업**).
- `docs/handoff/RECOVERY.md` 접촉 (**06H**). 깨진 사실은 R1이 이미 보고했다.
- `WORKSTATE.json`·`WORKSTATE.applier-log.jsonl` 수정. **at-rest를 exit 0으로 만들려는 시도.**

## 3. 완료 기준 (exit code)

```text
 1. dotnet build server -c Release                                          → 0
 2. ★ prepare/apply + --human-decision                                      → 2  removed-option (문구에 --human-decision)
 3. ★ prepare/apply + --root                                                → 2  removed-option (문구에 --root)
 4. ★ prepare/apply + --bogus-flag                                          → 2  usage / unknown-option
 5. 대조군: 올바른 인자만                                                    → 정상 동작 (2·3·4와 오류 문구가 달라야 한다)
 6. ★ state-transition --self-test                                          → 0  (case 4종 전부 기대 일치)
 7. ★ self-test 안에서 rollback-after-write                                 → ROLLED_BACK · hash==preimage · v2 로그 미기록
 8. ★ self-test의 기대값을 일부러 틀리게 적고 실행                          → 1  (자기를 반증한다)
 9. ★ 새 전이 envelope의 transitionContractSha256만 위조 → apply            → 1  envelope-contract-mismatch
      ← **고치기 전에 "지금은 통과한다"를 먼저 보여라** (코덱스 #1)
10. ★ outputs/launch/X.prompt.txt 에 legacy 호출 심고 callsite-check         → 1  (legacyCallsiteCount ≥ 1)
11. ★ docs/wiki/X.md (새 파일)에 legacy 호출 심고 callsite-check             → 1  (historical 접두사로 숨겨지지 않는다)
12. 10·11에서 심은 것 제거 후 callsite-check                                → 0
13. 기존 negative 회귀: 손위조 → state-corrupted-preapply · candidate 변조 → candidate-tampered ·
    transitionKind="EVIL" → unknown-transition-kind · PHASE_CHANGE → trusted-human-receipt-required   (전부 exit 1)
14. at-rest handoff-integrity                                              → 1 · failures 정확히 1건 (정상)
15. measure dev-pack (-c Release)                                          → 0
```

**통과를 믿기 전에 실패시킬 수 있음을 먼저 증명해라.** 특히 **2·3·9·10·11**.

## 목적 기준 (사람 판정 — ADR-005)

**"없앤 것은 없다고 말해야 한다. 그리고 고쳤다는 것은 시험할 수 있어야 한다."**

- 옵션을 지우는 목적은 **호출자가 알게 하는 것**이다. 조용히 무시하면 목적을 배신한다.
- rollback을 고치는 목적은 **실패해도 상태가 살아남는 것**이다. 시험할 수 없으면 고쳤는지 알 수 없다.

우회로: `--self-test`에 인자를 열기 · 스캔 범위를 줄여 0 만들기 · removed-option을 일반 usage로 뭉개기.
**전부 반려다. 자진 신고 없이 하면 반려다. 신고하면 감점이 아니다.**

**요구가 서로 모순돼 보이면 완화하지 말고 보고해라.**
(직전 네 작업 중 **세 번은 원인의 절반이 검수자가 준 요구의 모순 또는 공백**이었다. 이번에도 있으면 말해라.)

## 허용 파일 (allowlist)

- server/StateApplierCli.cs
- server/Harness/StateTransitionCallsiteCheckCli.cs
- server/Harness/HarnessRegistry.cs
- docs/handoff/CALLSITE-HISTORICAL.json
- outputs/state-transition/**
- docs/verification/06c1-r2-strict-cli.md
- docs/handoff/queue/directive-06C-1-R2-strict-cli.md

> `server/Harness/HandoffIntegrityChecker.cs`·`HandoffIntegrityCli.cs` **무접촉**(05H-R3 동시 작업) ·
> `docs/handoff/RECOVERY.md` **무접촉**(06H) · `server/ProjectionCli.cs` **무접촉** ·
> `server/Harness/DiCompletionCheckCli.cs`·`ClaimCheckCli.cs` **무접촉**(CODEX-GATE-04).

## 보고

`docs/verification/06c1-r2-strict-cli.md` — `_template.md` 형식 그대로.

**반드시 적을 것:**
- **완료 기준 2·3·9·10·11의 "고치기 전" 실제 출력.** **"뚫렸다"를 보인 다음에만 "막았다"가 성립한다.**
- **코덱스 #1(새 전이 self-hash)이 실제로 뚫려 있었는지.** 안 뚫려 있었으면 **그렇게 보고하라** — 코덱스 보고도 증거가 아니다.
- **`--self-test`의 case별 실제 출력.** 이것이 **rollback 경로의 최초 실행 기록**이다.
- 반증 시험에 쓴 임시 파일은 **전부 `$TEMP`에** 만들고, **저장소가 무결함을 blob 해시로 확인**하라(`git status`는 프록시다).

**자기보고는 증거가 아니다.** 검수자가 재실행하고 **코덱스가 read-only로 독립 검수한다.**
못 한 시험은 `NOT_VERIFIED`. **숨기면 반려다.**
