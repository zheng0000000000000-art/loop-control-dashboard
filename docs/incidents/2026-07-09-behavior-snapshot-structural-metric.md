# behavior snapshot 구조 지표 오판

## 증상
- 자기 리팩터링 dispatch에서 사본의 구조 지표가 개선됐는데 `verify-behavior`가 `behaviorEqual=false`를 반환했다.

## 원인
- behavior snapshot이 `programCsLines`, `appJsLines`, `maxFunctionLength` 같은 구조 진단 지표까지 동작 동일성 비교에 포함했다.
- 리팩터링의 목표 지표가 좋아질수록 동작 차이로 오판되는 구조였다.

## 해소
- `verify-behavior` 비교 단계에서 구조 진단 지표를 제외했다.
- outbox strict gate는 별도로 `measure dev-pack`을 실행해 구조 지표 밴드 진입을 판정한다.

## 후속
- 새 구조 진단 지표를 추가할 때 behavior 동일성 제외 대상인지 함께 판단한다.
