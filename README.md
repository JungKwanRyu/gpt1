# G3NX — 창세기전 3 홈브루 재구현

원본 게임 데이터를 분석해 Nintendo Switch 홈브루용 C/libnx 실행기를 작성하는 작업입니다.

**현재 0.7.0은 첫 전투와 전투 저장을 포함한 시험판입니다.** 적 AI, 원본 전투 대사·출구/전멸 조건, 공격·피격 애니메이션과 효과음, 전투 후 장면을 연결했습니다. 이동·공격·효과 재생 중에도 저장하고 같은 상태로 이어갈 수 있습니다. 원본 전체 게임의 이식이나 PC판과의 완전한 일치·새 전투의 Switch 실기 검증을 완료한 것은 아닙니다.

- [현재 버전 설치·동작·미구현 범위](docs/BATTLE_0_7_0.ko.md)
- [이전 0.6.0 기록](docs/COMBAT_0_6_0.ko.md)
- [이전 0.5.0 기록](docs/NAVIGATION_0_5_0.ko.md)
- [이전 0.4.0 기록](docs/SCENARIO_0_4_0.ko.md)
- [이전 0.3.0 기록](docs/SCENARIO_0_3_0.ko.md)
- [시작화면 복원 기록](docs/START_SCREEN.ko.md)
- [리버싱 기록](docs/HOMEBREW_REVERSE_ENGINEERING.ko.md)
- [첫 전투 복원 진행 및 검증 근거](docs/FIRST_BATTLE_RECONSTRUCTION.ko.md)

## 구성

- `homebrew/source`, `homebrew/include`: 네이티브 프런트엔드, 데이터 로더, 첫 시나리오 실행기와 저장 처리.
- `tools`: FAT16 이미지 읽기, PE 정적 분석, OBS/시나리오 해독, 자산 제작, 빌드·패키징·검증 도구.
- `docs`: 분석 근거, 지원 범위와 버전별 기록.

원본 게임·추출 자산·개발 도구·대용량 생성 자료·빌드 결과물은 저장소에 포함하지 않습니다. `g3_story_data.h`도 원본에서 생성하는 파일이며 추적하지 않습니다.

## 재생성 및 빌드

Python 3.12, 필요한 Python 라이브러리, FFmpeg, devkitA64/libnx/switch-tools가 필요합니다. Linux와 Windows 빌드 경로를 제공합니다. 자산 제작용 한글 폰트는 `--font`로 지정합니다. 아래는 Windows 예시이며 Linux에서는 실제 한글 폰트 경로를 사용합니다. 폰트 파일은 배포하지 않습니다.

```powershell
python -m pip install -r tools/requirements-reverse.txt
python tools/prepare_story.py Store/image.img
python tools/prepare_story.py Store/image.img --field 2
python tools/prepare_story.py Store/image.img --field 3
python tools/prepare_battle.py Store/image.img --font "C:/Windows/Fonts/malgun.ttf"
python tools/prepare_title.py Store/image.img
python tools/prepare_audio.py Store/image.img --tracks 3 6 35 42 --battle-effects
python tools/build_switch.py --toolchain "C:/devkitPro"
python tools/package_release.py
```

`Store/image.img`는 사용자가 별도로 준비해야 합니다. 분석 도구는 원본 EXE/DLL을 실행하지 않습니다.

## 검증

호스트 C 컴파일러와 기존 원본 추출 자료·자산을 준비한 뒤:

```powershell
python tools/analyze_game.py Store/image.img
python tools/prepare_assets.py Store/image.img
python tools/build_host.py
python -m unittest discover -s tools -p "test_*.py"
python tools/analyze_battle.py Store/image.img
```

사용자가 기존 버전의 Switch 음악 출력과 캐릭터 이동을 확인했습니다. 새 전투의 검증에는 원래 배치를 유지한 적 AI/출구 승리 플레이, 전투 중 저장 후 동일 상태 재실행, 다음 장면 전체 대사 검사, ASan/UBSan 및 Switch 빌드 검사를 사용합니다. 실기와 원본 PC 대조의 한계는 [0.7.0 검증 범위](docs/BATTLE_0_7_0.ko.md)를 참고하세요.
