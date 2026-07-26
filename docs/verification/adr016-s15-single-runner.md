# ADR-016 §15 이행 검증 — 게이트 러너를 하나로 만들었다

- **주체(actor)**: 결정은 **사람**(이 세션에서 선택), 구현·실측은 **조율 세션(Claude Opus 5)**.
  `server/` 루트는 코덱스 영역 밖이라 직접 경로다.
- **날짜**: 2026-07-26

## 무엇을 했나

| 파일 | 변화 |
| --- | --- |
| `server/ProgramVerifierCli.cs` | **358줄 → 124줄.** 게이트 실행 제거, `request`만 남김 |
| `server/GateReportReader.cs` | **신규.** 보고 수용 규칙의 유일한 정의 |
| `server/TrustOriginCli.cs` | 사본 규칙 제거, 공용 검증기 사용 |

`program-verify verify`는 **판정하지 않고 어디로 가야 하는지 말하며 exit 2**다.
조용히 다른 일을 하지 않는다.

## 새 흐름 — 끝에서 끝까지 실측

```
1) di-completion-check --gate WP-STATE-INTEGRITY-LAND --task s15-flow   → exit 0
2) program-verify request --gate WP-STATE-INTEGRITY-LAND --report <1의 보고>  → exit 0
     transitionRequestPath: outputs/transition-requests/TR-...json
3) trust-origin evidence --gate-report <1의 보고>                        → exit 0
```

**판정은 `di-completion-check`, 요청은 `program-verify`, 증거는 `trust-origin`.**
각자 하나씩 한다.

## 반증 시험 — 근거 없이 요청이 만들어지지 않는다

| 시험 | exit | 사유 |
| --- | --- | --- |
| 없는 보고 | 1 | `gate-report-missing` |
| 다른 게이트 id의 보고 | 1 | `gate-report-wrong-gate` |
| `--report` 없이 | 2 | 사용법 |
| **`baselineCommit`이 HEAD가 아닌 보고** | 1 | **`gate-report-baseline-mismatch`** |

**네 경우 모두 요청이 만들어지지 않았다** — 실행 후 `outputs/transition-requests/`에
정상 1건만 있었다(그 1건도 시험용이라 지웠다).

**네 번째가 중요하다.** 낡은 통과 보고를 나중에 다시 들이미는 것이 가장 그럴듯한 우회다.

회귀: `trust-origin --self-test` 0, `state-transition-selftest` 0, `recovery-selftest` 0.

## 오늘 같은 중복을 네 번 없앴다

```
BuiltInCommands       두 벌 → 하나 (HREG-02)
BinaryFreshness       복사 방지 (DICC-01 §1-A)
NormalizedContentHash 두 벌 → 하나 (NHASH-01)
게이트 보고 수용 규칙   두 벌 → 하나 (이 작업)
```

**전부 "같은 질문에 두 코드가 다른 답을 낼 수 있는 상태"였다.** `ADR-016` §6이 그 결과였고,
갈린 답 하나가 `TRUSTED_BASELINE` 선언 근거로 쓰였다.

## 지표는 만족했으나 목적은 미달인 부분

1. **기존 처분 21건의 `gateReport`는 전부 `program-verify` 산이다.** `GateReportReader`가
   두 산출자를 모두 받으므로 유효하지만, **이제 그 러너는 그런 보고를 더 이상 만들지 않는다.**
   재생성은 하지 않았다 — 그 보고들은 만들어진 시점에 유효했다.
2. ~~`CLI-CONTRACT.json`을 갱신하지 않았다~~ → **확인했더니 갱신할 것이 없다.**
   그 계약은 `{"command":"program-verify","critical":false}`만 기록하고 **하위 명령은 담지 않는다**
   (`--emit-cli-contract` 출력과 파일 양쪽 실측). `verify` → `request` 변화는 그 계약의 관심 밖이다.
   **미달로 적기 전에 확인했어야 했다** — 오늘 확인하지 않은 추정을 미달 항목으로 적어 이미 두 번 틀렸다.
3. ~~클론에서 새 흐름을 안 돌렸다~~ → **돌렸다.** 깨끗한 클론(baseline `c14f535`)에서:

   ```
   di-completion-check --gate WP-STATE-INTEGRITY-LAND --task flow   → exit 0
   program-verify request --gate ... --report <그 보고>              → exit 0
   trust-origin evidence --gate-report <그 보고>                     → exit 0
   di-completion-check --gate POST-COMMIT --task pc                  → exit 0
   ```

   **새 흐름이 조율자 트리 밖에서도 끝까지 돈다.**

4. ~~`POST-EXECUTOR`는 새 러너로 안 돌렸다~~ → **돌렸다.** 깨끗한 클론(baseline `6deed01`):

   | 트리 상태 | `di-completion-check --gate POST-EXECUTOR` |
   | --- | --- |
   | 깨끗함 | **FAIL 1/13** — 유일한 실패가 `gate-clean` `expected 1, actual 0` |
   | 더럽힘(`server/executor-scratch.txt`) | **PASS 13/13**, `worktreeCleanAtStart` false |

   **유일한 실패가 정확히 그 검사 하나**라는 것이 전제 불충족이라는 증거다.
   옛 러너(`program-verify`)로 잰 결과와 **같다** — 러너를 바꿔도 판정이 같음이 확인됐다.

   **이로써 세 게이트 모두 새 러너로, 클론에서 초록임이 실측됐다**
   (`POST-COMMIT` 14/14 · `LAND` 18/18 · `POST-EXECUTOR` 13/13).

