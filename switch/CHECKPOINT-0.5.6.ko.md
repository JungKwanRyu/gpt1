# WogSwitch 0.5.6 — PCM 오디오 출력과 원본 자료 진단

0.5.5 실기 로그는 원본 자료 3238개 항목의 검사를 마치고 304개 경고를 표시한 뒤,
미구현 오디오 출력에서 종료됐음을 보여 줍니다.

```
AUDIO NOT READY: 44100 Hz / 2 channels / 16 bits
mono_unhandled_exception_internal ... GameStartupException
```

304개 경고와 직접적인 종료 원인은 구분해야 합니다. 이 버전은 오디오 출력을 구현하고,
찾지 못한 원본과 읽기·쓰기 오류를 구분할 수 있게 합니다. 원본 파일 304개를 이 패키지가
추가하거나, 사용자 원본 자료의 완전성을 확인한 것은 아닙니다.

## 변경 내용

- libnx audout에 48kHz 스테레오 16비트 PCM을 전달합니다. 게임의 44.1kHz 출력을
  연속적인 선형 보간으로 변환합니다. 모노/스테레오, 8/16비트 입력을 지원합니다.
- 4096바이트 정렬 버퍼 4개에 20ms씩 담고, 드라이버가 소비한 뒤 재사용합니다.
  재생 위치, 대기, Flush와 종료를 연결합니다. 서비스 종료 후 버퍼를 해제합니다.
- 기본 게임 믹서의 BGM·효과음 혼합 출력 하나를 지원합니다. 별도 출력의 동시 요청은
  거절하며, 영상과 별도 재생기를 함께 사용하는 동작은 아직 검증하지 않았습니다.
- 초기화 실패는 출력 불가로 기록하고, 쓰기 실패는 해당 출력을 중지합니다.
  실제 소리가 출력됐다는 의미의 성공으로 처리하지 않습니다.
- 게임의 오류 보고 함수는 원인을 기록하고 종료 메시지를 게시합니다. 보고 과정에서
  또 다른 사용자 정의 예외를 던지던 경로를 제거합니다.
- asset-report.txt에는 실패한 자료 이름을 기록합니다. 같은 자료의 반복 요청은 한 번만
  기록해 렌더링 중 SD 쓰기가 반복되지 않도록 합니다.

## 설치와 재실행

1. ZIP의 switch/WogSwitchProbe 폴더 전체를 SD카드 같은 위치에 덮어씁니다.
   **NRO·managed·probe.ini를 모두 갱신**합니다.
2. 기존 release, original, save, home, launcher.ini를 유지합니다.
3. full application / title takeover로 실행하고 0.5.6 표시를 확인합니다.
4. DLL 패치·로딩 완료 후 X로 시작합니다. 자료 경고를 확인하면 게임 시작을 계속 시도합니다.
5. 결과 진단에는 probe.log, runtime.log, game-start.txt와 새 asset-report.txt가 필요합니다.

## 로그 해석

probe.log의 `PASS audio audout opened`, `PASS audio PCM buffer submitted`,
`PASS audio PCM buffer released`는 각각 장치 열기, 버퍼 제출, 드라이버 반환을 뜻합니다.
이 기록만으로 사용자가 실제로 소리를 들었는지까지 판단하지는 않습니다.
`AUDIO ERROR`나 `AUDIO OPEN FAILED`는 출력 불가 또는 중단입니다.

asset-report.txt의 항목은 다음과 같습니다.

- `NOT_FOUND_OR_UNMAPPED`: 원본을 찾지 못했거나 해당 자료 경로를 해석하지 못했습니다.
- `IO_ERROR`: 자료를 읽거나 쓰는 과정에서 발생한 예외와 위치입니다.
- `UNAVAILABLE_AFTER_IO_ERROR`: 위 IO 오류 후에도 자료를 만들지 못했습니다.

## 검증 범위

실제 C 변환기와 오디오 드라이버를 모의 audout 서비스로 실행했습니다.
44.1→48kHz 파형 오차·좌우 분리·청크 경계 연속성, 48kHz 동일성, 8비트 모노 변환,
버퍼 정렬·소유권·대기·재생 위치·Flush·종료와 실패 정리를 검사했습니다. UBSan을 사용했습니다.
reference의 WAV는 시험에서 만든 합성 신호이며 원본 게임 소리가 아닙니다.

실제 PC판 DLL의 AudioMixer에서 WogShim을 거쳐 SwitchAudioSink까지 PCM이 전달되는
것을 모의 네이티브 연결로 검사했습니다. 실제 OriginalAssets.Extract에 합성 자료 폴더를
주어 파일 부재와 PAK IO 오류를 구분하는지도 검사했습니다.
기존 실제 DLL 패치·캐시·로딩·Main 안내 후 취소 복귀와 SDK9 API 검사도 실행했습니다.
CI 번호와 결과는 verification.json에 있습니다.

0.5.6의 스위치 실기 음향·타이틀·전투·영상·저장/불러오기·성능 검증은 아직 필요합니다.
사용자 원본 로그와 PC 게임 DLL은 배포 ZIP에 포함하지 않습니다.
