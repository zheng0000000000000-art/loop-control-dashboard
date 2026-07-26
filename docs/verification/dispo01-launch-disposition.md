# DISPO-01 반입 검증 — 실행자 산출물의 처분을 기록으로 요구한다

- **주체(actor)**: 산출은 **코덱스**(`LAUNCH-DISPO-01`). 검증·반입은 **조율 세션(Claude Opus 5)**.
  결재는 **사람**(git user `Jaehyuk`).
- **날짜**: 2026-07-26 · 근거: `ADR-016` §14

## 사용한 하네스 (명령 · exit code · 수치)

| 명령 | exit | 수치 |
| --- | --- | --- |
| `codex-launch validate` / `launch --manual` | 0 / **0** | 변경 3, `scopeViolations` 0, 누락 0 |
| `build-verify` (패치 후) | 0 | 오류 0 |
| `launch-disposition outbox` | **1** | `launchCount` 16 · `violations` **16** (전부 `disposition-missing`) |
| `measure dev-pack` | 0 | `{"gate":"dev-pack","violations":0,"attempt":1}` |
| `doc-integrity` · `context-pack-integrity` · `handoff-integrity` | 0 | 착륙 **한 걸음** |

`HarnessRegistry.cs:28`에 `launch-disposition` 등재됨 — 두 게이트 러너가 아는 명령이 됐다.

## 반증 시험 (지시서 §4 — 8개 전부 실측, **사유까지 대조**)

| # | 픽스처 | 기대 | exit | 사유 |
| --- | --- | --- | --- | --- |
| 1 | 패치 있음 · 기록 없음 | 위반 1 | **1** | `disposition-missing` |
| 2 | `rejected` + `reason` | 위반 0 | **0** | — |
| 3 | `imported` · 보고 파일 없음 | 위반 1 | **1** | `gate-report-not-found` |
| 4 | `imported` · 보고의 launchId 다름 | 위반 1 | **1** | `gate-report-launch-id-mismatch` |
| 5 | **`imported` · 보고가 반입보다 앞섬** | 위반 1 | **1** | **`gate-report-predates-import`** |
| 6 | `imported` · 전부 정합 | 위반 0 | **0** | — |
| 7 | `no-output` · 패치 빈 것 | 위반 0 | **0** | — |
| 8 | **`no-output`인데 패치 있음** | 위반 1 | **1** | **`no-output-has-patch`** |

**시험 5가 이 지시서의 핵심이다.** 반입 **전에** 잰 게이트 보고를 갖다 붙이는 것이 가장
그럴듯한 거짓이고, 파일 존재만 보는 구현은 그걸 통과시킨다. 사유가 별도 코드로 나온다 —
`gate-report-not-found`(3)와 구분된다는 것이 실제로 내용을 읽는다는 증거다.

**시험 8은 반대 방향이다.** 기록이 실체보다 **적게** 말하는 경우도 위반이다.

## 실제 저장소 실측 — 16건 전부 미기록

```
launchCount 16 | violations 16 | 전부 disposition-missing
```

반입한 것(`GWIT-05-R2`·`GWIT-06`·`HREG-02`·`DICC-01`…), 요청 형식 오류로 버린 것(`GWIT-05`),
산출이 없던 것(`GWIT-04`)이 **파일로 전혀 구분되지 않는다.** 이제 그 사실이 수치로 나온다.

## 지시서 §3의 예측이 하나 빗나갔다

*"위반 15건"*이라고 적었는데 실측은 **16건**이다. 지시서를 쓴 시점의 15개에
**이 지시서를 쏜 발사(`LAUNCH-DISPO-01`) 자신이 만든 디렉터리 하나**가 더해졌다.
알 수 있었던 것이고, 예측을 고정 숫자로 적은 것이 잘못이다. **검사가 틀린 것이 아니다.**

## 매니페스트에 등재하지 않았다 (§6 순서)

지금 등재하면 **16건이 즉시 위반이라 영구 적색**이 되고, 그러면 아무도 안 본다(`FAIL-2026-010`).
오늘 `POST-COMMIT`에서 실제로 겪었다. 순서는 **backfill → 등재**다.

backfill은 **사람·조율자의 판단**이다. 실행자가 대신 정하지 않도록 지시서 §2에서 금지했고,
코덱스는 기존 16개를 건드리지 않았다(`changedPaths` 실측).

## 참조한 스킬

