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