---

# RequiredGateCommands 기계 대조 (append, 2026-07-26)

`HUMAN-INBOX`의 *"손 동기화라 이름을 또 바꾸면 또 끊긴다"* 를 닫는다.

## 지시서를 쓰지 않았다 — 영역이 아니었다

`server/TrustOriginCli.cs`는 **`server/` 루트라 코덱스 영역이 아니다**
(`PermittedWriteRoots = server/Harness/, skills/, docs/qa/`). 지시서로 쓰면 어느 실행자도
수행할 수 없다. **직접 경로로 했다.**

## 무엇을 했나

1. `RequiredCommandsMissingFromManifest(root)` — 목록과 매니페스트 LAND 검사 이름을 대조해
   **없는 이름들을 돌려준다.**
2. `GateReportRejection`이 그것을 **먼저** 본다. 어긋나면 `required-commands-stale`.
   종전에는 목록이 낡아도 사유가 `gate-report-missing-required-check`로 나와
   **보고서를 의심하게** 만들었다 — 실제 원인은 목록이었다.
3. `trust-origin --self-test`에 두 케이스 추가.

## 반증 시험

| 케이스 | 성격 | 결과 |
| --- | --- | --- |
| `required-commands-match-manifest` | 양성 | **pass** |
| **`required-commands-drift-detected`** | **음성** | **pass** |

**후자가 핵심이다.** 매니페스트 **사본**에서 필수 검사 하나를 지우고, 그것이 정확히
"빠진 것"으로 보고되는지 확인한다. 잡지 못하면 이 검사는 아무것도 하지 않는다.
**원본은 건드리지 않는다**(임시 경로에서 하고 지운다).

실측: `trust-origin --self-test` **26케이스 · 21음성 · failed 0 · exit 0**.
회귀: `gate-witness-check` exit 0, LAND 판정 exit 0, `trust-origin evidence` exit 0.

## ★ 같은 병이 하나 더 드러났다 — 케이스 수도 손 동기화다

케이스를 더하자 **세 곳의 상수**가 같이 움직여야 했다.

```
SelfTestNode(gatesPass, 24)                      → 26   (증거를 만드는 쪽)
SelfTestEvidencePass(evidence, "…", 24)          → 26   (증거를 검사하는 쪽, 정확 일치)
GATE-MANIFEST internalNegativeCases: 20          → 21
```

안 맞추면 `declare`가 `integration-gate-evidence-missing`으로 거절한다 — fail-closed지만
**사유가 원인을 가리키지 않는다.** `RequiredGateCommands`와 정확히 같은 모양이다.

**다만 매니페스트 쪽은 이미 기계가 본다** — `gate-witness-check`가 검사를 실제 실행해
`internalNegativeCases`를 대조한다(`DISPO`/`GWIT` 계열 작업). 코드 안의 두 상수는 아직 아니다.

## 지표는 만족했으나 목적은 미달인 부분

1. ~~`required-commands-stale` 배선을 시험하지 않았다~~ → **시험했다.** 클론에서
   `state-transition-selftest` → `…-renamed`로 **HREG-01식 이름 교체를 재현**했다.

   | 상태 | `trust-origin evidence --gate-report` |
   | --- | --- |
   | 정상 | **exit 0** |
   | 이름 교체 후 (같은 보고·같은 HEAD) | **exit 2 · `{"error":"required-commands-stale"}`** |

   **사유가 원인을 가리킨다.** 종전 같으면 `gate-report-missing-required-check`가 나와
   보고서를 의심하게 만들었을 자리다.

### ★ 그 시험이 시험의 결함을 잡았다

첫 실행에서 `required-commands-drift-detected`가 **거짓 실패**했다.
*"빠진 것이 정확히 1개"* 를 기대했는데 이미 하나가 빠져 있어 2가 된 것이다 —
**기준선이 깨끗하다고 가정한 시험**이었다.

지금 실재하는 이름 하나를 골라, 지웠을 때 빠진 목록이 **그만큼만** 늘어나는지 보게 고쳤다.
고친 뒤 어긋난 클론에서 다시 재니:

```
required-commands-match-manifest  → pass False   (목록이 낡았다 — 맞는 보고)
required-commands-drift-detected  → pass True    (탐지기는 작동한다)
```

**두 케이스가 서로 독립이 됐다.** 하나는 *"목록이 낡았다"*, 다른 하나는 *"탐지기가 산다"*를
말한다. 배선을 안 재고 넘어갔으면 이 케이스는 **실제 드리프트가 났을 때 원인을 거꾸로**
가리켰을 것이다.
2. **`SelfTestNode`/`SelfTestEvidencePass`의 상수 두 개는 여전히 손 동기화다.**
   케이스를 더하는 사람이 셋을 다 기억해야 한다. `cases.Count`를 그대로 쓰는 쪽이 옳지만
   **증거 검사 쪽이 "몇 개여야 한다"를 주장하는 것이 설계 의도**일 수 있어 건드리지 않았다.
