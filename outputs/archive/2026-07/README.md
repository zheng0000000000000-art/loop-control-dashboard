# archive/2026-07 — 아카이브 (2026-07-15, 검수자)

> **아무것도 삭제하지 않았다. 이동만 했다.** `git log --follow <파일>`로 이력이 그대로 따라온다.

## 왜 옮겼나

`docs/handoff/queue/`에 지시서 **44건**, `outputs/` 루트에 파일 **111건**이 쌓여 있었다.
**완료된 것과 진행 중인 것이 섞여 있으면 다음 세션이 무엇이 살아 있는지 알 수 없다.** 그래서 갈랐다.

## 무엇이 어디로

| 대상 | 어디로 | 판정 근거 |
| --- | --- | --- |
| **완료·폐기 지시서 36건** | `docs/archive/2026-07/directives/` | `docs/verification/`에 대응 검증문서가 있으면 완료. `CODEX-GATE-02`는 폐기(05H와 중복) |
| **실행자 로그 79건**(`sonnet-*.out/err.*`) | `outputs/archive/2026-07/executor-logs/` | **증거다(ADR-010 transport receipt 계열). 삭제하지 않는다.** |
| **스크래치/실험 18건** | `outputs/archive/2026-07/scratch/` | `measure_*`·`direct_test_*`·`ollama_*`·`anti-test-tmp/`·`test-temp/` 등 |
| **완료 DI의 발사 프롬프트** | `outputs/archive/2026-07/prompts/` | 대응 DI가 끝난 것 |

## 무엇을 남겼나 (살아 있는 것)

- **`docs/handoff/queue/` — 미완 8건**: `CODEX-GATE-04` · `GATE-CP-01` · `DISPATCH-01` · `FIX03` · `HARNESS01~04`
- **`outputs/launch/`** — `run-executor.ps1`(발사기) + 대기 중 프롬프트 + `*.exit.json`/`*.transport.json`(**증거**)
- **`outputs/gates/`·`outputs/review/`·`outputs/state-transition/`·`outputs/quarantine/`·`outputs/recovery/`** — 전부 **증거**. 손대지 않았다
- **`outputs/reviewer-log.md`·`outputs/review-log.md`** — append-only 기록(정본)

## 정리 후 게이트 (실측)

```
gate-clean 0 · handoff-integrity 0 · context-pack-integrity 0 · doc-integrity 0 · hs-scan 1(기대 1)
build-verify 0 · measure dev-pack 0
```

**파일을 옮긴 뒤에도 게이트가 전부 기대 exit와 일치한다.** 옮기기 전에도 그랬다 — 즉 이동이 아무것도 깨지 않았다.
