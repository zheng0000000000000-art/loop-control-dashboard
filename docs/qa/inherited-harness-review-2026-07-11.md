# H-5 기존 하네스 인수 검토

## 주체(actor)

- actor: codex
- 회차: 2026-07-11 21:45 KST
- 대상: `gate-clean`, `hs-scan`, `claim-check`, `doc-integrity`
- 금지 준수: 코드 미수정, git commit/push 미실행, 결재·반입 액션 미호출

## 참조한 스킬

- `skills/common/root-cause-diagnosis.md`
- `skills/common/hs-gate.md`
- `docs/handoff/CODEX-AUTO-15min-routine.md`
- `docs/handoff/CODEX-QUEUE.md`

## 데이터 존재 확인

| 하네스 | 실체 데이터 | 관문 |
| --- | --- | --- |
| `gate-clean` | git HEAD blob과 working tree bytes, normalized hash | PASS |
| `hs-scan` | `docs/wiki/failures/index.md`, `docs/handoff/HS-CANDIDATES.md` 메타 | PASS |
| `claim-check` | verification 문서, git/code reality, commit object | PASS |
| `doc-integrity` | JSON parse result, markdown fence/last-line checks | PASS |

## 사용한 하네스와 결과

| 명령 | exit code | 수치/결과 |
| --- | ---: | --- |
| `dotnet run --project server -c Release -- hs-scan` | 1 | `failureCaseCount=14`, candidate=`executor-orchestration(6)` |
| `dotnet run --project server -c Release --no-build -- gate-clean server/Harness` | 0 | `contentDirtyCount=0`, `representationOnlyCount=0` |
| `dotnet run --project server -c Release --no-build -- hs-scan` | 1 | 현재 S4 `executor-orchestration(6)`으로 트리거. 설계상 FAIL 아님, HS-GATE 의무 신호 |
| `dotnet run --project server -c Release --no-build -- claim-check ACTOR-01` | 0 | `claimCount=12`, `mismatchCount=0`, `verdict=MATCH` |
| `dotnet run --project server -c Release --no-build -- doc-integrity` | 0 | `checked=12`, `brokenCount=0`, `verdict=INTACT` |
| `dotnet run --project server -c Release --no-build -- verify-behavior` | 0 | `behaviorEqual=true` |
| `dotnet run --project server -c Release --no-build -- measure dev-pack` | 1 | `violationCount=1`, 기존 위반 잔존 |
| `dotnet run --project server -c Release --no-build -- hs-scan` | 1 | 반복 후보 유지 |

## 오탐·프록시 검토

- `gate-clean`: PASS/FAIL 원천은 normalized content hash다. raw `git status`는 후보 파일 목록으로만 사용하므로 FAIL-2026-010식 표현 차이 프록시를 줄인다.
- `hs-scan`: exit 1은 품질 실패가 아니라 HS-GATE 실행 의무 신호다. 현재 `executor-orchestration` component가 넓어 계속 트리거되는 점은 잔여 설계 위험이다.
- `claim-check`: H-6 이후 `.csproj`/`.csv` 경계 오탐은 해소됐다. 여전히 PascalCase 심볼 추출은 문서 형식에 민감하므로 claim 수를 판정 근거로 쓰지 않고 `mismatchCount`를 본다.
- `doc-integrity`: 끝 개행 없음만으로 실패시키지 않도록 되어 있어 기존 오탐을 피한다. 코드펜스/JSON parse/마지막 줄 중간 끊김은 실체 데이터다.

## 판정

- H-5 완료: PASS
- 재현/의심/오탐: 재현 0건, 의심 1건(`hs-scan` broad component 반복 트리거), 오탐 0건
- 후속 제안: `executor-orchestration` component를 더 좁은 failureClass로 나누거나, `judgedComponents` 메타를 추가해 같은 S4 후보가 무한 반복되지 않게 한다.
