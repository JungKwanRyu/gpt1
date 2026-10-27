# G3NX 홈브루 재구현 작업

> **현재 버전 0.6.0:** 양 진영 수동 조작으로 행동 주기·이동·기본 물리 공격·HP·전투불능을 검증한다. 원본 AI·승패 이벤트·전투 상태·공격 애니메이션은 미구현이다. [0.6.0 안내](../docs/COMBAT_0_6_0.ko.md)를 따른다. 사용자는 기존 버전의 Switch 음악과 캐릭터 이동을 확인했다. 새 전투 기능의 실기 동작은 미검증이다.

> **현재 버전 0.5.0:** 첫 전투 지도에서 아군 이동 범위·목적지 선택·이동 예산 차감을 검증할 수 있다. 전투 턴·공격·AI·승패는 미구현이다. 최신 재생성 절차는 [0.5.0 안내](../docs/NAVIGATION_0_5_0.ko.md)를 따른다. 아래는 이전 기록이다.

> **현재 버전 0.4.0:** 필드 2의 연출 이동·카메라·BGM·이동 중 저장을 추가했다. 자유 이동·전투는 미구현이다. 최신 실행·재생성·검증은 [0.4.0 안내](../docs/SCENARIO_0_4_0.ko.md)를 따른다. 아래는 이전 버전 이력이다.

> **현재 버전 0.3.0:** 시반슈미터 첫 대화 필드의 원본 명령 실행과 G3NX 진행 저장/불러오기를 추가했다. 전체 게임은 아직 실행할 수 없다. 설치 및 검증 범위는 [0.3.0 안내](../docs/SCENARIO_0_3_0.ko.md)를 따른다. 아래는 이전 버전 이력이다.

> **현재 버전 0.2.0:** 실행하면 원본 자산을 복원한 시작화면이 나온다. Start Game에서 세 에피소드 선택까지 가능하다. 전투·이벤트 실행, 저장/불러오기, 튜토리얼, 오디오와 동영상은 미구현이다. 실기 실행은 미검증이다. 아래 리소스 뷰어 설명은 0.1.0 이력이며, 현재 설치·재빌드 방법은 [시작화면 안내](../docs/START_SCREEN.ko.md)를 따른다.

**현재는 리소스 뷰어와 데이터 로더다. 창세기전 3을 플레이할 수 있는 이식판이 아니다. 2026-10-27 NRO 빌드와 파일 구조 검증을 완료했다. Switch 실기 실행은 아직 검증하지 않았다.**

분석 결과와 미구현 범위는 `../docs/HOMEBREW_REVERSE_ENGINEERING.ko.md`에 기록했다.

## 분석과 데이터 생성

저장소 루트에서 Python 3.12 및 `tools/requirements-reverse.txt`의 라이브러리를 사용한다.

```powershell
python -m pip install -r tools/requirements-reverse.txt
python tools/g3_image.py Store/image.img --manifest reverse/image-manifest.json
python tools/analyze_game.py Store/image.img
python tools/prepare_assets.py Store/image.img
```

분석은 원본 EXE/DLL을 실행하지 않는다. `reverse/original`의 파일은 정적 분석용이다.

## PC 로더 검사

Windows x64와 Visual Studio C/C++ 도구가 있는 환경에서 저장소 루트 기준:

```powershell
.\homebrew\build-host.cmd
python tools/test_resources.py
```

공용 C DLL을 컴파일하지 않았으면 native 검사는 건너뛰었다고 표시된다. 분석과 자산 생성을 먼저 수행해야 실제 자료에 대한 검사가 가능하다.

## Switch 빌드

공식 devkitPro 설치 절차로 `switch-dev` 개발 환경을 준비해야 한다. 필요한 구성에는 devkitA64, libnx, switch-tools, GNU make가 포함된다. `devkita64-rules`만으로는 빌드할 수 없다. 빌드 환경이 설치된 devkitPro 셸에서:

```sh
cd homebrew
make
```

성공 시 `g3nx.nro`가 생성된다. 이 작업 환경에서는 MSYS의 한글 설치 경로 매핑이 깨져 있어, 설치 캐시의 공식 패키지를 `.toolchain/local/opt/devkitpro`에 복사하고 Windows 도구를 직접 호출하는 방식으로 빌드했다. 기존 설치는 변경하지 않았다. 저장소 루트에서 재현할 수 있다.

```powershell
python tools/build_switch.py
```

다른 정상 설치를 사용할 경우 `--toolchain "C:/devkitPro"`처럼 경로를 지정한다. 결과는 `dist/switch/g3nx/g3nx.nro`에 복사된다. 빌드 도구 버전, NRO/NACP 검사 결과와 실행 파일/자산 해시는 같은 폴더의 `build-report.json`에 기록한다. GNU make 경로는 이번 환경에서 실행하지 않았다.

뷰어를 빌드한 뒤 사용할 SD 카드 배치는 다음과 같다.

```text
switch/g3nx/g3nx.nro       (빌드 성공 후 별도로 복사)
switch/g3nx/g3nx.assets    (prepare_assets.py로 생성됨)
```

기존 홈브루 실행 환경의 hbmenu에서 뷰어를 실행하도록 작성했다. L/R 및 좌우 방향키는 이미지 이동, ZL/ZR은 10개 이동, +는 종료다. 자산을 찾지 못하면 오류 화면을 표시한다. 기기 설정 변경이나 홈브루 환경 설치 작업은 포함하지 않는다.
