```context-pack
{
  "diId": "HREG-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee" },
    { "path": "docs/verification/_template.md", "sha256": "15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69" }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-HREG-01-selftest-registration.md",
    "docs/handoff/decisions/ADR-016-gate-runner-authority.md",
    "server/Harness/HarnessRegistry.cs"
  ],
  "forbiddenActions": ["commit", "push", "state-transition", "pass", "product-code-edit", "approve", "reject", "import", "edit-baseline"]
}
```

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다.
**DI 유형: `harness`** → 반증 시험 필수.

---

# HREG-01 — 게이트가 도는 self-test를 **등재한다** (명령 자체는 등재하지 않는다)

- actor: **HARNESS_EXECUTOR (codex)** — `server/Harness/**`는 `ADR-002` 코덱스 배타 영역이다.
- 출처: `ADR-016` §6·§7. 두 러너가 갈린 진짜 원인이다.

---

## 0. 문제 (실측 2026-07-26)

`WP-STATE-INTEGRITY-LAND` 게이트가 세 명령을 검사로 쓴다:

```
state-transition --self-test   (19 케이스)
recovery --self-test           (8 케이스)
trust-origin --self-test       (24 케이스)
```

**셋 다 `HarnessRegistry`에 없다.** `CliRouter` 명령이다. 그래서:

```
di-completion-check → FAIL-CLOSED  "unknown command"   (처음부터)
program-verify      → exit 2       "등록된 검사가 아니다"  (2026-07-26 엄격화 후)
```

**두 러너 모두 이 게이트를 돌리지 않는다.** 그런데 엄격화 전 `program-verify`는 돌렸고,
그 **14/14 PASS를 근거로 `TRUSTED_BASELINE`이 선언됐다**(`BASELINE-CHANGES.md` 2026-07-26).
지금 기준으로 그 통과는 **재현되지 않는다.**

세 명령을 **직접 돌리면 지금도 exit 0**이다. 검사가 틀린 게 아니라 **게이트가 그것을 모른다.**

## 1. 무엇을 하는가

**self-test 진입점만 `HarnessRegistry`에 등재한다.**

```
state-transition-selftest   →  StateApplierCli 의 --self-test 경로
recovery-selftest           →  RecoveryCli 의 --self-test 경로
trust-origin-selftest       →  TrustOriginCli 의 --self-test 경로
```

- **명령 자체(`state-transition`·`recovery`·`trust-origin`)를 등재하지 마라.** §2를 보라.
- 이름은 위 세 개로 고정한다. 조율자가 `GATE-MANIFEST`를 그 이름으로 고쳐야 하므로 임의로 바꾸면
  후속 작업이 어긋난다. 다른 이름이 낫다고 판단되면 **바꾸지 말고 보고에 적어라.**
- 각 등재는 `--self-test`만 부른다. 다른 하위 명령으로 넘어갈 수 있으면 안 된다.

## 2. ★ 왜 명령 자체를 등재하면 안 되는가 (넘으면 반려)

**`state-transition`은 `WORKSTATE`의 유일한 writer다.** 등재하면 게이트가 상태 writer를 부를 수
있게 된다. `GATE-MANIFEST`는 조율자 영역이므로, 등재된 명령은 **매니페스트 한 줄로 게이트에서
실행 가능해진다** — 사람 결재 없이 상태를 쓰는 경로가 열린다.

`trust-origin`도 `declare` 하위 명령으로 신뢰 원점을 만든다. `recovery`도 진단 외 경로가 있다.

**self-test만 등재하면 그 위험이 없다.** 실측: 세 self-test를 연속 실행한 뒤 워크트리 변경
파일 수가 **0 → 0**이었다(2026-07-26). 임시 디렉터리에서만 돈다.

## 3. 하지 않을 일

- `state-transition`·`recovery`·`trust-origin` 자체의 등재.
- `GATE-MANIFEST.json`·`HARNESSES.md` 수정 — **영역 밖.** 조율자가 후속으로 한다.
- self-test 케이스 수·내용 변경. 이 지시서는 **배선만** 한다.
- `di-completion-check`의 `BuiltInCommands` 확장으로 대신하는 것 — 그건 목록을 늘릴 뿐
  등재가 아니고, `program-verify`가 복제한 목록과 또 갈린다(`ADR-016` §7).

## 4. 완료 조건

### 지표 기준 (기계 판정)

- [ ] `build-verify` **exit 0**
- [ ] `dotnet run --project server -- state-transition-selftest` **exit 0** (나머지 둘도 동일)
- [ ] `HarnessRegistry.RegisteredNames`에 세 이름이 **전부** 있다
- [ ] 세 self-test 실행 후 **워크트리 변경 0** (비파괴 유지)
- [ ] `state-transition-selftest`에 다른 인자를 줘도 **`--self-test` 외 경로로 가지 않는다**

### 목적 기준 (사람 판정)

**"게이트가 아는 검사만 돌고, 게이트가 상태를 쓸 수는 없다."**

지표만 만족시키는 우회로: 세 명령을 통째로 등재하고 매니페스트에서 `--self-test`만 부르는 것.
그러면 지표는 전부 통과하지만 **매니페스트 한 줄로 상태 writer를 부를 수 있는 상태**가 된다.
**목적 미달이다.**

## 5. 반증 시험 (전부 실측. **코드 검토로 갈음하지 마라**)

| # | 시험 | 기대 |
| --- | --- | --- |
| 1 | `state-transition-selftest` 실행 | exit 0, 19 케이스 전부 pass |
| 2 | 세 self-test 연속 실행 후 `git status --porcelain` | **변경 0줄** |
| 3 | `state-transition-selftest prepare --transition-id X` 처럼 다른 인자 | **prepare로 가지 않는다**(거부 또는 self-test만 수행) |
| 4 | `HarnessRegistry.RegisteredNames` 출력 | 세 이름 존재, `state-transition` 등 원 명령은 **없음** |

**시험 2·3의 실제 출력을 실행 보고에 붙여라.** 특히 시험 3은 이 지시서의 목적 자체다.

## 6. 후속 (이 지시서가 하지 않는다)

조율자가 `GATE-MANIFEST.json`의 `WP-STATE-INTEGRITY-LAND`에서 세 검사의 `command`를
새 이름으로 바꾸고 `HARNESSES.md`를 `--emit-doc`으로 재생성한다. 그 뒤 두 러너로 다시 재면
`ADR-016`의 권위 이관과 `TRUSTED_BASELINE` 재측정이 가능해진다.

## 착륙 (두 걸음 — `skills/common/directive-authoring.md` §7)

착륙 전 `crossDirectivePinCollisions`를 확인하라. `DLINT-01`이 `server/Harness/**`의 여러 파일을
pin한다.

## 허용 파일 (allowlist)

- server/Harness/HarnessRegistry.cs

> 검증 문서는 영역 밖이라 조율자가 쓴다. `requiredInputs`에 `HarnessRegistry.cs`를 넣지 않은 것도
> 규칙이다(`_header.md`: 읽기 참조와 쓰기 대상은 겹치지 않는다).
