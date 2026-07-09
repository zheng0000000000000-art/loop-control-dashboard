# 스킬: 저장소 보는 법

버전: 1 | 대상: 이 저장소에서 무언가를 고치기 전에 어디를 봐야 할지 모를 때

## 절차

1. **역할별 폴더를 먼저 구분한다.**
   - `server/` — C# ASP.NET Minimal API. `Program.cs`가 라우트·오케스트레이션(측정→proposal 생성→검토→라우팅)을 갖고, 나머지는 기능별로 파일이 나뉜다(`Storage.cs`, `Engine.cs`, `Guardrails.cs`, `DevPackMeasures.cs`, `OllamaReviewer.cs`, `OllamaExecutor.cs`, `Notifier.cs`).
   - `dashboard/` — 정적 프런트(`index.html`, `style.css`, `app.js`)와 `dashboard/data/`(프로젝트별 실데이터).
   - `dashboard/data/{project}/` — 프로젝트 하나의 전체 상태: `workflow-definition.json`(정책), `workflow-state.json`(현재 상태), `run-log.json`(이력), `patch-proposal.json`(현재 제안 1개), `review-report.json`(검토 이력), `blueprint.json`(측정 기준), `measurement.json`(최신 측정값), `history/`(복원 지점·루프 스냅샷).
   - `docs/verification/` — 기능별 실행 검증 기록. 새 기능을 고칠 때 관련 기능의 지난 검증 문서를 먼저 읽으면 그 기능이 실제로 어떻게 동작하는지(그리고 어떤 함정이 있었는지) 빠르게 파악된다.
   - `skills/` — 이 폴더. 작업 방법론.
2. **"정책의 진실은 definition, 이력의 진실은 run-log·review-report"임을 기억한다.** 지금 무엇이 허용되는지 궁금하면 `workflow-definition.json`을 읽는다(예: `reviewerPolicy`, `executorPolicy`, `guardrails`). 과거에 무슨 일이 있었는지 궁금하면 `run-log.json`을 읽는다. 코드에 정책을 하드코딩하지 않는 것이 이 저장소의 원칙이다.
3. **코어 3파일(`Engine.cs`, `Storage.cs`, `Guardrails.cs`)은 항상 도메인 청결을 유지한다.** 이 파일들은 단계 ID·상태값·JSON 파일 읽고 쓰기만 알고, ollama·metricId·게임 용어·ntfy 같은 것은 전혀 모른다. 새 기능을 넣을 때 "이걸 어디에 넣을까" 고민되면 십중팔구 `Program.cs`나 새 파일(`XxxReviewer.cs`, `XxxExecutor.cs`, `Notifier.cs` 같은 패턴)이 맞는 자리다.
4. **C# top-level statements(`Program.cs`)의 로컬 함수는 접근제어자가 없고, 서로 다른 파일에서 호출할 수 없다.** `Program.cs` 안에서만 서로 호출 가능하다. 여러 파일에서 공유해야 하는 로직은 `public static class`로 분리된 별도 파일에 둔다.
5. **CLI와 HTTP 라우트가 같은 로직을 타야 하면 `RunXxxCore` 같은 공유 함수를 만들고, 양쪽에서 그 함수만 호출한다.** `Measure()`(HTTP)와 `RunMeasureCli()`가 `RunMeasureCore()`를 공유하는 것이 이 패턴의 예시다.

## 지켜야 할 것

- `dashboard/data/*/history/`는 `.gitignore` 대상이다 — 커밋 전 `git status`로 끼어들지 않았는지 확인한다.
- `bin/`, `obj/`, `.claude/`도 마찬가지로 커밋 대상이 아니다.
- 새 프로젝트 데이터 폴더를 만들 때는 기존 프로젝트 폴더 구조를 그대로 복사하고 `dashboard/data/projects.json`에 등록한다 — 구조를 새로 발명하지 않는다.

## 완료 판정

- 고치려는 내용이 코어 3파일 밖에 있다.
- definition·run-log·review-report 중 어느 것이 "진실의 출처"인지 헷갈리지 않고 설명할 수 있다.
- 새로 만든 함수가 필요한 파일에서만 정확히 호출된다(중복 구현이 없다).
