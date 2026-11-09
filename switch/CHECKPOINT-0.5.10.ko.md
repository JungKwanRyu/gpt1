# WogSwitch 0.5.10 — 파트2 진행 중 화면 합성 최적화

게임 진행 중 화면 합성에 사용되는 공통 픽셀 처리를 네이티브 코드로 옮기는 버전입니다.
원래 색 계산과 게임 동작을 유지하면서 반복되는 관리 코드 작업을 줄이는 것이 목표입니다.

## 변경

- 공통 DrawFrame의 8가지 UI 색 섞기와 페이드 처리를 네이티브 C로 옮겼습니다.
  투명 픽셀 건너뛰기, 클리핑, 가산·감광·닷지·스크린·글로우와 정수 반올림을 유지합니다.
- FillRect의 불투명·반투명 채우기를 네이티브 C로 옮겼습니다. 나레이션·장면 전환의
  화면 페이드에도 적용합니다.
- BlitMasked는 색조·상태 효과·파트1 특수 합성이 없는 일반 그림·글자·가산 처리를
  네이티브 C로 수행합니다. 나머지는 원래 게임 구현을 호출합니다.
- 파트1 타이틀에도 기존 640×480 네이티브 배경 복사를 적용했습니다.
- 원래 세 함수는 그대로 보존합니다. 특수 합성이나 새 경로가 지원하지 않는 입력은 원래
  구현으로 처리하며, PC 도구에서도 스위치 네이티브 코드를 로드하지 않습니다.
- 60초를 넘는 처리 간격은 performance.log의 LONG_GAP으로 별도 기록하고 일반 FPS
  표본을 다시 시작합니다. 이는 슬립이나 긴 정지 가능성을 기록하는 기준이며 모든 슬립을
  감지하거나 오디오 복귀를 고치는 기능은 아닙니다.

## 검증과 제한

실제 게임의 원래 DrawFrame/FillRect/BlitMasked와 새 경로를 1,106개 사례에서 픽셀 단위로
비교했습니다. 8개 UI 모드, 투명도·클리핑·페이드·가산·색조 및 PC 대체 경로를 검사합니다.
실제로 사용하는 C 소스를 UBSan으로 빌드해 관리 코드와 연결했습니다. 기존 네이티브 배경
4,000개 사례, SDK9 API 검사, .NET 9 실행 검사, 오디오·한글 경로·파일·PC 준비 회귀도 유지합니다.

이 검사는 스위치의 개선 FPS나 나레이션·전투 전체의 동작을 확정하지 않습니다.
실행은 계속 C# 인터프리터이며 CPU 합성의 모든 부분을 네이티브로 옮긴 것은 아닙니다.
게임 틱·대사·애니메이션을 건너뛰거나 프레임을 생략하지 않습니다.

## 설치와 확인

1. ZIP의 switch/WogSwitchProbe 전체를 SD카드 같은 위치에 덮어씁니다.
   기존 release, original, save, home, launcher.ini는 유지합니다.
2. full application/title takeover로 실행해 0.5.10 표시를 확인하고 X로 시작합니다.
   새 패치 캐시는 처음 한 번 다시 준비합니다. 원본 자료를 다시 복사할 필요는 없습니다.
3. 같은 파트2 챕터·나레이션 구간을 슬립 없이 20~30초 진행해 반응을 비교합니다.
4. 새 performance.log와 probe.log를 확인합니다. LONG_GAP은 일반 FPS와 분리된 긴 간격입니다.
   종료가 생기면 game-start.txt와 그 시각의 /atmosphere/crash_reports/*.log도 확인합니다.

JPEG 수정·한글 경로 대응·기존 저장 자료를 유지합니다. reference/에는 공개 코드의 합성
검사와 CI 기록만 있으며 사용자 로그 원문·덤프·원본 게임 DLL/자료는 포함하지 않습니다.

## 선택 기능: PC에서 SD 패치 캐시 준비

Microsoft .NET 10 Runtime이 있는 Windows PC에서, 먼저 SD 실행기를 이 버전으로 갱신한 뒤
ZIP의 pc-tools/prepare-sd.cmd를 실행합니다. 앱 경로(예: E:\switch\WogSwitchProbe)를 입력하고
PC PREPARE COMPLETE 뒤 SD를 안전하게 꺼냅니다. 결과는 pc-prepare-report.txt입니다.

명령: dotnet pc-tools/runtime/WogSwitch.Managed.dll --prepare-sd "E:\switch\WogSwitchProbe"

게임 Main·GPU·오디오를 실행하지 않습니다. 기존 save/assets가 있으면 편집 자료와 캐시를
보존하기 위해 원본 자료 재추출을 건너뜁니다.
