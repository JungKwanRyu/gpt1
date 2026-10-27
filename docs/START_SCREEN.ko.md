# G3NX 0.2.0 — 시작화면

원본 시작화면의 배경, 로고, 메뉴를 복원해 Switch 홈브루 프런트엔드를 작성했다. 게임 실행 파일을 구동하는 에뮬레이터가 아니라 새로 작성한 libnx/C 코드다. 제공된 부팅 스크린샷을 화면 이미지로 사용하는 방식은 적용하지 않았다.

## 설치와 조작

`dist/g3nx-start-screen-0.2.0.zip`을 풀고 안의 `switch` 폴더를 SD 카드 최상위로 복사한다. 기존 `g3nx.nro`는 새 파일로 교체하고 `g3nx.title`도 함께 복사한다.

```text
SD 카드/switch/g3nx/g3nx.nro
SD 카드/switch/g3nx/g3nx.title
```

hbmenu에서 `G3NX - The War of Genesis III`를 실행한다. 0.1.0의 큰 `g3nx.assets` 파일은 이번 실행에 사용하지 않으며 별도로 삭제할 필요는 없다.

- 방향키 또는 왼쪽 스틱: 메뉴 선택.
- A: 확인. B: 이전 화면. +: hbmenu로 종료.
- Start Game: Shivan Scimitar / Crimson Crusade / Apocalypse 선택 화면으로 이동.
- 에피소드 확인: 아직 게임 진행이 구현되지 않았다는 안내 표시. A/B로 선택 화면에 복귀.
- Load Game / Tutorial: 해당 기능이 미구현임을 안내.
- Exit: 종료.

## 복원한 원본 자산

`alpha.dll`의 타이틀 생성 함수 `0x100D5790`에서 자산과 배치 좌표를 확인했다.

| 자산 | 용도 | 코드 근거 |
| --- | --- | --- |
| `BGRs/0015.bgr` | 타이틀 배경 | `0x100D585B`에서 ID 15를 BGR 로더에 전달 |
| `Obss/0360.obs` 그룹 0 프레임 0, 1 | 영문/한글 로고 | `0x100D58DD` / `0x100D5965`의 ID 360, 프레임 0/1, 중심 `(320,244)` |
| `Obss/0751.obs` 그룹 0 프레임 0~3 | Start / Load / Tutorial / Exit | ID 751, 기준 `(320,240)` |
| 같은 OBS의 프레임 4 | 저작권 표기 | 원본 메뉴 스프라이트 |
| `BGRs/0096.bgr` | 에피소드 선택 배경과 제목 | 원본 BGR 항목을 해석 |
| `Eps/EPISODE.ELG` | 세 에피소드 설명 | 바이트 반전 및 CP949 해석 |

OBS 이미지 로더는 `0x100A6FC0`, 스팬 해제 루틴은 `0x100A6BF0`다. 이미지 그룹의 128바이트 헤더, 프레임별 `uint32` 페이로드 길이와 폭/높이/부호 있는 원점, 768바이트 RGB 팔레트를 확인했다. 압축 스트림은 `uint16` 스팬 수에 이어 건너뛰기 `uint16`, 리터럴 개수 `4×uint8 + uint8`, 팔레트 인덱스 바이트로 구성된다. 건너뛰기는 640픽셀 피치를 사용하며 프레임 폭 기준의 좌표로 변환해야 한다. 그룹 헤더의 투명 팔레트 인덱스를 알파 0으로 변환한다.

이 형식을 `tools/g3_obs.py`에서 해독한다. 인덱스 파일이 지정하는 묶음의 자산을 사용하며, 원본 개별 파일을 덮어쓰지 않는다. 출처와 SHA-256은 `dist/switch/g3nx/g3nx.title.json`에 기록했다.

`tools/prepare_title.py`는 복원 자산을 조합해 12개의 640×480 RGBA 장면을 `g3nx.title`에 저장한다. 원본 로고와 메뉴 위치를 유지하고 선택되지 않은 항목은 흐리게 표시한다. 에피소드 카드, 조작 안내, 미구현 기능 안내는 새로 작성한 화면이다. 입력에 따라 C 상태 머신이 장면을 선택한다. 화면은 원본 비율을 유지해 960×720으로 표시하고 좌우 160픽셀은 검게 채운다.

0.2.0은 정적인 시작화면을 지원한다. 원본의 애니메이션, 타이틀 음악이나 인트로 동영상까지 재현했다고 주장하지 않는다.

## 재생성과 빌드

저장소 루트에서 Python/Pillow와 기존 devkitA64 사본을 사용한다. 한글 조작/설명 텍스트의 래스터화에는 Windows의 `malgun.ttf`를 사용하며 폰트 파일 자체를 배포하지 않는다. 다른 환경에서는 `--font`로 한글 폰트를 지정한다.

```powershell
python tools/prepare_title.py Store/image.img
python tools/build_switch.py
.\homebrew\build-host.cmd
python tools/test_title.py
python tools/test_resources.py
```

NRO는 devkitA64 GCC 16.1.0과 libnx 4.12.0으로 경고를 오류로 처리해 빌드했다. ELF64/AArch64, NRO/ASET 세그먼트와 NACP 앱 제목을 검사했고, 해시는 `build-report.json`에 기록한다.

시작화면 관련 검사 6개와 기존 자원 로더 검사 7개를 통과했다. 실제 C 메뉴 상태 전이, 1,000회 입력에서 장면 범위 유지, 12개 장면의 C 로더 읽기, 4:3 화면 확대와 프레임버퍼 스트라이드를 검사했다. `title-native-preview.png`는 Switch에 쓰는 것과 같은 공용 C 렌더러를 PC에서 호출한 결과이며 실기 캡처가 아니다.

**스위치 실기 구동, 컨트롤러 반응, 실제 프레임 속도는 아직 확인하지 않았다. 전투·이벤트·저장 기능의 재구현은 별도 작업으로 남아 있다.**
