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
3. **클론에서 새 흐름을 안 돌렸다.** 조율자 트리에서만 왕복했다.