`skills/common/directive-authoring.md` §7.

## 지표는 만족했으나 목적은 미달인 부분

1. **`disposition.json`이 하나도 없다.** 하네스는 만들었지만 **기록은 아직 없다.**
   지금 상태는 *"구분되지 않는다"*를 *"구분되지 않는다고 세었다"*로 바꾼 것뿐이다.
   backfill 전까지 실질은 그대로다.
2. **`codex-launch`가 처분 기록을 요구하지 않는다.** 발사할 때 `state: "pending"`을 자동으로
   남기게 하면 미기록 자체가 사라지는데, 그건 영역 밖이라 별도 결재로 남겼다(§6-3).
3. **반입 여부를 하네스가 검증하지는 않는다.** `disposition.json`이 `imported`라고 말하면
   그렇게 믿는다(다만 게이트 보고와의 정합은 확인한다). 패치가 실제로 적용됐는지는
   diff로 추정할 수 없어 의도적으로 빼놓았다(§1-B) — **기록이 진실의 출처**라는 설계다.

---

# backfill (append, 2026-07-26) — 16건 전부 기록했고 매니페스트에 등재했다

원문의 미달 ①(*"기록은 아직 없다"*)을 닫는다. 원문은 그 시점에 참이었으므로 지우지 않는다.

## 처분을 어떻게 정했는가 — 전부 실측

| 상태 | 건수 | 근거 |
| --- | --- | --- |
| `imported` | **14** | 신규 파일이 추가된 커밋(`git log --diff-filter=A`) 7건 + 추가된 내용으로 찾은 커밋(`git log -S`) 7건 |
| `rejected` | **1** | `LAUNCH-GWIT-05` |
| `no-output` | **1** | `LAUNCH-GWIT-04` — `candidate.patch`가 빈 파일 |

**커밋 메시지로 짐작하지 않았다.** `importCommit`은 그 패치가 만든 파일이 실제로 추가된 커밋,
또는 그 패치가 추가한 줄이 실제로 들어간 커밋이다. CLAUDE.md가 금지한 *"커밋 접두사·타임스탬프
상관"* 을 쓰지 않았다.

`LAUNCH-GWIT-05`를 `rejected`로 판정한 근거도 실체다: 그 패치가 만든
`docs/qa/gate-witness/build-verify-broken/Program.cs`는 **저장소에 추가된 커밋이 없다.**
R2가 만든 `Broken.cs`는 `b213b63`에 있다. **두 산출물이 파일 단위로 구분된다.**

## ★ `gateReport`는 역사적으로 존재한 적이 없었다

```
outputs/gates/ 보고 32개 중 launchId를 담은 것: 0개
```

`state: "imported"`가 요구하는 연결을 **파이프라인이 한 번도 만들지 않았다.**
내가 방금 만든 검사의 통과 조건이 역사에 대해 **도달 불가능**했다는 뜻이다.

**지어 넣지 않았다.** `program-verify verify --gate POST-COMMIT --launch <id>`를
**14번 실제로 돌려** `outputs/gates/backfill/`에 남겼다. 전부 `PASS 12/12`.

각 `disposition.json`의 `note`에 이렇게 적었다:

> 반입 당시에는 launchId를 담은 게이트 보고가 만들어지지 않았다. 여기 적힌 gateReport는
> backfill 시점 HEAD에서 실제로 다시 잰 것이며, **반입 당시의 판정이 아니다.**

**14개 보고의 `baselineCommit`은 모두 같다.** 각 반입이 그때그때 개별로 게이트를 통과했다는
뜻이 아니라, **그 반입들을 모두 담은 트리가 지금 POST-COMMIT을 통과한다**는 뜻이다.
읽는 사람이 오해하지 않도록 여기와 각 파일에 남긴다.

## 등재 (§6-2)

backfill이 **끝난 뒤** 등재했다 — 순서를 지켰다.

```
POST-COMMIT + launch-disposition ['outbox']                                  exp 0
POST-COMMIT + launch-disposition ['docs/qa/gate-witness/.../case-01']        exp 1  ← 반증 witness
```

| 게이트 | witnessed | 미반증 |
| --- | --- | --- |
| POST-EXECUTOR | 13/13 | 0 |
| **POST-COMMIT** | **14/14** | **0** |
| WP-STATE-INTEGRITY-LAND | 18/18 | 0 |

