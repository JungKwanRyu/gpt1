# G3NX 0.4.0 — 필드 2 연출 이동과 BGM

0.3.0의 첫 대화 뒤에 원본 필드 2를 연결한다. **자유 이동과 첫 전투 완료를 구현한 버전은 아니다.** 필드 2가 끝나면 전투 6 복원 경계에서 멈춘다. 나머지 에피소드·음성·효과음·동영상·전체 스토리 연결도 미구현이다.

## 사용 방법

ZIP의 `switch/g3nx` 폴더 전체를 기존 hbmenu 환경의 SD 카드에 복사한다. `g3nx.nro`, `g3nx.title`, `g3nx.story`, `g3nx.field2`, `audio/`를 함께 설치한다. 이전 NRO나 자산 일부만 섞어 사용하지 않는다.

Start Game → Shivan Scimitar에서 시작한다. A로 첫 대화를 진행하고, 마지막 안내에서 A를 누르면 필드 2로 넘어간다. 네 캐릭터가 원본 명령에 따라 동시에 이동하고, OBS 프레임과 카메라 이동을 표시한다. 이 장면에서도 A로 대사를 넘긴다. B는 에피소드 선택으로, +는 종료다. Y는 BGM 음소거다.

X는 현재 장면·대화 페이지·진행 중 이동·카메라·캐릭터 동작을 함께 저장한다. Load Game은 해당 상태를 복원한다. 새 저장은 `g3nx-campaign-0.bin`과 `g3nx-campaign-1.bin` 두 슬롯이며 CRC, 시나리오 식별자, 상태 범위를 검사한다. 임시 파일을 완전히 기록한 뒤 슬롯 이름으로 바꾼다. 최신 슬롯이 손상되면 이전 유효 슬롯을 읽는다. 새 저장이 하나도 없을 때만 기존 `g3nx-progress-*.bin`을 읽어 필드 1에서 이어간다. 새 저장이 모두 손상됐으면 오래된 저장으로 임의 복귀하지 않는다.

## 구현 근거와 한계

- 원본 CPR의 `503(1) → 503(2) → 502(6)` 호출 순서를 사용한다. FLD 명령 703의 값 2를 다음 필드 번호로 해석하지 않는다.
- 필드 2의 302는 `100CDB40`에서 배우 좌표에 부호 있는 델타를 매 틱 더한다. 303은 `100CDBC0`에서 배경의 현재 이동 오프셋을 반영해 좌표를 설정한다. 새 실행기는 배경 기준 좌표를 유지하고 렌더링 때 카메라를 뺀다. 원본의 `100C82F0~100C8337` 좌표 갱신 경로와 대응한다.
- 401은 `100CDA60`의 배경 이동 경로를 따른다. BGR 93은 1300×500이며 640×480 뷰포트의 카메라 범위는 x 0~660, y 0~20이다. 네 배우의 이동을 먼저 등록하고 1번 장벽에서 완료를 기다린다.
- OBS의 원본 이미지, 기준점, 동작의 이미지 키와 표시 시간을 사용한다. 이미지 이외 OBS 명령의 일반 실행기, 원본 초당 틱 빈도, 팔레트 페이드는 완전히 복원하지 않았다. 원본과 동일한 실시간 연출 속도를 검증했다는 뜻은 아니다.
- 필드 1의 BGM 명령은 트랙 35, 필드 2는 트랙 42다. `.BGM`은 Microsoft ADPCM RIFF/WAVE이며 FFmpeg로 48kHz·16비트·스테레오 PCM으로 변환한다. 원본과 변환 결과의 해시를 기록한다. libnx audout 버퍼를 재사용해 재생하며 음소거·장면 이탈·종료 때 출력을 닫는다. 오디오 오류는 장면을 중단하지 않고 `AUDIO ERROR`를 표시한다.

첫 전투는 여전히 미구현이다. 공격·명중·방어·상태·행동 순서·AI·이동 제약을 임의 공식으로 대체하지 않는다. 첫 전투 완료 이후 장면도 건너뛰어 연결하지 않는다. 자세한 원본 복원 현황은 [첫 전투 기록](FIRST_BATTLE_RECONSTRUCTION.ko.md)을 참고한다.

## 검증

기존 30개 검사에 필드·저장 7개, 오디오 3개, 첫 장면 자산 크기 회귀 검사 1개, 패키징 3개를 추가해 총 44개를 통과했다. 필드 2 대사 순서와 최종 배우 위치, 네 배우의 동시 이동과 장벽, 이동 중 저장·복원·손상 슬롯 대체, CRC가 올바르더라도 잘못된 상태 거부, 이전 저장 이관, 슬롯 재사용과 중단된 임시 저장 무시를 검사한다. C 렌더러의 배경·캐릭터 픽셀은 원본에서 독립적으로 합성한 Pillow 결과와 일치했다. PCM 검사는 실제 변환 파일과 Python WAV 읽기를 비교하고 반복 경계·잘린 파일·틀린 포맷을 확인한다.

Linux 호스트 빌드와 devkitA64 Switch NRO 빌드, ELF/NRO/NACP 검사, 런타임 파일 해시와 배포 ZIP 검사를 수행했다. Windows 실행 경로는 이 환경에서 재검증하지 않았다. **Switch 실기 화면·사운드·SD 카드 저장·재개는 아직 검증하지 않았다.**

재현은 저장소 루트에서 수행한다. 원본 이미지와 폰트, FFmpeg, Python 의존성, 공식 SDK가 필요하다.

```sh
python tools/prepare_story.py Store/image.img --font /path/to/Korean.ttf
python tools/prepare_story.py Store/image.img --field 2 --font /path/to/Korean.ttf
python tools/prepare_title.py Store/image.img --font /path/to/Korean.ttf
python tools/prepare_audio.py Store/image.img --tracks 35 42
python tools/build_host.py
python -m unittest discover -s tools -p 'test_*.py'
python tools/build_switch.py --toolchain /path/to/devkitpro
python tools/package_release.py
```

`test_resources.py`는 기존 `prepare_assets.py` 결과와 7-Zip으로 독립 추출한 `reverse/original/G3P1/G3Launch.exe`, `alpha.dll`, `G3.pak`도 필요하다. 원본·추출 자료·폰트·생성 헤더·빌드 결과는 Git에 포함하지 않는다.
