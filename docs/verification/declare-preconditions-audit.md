# `12-B` 선행조건이 실제로 재는가 — 전수 확인

- **주체(actor)**: 조율 세션(Claude Opus 5). 사람 지시.
- **날짜**: 2026-07-26 · 대상: `TrustOriginCli.DeclareCore`의 선행조건

`HighRiskFailClosed()`가 `=> true` 상수였던 것을 계기로 **나머지도 상수·스텁인지** 전수 확인했다.

## 상수 반환 함수 — 이제 없다

```
grep -nE "=> *(true|false);" server/TrustOriginCli.cs   →   결과 없음
```

`HighRiskFailClosed`가 유일했고 오늘 실제 검사로 바꿨다.

## 조건별 판정

| # | 조건 | 무엇으로 재는가 | 판정 |
| --- | --- | --- | --- |
| 1 | `worktree-not-clean` | `git status --porcelain` 비었는지 | **실측 · 다만 규칙 위반**(§아래) |
| 2 | `trust-origin-already-established` | record 디렉터리의 `*.json` 개수 | 실측 |
| 3 | `baseline-commit-not-head` | `rev-parse --verify` 결과 대조 | 실측 |
| 4 | `baseline-snapshot-mismatch` | HEAD blob 해시 vs 현재 파일 해시 | **실측(강함)** |
| 5 | `legacy-failure-not-declarable`(harness) | reconciliation의 `HarnessErrors` | 실측 |
| 6 | `legacy-failure-not-declarable`(hard) | reconciliation의 hard failures | 실측 |
| 7 | `integration-gate-evidence-*` | 전달된 evidence 파일과 실재 대조 | **실측 · 일부 선언**(`casesRun`) |
| 8 | `high-risk-not-fail-closed` | **임시 저장소에서 3종 전이를 실제로 시도** | **오늘 상수→실측으로 바꿈** |
| 9 | `direct-writer-gate-failed` | 파일을 **정규식**으로 훑어 legacy callsite 수 | **프록시**(§아래) |
| 10 | `automatic-launcher-not-disabled` | `.claude/settings.json`에 `"hooks"` **문자열 포함** | **약한 프록시**(§아래) |
| 11 | `trust-origin-id-already-exists` | record 파일 존재 | 실측 |

## ① `IsWorktreeClean`이 `raw git status`를 쓴다 — CLAUDE.md가 금지한 방식

```
CLAUDE.md:29  | 트리 clean 판정 | `gate-clean` (raw `git status`로 판정하지 마라) |
TrustOriginCli:1035  IsWorktreeClean(root) => string.IsNullOrWhiteSpace(Git(root, "status --porcelain"))
```

**규칙을 문자 그대로 어긴다.** 다만 실측 결과 **지금은 갈리지 않는다** — 줄바꿈은
`.gitattributes`(`* text=auto eol=lf`)로 git 자신이 정규화하므로 raw status도 깨끗하다고 본다.
갈릴 수 있는 것은 `gate-clean`만 무시하는 **줄 끝 공백·BOM**이다.
*(시험 중 CRLF 재작성으로 갈림을 만들려다 `\r\r\n`을 만들어 진짜 내용 변경이 됐다 — 무효한 시험이었고, 그래서 다시 쟀다.)*

## ⑨ `DirectWriterGatePass`는 정규식 매치다

```
pattern = state-transition\s+--(?:transition-id|expected-workstate-sha256)
```

CLAUDE.md: *"정규식 매치는 증거가 아니다."* legacy callsite가 **다른 표기로 쓰이면 안 잡힌다.**
`LoadHistoricalFiles`로 예외 목록도 둔다 — 그 목록이 낡으면 조용히 통과한다.

## ⑩ `AutomaticLauncherEnabled`는 문자열 포함이다

```
File.Exists(.claude/settings.json) && text.Contains("\"hooks\"")
```

- **파일이 없으면 `false`** → 조건 통과. 실측: **이 저장소에 그 파일이 없다.**
- `"hooks": {}`(빈 객체)여도 **활성으로 본다** — 반대 방향 오탐.
- 다른 경로의 hook 설정은 **보지 않는다.**

## ★ 이 확인 중에 내가 만든 회귀를 찾았다

`HighRiskFailClosed`를 실제 검사로 바꾸면서 **fixture의 `prepare`/`apply` JSON이 stdout으로 샜다.**

```
trust-origin inspect → JSON 객체 4개 (정상 1개)
```

기계로 읽는 쪽이 깨진다. `Console.Out`을 `StringWriter`로 돌려 막았고
`inspect`·`--self-test` 둘 다 **1개**로 확인했다. **상수였을 때는 없던 부작용이다 —
정확성을 얻으면서 새 표면이 생겼고, 그것도 재야 했다.**

## 지표는 만족했으나 목적은 미달인 부분

1. **⑨⑩을 고치지 않았다.** 프록시를 실측으로 바꾸는 것은 `declare` 선행조건의 의미를 바꾸는
   일이라 **사람 결재**다. ⑩은 특히 *"파일이 없으면 통과"* 라 **부재를 안전으로 읽는다.**
2. **①의 규칙 위반도 고치지 않았다.** 지금은 갈리지 않지만, `gate-clean`으로 바꾸면
   *"줄 끝 공백만 다른 트리에서 declare가 통과"* 하게 되어 **완화 방향**이다. 어느 쪽이 옳은지는 결재다.
3. **⑦의 `casesRun`은 여전히 선언이다.** 오늘 표 하나로 모았을 뿐 실행으로 재지 않는다.
