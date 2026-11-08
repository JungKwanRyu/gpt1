# 스위치 홈브루 이식: 초기 검증 기록

검토일: 2026-11-07
원본 커밋: 573576b96fda40ee88a6a2b384de6d87743b73a7

## 현재 확인된 사실

- 로컬에 원본 저장소를 확보했다. 원본 코드와 DLL은 수정하지 않았다.
- WogiOS.csproj는 net10.0-ios, ios-arm64, UseInterpreter=true를 사용한다.
- GameRunner는 Mono.Cecil 기반 패치 후 AssemblyLoadContext로 게임 DLL을 불러와 Main을 호출한다.
- WogShim.dll과 WogPatcher.dll의 문자열에서 .NETCoreApp,Version=v10.0을 확인했다. 이는 정식 메타데이터/API 호환성 검증을 대체하지 않는다.
- 두 라이브러리의 소스는 이 저장소에 포함되지 않는다.
- 화면은 IosHost/IHost와 OpenGL ES, 음향은 AudioOut/IAudioSink와 AVAudioEngine을 사용한다.
- 패드 입력은 WogShim.Win32.XInput.Poll에 연결되므로 플랫폼 입력을 교체할 경계가 있다.
- 초기에는 작업 공간의 devkitA64/libnx가 PATH에 등록되지 않았으나, 이후 절대 경로를 사용해 진단 NRO 컴파일·링크에 성공했다.
- 현재 PATH에서 dotnet 실행 파일을 찾지 못했다. 전체 파일 시스템에 SDK가 없다는 뜻은 아니다.

## 새로 찾은 빌드 후보

https://github.com/BennettNotFound/NroForge

README에 따르면 .NET 9 애플리케이션을 Mono LLVM Full AOT + libnx로 NRO로 만드는 도구다. 문서는 이 도구의 새 runner를 실기로 재검증하지 않았다고 명시한다. twgios나 그 핵심 DLL을 지원한다는 증거는 없다.

현재 프로젝트는 .NET 10 + 실행 중 DLL 패치/로딩을 사용하므로 직접 호환된다고 판단할 수 없다. 기존 DLL의 타깃 문자열만 9로 바꾸는 방식은 적절한 호환성 해결책이 아니다.

## 구현 전에 통과해야 할 검증

1. 후보 툴체인의 최소 C# 예제를 빌드하여 NRO 생성 경로를 검증한다.
2. WogShim, WogPatcher와 게임 DLL의 실제 어셈블리 참조 및 네이티브 의존성을 추출한다.
3. .NET 10 실행 환경을 확보하거나, 소스를 통해 호환 대상 프레임워크로 재빌드할 경로를 확인한다.
4. Full AOT 경로를 쓴다면 게임 버전을 고정하고 PC에서 먼저 패치한 DLL 전체를 빌드 입력에 포함하는 방식을 시험한다. 리플렉션 및 제네릭 코드가 실제로 동작하는지도 별도로 확인한다.
5. 런타임 검증 후 SwitchHost, 그래픽/SkiaSharp, 오디오, 입력, SD카드 저장 경로를 구현한다.
6. 실기에서 타이틀, 한글, 전투, 음성/BGM, 저장/불러오기를 검증한다.

## 일정 판단

기존의 개발자 1명 기준 주/월 추정치를 AI 작업의 실제 소요 시간으로 환산할 근거는 없다. NRO 빌드와 기본 런타임 진단은 통과했지만 게임 API 및 그래픽 의존성 검증 전이므로 완료 시점은 확정하지 않는다. 핵심 DLL의 실제 메서드 실행, 첫 게임 화면, 플레이 검증 결과를 기준으로 남은 작업을 산정한다.

## 후속 구현 결과

`tools/build_switch_probe.py`와 `switch/probe/`를 추가했다. NroForge commit
`e41f2e68b212737da81ddc97fe5c7b881bb125e8`의 SDK를 검증하고 devkitA64 16.1.0으로
진단용 `WogSwitchProbe.nro` 컴파일·링크를 완료했다. ELF64 AArch64,
NRO/NACP/RomFS 형식 검사에 통과했다.

원래 SDK에는 Mono 인터프리터 구현도 포함되어 있다. 따라서 이식 경로를 Full AOT에만
한정할 필요는 없다. 진단 NRO는 INTERP_ONLY로 Mono 초기화, 관리 코드 호출,
WogShim.IHost / WogPatcher.GameLayout 타입 조회를 시도하고 로그를 남긴다.
이후 사용자가 제공한 실행 로그에서 이 네 항목의 PASS와 진단 완료 메시지를 확인했다.
아래 실행 결과 항목을 참고한다.

