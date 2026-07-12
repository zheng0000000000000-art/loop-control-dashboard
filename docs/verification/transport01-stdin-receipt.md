# TRANSPORT-01 작업 보고 — stdin 전달 + transport receipt 생산

## 주체 (actor)

실행자: Claude Sonnet 4.6 (TRANSPORT-01 세션, 2026-07-12)

## 변경 파일 (allowlist 범위 내)

- `outputs/launch/run-executor.ps1` — **전면 재작성** (stdin 전달 + replay + evidence 생산)
- `outputs/launch/TRANSPORT-PROBE.prompt.txt` — 실체 증명용 프로브 (한글·emoji·따옴표·역슬래시 포함)
- `outputs/launch/TRANSPORT-PROBE.transport.json` — 증명 산출물
- `outputs/launch/TRANSPORT-PROBE.exit.json` — 증명 sentinel
- `outputs/launch/usage-ledger.jsonl` — 토큰 원장 append

## 사용한 하네스와 exit code

| 하네스 | 명령 | exit code |
|--------|------|-----------|
| `measure dev-pack` | `dotnet run --project server -c Release -- measure dev-pack` | 0 (`violationCount: 0`) |
| `launch-check` | `dotnet run --project server -c Release -- launch-check TRANSPORT-PROBE outputs/launch/TRANSPORT-PROBE.transport.json` | **0** (TRANSPORT_VALID) |
| `handoff-integrity` | `dotnet run --project server -c Release -- handoff-integrity` | 0 (PASS) |
| `gate-clean server` | `dotnet run --project server -c Release -- gate-clean server` | 1 (pre-existing: LaunchCheckCli.cs는 코덱스 세션이 이미 수정, 내 범위 밖) |

### measure dev-pack 게이트 JSON

```json
{"gate":"dev-pack","violations":0,"attempt":1}
```

## 참조한 스킬

없음 (직접 구현).

## 실체 증명

### 1. payloadSha256 == replaySha256 (TRANSPORT_VALID)

프롬프트: `transport receipt 검증용 프로브. 한글·"따옴표"·\역슬래시\·🔍emoji 포함.`

```
sourceSha256   = 683dfc836f09fd95b5998f0e3c8052e9976965569973ae825e681b4c9622d03e
payloadSha256  = 683dfc836f09fd95b5998f0e3c8052e9976965569973ae825e681b4c9622d03e
replaySha256   = 683dfc836f09fd95b5998f0e3c8052e9976965569973ae825e681b4c9622d03e
replayEventCount = 1
verdict: TRANSPORT_VALID
```

### 2. launch-check exit 0

```
dotnet run --project server -c Release -- launch-check TRANSPORT-PROBE outputs/launch/TRANSPORT-PROBE.transport.json
→ exit 0, failureCount: 0, verdict: TRANSPORT_VALID
```

### 3. 명령행에 프롬프트 본문 없음

`Get-CimInstance Win32_Process` 실행 중 캡처 결과:

```
PID: 19004
CommandLine: "claude.exe" -p --verbose --input-format stream-json --output-format stream-json --replay-user-messages --dangerously-skip-permissions
PASS: 명령행에 본문 없음
```

sentinel에 `argLength` 필드 없음 (구버전 `-p "..."` 방식 폐기).

### 4. 손상 인코딩 fixture — mismatch로 잡힘

PowerShell 기본 StreamWriter(UTF-16 변환)로 동일 텍스트를 보내면:

```
payload sha256 (UTF-8):  4c32cda67377ab2cf9ebe38ce8e0d5b57e2a43550cf7007ee9b82504adc2ecaf
replay  sha256 (손상):   4364274a4eb60bc8af0b047ae0b54e91c007fb1d70cc15df0db9906c5c86aeb7
MISMATCH → TRANSPORT_INVALID  ✓
```

모델도 "메시지가 깨져서 도달했습니다"라고 응답했다 — ADR-010 §6와 일치.

## 설계 결정 사항

### stdin 읽기 순서
ReadToEndAsync를 stdin 쓰기 이전에 시작한다. 이유: stdout pipe 버퍼가 차면 프로세스가 블록되어 교착 발생. stdin을 먼저 닫고 읽기를 시작하면 대용량 출력 시 실패한다.

### replayEventCount 계산 방식
`--replay-user-messages`는 tool_result user 이벤트도 재생한다. 도구를 사용하는 세션은 user 이벤트가 여러 개다. `message.content`가 문자열인 이벤트만 카운트해 초기 프롬프트 replay만 센다. tool_result는 배열 content라 자동으로 제외된다.

### PowerShell 5.1 UTF-8 강제
`StandardInputEncoding` 프로퍼티가 없으므로 `BaseStream.Write(bytes)` 로 직접 쓴다. 이렇게 하지 않으면 한글이 깨진다(ADR-010 §6, 실측 확인).

## 기존 기능 보존 현황

| 기능 | 보존 여부 |
|------|-----------|
| sentinel (`.exit.json`) 생성 | ✓ |
| usage ledger append | ✓ |
| FILE-CLAIMS claim 등록/해제 | ✓ |
| 사람이 읽는 보고문 (`.out.log`) | ✓ |
| 지시서 allowlist 파싱 | ✓ |

## 지표는 만족했으나 목적은 미달인 부분

### [신고] TRANSPORT-PROBE2 tool-use 세션의 증명 미완

TRANSPORT-PROBE2 프로브(도구를 사용한 세션)에서 최초 실행 시 concurrent run 오염으로 evidence가 정상 생산되지 않았다. 수정 후 재실행했으나 백그라운드 잡과 동시 실행 충돌로 evidence가 오염됐다. 단순 프롬프트(TRANSPORT-PROBE)로 tool-use 없는 경우는 완전 증명됐다.

**영향**: tool_result 필터 로직은 코드로 확인했고 content 타입 검사로 올바르게 동작하지만, 실제 tool-use 세션 end-to-end 증명은 이 보고서에 포함되지 않는다.

**다음 사람의 후속 검증 권장**: 도구를 사용하는 실제 태스크 실행 후 launch-check exit 0 확인.

### [신고] gate-clean server 1 (pre-existing)

`server/Harness/LaunchCheckCli.cs`는 코덱스 세션에서 이미 수정됐고, 내 allowlist에 `server/**` 무접촉이라 건드리지 않았다. gate-clean server 실패는 내 작업 이전부터 존재했다.
