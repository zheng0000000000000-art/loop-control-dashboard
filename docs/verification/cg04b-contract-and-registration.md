# CG04B 검증 — CLI 계약 데이터와 게이트 등재

- **주체(actor)**: 조율 세션(Claude Opus 5). 대상이 `docs/handoff/**`라 코덱스 영역 밖이며
  **직접 경로**로 수행했다(사유: `CLAUDE.md` 관례 ①, 문서·데이터 파일).
- **날짜**: 2026-07-26 · **선행**: `CG04A` 반입(`947bafe`)

## 1-A. `CLI-CONTRACT.json` — **완료**

`di-completion-check --emit-cli-contract`의 출력을 그대로 썼다. **손으로 적은 항목은 없다.**

```
schemaVersion 1 · 명령 31 · critical 19
```

열거가 실재 배선을 읽었다는 증거: 출력에 조율자가 같은 날 추가한 `codex-launch`·`program-verify`가
들어 있다. 손으로 만든 목록이면 있을 수 없다.

## 1-B. 게이트 등재 — **등재하지 않기로 판정. 이유를 매니페스트에 기록**

`scope-check`·`claim-check`는 **등재하지 않았다.**

```
scope-check  → {"error":"usage: scope-check <directivePath|diId> [--actor ...]"}   exit 2
claim-check  → {"error":"사용법: claim-check <diId>"}                              exit 2
```

**둘 다 지시서별 검사다.** 게이트 시점에는 대상 diId가 없으므로 기대 exit code를 정할 수 없다.
원본 `CODEX-GATE-04` §3이 이 경우를 명시했다: *"판정할 수 없는 검사는 넣지 마라 — 기대값을 정할 수
없으면 넣지 말고 `note`에 이유를 적어라."* 그대로 따랐고 `GATE-MANIFEST.json`의
`POST-EXECUTOR`·`POST-COMMIT`에 `unregisteredChecks`로 사유를 남겼다.

`HARNESSES.md`는 `--emit-doc`으로 재생성했다(손으로 고치지 않았다).

## 반증 시험 — **시험 1 실패. 이 지시서는 완료가 아니다**

| # | 시험 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1 | `critical: true`인 `trust-origin` 배선을 임시 제거 후 게이트 | **exit 1**, 어느 명령인지 이름이 나온다 | **exit 1이지만 이름이 없다** ❌ |
| 2 | 배선 복구 | exit 0 | **PASS, exit 0** ✅ |

시험 1의 실제 기록(`outputs/gates/cli-neg.gate.json`):

```json
"failures": [{"subject": "", "code": "harness-error", "message": "The node already has a parent."}]
```

**계약 위반을 보고한 것이 아니라 하네스가 터졌다.** `System.Text.Json.Nodes`의 부모 중복 오류다.
배선이 온전하면 PASS가 나오고, **계약 위반이 실제로 생기면 크래시한다** — 즉 이 검사기는
**자기가 존재하는 이유인 경로에서만 동작하지 않는다.**

**`exit 1`이라는 지표는 만족했고 목적은 미달이다.** 게이트가 멈추기는 하지만 *무엇이 사라졌는지*
말하지 못한다. 원본 §2가 요구한 것은 멈춤이 아니라 **"배선이 사라지면 게이트가 그것을 말한다"**였다.

## 지표는 만족했으나 목적은 미달인 부분

1. **위 시험 1이 목적 미달의 정확한 사례다.** exit code만 보면 통과로 셀 수 있었다.
   `failures[]`를 열어보지 않았으면 "게이트가 잡았다"고 보고했을 것이다.
2. **원인은 `CG04A` 반입 코드에 있다**(`server/Harness/DiCompletionCheckCli.cs`, 코덱스 배타 영역).
   조율자가 고칠 수 없다. 후속 지시서 `CG04A-R1`로 낸다.
3. **`CG04A` 반입 때 반증 시험 2·3·5를 실측하지 않은 결과가 여기서 드러났다.** 지시서가
   *"코드 검토로 갈음하지 마라"*고 명시했는데 코드 반영 확인에 그쳤고, 건너뛴 시험이 잡았을
   결함을 다음 지시서가 대신 만났다.
4. 따라서 **`CG04B`는 1-A·1-B만 완료이고 §3의 완료 조건("계약에 있는 명령을 배선에서 하나 지우면
   게이트가 exit 1")은 미충족**이다. `CG04A-R1` 착륙 후 시험 1을 다시 돌려야 한다.

---

## 정정 (2026-07-26, 같은 세션) — 시험 1이 통과로 바뀌었다

`CG04A-R1` 반입(`6a6bf9e`) 뒤 같은 시험을 다시 돌렸다. **위 §"반증 시험" 표의 시험 1 판정을
실패에서 통과로 정정한다.** 원 기록은 지우지 않는다 — 그때는 실제로 실패였다.

| # | 시험 | 이전 (`a29b80f` 시점) | 지금 (`6a6bf9e` 이후) |
| --- | --- | --- | --- |
| 1 | `critical` 배선 제거 후 게이트 | exit 1이지만 `harness-error`, `subject` 비어 있음 ❌ | **exit 1 · `cli-wiring-missing` · 명령 이름 있음** ✅ |
| 2 | 배선 복구 | PASS ✅ | **`cli-wiring-missing` 0건** ✅ |
| 4 | 두 개 동시 제거 | 미실시 | **둘 다 보고** ✅ |

```json
[{"subject": "recovery",     "critical": false, "code": "cli-wiring-missing", "message": "contract command is not wired"},
 {"subject": "trust-origin", "critical": false, "code": "cli-wiring-missing", "message": "contract command is not wired"}]
```

**따라서 `CG04B` §3의 완료 조건 "계약에 있는 명령을 배선에서 하나 지우면 게이트가 exit 1"은
이제 충족된다.** 목적 기준 *"배선이 사라지면 게이트가 그것을 말한다"*도 만족한다 — 이름이 나온다.

### 그래도 남는 것 (완료 판정을 부풀리지 않는다)

1. **`critical: true` 경로는 여전히 미검증이다.** 이번에 지운 `recovery`·`trust-origin`은 생성기가
   `critical: false`로 분류했다. 원본 §2는 *"`critical: true`가 사라지면 **무조건 실패**"*를 요구하는데
   그 경로를 통과시킨 시험이 없다. `critical: true`인 명령(예: `measure`·`projection`)으로 다시 재야 한다.
2. **시험 3(계약에 없는 새 명령 → warning)을 돌리지 않았다.** 실패가 아니라 warning인지 미확인이다.
3. 위 둘 때문에 **`CG04B`를 "전부 완료"로 적지 않는다.** §3의 해당 항목만 충족으로 정정한다.