표준 라이브러리만 사용하는 CLI 메타데이터 분석기로 원본 DLL 두 개가 순수 IL이며
32비트 전용이 아니고, 직접적인 P/Invoke가 없음을 확인했다.
SkiaSharp 3.119.0.0, Mono.Cecil 0.11.6.0 및 System.Runtime 10.0.0.0 참조를 확인했다.
168개 런타임 DLL도 메타데이터 파서로 읽을 수 있었다.

현재 빌드에는 SkiaSharp/Mono.Cecil이 없고 .NET 9 BCL을 사용한다.
게임 전체의 .NET 10 호환성, 그래픽, 음향, 게임 로딩과 성능은 검증되지 않았다.
SDK 다운로드 주소 builds.dotnet.microsoft.com은 현재 환경의 네트워크 정책에서
HTTP CONNECT 403으로 차단됐다. 네트워크 지침에 따라 허용 목록 변경을 요청했다.

실기 시험 방법과 남은 구현은 `switch/README.ko.md`에 기록했다.

## 사용자 제공 실행 로그 결과 — 2026-11-07

원문: `switch/reports/probe-user-2026-11-07.log`.
이 결과는 사용자가 전달한 로그에 근거하며, 내가 직접 기기를 조작해 관찰한 결과는 아니다.

| 검사 | 결과 |
| --- | --- |
| Mono 도메인 초기화 | PASS |
| 관리 코드 실행: Math.Abs(-7) 반환값 7 | PASS |
| 원본 WogShim DLL 로딩 및 IHost 메타데이터 조회 | PASS, 11개 메서드 |
| 원본 WogPatcher DLL 로딩 및 GameLayout 메타데이터 조회 | PASS, 22개 메서드 |

로그는 오류 메시지 없이 `Probe complete`에 도달했다. PLUS로 종료하라는 안내가 마지막이므로
실제 종료/정리 절차 성공까지 증명하지는 않는다.

현재 확인된 것은 기본 관리 코드 실행과 일부 타입 조회다. WogShim/WogPatcher의
실제 메서드 실행, DLL 전체의 .NET 10 API 호환성, 게임 실행, 그래픽, 음향,
저장 및 성능 검증으로 확대 해석하지 않는다. 다음 검증 대상은 이들 실행 경로와
SkiaSharp/Mono.Cecil 의존성, 스위치용 화면·입력·오디오 호스트다.

## 0.2 구현 결과 — 2026-11-07

사용자가 환경을 재게시했다고 알렸으나, 현재 작업의 상태 조회는 여전히
네트워크 revision 1과 기존 허용 목록을 반환했다. SDK 주소의 새 연결도 CONNECT 403이었다.
로컬 정책을 임의 변경하지 않고 GitHub Actions에서 .NET 10.0.403으로 관리 코드를 빌드했다.

초기 CI는 읽기 전용 토큰으로 비공개 릴리스 자료를 읽지 못했다. 토큰을 contents:write로
확대하는 변경은 자동 승인 검토에서 거부됐다. 해당 변경은 적용하지 않았으며,
고정된 공개 원본 커밋에서 DLL을 읽고 SHA-256을 검증하는 읽기 전용 빌드로 수정했다.

Linux 검사에서 아래 항목이 통과했다.

- 원본 WogPatcher의 GameLayout 생성자·속성·복제·동등성 비교.
- 원본 WogShim의 Host.Use / Host.Current 실제 실행.
- Mono.Cecil 0.11.6을 사용한 원본 WogShim DLL 읽기.
- SkiaSharp 3.119.0의 Linux 네이티브 라이브러리를 통한 픽셀 생성·색상 확인.

스위치용으로 EGL ES 3 / GL 3.3 컨텍스트 생성, 색상 출력, 버퍼 교환과 컨트롤러→XInput
브리지를 구현했다. 관리 코드 호출과 C 내보내기 11개가 일치함을 확인했다.
전체 네이티브 NRO 컴파일·링크와 형식/패키지 검사에 통과했다.
이후 사용자가 제공한 로그와 화면 동작 확인에서 아래 실행 항목들이 통과했다.

SkiaSharp 스위치 네이티브 렌더링, 실제 게임 DLL 패치/로딩, 원본 자료,
한글 글꼴, 음향과 저장 기능은 아직 구현·검증하지 않았다.

