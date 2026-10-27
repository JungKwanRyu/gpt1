# G3NX — 창세기전 3 홈브루 재구현

원본 게임 데이터를 분석해 Nintendo Switch 홈브루용 C/libnx 실행기를 작성하는 작업입니다.

**현재 0.4.0은 전체 게임 이식판이 아닙니다.** 시작 메뉴, 시반슈미터 첫 대화와 필드 2의 원본 연출 이동·카메라·BGM, 이동 중 진행 저장·불러오기를 지원합니다. 자유 이동, 전투, 나머지 에피소드, 음성·효과음·동영상은 아직 구현하지 않았습니다.

- [현재 버전 설치·동작·미구현 범위](docs/SCENARIO_0_4_0.ko.md)
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

Python 3.12, 필요한 Python 라이브러리, FFmpeg, devkitA64/libnx/switch-tools가 필요합니다. Linux와 Windows 빌드 경로를 제공합니다. 자산 제작용 한글 폰트는 기본적으로 Windows의 `malgun.ttf`를 사용합니다. Linux에서는 `prepare_story.py`와 `prepare_title.py`에 `--font /path/to/Korean.ttf`를 지정합니다. 폰트 파일은 배포하지 않습니다.

```powershell
python -m pip install -r tools/requirements-reverse.txt
python tools/prepare_story.py Store/image.img
python tools/prepare_story.py Store/image.img --field 2
python tools/prepare_title.py Store/image.img
python tools/prepare_audio.py Store/image.img --tracks 35 42
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

0.2.0의 시작화면은 사용자가 실기 동작을 확인했습니다. 0.4.0의 시나리오·이동·저장·BGM 기능은 호스트 검사와 Switch 빌드를 통과했으며, 화면·소리·SD 저장의 실기 확인은 별도로 필요합니다. 자유 이동과 첫 전투 완료는 미달성입니다.
