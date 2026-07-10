# 실패 사례 위키

이 위키는 완결된 실패를 재사용 가능한 설계 지식으로 남긴다. 사례는 사실과 가설을 분리하고, 재발 방지와 남은 위험까지 기록한다.

## 분류

- `failureClass`: 실패의 성격이다. 한 사례에 둘 이상을 붙일 수 있다.
- `component`: 실패가 관찰되거나 수정된 구성요소다. 예: `outbox-import`, `remote-token`, `dashboard-approval`.
- `status`: `해결됨`, `완화됨`, `알려진 실패`, `관찰 중` 중 하나를 쓴다.
- `severity`: `low`, `medium`, `high`, `critical` 중 하나를 쓴다.

## 검색

- 전체 목록은 [index.md](index.md)를 본다.
- 구성요소별 목록은 [by-component/](by-component/)를 본다.
- 실패 클래스별 목록은 [by-failure-class/](by-failure-class/)를 본다.
- 원문 사례는 [cases/](cases/) 아래에 둔다.

## ID 규칙

- 형식: `FAIL-YYYY-NNN`.
- `YYYY`는 최초 발생 또는 등록 기준 연도다.
- `NNN`은 같은 연도 안에서 001부터 증가한다.
- 파일명은 `FAIL-YYYY-NNN-short-slug.md` 형식을 쓴다.
- ID는 재사용하지 않는다. 해결된 실패도 삭제하지 않고 상태만 바꾼다.

## 작성 규칙

- [템플릿](_template.md)의 머리 필드와 필수 10항목을 모두 채운다.
- 사실로 확인된 내용과 가설을 구분한다. 미확정 원인은 `가설` 또는 `미확정`이라고 표시한다.
- 사람 결재나 반입으로 막힌 사건은 "막힘" 자체도 영향으로 기록한다.
- 해결책은 선택한 해결책뿐 아니라 검토한 대안을 남긴다.
- 사례를 추가하면 전체 색인, 구성요소 색인, failureClass 색인에도 링크를 추가한다.

