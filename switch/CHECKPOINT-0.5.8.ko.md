# WogSwitch 0.5.8 — SDK9 셰이더 호환 및 캐릭터 경로 처리

0.5.7 실기 로그에서는 DLL 패치·로드와 오디오 장치 열기·PCM 버퍼 제출·반환이
성공했습니다. 실제로 소리가 들렸는지는 사용자 확인이 필요합니다. 게임 프레임은 0개였고,
셰이더 준비에서 .NET 10의 InlineArray5 타입을 SDK9가 찾지 못해 종료했습니다.

## 이번 변경

- WogShim의 HlslBlob.Pack을 SDK9에 있는 String.Join 배열 오버로드로 바꿨습니다.
  WOGHLSL·이름·진입점·프로필·소스의 UTF-8/NUL 구분 형식은 그대로입니다.
- 게임 DLL의 InlineArray4<T>는 .NET 8부터 지원된 InlineArrayAttribute를 사용하는
  모듈 내부 구조체로 옮깁니다. System.* 참조 버전도 9로 맞춰 .NET 9에서 실행 검사합니다.
- SDK9 API 검사는 메서드뿐 아니라 타입 참조도 검사합니다. 이전 검사에서
  InlineArray5 타입을 놓친 범위를 보완했습니다.
- 캐릭터 폴더는 실제 디스크 이름을 한 번 읽어 NFC/NFD 표기와 고유한 4자리 ID로
  대응시킵니다. 추출 목적지·존재 확인·AssetsFolder.Find가 같은 경로를 사용합니다.
  ID가 중복되고 표기도 일치하지 않으면 임의의 폴더를 고르지 않습니다.
  기존 폴더 이름·편집 메타데이터·원본 자료를 변경하거나 삭제하지 않습니다.
- 첫 IO 오류에는 목적지 폴더 존재 여부와 UTF-8 바이트를 기록합니다.

304개 경고는 원본이 없다는 증거가 아니었습니다. 실제 로그의 304개는 모두
characters의 한글 폴더를 생성하거나 추출 파일을 쓰는 단계의 IO 오류입니다
(DirectoryNotFound 179개, FileNotFound 125개). 디스크 폴더의 실제 이름이 로그에
없어 유니코드 표기 차이가 실기의 최종 원인인지는 아직 확정할 수 없습니다.
이번 경로 대응은 그 경우를 처리하며, 같은 오류가 남으면 새 경로 진단으로 구분합니다.

## 검증 범위

실제 수정한 셰이더 함수의 UTF-8 바이트 순서·한글·빈 문자열·null을 비교하고,
실제 패치된 OriginalAssets.Extract 및 AssetsFolder.Find에 NFC 목록/NFD 폴더를
주어 추출과 읽기·메타데이터 보존을 검사합니다. 고유 ID 대응과 중복 거부도 확인합니다.
실제 PC 게임 DLL의 패치·로드·SDK9 타입/메서드 검사, 기존 오디오·파일·PC 준비
회귀 검사를 수행합니다. 게임 타이틀·GPU 셰이더 실행·전투·영상·저장/불러오기는
이 환경에서 실기 검증할 수 없으므로 이번 버전의 스위치 실행 결과가 필요합니다.

## 기본 개선: PC 작업 없이 적용

1. 플랫폼 패치 뒤 원본 검증 함수를 직접 호출합니다. 호환성 검사와 남은 P/Invoke 검사는
   유지하고, 두 번째 Retarget/RedirectCalls/RewritePInvokes·DLL 저장·복사를 없앴습니다.
2. X 입력 시 이미 로딩한 게임 DLL을 재사용합니다. 원본 DLL SHA-256은 다시 확인하지만
   전체 캐시 검사와 로딩을 다시 반복하지 않습니다.
3. 원본 자료 준비는 저장된 assets 파일 목록을 한 번 읽어 메모리에서 존재 여부를 확인합니다.
   추출 성공·삭제를 목록에 반영하고 준비를 다시 시작할 때 새로 읽습니다. 목록을 읽지
   못하면 기존 파일 검사로 돌아갑니다. 304개 경고를 숨기거나 누락 자료를 있다고 처리하지 않습니다.
4. 원본 자료의 중간 진행 로그는 최대 0.5초에 한 번 기록합니다. 첫 기록·완료·오류·경고는
   보존합니다. 화면 진행 처리와 자료 추출 자체를 건너뛰지는 않습니다.

## 설치

