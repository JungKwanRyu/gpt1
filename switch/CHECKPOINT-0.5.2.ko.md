# WogSwitch 0.5.2 — SD카드 패치 파일 교체 수정

사용자가 제공한 0.5.1 로그에서 원인을 확인했습니다.

```text
Stage: apply Switch platform patches
IOException / 0x11 / IO_FileExists_Name
PlatformPatches.WriteAndReplace -> File.Move(..., overwrite: true)
```

스위치의 파일 이름 변경 동작은 대상 파일이 이미 존재하면 실패합니다.
패치 과정에서 만들어 둔 DLL을 임시 파일로 교체할 때 이 오류가 발생했습니다.
0.5.2는 DLL 직렬화를 메모리에서 완료한 뒤 앱의 패치 캐시 파일을 직접 다시 씁니다.
사용자가 넣은 release의 PC DLL과 원본 자료는 읽기만 합니다.

패치·재검사·해시 기록이 모두 끝나야 완료 캐시로 취급하므로 중간 실패 파일은
재사용하지 않습니다. 캐시 버전을 올려 이전 실패 캐시와도 분리했습니다.
기존 캐시를 수동으로 삭제할 필요는 없습니다.

## 설치와 재실행

1. ZIP의 `switch/WogSwitchProbe` 폴더 전체를 SD카드의 같은 위치에 덮어씁니다.
   **managed 폴더도 함께 복사**해야 합니다.
2. 기존 `release`, `original`, `save`, `launcher.ini`를 그대로 사용합니다.
   PC 게임 DLL이나 원본 자료를 다시 옮길 필요는 없습니다.
3. 기존과 같은 full application / title takeover 방식으로 실행합니다.
4. `게임 DLL 패치·로딩 확인 완료`가 표시되면 X로 게임 시작을 시도합니다.
5. PLUS로 종료하고 `probe.log`, 생성됐다면 `load-report.txt`, `game-start.txt`를 전달합니다.

로그 첫 줄과 관리 코드 표시는 0.5.2여야 합니다.

## 이번 실기 로그에서 확인된 성공

0.5.1에서 두 PC DLL의 읽기·해시 검사, 원본 패처 실행, 파트1·파트2 폴더 구조와
PC assets 폴더 인식이 통과했습니다. 한글 글리프와 래스터, PNG 인코딩·디코딩·저장,
GPU 업로드 및 CPU/GPU 픽셀 일치, A 입력, 그래픽 루프 종료도 실기에서 통과했습니다.
이번 실패 지점은 게임 DLL 교체이며, 게임 Main 호출에는 아직 도달하지 않았습니다.

## 수정판 검사와 남은 범위

- CI에서 기존 파일 위의 `.switch-temp` 이름 변경을 EEXIST로 거부하도록 동작을 재현했습니다.
- 옛 File.Move 방식이 실패하는 것을 먼저 확인하고, 같은 조건에서 새 방식의 실제
  PC판 v1.0.86 패치·캐시·로딩·호환성 재검사·Main 안내 및 취소 복귀가 통과했습니다.
- 고정된 Switch SDK9의 메서드 참조 검사도 실행기·WogShim·패처·게임 DLL 두 개 모두 통과했습니다.
- 0.5.1의 Contains/Reverse 호환 처리와 안전한 예외 기록은 유지합니다.
- 재현 시험은 Linux의 동작 주입 시험이며 0.5.2 자체의 실기 결과는 아직 받지 않았습니다.
- PCM 음향 출력, 실제 원본 자료를 사용한 타이틀·전투·저장/불러오기·성능 검증은 남아 있습니다.

기본 위치는 release에 PC DLL 두 개와 PC판 assets, original/part2에 TXR·Chr·Obs,
original/part1에 Chars·Obss·Btls가 있는 구성입니다. 상세 기본 설정은 launcher.ini.example을 참고합니다.