`launch-disposition outbox` → **exit 0, launchCount 16, violations 0.**

## 지표는 만족했으나 목적은 미달인 부분

1. **`gateReport` 14개가 동일한 측정이다.** 계약(§1-A: `baselineCommit >= importCommit`)은
   진실하게 만족하지만, **반입 시점 판정이라는 강한 의미는 없다.** 앞으로의 발사부터가 진짜다.
2. **`codex-launch`가 여전히 처분을 요구하지 않는다.** 다음 발사도 `disposition-missing`으로
   시작하고 사람이 손으로 채워야 한다. 발사 시점 `state: "pending"` 자동 기록은 별도 결재다.
3. **`no-output` 사유는 실행 보고의 서술을 옮긴 것이다.** `candidate.patch`가 빈 파일이라는 것은
   실측이지만, *왜* 비었는지(NU1301 TLS 실패)는 **코덱스의 자기보고**다. 내가 재현하지 않았다.

---

# DISPO-02 (append, 2026-07-26) — `pending`을 알게 하고 런처가 자동으로 남긴다

## 반증 시험 (지시서 §4 — 6개 전부 실측, 사유까지)

| # | 픽스처 | 기대 | exit | 사유 |
| --- | --- | --- | --- | --- |
| 1 | `pending` 정상 | 위반 1, 새 사유 | **1** | **`disposition-pending`** |
| 2 | `pending` · `actor` 없음 | 또 다른 사유 | **1** | **`actor-missing`** |
| 3 | `pending` · launchId 불일치 | 불일치 사유 | **1** | **`launch-id-mismatch`** |
| 4 | 알 수 없는 state | 변화 없음 | **1** | `state-invalid` |
| 5 | `pending` 1 + 정상 `imported` 1 | **위반 1** | **1** | `disposition-pending` 하나만 |
| 6 | 기존 case-01~08 | 이전과 동일 | **1,0,1,1,1,0,0,1** | 동일 |

**사유가 셋 다 다르다**(1·2·3). 필드를 실제로 읽는다는 증거이며, *"기록이 없다"* 와
*"아직 안 정했다"* 가 출력에서 구분된다 — 지시서 §0의 목적이다.

**시험 5가 세는 방식을 잡았다.** 2도 0도 아닌 1이다.

## 런처 (§6-1, 조율자 몫)

`codex-launch`가 발사 성공 시 `disposition.json`을 `state: "pending"`으로 남긴다.
**이미 파일이 있으면 덮지 않는다** — 실측으로 확인(`LAUNCH-GWIT-05`의 `rejected` 유지).

## ★ 이 반입 자체가 필요성을 실물로 보여줬다

`DISPO-02`를 쏘자 outbox가 **17건**이 되고 `LAUNCH-DISPO-02` 하나가 `disposition-missing`으로
잡혔다. 런처가 아직 안 고쳐진 시점의 발사였기 때문이다. **검사가 자기를 만든 발사를 잡았다.**

## 그리고 순서 하나를 더 드러냈다

`LAUNCH-DISPO-02`의 처분을 정하려고 게이트 보고를 먼저 만들려 했더니 **FAIL 1/14**였다.
사유는 `launch-disposition ['outbox']`가 그 시점에 위반을 냈기 때문이다.

**처분을 정하기 전에는 게이트가 통과할 수 없다.** 의도한 강제 방향이 실물로 확인됐다.
처분을 먼저 쓰고 다시 재서 **PASS 0/14**를 얻었다.

### 자기참조 충돌 하나 — 임시 경로로 우회했다

두 번째 시도도 FAIL이었는데 사유가 달랐다:

```
launch-disposition ['outbox'] exp 0 got 2
  "The process cannot access the file ... LAUNCH-DISPO-02.gate.json"
```

`disposition.json`이 가리키는 `gateReport` 파일에 **그 게이트 실행의 stdout을 직접 리다이렉트**하고
있었다. 검사가 읽으려는 파일을 같은 실행이 쓰고 있었다. **임시 경로에 재고 PASS를 확인한 뒤
복사**해서 풀었다.

**남는 성질**: `gateReport`가 가리키는 경로에 게이트 출력을 직접 쓰면 그 게이트가 실패한다.
하네스가 막지는 않는다 — 운영 관례로 남는다.

## 지표는 만족했으나 목적은 미달인 부분

