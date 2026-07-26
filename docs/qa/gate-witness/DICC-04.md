# DICC-04 게이트 witness

- 주체(actor): HARNESS_EXECUTOR (codex)
- 날짜: 2026-07-26
- 직접 경로 사유: 지시서가 `server/Harness/DiCompletionCheckCli.cs`를 코덱스 배타 영역으로 지정하고 해당 파일을 allowlist에 명시했다.

## 참조한 스킬

- `skills/common/directive-authoring.md`
- `skills/common/directive-writing.md`
- `skills/common/executor-launch.md`
- `skills/common/hs-gate.md`
- `skills/common/powershell-encoding.md`
- `skills/common/root-cause-diagnosis.md`
- `skills/common/verification.md`
- `skills/domains/dev/file-navigation.md`
- `skills/domains/docs/README.md`

## 입력 무결성

- `docs/directives/_header.md`: `b37a27f81792e82575a793f671839fdf463895e8ce4d1d4ccf7c5bea1213b2ee`
- `docs/verification/_template.md`: `15f1b6dbdb703c94d6d7259b9417e17f438c980fad25b50b7ed96bc4da354b69`
- 두 값은 지시서의 `requiredInputs`와 일치했다.

## 수정 전 반증 시험

명령:

```text
dotnet run --project server -- di-completion-check --gate POST-COMMIT --manifestt docs/qa/fixtures/reconciliation/A/GATE-MANIFEST.json --task DICC-04-before
```

관측:

- exit: `1`
- CLI 본문 도달 여부: 도달하지 못함
- 원인: NuGet 원본 `https://api.nuget.org/v3/index.json` 접근 중 TLS 인증서가 없어 `NU1301`
- 저장된 `server/bin/**/LocalFirstWorkflowDashboard.Server.dll`: 없음
- 따라서 이 격리 복사본에서는 지시서에 기록된 수정 전 `exit 0 · checkCount 14 · 성공 판정`을 재측정하지 못했다.

## 변경 내용

- 문서화된 옵션만 인식한다.
- `--gate`, `--task`, `--launch`, `--manifest`는 다음 토큰이 없거나 `--`로 시작하면 `missing-option-value: <option>`을 stderr에 쓰고 exit 2로 종료한다.
- 그 밖의 토큰은 `unknown-option: <token>`을 stderr에 쓰고 exit 2로 종료한다.
- 두 오류 경로 모두 manifest 해석과 게이트 보고서 생성 전에 종료한다.
- `--emit-doc`의 선택적 값과 `--emit-cli-contract`의 스위치 동작은 기존 분기를 유지했다.

## 실행 검증 관측

아래 명령은 모두 코드 실행 전 복원 단계에서 같은 `NU1301`로 exit 1이었다.

| 명령 | exit | 시험 본문 |
| --- | ---: | --- |
| `dotnet build server --no-restore` | 1 | 미도달 |
| `dotnet run --project server -- context-pack-integrity` | 1 | 미도달 |
| `dotnet run --project server -- build-verify` | 1 | 미도달 |
| `dotnet run --project server -- measure dev-pack` | 1 | 미도달 |

`{"gate":"dev-pack","exit":1,"violations":"not-measured","attempt":1,"reason":"NU1301 TLS certificate failure before CLI execution"}`

§4의 8개 시험과 두 회귀 게이트도 실행 가능한 서버 산출물이 없어 실측하지 못했다. `context-pack-integrity`가 본문에 도달하지 않아 stale pin 및 `requiredInputs`/`readOrder` 소유자 조사 결과도 얻지 못했다.

## 지표는 만족했으나 목적은 미달인 부분

- 실행 환경의 패키지 복원 실패 때문에 지표 충족 여부를 측정하지 못했다.
- 코드 변경은 인자 검증에만 한정했으나, 별도 프로그램 검증 전에는 목적 달성 여부를 선언하지 않는다.
