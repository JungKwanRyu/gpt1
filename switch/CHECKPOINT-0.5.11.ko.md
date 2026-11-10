# WogSwitch 0.5.11 — 필드 배경 최적화와 종료 경로 수정

필드 배경과 보관된 장면 그림을 네이티브 코드로 그리며, 관리 코드 실행이 끝나면
SDK의 정상 앱 종료 경로로 복귀합니다.

## 변경

- EGL 화면 종료 후 소프트웨어 콘솔을 다시 열거나 갱신하지 않습니다.
  실행 결과는 probe.log/runtime.log에 남기고 홈브류 메뉴로 복귀합니다.
- 필드 배경의 640×480 픽셀 반복을 네이티브 C로 옮겼습니다. 노란 색상 키,
  카메라 위치, 배경 조각의 시차·흐름·겹침 순서와 빈 영역 보존을 유지합니다.
- 필드 장면에 보관된 그림을 다시 그릴 때 기존 네이티브 불투명 복사를 사용합니다.
  그림의 덮개 층과 이후 인물·물체를 그리는 순서는 유지합니다.
- 원래 필드 메서드를 보존합니다. 배경이 없거나 조각이 64개를 넘는 경우 등
  새 배경 경로가 지원하지 않는 입력은 원래 코드로 처리합니다.
- performance.log에 background, battle, field, moses, ui, title, cutscene,
  rest를 추가했습니다. 화면 합성 한 번당 시간이며 compose의 세부 항목입니다.
  upload/draw와 더해서 중복 집계하면 안 됩니다.

## 검증과 제한

실제 게임 DLL의 원래 필드 배경·장면 함수와 새 경로를 픽셀 및 반환값으로 비교합니다.
검사는 색상 키·시차·흐름·겹침·빈 영역·클리핑·보관 그림·PC 대체 경로를 포함합니다.
실제 C 소스를 UBSan으로 빌드해 관리 코드와 연결하고, 종료 코드는 EGL/libnx를
대체한 시험에서 검사합니다. 기존 공통 합성, SDK9 API, .NET 9 실행,
오디오·파일·한글 경로 및 Windows/Linux PC 준비 검사도 유지합니다.

실기에서 개선 FPS와 모든 챕터·전투의 동작은 아직 검증하지 않았습니다.
C# 인터프리터의 다른 작업과 자료 읽기 지연이 남을 수 있습니다.
게임 틱·대사·애니메이션을 건너뛰거나 프레임을 생략하지 않습니다.

## 설치와 확인

1. ZIP의 switch/WogSwitchProbe 전체를 SD카드 같은 위치에 덮어씁니다.
   기존 release, original, save, home, launcher.ini는 유지합니다.
2. full application/title takeover로 실행해 0.5.11을 확인하고 X로 시작합니다.
   새 패치 캐시는 처음 한 번 다시 준비합니다. 원본 자료를 다시 복사할 필요는 없습니다.
3. 같은 챕터 선택·나레이션·게임 구간을 슬립 없이 각각 20~30초 진행해 비교합니다.
4. PLUS로 종료해 홈브류 메뉴 복귀를 확인합니다. 새 performance.log와 probe.log로
   구간별 시간을 확인하고, 오류 종료 시 그 시각의 /atmosphere/crash_reports/*.log를 확인합니다.

reference/에는 공개 코드의 합성 검사와 CI 기록만 포함합니다.
사용자 장치 로그·덤프·원본 게임 DLL·게임 자료는 포함하지 않습니다.

## 선택 기능: PC에서 SD 패치 캐시 준비

Microsoft .NET 10 Runtime이 있는 Windows PC에서 SD 실행기를 먼저 이 버전으로 갱신한 뒤
ZIP의 pc-tools/prepare-sd.cmd를 실행합니다. 앱 경로(예: E:\switch\WogSwitchProbe)를 입력하고
PC PREPARE COMPLETE 뒤 SD를 안전하게 꺼냅니다. 결과는 pc-prepare-report.txt입니다.

명령: dotnet pc-tools/runtime/WogSwitch.Managed.dll --prepare-sd "E:\switch\WogSwitchProbe"

게임 Main·GPU·오디오를 실행하지 않습니다. 기존 save/assets 편집 자료와 캐시는 보존합니다.