1. **`pending` 유예가 없다.** 발사 후 사람이 처분을 정할 때까지 `POST-COMMIT`이 빨갛다.
   의도한 것이지만 **운영 감각을 바꾸므로 `HUMAN-INBOX`에 결재로 올렸다.**
2. **자기참조 충돌을 코드가 막지 않는다.** `gateReport` 경로에 직접 리다이렉트하면 실패하는데,
   그 사실을 아는 방법은 이 문서뿐이다.
3. **`LAUNCH-DISPO-02`의 `gateReport`는 반입 커밋에서 잰 진짜 측정이다** — backfill 14건과 달리
   그 발사에 대응하는 시점의 판정이다. 앞으로의 발사도 이 모양이어야 한다.

---

# 자기참조 충돌을 코드로 막았다 (append, 2026-07-26)

앞 절의 미달 ②(*"자기참조 충돌을 코드가 막지 않는다"*)를 닫는다.

## 무엇이 문제였나

`disposition.json`의 `gateReport`가 가리키는 경로에 **게이트 실행의 stdout을 셸 리다이렉트**하면,
실행 내내 그 파일이 열려 있다. 그 파일을 읽는 `launch-disposition`이 죽는다.

```
launch-disposition ['outbox'] exp 0 got 2
  "The process cannot access the file ... LAUNCH-DISPO-02.gate.json"
```

**검사가 읽으려는 파일을 같은 실행이 쓰고 있었다.** 문서로 "그러지 마라"고 적는 것은
관례일 뿐이고, 다음 사람은 같은 곳에 빠진다.

## 무엇을 했나

`program-verify verify|request ... --out <path>`를 추가했다(`ProgramVerifierCli.cs`).

- **검사가 전부 끝난 뒤에만** 쓴다. 실행 중에는 그 경로를 건드리지 않는다.
- **임시 파일에 쓴 뒤 옮긴다.** 중간에 죽어도 반쯤 쓰인 JSON이 남지 않고 이전 보고가 남는다.

## 실측 — 충돌하던 그 상황을 그대로 재현

| 방식 | 결과 |
| --- | --- |
| 셸 리다이렉트 `> outputs/gates/backfill/LAUNCH-DISPO-02.gate.json` | `launch-disposition` **exit 2** (파일 접근 불가) → 게이트 FAIL |
| **`--out outputs/gates/backfill/LAUNCH-DISPO-02.gate.json`** (더러운 트리) | 충돌 **없음**. 실패는 `gate-clean` 하나뿐 — 커밋 전이라 참인 실패 |
| **`--out …`** (커밋 후 깨끗한 트리) | **exit 0** |

**같은 경로, 같은 게이트, 같은 검사인데 쓰는 시점만 옮겨서 통과한다.**

## 지표는 만족했으나 목적은 미달인 부분

1. **셸 리다이렉트를 여전히 쓸 수 있다.** `--out`은 안전한 대안을 준 것이지 위험한 길을 막은 것이
   아니다. 프로세스가 자기 stdout이 어디로 가는지 알 수 없어 코드로 탐지할 방법을 찾지 못했다.
   **다만 이제 "관례를 지켜라"가 아니라 "이 인자를 써라"가 됐다** — 지킬 수 있는 형태다.
2. **`di-completion-check`에는 `--out`이 없다.** 그쪽은 자체 evidence 경로에 쓰므로 같은 충돌이
   나려면 `gateReport`가 그 경로를 가리켜야 한다. 지금은 그런 기록이 없지만 구조적으로 가능하다.
   **코덱스 영역이라 후속이다.**
3. **`launch-disposition`은 여전히 읽기 실패 시 전체를 exit 2로 중단한다.** 한 파일이 잠겨 있으면
   나머지 16건도 판정되지 않는다. 건별 위반(`gate-report-unreadable`)으로 낮추는 편이 낫다 —
   **코덱스 영역이라 후속이다.**

---

# DISPO-03 (append, 2026-07-26) — 한 건이 깨져도 나머지를 판정한다

앞 절의 미달 ③(*"읽기 실패 시 전체를 exit 2로 중단한다"*)을 닫는다.

## 반증 시험 (지시서 §4 — 6개 전부 실측, 사유까지)

