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
