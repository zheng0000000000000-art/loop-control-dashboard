# 지시서 공통 불변 제약 (_header)

적용 범위: **지시서 #11까지는 이 내용이 각 지시서에 인라인으로 실렸고, #12부터는 본 파일 참조로 대체된다.** 지시서 본문에는 "이 지시서는 docs/directives/_header.md의 불변 제약을 따른다" 1줄만 쓴다.

## 불변 제약

- 코어 3파일(Engine.cs / Storage.cs / Guardrails.cs)은 도메인 무지를 유지한다. 이번 작업의 도메인 문자열을 넣지 않는다.
- 생성≠검토. 기준 파일(blueprint.json, workflow-definition.json)은 수정하지 않는다 — 기준 변경은 사람만.
- 결재·반입·ack를 대행하지 않는다. approve/reject/approve-import/reject-import 미호출.
- 예측과 사실을 분리해 기록한다. 추정값에는 산정 방식을 명기한다.
- 주석은 한국어, 기능 설명만("왜"는 DECISIONS 몫).
- 코드 변경의 기본 경로는 dispatch/outbox다. 단 skills/·docs/ 문서 변경은 관례상 직접 경로 허용. 예외를 썼으면 보고에 사유를 남긴다.
- 작업 후 `dotnet run --project server -- measure dev-pack` 게이트를 통과 기준으로 확인한다.


## 허용 파일 (allowlist) — 모든 지시서 필수 (2026-07-11 신설)

모든 지시서는 **기계가 파싱할 수 있는 허용 파일 절**을 포함한다. 형식 고정:

```
## 허용 파일 (allowlist)
- server/Harness/**
- server/Cli/CliRouter.cs
- docs/verification/<di>.md
- docs/handoff/WORKSTATE.json
```

- glob 패턴. 한 줄에 하나. **이 목록 밖의 파일을 수정하면 산출물 전체가 반려된다.**
- **왜 산문이 아니라 목록인가**: 기존 '경계' 절은 "server/ + 위 문서만" 같은 산문이라 **기계가 검사할 수 없었다.**
  실행자가 범위를 벗어나도 아무도 자동으로 잡지 못했다 — I-1(지시서 이탈)이 반복된 구조적 이유다.
- 이 절이 `scope-check` 하네스의 입력이 된다. **판정할 데이터를 먼저 만든다** — 근거 없는 하네스는 프록시에 기대다 오보를 낸다(FAIL-2026-012).
- 산문 '경계' 절은 사람용 설명으로 유지하되, **기계 판정 근거는 이 allowlist다.**

## Context Pack — 모든 지시서 필수 (P0-05, 2026-07-12 신설)

모든 지시서는 **머리에** 기계가 파싱할 수 있는 Context Pack 블록을 포함한다. 형식 고정 — 언어 태그는 `context-pack`:

````
```context-pack
{
  "diId": "FEAT-01",
  "requiredInputs": [
    { "path": "docs/directives/_header.md", "sha256": "58e7595e..." }
  ],
  "readOrder": [
    "docs/context/RUNTIME-INDEX.md",
    "docs/directives/_header.md",
    "docs/handoff/queue/directive-FEAT01-conditional-delegation.md"
  ],
  "forbiddenActions": ["git commit", "git push", "approve", "reject", "import", "spawn-executor", "edit-baseline"]
}
```
````

### 세 필드의 뜻

- **`requiredInputs`** — 이 작업을 하려면 **읽어야 하는 참조 입력**. 경로 + **sha256**.
  - **`sha256`은 프로그램이 계산한다. LLM이 적지 않는다.** 해시를 손으로 적는 순간 그건 검증이 아니라 창작이다.
  - **allowlist(쓰기 대상)와 겹치지 않는다.** 읽기 참조 = `requiredInputs`, 쓰기 대상 = `allowlist`. 작업 중 바뀌는 파일에 해시를 걸면 게이트가 **자기 작업에 걸려 넘어진다.**
- **`readOrder`** — 읽는 순서. **L0(가장 짧은 상태) → 지시서 → 참조** 로 좁혀 읽는다. 실행자가 저장소를 헤매지 않게 한다.
- **`forbiddenActions`** — 이 작업에서 금지된 행위. 산문이 아니라 목록이라 기계가 대조할 수 있다.

### 왜 필요한가 (실측 근거)

**지시서가 삭제된 파일을 계속 가리켰다.** `docs/STATUS.md`가 "ORCH-01 참조 `.cs` 준비됨"이라고 적었지만 그 파일은 커밋 `797e7bc`가 **이미 지운 상태**였다. 발사 직전에 **사람이 손으로** 발견했다 — 하네스 6개가 돌고 있었는데 아무도 못 잡았다. 참조 입력에 **경로만 있고 해시가 없었기 때문**이다.

`context-pack-integrity` 하네스가 이 블록을 읽어 판정한다: 파일이 사라졌거나(`missing`) 해시가 어긋나면(`stale`) **exit 1**. 발사 전에 기계가 막는다.

### 하위 호환

기존 지시서(#1~#19)에는 소급 적용하지 않는다. **신규 지시서부터 필수**이며, 하네스는 블록이 **없는** 지시서를 `skipped`로 세고 실패로 치지 않는다 — 과거 때문에 게이트가 영구 잠기면 안 된다(FAIL-2026-010의 교훈).

## 완료 조건 작성 규칙 — 지표와 목적을 분리하라 (ADR-005)

1. **지표 기준**(기계 판정): measure·build·verify-behavior 등 exit code로 판정 가능한 것.
2. **목적 기준**(사람 판정): 이 작업이 **무엇을 위해** 존재하는가. 지표는 목적의 프록시일 뿐이다.

**지표만 만족시키고 목적을 우회하면 실패다.** 실행자는 verification 문서의 `## 지표는 만족했으나 목적은 미달인 부분`에 자진 신고한다 — 신고하면 감점이 아니고, 숨기면 반려다.

## 검수 기준 (모든 지시서가 상속하는 공통 항목 — 개별 지시서는 여기에 자기 항목을 더한다)

- [ ] `build-verify` 하네스 **exit 0** (문자열 정규식으로 빌드 성패를 판정하지 않는다 — I-11)
- [ ] `verify-behavior` → `behaviorEqual: true` (동작 보존)
- [ ] `measure dev-pack` **비악화** (제출 전 반드시 실행. 하네스가 게이트를 지키는데 하네스 자신이 게이트를 깨는 일이 있었다 — 2026-07-11 P0-03)
- [ ] 변경 파일이 `## 허용 파일 (allowlist)` 안에 있다 (`scope-check`)
- [ ] verification 문서에 **①주체(actor) ②사용한 하네스와 결과(명령·exit code·수치) ③참조한 스킬** 기록
- [ ] verification 문서에 **`## 지표는 만족했으나 목적은 미달인 부분`** 자진 신고 (없으면 "없음" — ADR-005)
