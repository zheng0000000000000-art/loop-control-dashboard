# FAIL-2026-006 — Storage 프로젝트 경로가 sibling prefix 디렉터리를 통과시킴

- 상태: 해결됨
- 최초 발생일: 2026-07-10
- 최근 발생일: 2026-07-10
- 관련 DI: S-01 경로 escape 재현 라운드
- 구성요소: storage-project-path
- failureClass: path_escape, design_learning
- 심각도: medium

## 발생 상황

`Storage.ProjectPath`는 `projects.json`의 `path`를 읽어 `DataRoot` 아래 프로젝트 경로를 계산한다. 경로 정규화 뒤 `fullPath.StartsWith(DataRoot)`만 검사하고, 디렉터리 separator 경계는 확인하지 않는다.

## 관찰된 증상과 영향

`./data/../data-escape` 같은 설정값은 정규화 후 `DataRoot`의 자식이 아닌 sibling prefix 디렉터리로 해석될 수 있다. 해당 full path는 `DataRoot` 문자열로 시작하기 때문에 현재 검사에서는 통과한다. `projects.json`은 신뢰 설정 파일이지만, 경로 검증 함수의 보안 경계로는 불완전하다.

## 발생 이유(직접·근본·기여, 미확정은 가설 표시)

- 직접 원인: `Path.GetFullPath` 후 `StartsWith(DataRoot)`만 검사한다.
- 근본 원인: 경로 root 포함 여부를 문자열 접두 비교로 판단하고, `fullPath == root` 또는 `root + separator` 경계를 확인하지 않았다.
- 기여 요인: `./data/` prefix를 제거한 뒤 남은 `../...` segment를 별도 차단하지 않는다.
- 미확정: 현재 운영에서 신뢰되지 않은 사용자가 `projects.json`을 수정할 수 있는 경로는 확인하지 않았다.

## 검토한 해결 방법

- 현재처럼 `StartsWith(DataRoot)`만 유지한다.
- `Path.GetRelativePath(DataRoot, fullPath)`가 `..`로 시작하거나 rooted path인지 검사한다.
- `fullPath == DataRoot` 또는 `fullPath.StartsWith(DataRoot + Path.DirectorySeparatorChar)` 조건으로 separator-bounded 검사를 적용한다.

## 선택한 해결 방법

이번 QA 작업에서는 코드를 수정하지 않았다. 구현 실행자에게 separator-bounded root 검사 또는 `Path.GetRelativePath` 기반 검사를 적용하도록 이관한다.

## 판단 기준

정규화된 경로가 root 문자열로 시작하는 것만으로는 root 내부 경로라고 판단하지 않는다. 디렉터리 경계까지 확인해야 한다.

## 검증 결과

논리 PoC에서 `DataRoot=C:\repo\dashboard\data`, configured path `./data/../data-escape`를 적용하면 full path가 `C:\repo\dashboard\data-escape`가 된다. `StartsWith(DataRoot)`는 true지만 separator-bounded 검사는 false다.

## 재발 방지

- 모든 root 경로 검증에서 separator 경계 또는 `Path.GetRelativePath` 기반 검사를 사용한다.
- sibling-prefix 케이스(`data-escape`, `outbox-escape`, `workspace-escape`)를 QA 체크리스트에 포함한다.

## 후속 작업과 잔여 위험

- 코드 수정은 구현 실행자에게 이관한다.
- `projects.json`은 현재 신뢰 설정 파일이므로 외부 공격 표면은 제한적이다.
- 같은 패턴이 다른 경로 검증 함수에도 있는지 추가 점검이 필요하다.

## 발생 이력

- 2026-07-10: 버그헌트 라운드 1에서 S-01 의심 등록.
- 2026-07-10: 후속 재현 라운드에서 sibling-prefix escape 논리 PoC 확인.
- 2026-07-10: `FAIL-2026-006`으로 실패 위키 등록.
