# CG04A-R1 검증 — CLI 계약 검사가 위반을 **말한다**

- **주체(actor)**: 산출은 **코덱스**(`CodexHarnessLauncher`, `LAUNCH-CG04A-R1`).
  검증·반입 집행은 **조율 세션**, 결재는 **사람**. 검증 문서는 코덱스 영역 밖이라 조율자가 쓴다.
- **날짜**: 2026-07-26 · **출처**: `CG04B` 반증 시험 1이 잡은 결함

## 사용한 하네스

| 명령 | exit | 수치 |
| --- | --- | --- |
| `codex-launch validate` | 0 | `ACCEPTED` |
| `codex-launch launch --manual` | 0 | 1파일, `scopeViolations` 0 |
| `build-verify` (패치 후) | 0 | 오류 0 |
| `measure dev-pack` | 0 | `violations 0` |
| `context-pack-integrity` · `doc-integrity` · `handoff-integrity` | 0 | — |

## 반증 시험 — **이번에는 전부 실측했다**

| # | 시험 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1·4 | `critical` 배선 **2개** 동시 제거 | exit 1 · 이름이 나온다 · **둘 다** 보고 | ✅ 아래 원문 |
| 2 | 배선 복구 | `cli-wiring-missing` 사라짐 | ✅ **0건** |

시험 1·4 원문:

```json
[{"subject": "recovery",      "critical": false, "code": "cli-wiring-missing", "message": "contract command is not wired"},
 {"subject": "trust-origin",  "critical": false, "code": "cli-wiring-missing", "message": "contract command is not wired"},
 {"subject": "gate-clean",    "code": "exit-mismatch", "message": "expected 0, actual 1"}]
```

**고쳐진 것이 정확히 세 가지다:**

1. `code`가 `harness-error` → **`cli-wiring-missing`**. 하네스가 터지지 않고 판정한다.
2. `subject`가 빈 문자열 → **사라진 명령 이름**. 무엇이 없어졌는지 말한다.
3. 두 개를 지웠더니 **둘 다** 나온다. 첫 번째에서 멈추지 않는다(시험 4).

세 번째 실패 `gate-clean exit-mismatch`는 검증 중 트리가 더러워서 나온 것이며 이 지시서와 무관하다.

## 이전 상태와의 대조

`CG04B` 시험 1(수정 전):

```json
[{"subject": "", "code": "harness-error", "message": "The node already has a parent."}]
```

**둘 다 exit 1이다.** 지표만 보면 구분되지 않는다 — `failures[]`를 열어야 갈린다.
`CG04B` 검증 문서에 *"exit code만 보면 통과로 셀 수 있었다"*고 적은 그 자리다.

## 지표는 만족했으나 목적은 미달인 부분

1. **`critical: false`로 나온다.** 계약 생성기가 `trust-origin`·`recovery`를 critical로 분류하지
   않았다. 원본 `CODEX-GATE-04` §2의 critical 목록은 `state-transition`·`projection`·`measure`·
   하네스 전부이고 이 둘은 거기 없으므로 **생성기 기준으로는 일관**되지만, `critical: true`인
   명령이 사라졌을 때 **무조건 실패**하는지는 이번 시험이 확인하지 못했다. 별도 시험이 필요하다.
2. **`CG04B` §3의 완료 조건은 이제 충족된다**고 본다. 다만 그 판정은 `CG04B`의 verification에
   append로 정정해야 하며 이 문서가 대신하지 않는다.
3. `ADR-016`의 `unknown command` 3건은 여전히 그대로다. `HarnessRegistry` 등재는 별도다.
