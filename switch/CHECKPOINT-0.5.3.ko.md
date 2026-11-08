# WogSwitch 0.5.3 — DLL 복사 및 게임 파일 이동 호환 처리

0.5.2 실기 로그는 플랫폼 패치 적용과 재검사를 통과한 다음,
검사한 DLL을 캐시에 복사하는 File.Copy에서 실패했습니다.
오류는 IOException, HResult 0x58(88, ENOSYS)입니다.

SDK 네이티브 코드를 확인하면 파일 복사 뒤 fchmod로 권한 정보를 복사하고,
지원하지 않는 파일 권한 변경은 ENOSYS로 실패할 수 있습니다.
0.5.3은 파일 내용을 명시적인 Read/Write 반복문으로 복사합니다.
FileStream.CopyTo의 네이티브 최적화 경로도 사용하지 않습니다.

게임 DLL 두 개 내부의 File.Copy와 File.Move 호출도 같은 호환 함수로 바꿉니다.
이동 시 기존 파일을 별도 이름에 보관하고, 비어 있는 목적지로 이동합니다.
이동이 실패하면 이전 파일을 복구합니다. 원본 release 및 part1/part2 폴더에 대한
이 호환 함수의 쓰기·이동은 차단합니다. 패치 캐시는 새 버전으로 자동 분리합니다.

## 설치

1. ZIP의 switch/WogSwitchProbe 폴더 전체를 SD카드의 같은 위치에 덮어씁니다.
   **NRO와 managed 폴더를 모두 갱신**해야 합니다.
2. 기존 release, original, save, launcher.ini는 그대로 사용합니다.
   launcher.ini.example은 설정 참고용입니다.
3. 기존과 같은 full application / title takeover 방식으로 실행합니다.
4. 시작 로그와 관리 코드에 0.5.3이 표시되는지 확인합니다.
5. 게임 DLL 패치·로딩 확인 완료가 표시되면 X로 게임 시작을 시도합니다.
6. PLUS로 종료한 후 probe.log와 load-report.txt, 생성됐다면 game-start.txt로 결과를 확인합니다.

## 검증 범위

시험 환경에서 권한 변경을 ENOSYS로, 기존 파일을 덮어쓰는 이름 변경을 EEXIST로
실패하도록 주입합니다. 옛 방식의 실패를 먼저 확인하고, 새 방식의 바이트 일치,
덮어쓰기 거부, 같은 파일 복사 거부, 파일 이동과 실패 시 복구를 검사합니다.
같은 조건에서 실제 PC판 v1.0.86 DLL의 패치·캐시 재생성·재검사·로딩과,
Main의 원본 폴더 안내 후 취소 복귀를 검사합니다.
SDK9 메서드 참조와 실행기·패치한 게임 DLL의 파일 복사/이동 호출 경로도 검사합니다.
구체적인 CI 결과는 ZIP의 verification.json과 reference의 시험 기록을 참고합니다.

0.5.2 실기에서 한글·PNG·GPU 픽셀 일치·A 입력은 통과했습니다.
0.5.3 실기, 실제 게임 타이틀·전투·저장/불러오기·성능 검증은 아직 남아 있습니다.
PCM 음향 출력도 미구현입니다. 이 패키지는 완성된 게임 실행판이 아닙니다.

기본 PC 파일 위치: release/WarOfGenesis.dll, release/WarOfGenesis.Assets.dll,
release/assets 전체. 원본 자료 위치: original/part2의 TXR·Chr·Obs,
original/part1의 Chars·Obss·Btls와 나머지 원본 파일입니다.