| # | 픽스처 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1 | `gateReport`가 **디렉터리**를 가리킴 | 위반 1, `not-found`와 다른 사유 | **`gate-report-unreadable`** |
| 2 | `gateReport`가 JSON이 아님 | 또 다른 사유 | **`gate-report-unparsable`** |
| 3 | `disposition.json`이 JSON이 아님 | `state-invalid`와 다른 사유 | **`disposition-unparsable`** |
| 4 | **깨진 1건 + 정상 2건** | **launchCount 3 · violations 1** | **3 · 1** |
| 5 | 루트 경로 없음 | exit 2 | **2** |
| 6 | 기존 case-01~13 | 이전과 동일 | **1,0,1,1,1,0,0,1,1,1,1,1,1** — 동일 |

**시험 4가 목적 자체다.** `launchCount`가 2였으면 깨진 건을 목록에서 빼버린 것이고
(*"그런 발사는 없었다"*), `violations`가 3이었으면 정상 2건까지 물들인 것이다.

**사유가 넷 다 다르다** — `not-found`·`unreadable`·`unparsable`(보고)·`unparsable`(처분).
*"없다"* 와 *"있는데 못 읽는다"* 가 구분된다. 후자는 대개 다른 프로세스가 쓰고 있다는 뜻이고,
그게 이 문제의 출발점이었다.

## DISPO-02가 끝까지 확인됐다

이 발사에서 런처가 `disposition.json`을 `pending`으로 **자동 생성**했고,
`launch-disposition`이 `disposition-missing`이 아니라 **`disposition-pending`**으로 잡았다.

```
launch-disposition outbox → launchCount 18 | violations 1
  LAUNCH-DISPO-03 -> disposition-pending
```

**기록이 없는 것과 아직 안 정한 것이 실제로 구분된다.**

## ★ 순서를 하나 더 배웠다 — 보고를 먼저, 처분을 나중에

처분을 먼저 쓰고 게이트를 돌렸더니 **FAIL 1/14**였다. 사유:

```
launch-disposition ['outbox'] exp 0 got 1     (gate-report-not-found)
```

`disposition.json`이 가리키는 보고를 **그 실행이 만들기 때문에**, 첫 실행 시점에는 그 파일이
아직 없다. `--out`이 검사 뒤에 쓰므로 필연이다. 다시 재니 **PASS 0/14**.

**운영 순서**: `--out`으로 보고를 먼저 만들고 → 처분을 쓰고 → 다시 재서 확인한다.
처분을 먼저 쓰면 반드시 한 번 실패한다. **결함이 아니라 의존 순서다.**

## 지표는 만족했으나 목적은 미달인 부분

1. **위 순서를 코드가 강제하지 않는다.** 아는 방법은 이 문서뿐이다.
   `--out` 때 그 경로가 어떤 `disposition.json`에 참조되는지 보고 미리 만들어 두는 식의
   해결이 가능하지만, 러너가 처분 파일을 아는 것은 결합이 과하다고 판단해 하지 않았다.
2. **`di-completion-check`에는 `--out`이 없다.** `DISPO-03` §6에 별도 지시서로 남겼다.
3. **`gate-report-unreadable` 메시지에 예외 원문이 그대로 실린다.** 절대 경로가 노출되고
   길다. 사유 코드로 판정하면 되지만 출력이 지저분하다 — 고치지 않았다.

---

# 정정 (2026-07-26) — `di-completion-check --out`은 필요 없었다

앞 절들에서 두 번 *"`di-completion-check`에는 `--out`이 없어 같은 자기참조 충돌이 가능하다"*고
적었다(`DISPO-03` §6, 이 문서 미달 ②). **전제를 확인하지 않고 쓴 문장이었고, 틀렸다.**

| 확인한 것 | 실측 |
| --- | --- |
| 출력 경로를 정할 수 있는가 | **된다.** `--task dispo-probe` → `outputs/gates/dispo-probe.gate.json`, exit 0 |
| 증거를 언제 쓰는가 | **검사가 끝난 뒤**(`DiCompletionCheckCli.cs:82`) |
| 자기참조 충돌이 나는가 | **안 난다.** 실행 중 그 경로를 열어 두지 않는다 |

`program-verify`가 충돌한 이유는 **stdout을 셸로 리다이렉트해야 했기 때문**이다.
그쪽은 처음부터 파일을 자기가 쓴다. **같은 문제가 아니었다.**

## 대신 진짜 결함을 찾았다

`di-completion-check` 보고에 **`launchId`가 없다**(최상위 키 12개 확인).
`launch-disposition`은 `gateReport`의 `launchId`가 발사와 같은지 본다.
그러므로 **그 보고는 `gateReport`로 쓸 수 없다.**

