# SESSION-BRIEF 2026-07-15 — 검수자 세션 인수인계

> **이 문서만 읽어도 이어받는다.** 판정 근거는 `outputs/reviewer-log.md`, 상태 정본은 `docs/context/RUNTIME-INDEX.md`.
> **손으로 쓴 문서보다 기계가 만든 L0를 믿어라.** 이 문서도 손으로 썼다.

---

## 1. 지금 (전부 실측)

```
브랜치   wp/state-integrity      ← 통합 branch. main 아니다. 조각 land 금지
상태     P00 / WP-00 / DI-00-04 / blocked   (blockers 2건)
트리     dirty 0                ← 깨끗하다
미푸시   104건                  ← 사람 게이트
조율자   ⛔ 중단 (예약작업 disabled, 사람 결정 2026-07-13)
코덱스   ⛔ 중단 — 설치는 됐으나 호출 가능한 헤드리스 진입점이 없다 (ADR-015)
발사     사람 승인 건별 수동만
```

### ★ 게이트 전부 PASS (등재 expectedExit == 실측)

```
gate-clean 0 · handoff-integrity 0 · context-pack-integrity 0 · doc-integrity 0 · hs-scan 1(기대 1)
build-verify 0 · verify-behavior 0 · measure dev-pack 0 · recovery inspect 0
```

**이 세션에서 `handoff-integrity`와 `context-pack-integrity`를 1 → 0으로 닫았다.** 아래 §2.

---

## 2. 이번 세션에 한 일

### (1) `DI0004-BLOCKED-CODEX` — 사람 결재 `BC-003`으로 정리

**실체**: 그 전이는 **적용됐는데**(`appliedAt 2026-07-12T14:02:29.94Z`) **성공 로그가 없었다**. post-apply 검증이 실패한 이유는 **당시 `handoff-integrity`가 단수 `blocker`를 읽던 버그**다(GUARD-03이 고침). **상태는 정당했고 검증기가 잘못 실패시켰다.** v1엔 rollback이 없어 적용만 남았다 — **v2(06C-1)는 rollback이 있어 재발하지 않는다. 레거시 1건이다.**

**교착이었다**: `TRUST-ORIGIN` 선행조건 2(`reconciliation exit 0`)와 `recoveryApplyReady=false`(receipt 없음)가 **서로를 잠갔다.**

**처리**: `recovery evidence`로 증거 생성(`outputs/recovery/DI0004-BLOCKED-CODEX/`, `stateMutated:false`) → HUMAN-INBOX 등재 → **사람이 (가)안 결재** → `applier-log`에 **corrective append 1줄**(`BASELINE-CHANGES` BC-003). **`WORKSTATE.json`은 건드리지 않았다.**

> **되돌리는 법은 한 줄이다** — append한 마지막 줄만 지우면 정확히 이전 상태.

**기각한 안 3개** (근거는 BC-003에):
- reconciliation에 `legacy-postapply-orphan` 신설 → **검사를 약화한다**
- 부트스트랩 선행조건 2 완화 → **신뢰의 바닥을 어긋난 상태 위에 놓는다**
- **`GATE-MANIFEST`의 `handoff-integrity` expectedExit를 1로 등재** → **"상태가 어긋난 것이 정상"이라고 선언하는 것. 그 검사는 그 순간 죽는다.**

> **셋 다 판정이 불편해서 기준을 옮기는 모양이다**(`CLAUDE.md` 금지사항 1번).
> **다음 세션에서 누가 다시 "MANIFEST가 낡았다"고 하면, 이 절을 보여줘라.**

### (2) `context-pack-integrity` 연쇄 stale → 0

지시서가 서로를 `requiredInputs`로 참조해서, **A를 고치면 A를 참조하는 B가 stale**이 됐다. 수렴할 때까지 3회 반복했다.
**→ 이게 `--emit-hashes`가 필요한 이유다**(`GATE-CP-01`). **손으로는 못 쫓아간다.**

### (3) 파일 정리 — **아무것도 삭제하지 않았다**

| | 전 | 후 |
| --- | --- | --- |
| `queue/` 지시서 | 44 | **8** (미완만) |
| `outputs/` 루트 | 111 | **9** |

`git mv`로 이동 → **`git log --follow`가 이력을 따라온다.** 안내문: `docs/archive/2026-07/README.md`.
**실행자 로그 79건은 이동만 했다 — `ADR-010` transport receipt 계열의 증거다. 삭제하면 검증 근거가 사라진다.**

---

## 3. 다음에 할 일

### 1′. `GATE-TRUTH-01` (아직 지시서 없음) — **Trust Origin보다 먼저다**

**evidence가 게이트를 뭉갠다** (실측, `TrustOriginCli.cs:308~314`):

```csharp
["releaseBuild"] = gatesPass ? "PASS" : "NOT_RUN",
["docIntegrity"] = gatesPass ? "PASS" : "NOT_RUN",
["devPack"]      = { violationCount = gatesPass ? 0 : -1 }
```

