# WogSwitch 0.4 — PC 게임 DLL 패치·로딩

이번 버전은 실제 PC판 DLL 두 개를 스위치에서 패치하고, 패치본 로딩과
`DuelDx.Program.Main` 진입점 조회를 확인합니다. Main은 호출하지 않습니다.
게임 플레이·음향·세이브 실행은 아직 검증하지 않았습니다.
0.3의 이미지·한글·PNG·A 버튼 확인도 포함합니다. 0.3 실기 결과는 아직 받지 않았습니다.

## 설치와 게임 파일 준비

1. ZIP의 `switch/WogSwitchProbe` 폴더 전체를 SD카드의 같은 위치에 덮어씁니다.
2. PC판의 **같은 버전**에서 가져온 파일을 아래 위치에 복사합니다.

   ```text
   switch/WogSwitchProbe/release/WarOfGenesis.dll
   switch/WogSwitchProbe/release/WarOfGenesis.Assets.dll
   switch/WogSwitchProbe/release/assets/       ← PC판에 동봉된 assets가 있으면 함께 복사
   ```

   iOS/안드로이드에서 이미 패치한 DLL 대신 원래 PC판 DLL을 사용합니다.
   이번 DLL 로딩 확인에는 창세기전 원본 게임 자료 폴더가 없어도 됩니다.
3. 0.2와 같은 full application / title takeover 방식으로 실행합니다.
4. 한글 화면에 `게임 DLL 패치·로딩 확인 완료`와 PC판 버전이 표시되는지 확인합니다.
   파일이 없으면 복사할 두 파일명을 보여줍니다. A를 누르면 배너가 빨간색으로 바뀝니다.
5. PLUS로 종료하고 아래 파일을 전달합니다.

   ```text
   switch/WogSwitchProbe/probe.log
   switch/WogSwitchProbe/load-report.txt
   switch/WogSwitchProbe/render.png
   ```

실행할 때 이 결과 파일들은 새 내용으로 바뀝니다. 원래 PC DLL과 원본 자료는 읽기만 합니다.
패치본은 `patched-load/<입력 해시>/`에 만들며, 게임 저장 폴더는 생성하지 않습니다.

## PC판 DLL이 보이지 않는 경우

현재 검사한 PC판은 **v1.0.86**입니다. 설치판의 앱 폴더에서 두 DLL을 가져오거나,
[공식 v1.0.86 전체 패키지](https://github.com/Kyeongrok/the-war-of-genesis-clicker/releases/download/v1.0.86/WarOfGenesis-1.0.86-full.nupkg)를
ZIP으로 열어 `lib/app/` 안의 두 DLL과 `assets`를 위 위치로 복사합니다.
압축 앱에서 `.nupkg`를 열지 못하면 복사본의 확장자를 `.zip`으로 바꿔 엽니다.

Python 3.11 이상을 사용하는 PC에서는 ZIP에 포함된 `prepare_game_files.py`를 실행하면
공식 패키지를 받아 SHA-256을 확인하고 필요한 파일만 준비합니다.

```sh
python prepare_game_files.py
```

이미 받은 패키지를 사용할 수도 있습니다.

```sh
python prepare_game_files.py --package WarOfGenesis-1.0.86-full.nupkg
```

기존 게임 파일이 들어 있는 대상 폴더는 덮어쓰지 않습니다.

## 선택 설정

`switch/WogSwitchProbe/launcher.ini`에서 자료 위치를 바꿀 수 있습니다.
기본값이면 위 폴더에 복사만 하면 됩니다. 상대 경로는 `switch/WogSwitchProbe` 기준이며,
`sdmc:/games/...` 같은 SD 절대 경로도 받습니다.

```ini
[game]
release=release
original=original/part2
part1=original/part1
assets=release/assets
```

`original`과 `part1`은 다음 게임 시작 단계에서 쓸 위치입니다. 이번에는 폴더 구조만
보고서에 기록합니다. 파트2는 `TXR/Txr.dat`, `Chr`, `Obs`, 파트1은 `Chars`, `Obss`, `Btls`
존재 여부를 확인하며 전체 자료 무결성 검사는 하지 않습니다.

## 확인한 범위와 남은 문제

Linux에서 공개 PC판 v1.0.86으로 실제 원본 패처 실행, 패치 DLL 두 개 로딩,
Main 조회가 통과했습니다. 캐시 재사용, 출력 파일 손상 시 보존·재생성,
원본 DLL 내용 변경 시 캐시 무효화, 서로 다른 DLL 버전 거부도 시험했습니다.

원본 패처는 참조 13개를 바꾸고 P/Invoke 82개를 수정했지만 **미지원 항목 90개**를
보고했습니다. Windows 전용 `SetThreadExecutionState`, `GetDpiForWindow` 호출과
버그 리포트 창의 WinForms 타입·메서드가 포함됩니다. 0.4는 이 내용을 숨기지 않고
`load-report.txt`에 기록합니다. DLL 로딩 성공이 게임 실행 성공을 의미하지 않습니다.
보고서는 `Main invoked: no`를 명시합니다.

스위치에서의 패처·캐시·DLL 로딩 및 0.3 Skia 실기 검증은 사용자 실행 결과 대기입니다.

## 재빌드

관리 코드와 실제 PC 파일 검사는 각각 `wogswitch-managed.yml`,
`wogswitch-game-audit.yml`의 읽기 전용 GitHub Actions에서 실행합니다.
테스트에 받은 PC 게임 DLL은 포트 배포 ZIP에 포함하지 않습니다.

```sh
python3 tools/build_switch_probe.py \
  --nroforge /workspace/NroForge \
  --devkitpro /workspace/setup-tools/root/opt/devkitpro \
  --managed /workspace/twgios/build/managed-0.4 \
  --skia /workspace/twgios/build/skia-native-final/output/skia-native \
  --checkpoint-version 0.4.0 \
  --output /workspace/twgios/build/switch-probe-0.4
```
