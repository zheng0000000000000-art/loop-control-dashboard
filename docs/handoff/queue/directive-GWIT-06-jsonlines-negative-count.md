```context-pack
{
  "diId": "GWIT-06",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-GWIT-06-jsonlines-negative-count.md",
    "docs/handoff/queue/directive-GWIT-04-selftest-negative-count.md",
    "server/Harness/GateWitnessCheckCli.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# GWIT-06 — `gate-witness-check`가 여러 줄 JSON을 읽게 한다

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 선행: `GWIT-01`(검사 신설), `GWIT-04`(음성 사례 수 검증).

---

## 0. 문제 (실측 2026-07-26)

`WP-STATE-INTEGRITY-LAND`에 `requireFailureWitness`를 켜자 `state-transition-selftest`가
**반증 없음**으로 잡혔다. **그런데 그 검사의 `internalNegativeCases: 15` 주장은 옳다.**

```
recovery-selftest          JSON 객체 1개   → 검증 통과
trust-origin-selftest      JSON 객체 1개   → 검증 통과
state-transition-selftest  JSON 객체 46개  → 검증 실패
```

`CountInternalNegativeCases`가 stdout 전체를 `JsonNode.Parse`로 **한 덩어리**로 읽는다.
객체가 연달아 오면 `JsonException`이 나고 `catch`가 **0건**으로 센다.
그래서 15건을 실제로 가진 검사가 "반증 없음"이 된다.

fail-closed라 안전 방향이지만 **참인 것을 거짓으로 보고**하므로 게이트를 켤 수 없다.
켜면 영구 적색이 되고 그게 `FAIL-2026-010`이다. 그래서 플래그를 되돌려 둔 상태다.

**꺼져 있는 동안에는 주장을 그냥 믿는다**(`!validateInternalClaims ||`).
즉 지금 LAND의 `totalUnwitnessed 0`은 **검증한 0이 아니라 믿은 0**이다. 이걸 되돌리는 게 목적이다.

## 1. 무엇을 하는가

`CountInternalNegativeCases`가 **연달아 오는 JSON 값들**(JSON Lines / 붙어 있는 객체)을
읽을 수 있게 한다.

- 한 덩어리로 파싱되면 지금과 **똑같이** 동작해야 한다. 객체 1개짜리 두 self-test의
  결과가 바뀌면 안 된다.
- 파싱이 안 되면 **여전히 0**이다. fail-closed를 풀지 마라.
- 값 선택은 기존 `FindCaseCount`와 같은 규칙(`internalNegativeCases`·`negativeCaseCount`·
  `rejectedCaseCount` 중 최댓값)을 **객체들에 걸쳐** 적용한다.

## 1-A. 합산하지 마라 — 최댓값이다

46개 객체를 돌면서 카운터를 **더하면** 실제보다 큰 수가 나오고, 그러면 거짓 주장이
통과한다. **검사마다 하나의 요약 카운터**를 읽는 것이므로 **최댓값**을 쓴다.
§4 시험 2가 이걸 잡는다.

## 1-B. 부분 파싱을 성공으로 세지 마라

앞쪽 몇 개만 읽히고 뒤가 깨진 출력을 "읽었다"로 처리하면 잘린 출력이 통과한다.
**남은 문자가 공백 말고 남아 있으면 파싱 실패로 본다.**

## 2. 하지 않을 일 (하면 반려)

- `state-transition-selftest`나 `StateApplierCli`의 출력 형식 변경 — **영역 밖**이고,
  다른 소비자가 그 형식에 의존한다. **읽는 쪽을 고친다.**
- `GATE-MANIFEST.json` 수정 — 영역 밖. `requireFailureWitness`를 켜는 것은 조율자가 한다(§6).
- `catch`를 넓혀 예외를 삼키는 것.
- 문자열 검색(`Contains("negativeCaseCount")` 등)으로 수를 세는 것. **JSON으로 읽어라.**
  CLAUDE.md: *"출력 문자열·정규식으로 성패를 세지 마라."*

## 3. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `gate-witness-check` (현재 매니페스트) **exit 0**, `totalUnwitnessed` **0** — 회귀 없음
- [ ] `gate-witness-check docs/qa/gate-witness/require-failure-witness.json` **exit 1** — 반증 살아 있음
- [ ] 새 픽스처로 §4의 6개 시험이 전부 기대값

### 목적 기준 (사람 판정)

**"`state-transition-selftest`의 15건 주장을 실행 증거로 확인할 수 있고, 999건 주장은 여전히 막힌다."**

## 4. 반증 시험 (전부 실측. **코드 검토로 갈음하지 마라**)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | LAND에 `requireFailureWitness`를 켠 **사본 매니페스트**로 실행 | `state-transition-selftest`가 **반증 있음** |
| 2 | 같은 사본에서 그 검사의 `internalNegativeCases`를 **999**로 | **반증 없음** — 거짓 주장은 여전히 막힌다 |
| 3 | 같은 사본에서 **16**으로 (실제 15보다 1 큼) | **반증 없음** — 합산이면 통과해 버린다 |
| 4 | 객체 1개짜리 `recovery-selftest`·`trust-origin-selftest` | **변화 없음** |
| 5 | 뒤쪽이 잘린 출력을 내는 픽스처 | **0건**(fail-closed) |
| 6 | JSON이 아닌 출력을 내는 픽스처 | **0건**(fail-closed) |

**시험 3이 §1-A를 지키는지 보는 자리다.** 46개를 더하면 15보다 훨씬 큰 수가 나와 16이 통과한다.
**시험 2만으로는 부족하다** — 999는 합산으로도 안 넘을 수 있다.

시험 1·2·3의 사본 매니페스트는 `docs/qa/gate-witness/` 아래에 둬라. **원본을 수정하지 마라**(§2).

시험 5·6은 픽스처가 필요하다. 이미 있는 `docs/qa/gate-witness/`의 기존 파일은 **건드리지 말고**
새 파일로 만들어라 — `stale-pin-directive.md`·`gate-clean-*.status`·`doc-integrity-mismatch/`·
`require-failure-witness.json`·`build-verify-*`·`measure-violating/`·`behavior-snapshot-mismatch.json`은
모두 다른 검사의 witness다. allowlist가 디렉터리 단위라 기술적으로는 열려 있지만 범위 밖이다.

## 5. 착륙 (두 걸음 — 확정)

`GWIT-04`가 `server/Harness/GateWitnessCheckCli.cs`를 **requiredInputs로 pin하고 있다**
(`directive-GWIT-04-selftest-negative-count.md:13`). 이 파일을 고치면 그 pin이 stale해져
`context-pack-integrity`가 **exit 1**을 낸다. 이번엔 소유 지시서를 실측으로 확인해 뒀다 —
**추측이 아니다.** 반입 후 pin을 갱신하면 exit 0이 된다.

## 6. 후속 (이 지시서가 하지 않는다)

조율자가 `WP-STATE-INTEGRITY-LAND`의 `requireFailureWitness`를 켜고 `totalUnwitnessed 0`을
**검증한 0**으로 다시 실측한다. **§4 시험 1이 통과하기 전에 켜지 마라** — 영구 적색이 된다.

## 허용 파일 (allowlist)

- server/Harness/GateWitnessCheckCli.cs
- docs/qa/gate-witness/**

> 검증 문서는 영역 밖이라 조율자가 쓴다.

---

## §5 정정 (2026-07-26, 반입 후) — 착륙 2단계는 필요 없었다

§5는 *"`GWIT-04`가 `server/Harness/GateWitnessCheckCli.cs`를 **requiredInputs로 pin**하고 있다"*고
적었고 *"실측으로 확인해 뒀다 — 추측이 아니다"*라고 단언했다. **틀렸다.**

그 경로는 `GWIT-04`의 **`readOrder`**에 있다(`directive-GWIT-04:13`). `readOrder`에는 sha가 없어
stale해질 수 없다. 반입 후 `context-pack-integrity`는 **exit 0**이었고 착륙은 한 걸음으로 끝났다.

**원인은 내 읽기다.** 파일명으로 grep해 행 번호만 보고 **어느 블록인지 확인하지 않았다.**
같은 세션에서 "소유 지시서를 가정하지 말고 grep해서 찾아라"라고 써 놓고, grep 결과를
다시 프록시로 읽었다. 행이 잡혔다는 사실은 **pin이라는 증거가 아니다.**

원문은 지우지 않는다 — 지시서가 무엇을 근거로 쓰였는지가 기록이다.
