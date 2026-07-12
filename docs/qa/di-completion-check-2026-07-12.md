# di-completion-check QA — 2026-07-12

## 주체

- 작성/실행: Codex
- 직접 경로 사유: 지시서가 쓰기 영역을 `server/Harness/`, `docs/handoff/GATE-MANIFEST.json`, `docs/handoff/HARNESSES.md`, `docs/qa/`, `docs/handoff/sessions/`로 명시했다.
- 결재/반입/발사: 수행하지 않음.

## 참조한 스킬

- `skills/common/directive-writing.md`
- `skills/common/executor-launch.md`
- `skills/common/hs-gate.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/verification.md`
- `skills/domains/dev/file-navigation.md`
- `skills/domains/docs/README.md`

## 구현 확인

- `docs/handoff/GATE-MANIFEST.json`: `POST-EXECUTOR`, `POST-COMMIT` 두 게이트로 분리했다.
- `server/Harness/DiCompletionCheckCli.cs`: manifest를 읽어 order 순서로 CLI를 실행하고 `expectedExit`와 실제 exit code를 비교한다.
- `server/Harness/HarnessRegistry.cs`: `di-completion-check`를 등록하고 등록 이름 목록을 노출한다.
- `docs/handoff/HARNESSES.md`: `di-completion-check --emit-doc`으로 manifest와 registry에서 생성했다.
- 증거 JSON 최상위에 `gateVerdict`를 둔다.

## 필수 반증 시험

| 시험 | 명령 요약 | exit | 증거 |
| --- | --- | --- | --- |
| `hs-scan` 기대값을 0으로 바꾼 사본 | `di-completion-check --gate POST-COMMIT --manifest <bad-hs> --task final-bad-hs` | 1 | `outputs/gates/final-bad-hs.gate.json` |
| 없는 command 사본 | `di-completion-check --gate POST-COMMIT --manifest <unknown> --task final-unknown` | 1 | `outputs/gates/final-unknown.gate.json` |
| 없는 manifest 경로 | `di-completion-check --gate POST-COMMIT --manifest <missing> --task final-missing` | 1 | `outputs/gates/final-missing.gate.json` |
| 정상 manifest, POST-COMMIT | `di-completion-check --gate POST-COMMIT --task final-post-commit` | 1 | `outputs/gates/final-post-commit.gate.json` |
| 정상 manifest, POST-EXECUTOR | `di-completion-check --gate POST-EXECUTOR --task final-post-executor` | 0 | `outputs/gates/final-post-executor.gate.json` |

`POST-COMMIT` 실패는 현재 커밋 전 워크트리라 `gate-clean server`가 expected 0 / actual 1로 잡힌 결과다. `POST-EXECUTOR`에서는 같은 `gate-clean server`가 expected 1 / actual 1로 통과했다.

## 경고 확인

- `final-post-executor.gate.json`에서 `measure`는 `mutates-state` 경고로 표시된다.
- manifest 밖 등록 하네스는 9개로 표시된다: `call-integrity-check`, `claim-check`, `di-completion-check`, `e2e-usage`, `launch-check`, `path-guard-check`, `project-api-edge-check`, `scope-check`, `template-sync-check`.
- `gateVerdict`는 `final-post-executor`에서 `PASS`, 실패 케이스들에서 `FAIL`이다.

## 품질 게이트

{"gate":"dev-pack","violations":0,"attempt":1}

실행 명령:

- `dotnet run --project server -- build-verify` → exit 0
- `dotnet run --project server -- di-completion-check --emit-doc docs/handoff/HARNESSES.md` → exit 0
- `dotnet run --project server -- measure dev-pack` → exit 0, `violationCount: 0`

## 지표는 만족했으나 목적은 미달인 부분

- `POST-COMMIT` 정상 manifest는 현재 작업 중 dirty 상태에서 실패한다. 커밋 직후 조율자 시점에서 다시 실행해야 이 게이트의 진짜 통과 여부가 확인된다.
- `measure`는 여전히 run-log/proposal 상태를 바꾸는 검사다. 이번 작업은 이를 고치지 않고 manifest와 리포트 경고로 드러내는 데 그쳤다.
- `--gate` 기본값은 새 manifest에 실제로 존재하는 `POST-EXECUTOR`로 두었다. 지시서의 오래된 문장인 기본 `DI-COMPLETION`은 두 게이트 manifest와 충돌해 적용하지 않았다.
