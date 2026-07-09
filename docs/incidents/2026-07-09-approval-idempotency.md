# 결재 이중 클릭으로 중복 report 생성

## 증상

사람이 승인 버튼을 두 번 클릭해 같은 proposal에 human 승인 report가 두 장 생성됐다.

## 원인

서버 결재 경로가 이미 `decided`인 proposal을 별도로 거부하지 않았다. 대시보드도 승인·거절 요청 중 버튼을 즉시 잠그지 않았다.

## 해소

- 서버가 `proposal.lifecycle == "decided"`이면 restore 지점 생성 전에 `409 review.already_decided`를 반환한다.
- 대시보드는 승인·거절 클릭 즉시 버튼을 비활성화하고 요청 중 상태를 표시한다.
- `review.already_decided`를 받으면 이미 결재된 건이라고 안내하고 화면을 재조회한다.

## 재발 방지

결재 완료 건에 대한 재요청은 파일을 쓰지 않고 report·run-log·history 수를 유지한다.
