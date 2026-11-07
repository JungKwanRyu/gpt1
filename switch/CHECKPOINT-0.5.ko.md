# WogSwitch 0.5 — Windows 의존성 처리와 게임 시작 시도

0.4에서 보고된 미지원 항목 90개를 처리했습니다. 같은 원본 패처로 다시 검사한
v1.0.86의 결과는 **미지원 항목 0개**입니다. Linux에서 실제 게임 Main을 호출하여
원본 폴더 선택 안내에 도달했고, 취소하면 정상 복귀하는 것을 확인했습니다.
이 결과는 타이틀·전투·음향·세이브 플레이 검증과 구분합니다.

## 설치와 실행

1. ZIP의 `switch/WogSwitchProbe` 폴더 전체를 SD카드의 같은 위치에 덮어씁니다.
   게임 DLL·원본 자료·세이브·`launcher.ini`는 ZIP에 들어 있지 않습니다.
   새 설치는 기본 경로를 사용합니다. 경로를 바꾸려면 `launcher.ini.example`을
   `launcher.ini`로 복사해 편집합니다.
2. 0.4와 같이 같은 PC판 버전의 원본 DLL 두 개와 PC판에 동봉된 `assets`를 준비합니다.

   ```text
   switch/WogSwitchProbe/release/WarOfGenesis.dll
   switch/WogSwitchProbe/release/WarOfGenesis.Assets.dll
   switch/WogSwitchProbe/release/assets/
   ```

   검사한 PC판은 v1.0.86입니다. `prepare_game_files.py`로 공식 패키지에서 필요한
   파일을 준비할 수도 있습니다. DLL만으로 로딩 검사까지 할 수 있고,
   시작 시도에는 PC판에 동봉된 `assets`도 필요합니다. 누락되면 Main 호출 전에 안내합니다.
3. full application / title takeover 모드로 실행합니다.
4. 패치·로딩 확인 화면에서 **X**를 누르면 게임 Main을 한 번 호출합니다.
5. 안내창에서는 **방향키로 선택, A 확인, B 취소**를 사용합니다. PLUS는 종료합니다.
6. 종료 후 아래 결과를 전달합니다.

   ```text
   switch/WogSwitchProbe/probe.log
   switch/WogSwitchProbe/load-report.txt
   switch/WogSwitchProbe/game-start.txt
   ```

`game-start.txt`는 X로 시작을 시도했을 때 작성됩니다. 화면 문제는 `render.png`도
함께 전달합니다. 게임 Main의 정적 상태 때문에 한 실행당 시작 시도는 한 번이며,
다시 시도하려면 앱을 종료하고 재실행합니다.

## 원본 자료 경로

기본값은 아래와 같습니다. 경로 설정은 0.4와 같습니다.

```ini
[game]
release=release
original=original/part2
part1=original/part1
assets=release/assets
```

파트2는 `original/part2/TXR/Txr.dat`, `Chr`, `Obs`가 있는 구조입니다.
파트1은 `original/part1/Chars`, `Obss`, `Btls`가 있는 구조입니다.
폴더 선택 요청에는 설정된 해당 경로를 넘깁니다. 폴더가 없으면 안내하고 취소합니다.
PC 드라이브 자동 검색은 설정 경로 조회로 교체했습니다.

게임이 준비하는 자료와 설정은 앱의 `save` 아래에 만듭니다. 원본 PC DLL은
읽기만 하고, 패치본은 입력·호스트 버전을 포함한 해시별 `patched-load`에 만듭니다.
기존 0.4 캐시는 유지하고 새 캐시를 사용합니다.

## 변경과 검사

- WinForms 버그 리포트 UI는 스위치 안내 메시지로 대체합니다. 해당 게임 내 창의
  작성·전송 기능은 제공하지 않습니다. `probe.log`와 `game-start.txt`로 오류를 확인합니다.
- Windows 전용 전원 관리 호출은 제거하고 DPI 호출은 표준 96 DPI 값으로 대체합니다.
- 게임의 Exit/ReportCrash는 호스트로 제어를 돌려줘 오류가 나도 기록을 남깁니다.
- 게임과 자료 DLL의 ApplicationData/LocalApplicationData 경로를 앱 저장 폴더로 연결합니다.
- 원본 패처를 다시 실행해 90→0을 확인했습니다. 옛 90개 보고서는 캐시에 별도 보존합니다.
- 두 패치 게임 DLL에 직접 P/Invoke 선언이 남지 않은 것도 검사했습니다.
- Linux에서 실제 Main의 원본 폴더 안내·취소 복귀, 보고서 UI 대체 함수 실행을 검사했습니다.
- 동일 네이티브 화면 코드로 GLES 3 / GL 3에서 전체 RGBA 출력과 안내창 종료 후
  프로그램·VAO·프레임버퍼·텍스처·뷰포트·마스크·활성 상태 복원을 검사했습니다.

## 아직 확인하지 않은 부분

스위치 실기에서의 0.3~0.5 한글·이미지·패처·Main 실행 결과를 아직 받지 않았습니다.
실제 원본 자료를 사용한 타이틀, 전투, 저장/불러오기와 성능은 미검증입니다.
**PCM 음향 출력은 아직 연결하지 않았습니다.** 게임이 출력 장치를 열면
`AUDIO NOT READY`와 요청한 형식을 기록합니다. 예외가 게임 밖으로 전달되면
시작 실패 결과를 남깁니다.
이는 이번 버전의 알려진 다음 통합 단계입니다.

`Game frames presented`는 WogShim 호스트의 버퍼 교환 성공 횟수입니다. 진행 안내
화면을 포함할 수 있으며, 타이틀이나 게임 플레이의 성공 판정은 아닙니다.
패처의 미지원 항목 0개도 .NET 9/10 전체 API 호환성을 증명하지 않습니다.
