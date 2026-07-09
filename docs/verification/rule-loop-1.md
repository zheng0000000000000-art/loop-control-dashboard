# 규칙 루프 1차 실행 검증

검증일: 2026-07-09

## 결과 요약

| 단계 | 명령 | 응답 요약 | 판정 |
| --- | --- | --- | --- |
| 서버 기동 | `dotnet run --project server --no-build` | 백그라운드 PID `1624`로 기동했다. `GET /api/projects/dev-pack/state`가 `200`을 반환했다. | O |
| 1차 측정 | `Invoke-RestMethod -Method Post -Uri http://localhost:5183/api/projects/dev-pack/actions/measure -Body '{}'` | `dashboard/data/dev-pack/measurement.json`이 생성됐다. metric 수는 `7`, null 값은 `0`이다. `loopState=aligned`, `deviationCheck=passed`가 됐다. | O |
| 수동 대조 1 | `[regex]::Matches(ko.json, '(?<!필)요\\.|(?<!필)요\"|습니다').Count` | 직접 측정값 `0`, measurement의 `koPoliteEndings=0`과 일치했다. | O |
| 수동 대조 2 | `docs/directives/06-rule-loop.md`의 `검수 기준` 섹션 목록 항목 직접 집계 | 직접 측정값 `7`, measurement의 `directiveAcceptanceCriteria=7`과 일치했다. | O |
| 수동 대조 3 | `dashboard/data` 하위 JSON 중 schemaVersion 누락 파일 직접 집계 | 직접 측정값 `0`, measurement의 `schemaVersionMissing=0`과 일치했다. | O |
| 위반 주입 | `ko.json`에 `"verificationProbe": "검증용 임시 문장이에요."`를 임시 추가 후 `POST actions/measure` | `koPoliteEndings=1`, evidence는 `dashboard/data/lang/ko.json:178`이었다. `deviationCheck=warning`, `changeReview=pending_review`, proposal `proposal-1783562805916`이 `submitted`로 생성됐다. | O |
| 해소 확인 | 임시 문장 원복 후 `POST actions/measure` | `koPoliteEndings=0`으로 복귀했다. 남은 위반 수는 `0`이다. `loopState=aligned`, `overallStatus=completed`, `deviationCheck=passed`가 됐다. | O |
| 불변 확인 | `rg -n '/measurement|actions/measure' server\\Program.cs` 및 라우트 목록 확인 | measurement 직접 수정 라우트는 없다. 존재하는 라우트는 `GET /measurement`와 `POST /actions/measure`뿐이다. | O |

## 1차 측정 값

| metricId | value | evidence |
| --- | ---: | --- |
| domainWordsInEngine | 0 |  |
| functionsWithoutComment | 0 |  |
| directiveAcceptanceCriteria | 7 | `docs/directives/06-rule-loop.md` |
| koPoliteEndings | 0 |  |
| verdictInProposalFile | 0 |  |
| devRoleInRuntimeLogs | 0 |  |
| schemaVersionMissing | 0 |  |

## 위반 주입 전후

| 시점 | koPoliteEndings | evidence | deviationCheck | changeReview | proposal |
| --- | ---: | --- | --- | --- | --- |
| 1차 측정 | 0 |  | passed | not_started | 없음 |
| 임시 위반 주입 후 | 1 | `dashboard/data/lang/ko.json:178` | warning | pending_review | `proposal-1783562805916`, submitted |
| 원복 후 재측정 | 0 |  | passed | not_started | `proposal-1783562805916`, superseded |

## 라우트 확인

확인된 measurement 관련 라우트:

```text
app.MapGet("/api/projects/{projectId}/measurement", ...)
app.MapPost("/api/projects/{projectId}/actions/measure", ...)
```

`POST`, `PUT`, `PATCH`, `DELETE /api/projects/{projectId}/measurement` 형태의 수정 엔드포인트는 없다.

## 사람 결재 대기

최종 해소 측정에서 남은 위반이 `0`이므로 사람 결재 대기 proposal은 없다. 검증 중 생성된 `proposal-1783562805916`은 임시 위반을 대상으로 생성됐고, 원복 후 재측정에서 `superseded`로 정리됐다. 승인 액션은 실행하지 않았다.
