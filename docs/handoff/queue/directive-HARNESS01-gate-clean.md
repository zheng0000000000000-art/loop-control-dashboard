# HARNESS-01 — gate-clean: 트리 clean을 정규화 내용 해시로 판정 (dotnet run -- gate-clean)

이 지시서는 `docs/directives/_header.md`의 불변 제약을 따른다. 작업 시작 전 먼저 읽어라.
유형: harness. 승격 근거: HS-CANDIDATES.md HS-01 (12/12 즉시제작). 실패 데이터: FAIL-2026-010(줄바꿈이 발사 게이트를 영구 잠금), FAIL-2026-005(실행 판정을 프록시에 의존해 오판). KNOWN-ISSUES I-8.

## 왜 만드나 — 한 줄
게이트가 물어야 할 것은 **"바이트가 다른가"가 아니라 "내용이 다른가"**다. 이 하네스는 그 질문을 코드로 고정한다.

## 배경 (실측된 사고)
조율자 발사조건①은 `git status --porcelain -- server`가 비어야 통과였다. 어떤 도구가 `server/*.cs` 5개를 LF→CRLF로 되쓰자(**내용 변경 0** — CR 제거 시 HEAD와 SHA 완전 일치) git은 이들을 영구 "수정됨"으로 인식했고, 발사조건①이 07-09부터 **며칠간 거짓**이 되어 큐가 #3에서 정지했다. 동시에 조율자는 내용 기준으로 "커밋할 것 없음"이라 정직하게 보고했다 — **커밋할 것도 없고 dirty라서 발사도 못 하는 교착**. `.gitattributes`로 위생은 고쳤지만, BOM·후행공백 등 다른 표현이 언제든 같은 교착을 만든다. 게이트 자체가 표현에 둔감해져야 한다.

## 전제 조건
server/ clean. 순차(FAIL-004).

## 목표
읽기 전용 CLI `gate-clean [경로 ...]`(기본 `server`)를 만든다. 지정 경로의 트리가 깨끗한지를 **정규화된 내용 해시**로 판정하고, **표현만 다른 파일**과 **실내용이 바뀐 파일**을 분해해 리포트한다. 게이트는 후자만 본다.

## 작업
1. 참조 스캐폴드 `docs/handoff/queue/GateCleanCli.reference.cs`를 `server/GateCleanCli.cs`로 이식하고, `server/Cli/CliRouter.cs TryRun`에 `gate-clean` 분기 등록(기존 패턴 그대로).
2. 정규화 규칙(이 순서 고정): ①UTF-8 BOM 제거 ②CRLF·CR → LF ③각 줄 후행 공백/탭 제거 ④파일 끝 개행 통일. 그 뒤 SHA-256 비교.
3. 판정:
   - 정규화 해시 같음 + 원본 바이트 같음 → `clean`
   - 정규화 해시 같음 + 원본 바이트 다름 → `representation-only` (**게이트 통과**, 위생 경고)
   - 정규화 해시 다름 → `content-dirty` (**게이트 차단**)
   - 미추적(`??`)·삭제(`D`) → `content-dirty`로 본다(정규화 비교 대상 아님).
4. JSON 출력: `{harness, paths[], contentDirtyCount, representationOnlyCount, gate:"PASS"|"FAIL", files:[{path,gitStatus,verdict,reason,...}], hygieneWarning}`.
   - `reason`에 표현차의 종류를 사람이 읽게 적는다("LF→CRLF 재작성 — 내용 동일" 등). **이 정보가 이번 교착을 며칠간 안 보이게 만든 바로 그 정보다.**
5. exit code: 0=PASS(contentDirty 0), 1=FAIL(실내용 변경 존재), 2=실행 오류.
6. `representationOnly > 0`이면 게이트는 통과시키되 `hygieneWarning`을 채운다 — 어떤 도구가 파일을 되쓰고 있다는 신호이므로 삼키지 않는다.
7. **안전**: git 읽기 전용(`status`/`show`)만. 인덱스·워킹트리·상태파일 무변경. 부작용 0.

## 검수 기준 (검증 가능 7개)
1. `dotnet run --project server -c Release -- gate-clean server`가 위 스키마 JSON 출력. 현재 트리(정상)에서 `gate:"PASS"`, exit 0.
2. **장애주입 A(표현차)**: `server/` .cs 하나를 CRLF로 되쓰면 → raw `git status`는 dirty지만 하네스는 `verdict:"representation-only"`, `gate:"PASS"`, exit 0, `hygieneWarning` 채워짐. **이것이 FAIL-010 회귀 테스트다.**
3. **장애주입 B(실내용)**: .cs에 실제 코드 한 줄 추가 → `verdict:"content-dirty"`, `gate:"FAIL"`, exit 1.
4. **장애주입 C(BOM)**: 파일에 UTF-8 BOM 추가 → `representation-only`(게이트 통과).
5. 미추적 파일 존재 시 `content-dirty`로 잡히고 게이트 FAIL.
6. 실행 전후 워킹트리·인덱스 불변(부작용 0). `git status` 출력이 실행 전후 동일.
7. `dotnet build server -c Release` 0/0, `verify-behavior` behaviorEqual:true, 코어 3파일(Engine/Storage/Guardrails) 무접촉.

## v9 산출물
WORKSTATE 갱신(diId HARNESS-01), `docs/verification/harness01-gate-clean.md`(7기준 실측 + 장애주입 A·B·C 결과), `docs/directives/HARNESS01-gate-clean.md` 보관.

## 소비자 (이 하네스를 쓸 곳 — 이번 범위 아님, 각각 후속)
- **조율자 발사규칙①**: `git status --porcelain -- server` 대신 `gate-clean server` exit code로 판정. (규칙 문서 갱신은 사람/검수자.)
- **ORCH-01 관측 스캐폴드**: `IsServerTreeClean()`을 `gate-clean` 호출로 교체(참조본에 이미 반영).
- 검수자 VERIFY-PROTOCOL, 코덱스 QA 루틴.

## 경계 / 보고
server/ + 위 문서만. dashboard/·docs/qa/·docs/wiki/ 무접촉. git commit/push 금지. `-c Release`. stdout에 수행요약·검수기준 자가점검표·장애주입 A/B/C 결과 JSON 출력. rate limit 시 마지막 줄 QUOTA_SIGNAL.