ZIP의 switch/WogSwitchProbe 전체를 SD카드 같은 위치에 덮어씁니다. NRO·managed·probe.ini를
함께 갱신하고 기존 release, original, save, home, launcher.ini는 유지합니다.
full application / title takeover로 실행해 0.5.8 표시를 확인합니다.
새 버전은 패치 캐시가 달라 처음 한 번 준비가 필요합니다. 아래 PC 준비를 사용하면
스위치에서 그 DLL 변환을 수행하는 부담을 줄일 수 있습니다.

## 선택 기능: PC에서 SD카드를 미리 준비

Windows의 Microsoft .NET 10 Runtime이 필요합니다. SDK 설치는 필요하지 않습니다.
공식 설치 안내: https://dotnet.microsoft.com/download/dotnet/10.0

1. 위 설치대로 SD카드의 실행기를 먼저 갱신하고 원본 게임 자료를 제자리에 둡니다.
2. PC에 SD카드를 연결하고 ZIP에서 푼 pc-tools/prepare-sd.cmd를 실행합니다.
3. 앱 폴더의 PC 경로를 입력합니다. 예: `E:\switch\WogSwitchProbe`.
4. PC PREPARE COMPLETE가 나오면 SD카드를 안전하게 꺼내 스위치에서 실행합니다.

직접 명령으로 실행할 수도 있습니다.

```text
dotnet pc-tools/runtime/WogSwitch.Managed.dll --prepare-sd "E:\switch\WogSwitchProbe"
```

이 도구는 같은 버전의 관리 코드인지 확인하고, DLL 패치·검사 캐시를 SD카드에 만듭니다.
원본 파트2와 release/assets가 있고 save/assets가 아직 비어 있을 때는 자료도 미리 추출합니다.
기존 save/assets가 있으면 사용자 편집 자료와 캐시를 보존하기 위해 PC 추출을 건너뜁니다.
원본 게임 DLL이나 원본 폴더에는 쓰지 않으며 게임 Main·GPU·오디오를 실행하지 않습니다.
원본이 일부 없으면 그대로 기록합니다. 도구가 원본 자료를 다운로드하거나 추가하지 않습니다.

PC 준비 후에도 스위치에서는 원본·캐시 검사, 일부 배포 메타데이터 반영과 원본 자료 목록
확인을 수행할 수 있습니다. 처음 준비 시간을 완전히 없애는 기능은 아닙니다.
기존 캐시가 있는 PC 보고서의 unavailable=-1은 원본을 다시 검사하지 않았다는 뜻입니다.

## 검증과 속도 측정 범위

실제 PC판 DLL로 새 검사가 기존 전체 패치 검사의 정상 결과와 90개 오류 결과에 모두
일치하는지 확인했습니다. 새 검사는 DLL 바이트를 변경하지 않습니다.
합성 시험에서는 3238번의 파일 존재 확인을 한 번의 폴더 목록으로 처리하고,
추출·삭제·다음 준비 시 갱신을 검사했습니다. 빠른 중간 진행 3239개는 8개로 합쳤으며
마지막 기록과 오류·경고를 보존했습니다.

Windows와 Linux에서 실제 배포 DLL만 둔 PC 도구를 실행하고, SD 폴더를 다른 위치로
복사한 뒤에도 캐시가 재사용되고 기존 편집 파일·원본 DLL이 유지되는지 확인했습니다.
오디오·자료 오류 진단·Main 안내 후 취소 복귀와 SDK9 API 검사도 유지합니다.

reference/verification-timing.json은 Linux .NET 환경에서 한 번 측정한 재검사 단계입니다.
해당 측정의 기존 전체 재패치 682ms, 새 읽기 전용 검사 32ms는 스위치 측정값이나
전체 시작 시간의 배수가 아닙니다. 스위치에서의 실제 단축 시간은 같은 SD카드로 비교해야 합니다.

## 결과를 확인할 파일

기존 probe.log, runtime.log, game-start.txt, asset-report.txt를 사용합니다.
PC 도구 결과는 pc-prepare-report.txt에 남습니다. 같은 경고가 남으면 새로 실행한 뒤의
asset-report.txt, game-start.txt, probe.log, runtime.log를 확인합니다. ASSET PATH alias가
있으면 기존 캐릭터 폴더 이름을 대응한 것입니다. 타이틀·음향·전투·영상·저장/불러오기는
실기에서 계속 확인해야 합니다. reference/desktop-smoke.log는 PC 빌드 검사 기록입니다.
