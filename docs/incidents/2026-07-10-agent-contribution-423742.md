# dashboard/data/dev-pack/blueprint.json의 note 필드 한글 깨짐(programCsLines·appJsLines·maxFunctionLength)…

## 제출

기여 ID: contribution-20260709193423742
제출 시각: 2026-07-10T04:34:23.7447625+09:00

## 내용

dashboard/data/dev-pack/blueprint.json의 programCsLines·appJsLines·maxFunctionLength 세 항목 note 필드가 한글 대신 물음표(?)로 깨져 있다. 예: programCsLines note = "Program.cs ? ?. ?? ?? ?? 2419?, ??? +10%", maxFunctionLength note = "??? ??? ??? ?? ?? ??. ???? ?? ???". AGENT-GUIDE.md 자기사용 검증(GET /api/projects/dev-pack/context) 중 발견했다.

## 근거

node -e를 통해 blueprint.json을 utf8로 읽어 programCsLines/appJsLines/maxFunctionLength의 note 필드를 출력하면 UTF-8 대체 문자가 아닌 리터럴 물음표 문자가 다수 포함되어 있다(다른 12개 항목의 note는 정상 한글). blueprint.json은 이번 작업에서 수정하지 않았다 — 기준 파일 직접 수정 금지 원칙에 따라 사실만 기록한다.