`ADR-016` §8은 게이트 권위를 `di-completion-check`에 줬다. 그런데 **권위 있는 러너로 판정하면
그 판정을 처분에 기록할 수 없다.** 규칙과 도구가 반대를 가리키고, 사람은 기록되는 쪽을 쓰게 된다.
`DICC-02`가 이것을 맡는다.

**교훈**: "같은 결함이 저기도 있을 것"이라는 추정을 **미달 항목으로 두 번 적었다.**
자진 신고는 좋지만 **확인하지 않은 추정을 사실처럼 적으면 그것도 잘못된 기록**이다.

---

# DICC-02 (append, 2026-07-26) — 권위 있는 러너도 처분 증거를 낼 수 있다

## 반증 시험 (지시서 §4 — 6개 전부 실측)

| # | 시험 | 기대 | 실측 |
| --- | --- | --- | --- |
| 1 | `--launch LAUNCH-X --task dicc02-t1` | 보고에 `launchId`, 파일은 `t1.gate.json` | **`launchId: "LAUNCH-X"`**, 파일명 일치 |
| 2 | **보고에 `launchId` 없음 + 처분은 `LAUNCH-X`** (case-18) | **막는다** | **exit 1** |
| 3 | 보고 `LAUNCH-Y` + 처분 `LAUNCH-X` (case-19) | 막는다 | **exit 1** |
| 4 | 보고·처분 둘 다 `LAUNCH-X` (case-20) | 위반 0 | **exit 0** |
| 5 | 낡은 바이너리 + `--launch LAUNCH-X` | exit 2 · `NOT-MEASURED` · `launchId` | **2 · NOT-MEASURED · `LAUNCH-X` · 검사 0개** |
| 6 | 인자 없이 | 이전과 동일 · `launchId` 없음 | **`launchId: null`**, 판정 동일 |

**시험 2가 핵심이었다.** 귀속을 더하면서 *"없으면 아무거나 맞는다"* 로 만들면
`DISPO-01` 시험 4가 막던 거짓이 다시 열린다. **없으면 여전히 막힌다.**

**시험 5**: 못 잰 보고에도 대상이 적힌다. *"무엇을 못 쟀는지 모르는 기록"* 이 안 남는다.

## 무엇이 풀렸나

`ADR-016` §8은 게이트 권위를 `di-completion-check`에 줬는데, 그 러너의 보고에는
`launchId`가 없어 **`gateReport`로 쓸 수 없었다.** 권위 있는 쪽으로 판정하면 기록할 수 없고,
기록하려면 권위 없는 쪽을 써야 했다 — **규칙과 도구가 반대를 가리켰다.**

이제 **두 러너 모두 처분 증거를 낼 수 있다.** §8의 정본 결정을 사람이 실제로 실행할 수 있다.

## 지표는 만족했으나 목적은 미달인 부분

1. **`ADR-016` §8은 여전히 결정되지 않았다.** 실행 가능해졌을 뿐, 어느 쪽이 정본인지는
   사람 결재로 남아 있다. 같은 일을 하는 코드가 두 벌인 상태도 그대로다.
2. **기존 처분 15건의 `gateReport`는 전부 `program-verify` 산이다.** 앞으로 어느 쪽으로
   낼지 정해지면 섞이게 된다. 하네스는 러너 종류를 구분하지 않는다 — 의도한 것이지만
   *"무엇이 판정했는가"* 를 보려면 보고를 열어 `harness`/`verifier` 필드를 봐야 한다.
3. **`--launch`를 안 써도 아무도 경고하지 않는다.** 처분을 쓸 때가 되어서야 mismatch로 드러난다.
   런처가 `pending`을 남기듯, 게이트 러너가 발사 맥락을 자동으로 알 방법은 없다.

## ★ 정정 (같은 날, 반입 직후) — `DICC-02`는 절반만 고쳤다

바로 위에서 *"두 러너 모두 처분 증거를 낼 수 있다"* 고 적었다. **거짓이다.**

`di-completion-check`가 낸 보고를 실제로 `gateReport`로 써 보니:

```
launch-disposition outbox → LAUNCH-DICC-02 -> gate-report-baseline-invalid
di-completion-check 보고 키: ... launchId 있음 ... baselineCommit 없음
```

