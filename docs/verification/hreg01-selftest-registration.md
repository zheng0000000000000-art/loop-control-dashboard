# HREG-01 검증 — self-test 등재, 명령 자체는 등재하지 않음

- **주체(actor)**: 산출은 **코덱스**(`LAUNCH-HREG-01`, 1파일·scope 위반 0).
  검증·반입 집행은 **조율 세션**, 결재는 **사람**.
- **날짜**: 2026-07-26 · **출처**: `ADR-016` §6·§7

## 반증 시험 — 4종 전부 실측

| # | 시험 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1 | 세 self-test 실행 | exit 0 | **셋 다 exit 0** ✅ |
| 2 | 연속 실행 후 워크트리 | 변경 0 | **1줄 → 1줄**(그 1줄은 패치 자신) ✅ |
| 3 | `state-transition-selftest prepare --transition-id EVIL --request …` | prepare로 가지 않는다 | **self-test만 수행, exit 0, `WORKSTATE` 0줄 변경** ✅ |
| 4 | `RegisteredNames` 대조 | 세 이름 존재, 원 명령 없음 | **아래** ✅ |

시험 4 — 등재 목록:

```
gate-clean hs-scan claim-check doc-integrity launch-check scope-check build-verify
path-guard-check call-integrity-check template-sync-check project-api-edge-check
handoff-integrity context-pack-integrity di-completion-check state-transition-callsite-check
state-transition-selftest recovery-selftest trust-origin-selftest
```

`state-transition`·`recovery`·`trust-origin` **원 명령은 없다.** 이 지시서의 목적이 그것이다 —
등재되면 `GATE-MANIFEST` 한 줄로 게이트가 `WORKSTATE` writer를 부를 수 있게 된다.

**시험 3이 목적 자체다.** 자기 이름 뒤에 `prepare`를 붙여도 그 경로로 새지 않는다.

## 착륙 (두 걸음)

1. **①** 패치 적용 → `context-pack-integrity` **exit 1** (`HarnessRegistry.cs` stale)
2. **②** pin 갱신 → **exit 0**

②의 pin은 `DLINT-01`이 아니라 **`GATE-TRUTH-01`**이 갖고 있었다(`1c804beaaf8b…` → `f5dcb5050458…`).
조율자가 처음에 `DLINT-01`을 가정하고 고치려다 실패했고, `crossDirectivePinCollisions`가 아니라
**stale 경로를 직접 grep해서** 실제 소유 지시서를 찾았다. 착륙 절차(§7)가 *"발사 전에 목록을
확인한다"*고 한 이유가 이것이다 — 확인하지 않으면 착륙 때 엉뚱한 파일을 고치려 든다.

## 사용한 하네스

`codex-launch validate/launch` 0 · `build-verify` 0 · `context-pack-integrity` 0(②이후) ·
`measure dev-pack` 위반 0 · `doc-integrity` 0 · `handoff-integrity` 0.

## 지표는 만족했으나 목적은 미달인 부분

1. **게이트는 아직 옛 이름을 쓴다.** `GATE-MANIFEST`의 `WP-STATE-INTEGRITY-LAND`가 여전히
   `state-transition --self-test`를 부르므로 **두 러너 모두 그 게이트를 거부한다.**
   이름 교체는 조율자 후속이며 이 지시서 범위 밖이다(지시서 §6).
2. 따라서 **`TRUSTED_BASELINE` 재측정은 아직 불가능하다.** 등재는 끝났지만 게이트가 그 이름을
   가리키지 않는다.
3. `ADR-016` §7의 `BuiltInCommands` 복제는 그대로다.