- **`gatesPass` 불린 하나를 여러 게이트 결과로 복제한다.** 게이트별 **실제 exit code를 담지 않는다.**
- **`context-pack-integrity`는 evidence에 아예 없다.** `HarnessRegistry` 15개 중 evidence가 담는 건 8개뿐이다.

> **evidence가 "통합 검증 통과"라고 쓰는데, 등재된 게이트의 절반을 안 보고, 본 것도 불린 하나로 뭉갠다.**
> **이대로 Trust Origin을 선언하면 record가 거짓이 된다.** 부트스트랩은 "신뢰의 바닥"이다 — **거짓 위에 놓을 수 없다.**

**묶어서 한 지시서로**: ①evidence가 게이트별 실제 exit를 담게 ②evidence에 `context-pack-integrity` 추가 ③`--emit-hashes`(계산하는 쪽과 검사하는 쪽이 같은 프로그램이어야 한다).
**따로 고치면 또 빠뜨린다.**

### 2. Trust Origin 부트스트랩 — **1′ 이후**

지금 `trust-origin verify` **exit 1**(record 없음 — 정상). 선행조건 2(`reconciliation exit 0`)는 **이제 충족됐다.**
**land gate 12번(clean replay 또는 부트스트랩 의식)은 사람이 직접 한다.**

### 큐 (미완 8건)

`CODEX-GATE-04`(Debug 바이너리 결함) · `GATE-CP-01` · `DISPATCH-01` · `FIX03` · `HARNESS01~04`

---

## 4. 절대 하면 안 되는 것

- **`main`에 조각 커밋** — 통합 branch에서만. **넷이 다 모인 뒤 land gate 12개.**
- **`--human-decision` 파일을 직접 써서 전이를 통과시키는 것** — **그게 위조다.** (검수자가 오늘 실제로 그랬다: `outputs/decisions-diId-2026-07-12.json`)
- **`GATE-MANIFEST`의 expectedExit를 실측에 맞춰 바꾸는 것** — **검사를 죽이는 짓이다.** 고쳐야 할 것은 상태다.
- **자동 스케줄러 재가동** — `TRUSTED_BASELINE` 선언 전까지 금지.
- **`WORKSTATE`를 손으로 고치는 것** — `state-transition`으로만(`RECOVERY.md`).
- **증거 삭제** — `outputs/`의 `*.transport.json`·`*.exit.json`·`*.out.jsonl`·`gates/`·`recovery/`·`quarantine/`.

---

## 5. 함정 (검수자가 직접 당한 것)

| 함정 | 실체 |
| --- | --- |
| **`--no-build`** | Release 산출물이 낡으면 **게이트 넷이 exit 2**로 나온다. **이 세션에서도 첫 측정을 이걸로 망쳤다.** 재면 **먼저 빌드해라** |
| **게이트가 Debug 바이너리를 검사한다** | `DiCompletionCheckCli:142`에 `-c Release`가 없다 (`CODEX-GATE-04`가 고친다) |
| **exe 직접 실행** | 저장소 루트를 **부모 폴더**로 잡는다 → `measure` exit 2 |
| **`recovery evidence --out` 상대경로** | `server/` 기준으로 해석된다. **절대경로를 줘라** |
| **`outputs/sonnet-*.out.log`** | 재발사 때 갱신 안 된다. 정본은 **`.out.jsonl`의 `result` 이벤트** |
| **PowerShell `Set-Location`** | .NET `CurrentDirectory`를 **안 바꾼다** |

---

## 6. 검수자가 틀린 것 (숨기지 않는다)

1. **"코덱스 CLI가 없다"** — `where` 무결과만 보고 **부재를 단정했다.** 실제로는 MS Store 앱으로 설치돼 있었다.
   → **"없다"는 판정에도 증거가 필요하다. 부재를 주장할 때는 탐색 범위를 함께 적어라.**
2. **"post-apply 실패 전이를 failure로 잡으면 오탐"**이라고 코덱스 프롬프트에 적었다. **틀렸다.** 05H 설계가 옳다 — **적용됐는데 성공 로그가 없으면 정말로 어긋난 것**이다.
3. **`--verdict` 구멍을 지적해놓고 내가 그 구멍으로 통과했다**(손으로 쓴 verdict 파일).
4. **`CliRouter.cs`를 allowlist에서 빠뜨려** `state-transition` 배선이 조율자에게 격리당했다.
5. **이 세션에서도 `--no-build`로 첫 측정을 망쳤다** — 내가 문서에 적어둔 함정에 내가 걸렸다.

---

## 7. 새 세션이 처음에 할 일

1. `docs/context/RUNTIME-INDEX.md` — **L0를 먼저 읽어라**
2. `git branch --show-current` → **`wp/state-integrity`인지 확인**
3. **빌드부터**: `dotnet build server -c Release` — 그 다음에 게이트를 재라
4. 게이트 9종을 **직접 재실행**해 이 문서의 §1과 대조하라. **자기보고는 증거가 아니다 — 이 문서도 자기보고다.**
5. `GATE-TRUTH-01` 지시서를 쓰고 발사 → 그 다음 Trust Origin