`launch-disposition` §1-A는 **두 가지**를 본다 — `launchId` 일치 **그리고**
`baselineCommit >= importCommit`. **`DICC-02` 지시서는 앞의 하나만 요구했다.**

**내가 `DISPO-01` §1-A에 직접 쓴 규칙인데 후속 지시서에서 빠뜨렸다.**
그리고 `launchId`가 실리는 것만 확인하고 *"이제 쓸 수 있다"* 고 단정했다 —
**끝까지 써 보지 않고 중간 지표로 결론을 냈다.** 시험 4가 그걸 잡으라고 있었는데,
그 시험은 픽스처(`case-20`)로만 돌았고 **실제 러너 산 보고로는 안 돌았다.**

### 지금 상태

`LAUNCH-DICC-02`의 `gateReport`는 **`program-verify` 산으로 바꿨다.**
`launch-disposition outbox` **exit 0**(19/19). 저장소는 정합하다.

`DICC-02`가 더한 `launchId`는 옳고 필요하다 — **다만 충분하지 않다.**
`baselineCommit`을 더하는 후속 지시서가 필요하다.

---

# DICC-03 (append, 2026-07-26) — 권위 있는 러너의 보고를 실제로 왕복시켰다

앞 정정(*"`DICC-02`는 절반만 고쳤다"*)을 닫는다.

## ★ 시험 1 — 픽스처가 아니라 **실물**로 왕복

`DICC-02`가 실패한 지점이 여기였다. 이번에는 **조율자가 직접** 돌렸다.

```
1) di-completion-check --gate POST-COMMIT --launch LAUNCH-PROBE --task dicc03-probe
2) 그 보고를 gateReport로 적은 처분을 임시 루트(.dicc03-probe/)에 구성
3) launch-disposition .dicc03-probe   →  exit 0 · violations 0
```

보고 실측:

| 항목 | 값 |
| --- | --- |
| `launchId` | `LAUNCH-PROBE` |
| `baselineCommit` | 40자리 hex **True**, `git rev-parse HEAD`와 **일치** |
| `worktreeCleanAtStart` | `False` — 그 시점 트리가 실제로 더러웠다 |

**`di-completion-check`의 보고가 이제 `gateReport`로 쓰인다.**
`ADR-016` §8이 권위를 준 러너로 판정한 결과를 처분에 기록할 수 있다.

## 나머지 시험

| # | 시험 | 실측 |
| --- | --- | --- |
| 4 | 낡은 바이너리 + `--launch` | **exit 2 · `NOT-MEASURED` · 검사 0개 · `launchId`·`baselineCommit` 실림** |
| 5 | `--launch` 없이 | `launchId` 없음, **`baselineCommit`은 여전히 40hex** — 발사와 독립 |
| 6 | `case-01`~`case-20` 회귀 | `10111001111111111110` — **이전과 동일** |

**시험 5가 설계 의도를 지킨다.** `baselineCommit`은 *"무엇을 쟀는가"*이므로 발사 귀속과 독립이다.

### 시험 6에서 내가 한 번 틀렸다

실측값을 손으로 적어 둔 "이전" 문자열과 대조했는데 **그 손으로 적은 쪽이 틀렸다.**
실제 이전값을 구성 요소에서 다시 조립하면(`case-01~13` `1011100111111` +
`case-14~17` `1111` + `case-18~20` `110`) 실측과 정확히 같다. **회귀는 없다.**

*기억으로 적은 기준선과 대조하면 그 기준선이 틀릴 수 있다* — 오늘 이 실수를 여러 번 했다.

## 지표는 만족했으나 목적은 미달인 부분

1. **코덱스의 빌드 환경이 달랐다.** 보고에 따르면 .NET 8 SDK 타기팅 팩이 없어
   `-p:TargetFramework=net10.0`으로 컴파일했다. **조율자 환경에서 다시 빌드해 시험을 전부
   재실행했으므로 판정은 조율자 실측이다** — 다만 코덱스가 무엇을 확인했는지는 그 보고뿐이다.
2. **`ADR-016` §8은 여전히 사람 결재다.** 이제 두 러너 모두 처분 증거를 낼 수 있어
   결정이 **실행 가능**해졌을 뿐이다.
3. **기존 처분 20건의 `gateReport`는 전부 `program-verify` 산이다.** 섞어 쓸지, 한쪽으로
   통일할지는 §8 결정에 딸린다.