관리 코드 CI: https://github.com/JungKwanRyu/gpt1/actions/runs/42364486694
소스 체크포인트: https://github.com/JungKwanRyu/gpt1/tree/codex/wogswitch-managed-host
실기 확인 방법: `switch/CHECKPOINT-0.2.ko.md`.

## 0.2 사용자 실행 결과 — 2026-11-07

사용자는 파란 화면에서 A 버튼을 누르면 붉게 바뀌는 동작을 확인했다.
새 로그 `switch/reports/probe-user-0.2-2026-11-07.log`에는 아래 결과가 기록돼 있다.

- 원본 WogPatcher의 GameLayout 생성자·속성·복제·동등성 비교: PASS.
- 원본 WogShim의 Host.Use / Host.Current: PASS.
- Mono.Cecil 0.11.6의 원본 DLL 읽기: PASS.
- EGL 1.4, 1280×720, GLES=1: PASS.
- GPU NV12B, OpenGL ES 3.2 Mesa 26.2.4.
- WogShim GlLoader.Load의 관리 코드→네이티브 함수 포인터 연결: PASS.
- Switch A 입력→WogShim XInput.State: PASS.
- 관리 코드 화면/입력 루프 완료 및 종료 코드 0.

이로써 현재 사용한 Wog DLL의 일부 실제 메서드가 Mono 9 기반 스위치 환경에서
실행됨을 확인했다. DLL 전체의 .NET 10 API 호환성을 보장하는 결과는 아니다.
다음 핵심 의존성은 SkiaSharp의 스위치 네이티브 구현과 글꼴/이미지 렌더링이다.
실제 게임 DLL 실행, 원본 자료 로딩, 음향, 저장은 여전히 확인되지 않았다.


## 0.3 이미지·한글 구현 — 2026-11-07

SkiaSharp 3.119의 고정 Skia 소스를 devkitA64로 정적 빌드했다. CPU 래스터와
FreeType, PNG 등 이미지 코덱을 사용하고 실제 C API 921개를 Mono 라이브러리
조회에 연결했다. 관리 DLL의 Skia 호출 877개와 네이티브 내보내기 이름이 모두 일치한다.
Linux용 네이티브 바이너리를 스위치에 복사하는 방식은 사용하지 않았다.

명시적 NanumGothic 로딩, 한글 글리프·래스터 픽셀 검사, PNG 인코딩·디코딩·저장,
RGBA 비트맵의 GPU 텍스처 출력과 샘플 픽셀 비교를 구현했다. Linux에서 한글·PNG 검사가
통과했고 같은 셰이더의 GLES 3 / GL 3 전체 프레임버퍼가 원본 PNG와 일치했다.
스위치 NRO 컴파일·링크·형식 검사는 통과했다. 스위치 실기 결과는 아직 받지 않았다.

Skia 자체의 Ganesh/Graphite GPU 컨텍스트는 비활성화한 CPU 빌드다. 게임의 실제
Skia/GPU 사용 경로, 게임 자료 로딩·패치·시작, 음향·세이브·성능 검증은 남아 있다.
실행 방법과 범위는 `switch/CHECKPOINT-0.3.ko.md`에 기록했다.

## 0.4 게임 DLL 패치·로딩 구현 — 2026-11-07

공개 PC판 v1.0.86 전체 패키지의 SHA-256을 고정하여 실제 DLL로 호환성 검사를 했다.
GameLauncher.Prepare는 참조 13개와 P/Invoke 82개를 수정했고, 패치한 DLL 두 개를
로드해 DuelDx.Program.Main을 찾았다. Main은 호출하지 않았다. 미지원 항목 90개는
Windows 전용 호출 및 버그 리포트 UI 타입·메서드이며 게임 시작 전에 추가 처리가 필요하다.

스위치용으로 입력 경로 설정, DLL 이름·형식·버전 검사, 원본 읽기 전용 패치,
출력 해시 검증 캐시, 손상된 캐시 보존·복구, 패치본만 허용하는 DLL 조회를 구현했다.
설정과 실패 원인·호환성 보고서를 화면 및 파일에 남긴다. PC판 System.* 런타임을
스위치 BCL 대신 조회하지 않는다. SD 기본 장치를 지정하고 /switch/... 절대 경로를
사용해 Unix 기반 AssemblyLoadContext의 절대 경로 요구를 맞췄다.

