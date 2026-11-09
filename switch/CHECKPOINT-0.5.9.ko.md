# WogSwitch 0.5.9 — JPEG 크래시 수정 및 메뉴 배경 처리 개선

0.5.8은 실기에서 시작 화면까지 진입했습니다. 304개 자료 오류는 사라졌지만 새 게임을
누르면 2168-0002(Data Abort)로 종료됐고, 메뉴 반응은 약 3~4초 늦었습니다.

## 종료 원인과 수정

크래시 모듈의 빌드 ID가 보관한 0.5.8 ELF와 일치합니다. 종료 지점은
init_simd → jsimd_can_ycc_rgb → jpeg_start_decompress → SkJpegCodec::onGetPixels
경로입니다. libjpeg의 ARM SIMD 초기화에서 스위치와 맞지 않는 TLS 접근이 발생했습니다.
스위치 CPU에 NEON 기능이 없다는 뜻은 아닙니다.

libjpeg-turbo가 제공하는 jsimd_none 디스패처를 선택해 Skia를 다시 빌드했습니다.
JPEG 읽기 기능은 유지하며, 문제를 일으킨 초기화 함수와 TLS 심볼이 새 라이브러리와
최종 ELF에 남지 않는지 검사합니다. 실행기 시작 시 합성 baseline/progressive JPEG를
직접 읽어 크기·색·알파를 확인하고 probe.log에 결과를 남깁니다.

## 메뉴 지연 개선과 측정

타이틀과 연대표는 매 프레임 640×480 배경에 SetPixel을 약 30만 번 호출했습니다.
인터프리터에서 반복하던 이 부분을 네이티브 배경 복사 한 번으로 바꿨습니다.
불투명 알파·화면 경계 처리는 기존과 동일하게 유지합니다. 메뉴·반짝임·글자는 그대로
그립니다. 실제 게임 SetPixel 결과와 새 관리 코드/네이티브 C 경로를 픽셀 단위로 비교하고,
극단적인 좌표 등을 포함한 4,000개 C 테스트를 UBSan으로 검사합니다.

3~4초 지연 중 이 코드가 차지한 비율은 아직 기기에서 측정하지 못했습니다. 이번 버전의
실제 반응 시간이나 개선 배수도 확정하지 않습니다. performance.log에 첫 프레임의 준비를
제외하고 약 5초마다 FPS와 compose(합성)·upload(올리기)·draw(그리기/스왑 포함)·
pump(보조 게임 상태 갱신)의 평균 벽시계 시간을 적습니다. draw는 GPU 단독 시간이 아닙니다.
같은 PERF 줄을 probe.log에도 남깁니다. 이 측정은 화면 합성을 건너뛰지 않습니다.

## 설치와 확인

1. ZIP의 switch/WogSwitchProbe 전체를 SD카드 같은 위치에 덮어씁니다.
   NRO와 managed를 함께 갱신하고 기존 release, original, save, home, launcher.ini는 유지합니다.
2. full application/title takeover로 실행해 0.5.9 표시를 확인합니다.
3. probe.log의 PASS native JPEG decode baseline/progressive를 확인하고 X로 시작합니다.
4. 시작 메뉴에서 약 15초 동안 방향키로 메뉴를 옮긴 뒤 새 게임을 선택합니다.
5. 같은 문제가 남으면 새 probe.log, game-start.txt, performance.log와 종료 시각에 생성된
   /atmosphere/crash_reports/의 .log를 확인합니다. asset-report.txt는 자료 오류가 생길 때 유용합니다.

새 패치 캐시는 처음 한 번 다시 준비합니다. 원본 PC 게임 DLL이나 원본 자료를 다시 받을
필요는 없습니다. 기존 자료와 편집 내용을 지우지 마세요.

## 선택 기능: PC에서 SD DLL 패치 캐시 준비

Microsoft .NET 10 Runtime이 있는 Windows PC에서, 먼저 SD 실행기를 이 버전으로 갱신한 뒤
ZIP의 pc-tools/prepare-sd.cmd를 실행합니다. 앱 경로(예: E:\switch\WogSwitchProbe)를 입력하고
PC PREPARE COMPLETE 뒤 SD를 안전하게 꺼냅니다. 같은 버전의 관리 코드를 사용하는지 확인합니다.

직접 명령: dotnet pc-tools/runtime/WogSwitch.Managed.dll --prepare-sd "E:\switch\WogSwitchProbe"

PC 도구는 게임 Main·GPU·오디오를 실행하지 않습니다. 기존 save/assets가 있으면 편집 내용과
캐시를 보존하기 위해 PC에서 원본 자료 재추출을 건너뜁니다. 결과는 pc-prepare-report.txt입니다.

## 검증 범위

실제 PC판 1.0.86 DLL 패치·로드, SDK9 타입/메서드 참조, .NET 9 셰이더와 배열 구조,
한글 경로·파일 교체·오디오의 기존 회귀 검사를 유지합니다. JPEG 합성 테스트의 PC 결과와
네이티브 코드 정적 검사는 스위치에서 새 게임·연대표·전투·영상이 모두 동작한다는 증거는
아닙니다. 0.5.9의 새 게임 진입과 메뉴 반응은 실기 확인이 필요합니다.

reference/에는 합성 테스트와 CI 결과만 있습니다. 사용자 크래시 원문·메모리 덤프·원본 게임은
패키지에 포함하지 않습니다. JPEG 처리 경로만 변경했고 게임 실행은 계속 인터프리터 모드입니다.