Linux에서 같은 로더 소스로 실제 패치·로딩, 캐시 재사용·손상 복구·원본 변경 감지,
혼합 버전과 입력/출력 경로 중첩 거부, 원본 DLL 보존 검사가 통과했다.
공식 PC 패키지에서 필요한 입력 555개만 준비하는 보조 스크립트도 검사했다.
스위치 NRO 링크와 형식 검사는 통과했으며 실기 결과는 아직 받지 않았다.
0.3의 스위치 한글·이미지 실기 결과도 미확인이다.

관리 코드 CI: https://github.com/JungKwanRyu/gpt1/actions/runs/42395927871
실제 게임 파일 CI: https://github.com/JungKwanRyu/gpt1/actions/runs/42395927933
실행 안내: `switch/CHECKPOINT-0.4.ko.md`.

## 0.5 플랫폼 패치와 실제 Main 호출 — 2026-11-07

원본 패처가 v1.0.86에서 보고한 미지원 90개는 Windows 전원/DPI 호출과 버그 리포트
WinForms UI 의존성을 처리한 뒤, 같은 공개 패처로 재검사하여 0개가 됐다.
버그 리포트 창은 호스트 안내 메시지로 대체하고 PC 전용 폴더 검색은 설정 경로로
교체했다. Windows UI의 모든 기능을 구현했다는 의미는 아니다.

Linux에서 실제 게임 Main을 호출해 원본 폴더 선택 안내에 도달했고 취소 후 정상
복귀했다. 직접 P/Invoke가 두 패치 게임 DLL에 남지 않은 것을 확인했다.
버그 리포트 진입점 대체 함수도 실제 호출했다. PC 동봉 assets가 없을 때는 Main
호출 전에 거부한다. 스위치에서는 X로 한 번 시작하고 요청한 안내창을 패드로 조작한다.
종료·오류는 관리 예외로 호스트에 돌려주며 game-start.txt에 호출과 결과를 기록한다.

GLES 3 / GL 3에서 실제 네이티브 화면 코드를 추출·컴파일해 전체 RGBA 픽셀 일치와
안내창 종료 후 그래픽 상태 복원을 검사했다. 스위치 NRO 링크·형식 검사도 통과했다.
이것은 실기나 타이틀·게임 플레이의 성공 판정이 아니다.

PCM 출력은 아직 연결하지 않았다. 원본 자료를 사용한 타이틀·전투, 저장/불러오기,
성능 및 0.3~0.5 스위치 실기 검증은 남아 있다.

관리 코드 CI: https://github.com/JungKwanRyu/gpt1/actions/runs/42406056725
실제 Main 검사 CI: https://github.com/JungKwanRyu/gpt1/actions/runs/42406056693
실행 안내: `switch/CHECKPOINT-0.5.ko.md`.

## 0.5 사용자 실패 로그와 0.5.1 수정 — 2026-11-08

수신 로그 `switch/reports/probe-user-0.5-2026-11-08.log`에서 기본 Wog 호출과
Mono.Cecil 읽기는 성공했으나, 이후 예외 출력 중 System.SR의 NullReferenceException이
발생해 최초 예외가 가려졌다. 그래픽 초기화 단계에는 도달하지 못했다.

실제 SDK9 BCL을 복원해 메서드 참조를 검사한 결과, 설정 파서의 .NET 10 전용
ReadOnlySpan/comparer Contains와 로더·WogPatcher의 배열 Reverse 호출이 존재하지 않았다.
명시적 IEnumerable 호출과 빌드 시 WogPatcher의 호출 한 개 호환 처리를 적용했다.
원본 공개 WogPatcher는 먼저 SHA-256을 검증하고, 변경 후 해시와 차이를 JSON에 남긴다.
WogShim과 사용자가 넣는 PC 게임 DLL 입력은 수정하지 않는다.

예외 출력은 ToString 없이 타입·HResult·메시지·스택·내부 예외를 각각 기록하도록
바꿨고, 오류 출력 자체가 실패해도 내부 원인이 남는 회귀 검사를 추가했다.
0.5.1의 실행기·WogShim·호환 WogPatcher·패치된 게임 DLL 두 개는 SDK9 미해결
메서드 참조 0개로 검사됐다. 실제 Main의 원본 폴더 안내·취소 복귀 검사도 통과했다.
수정판의 실기 재확인은 아직 필요하며 PCM 출력·플레이 검증 범위는 이전과 같다.

관리 코드 CI: https://github.com/JungKwanRyu/gpt1/actions/runs/42429348671
SDK9 및 게임 검사 CI: https://github.com/JungKwanRyu/gpt1/actions/runs/42429348675
설치 안내: `switch/CHECKPOINT-0.5.1.ko.md`.
