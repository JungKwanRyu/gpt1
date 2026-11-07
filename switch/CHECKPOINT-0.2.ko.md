# WogSwitch 0.2 실행 확인

이 버전은 **핵심 DLL 코드 실행 + GPU 출력 + 컨트롤러 연결**을 확인하는 중간 빌드입니다.
창세기전 게임 플레이, 원본 자료 로딩, 한글 글꼴 렌더링, 음향, 세이브는 아직 구현하지 않았습니다.

## 실행 방법

1. ZIP을 풀고 `switch/WogSwitchProbe` 폴더 전체를 SD카드의 같은 위치에 덮어씁니다.
   0.1과 같은 앱 경로이며 이전 `probe.log`는 실행할 때 새 로그로 바뀝니다.
2. 기존 홈브루 환경에서 full application / title takeover 모드로 실행합니다.
   앨범 applet 모드는 지원하지 않습니다.
3. 성공하면 파란색 계열 화면이 나타납니다. **A를 누르면 붉은색으로 변하는지** 확인합니다.
4. PLUS로 빠져나온 뒤 SD카드의 `switch/WogSwitchProbe/probe.log`를 전달합니다.

실패하면 마지막 성공 단계와 오류를 파일에 남깁니다. 원본 게임 자료는 이번에도 필요 없습니다.

## 로그에서 확인할 항목

- `PASS WogPatcher method execution`: 원본 DLL의 생성자·속성·레코드 비교 실행.
- `PASS WogShim method execution`: 원본 Host.Use/Host.Current 실행.
- `PASS Mono.Cecil 0.11.6`: 패치 도구가 원본 DLL을 실제로 읽음.
- `PASS EGL ...`: 스위치 GPU 컨텍스트 생성.
- `PASS WogShim GlLoader.Load`: 관리 코드와 네이티브 GPU 함수 포인터 연결.
- `PASS controller input`: Switch A 입력을 WogShim의 XInput 상태로 전달.

`PASS`의 의미는 해당 로그 항목에 한정됩니다. 게임 구동 성공이나 SkiaSharp의
스위치 네이티브 지원 성공을 의미하지 않습니다. 새 버전의 스위치 실기 결과는 아직 없습니다.

## 빌드에서 완료한 검사

- .NET 10.0.403 SDK로 관리 코드 빌드.
- Linux에서 원본 Wog DLL 메서드 실행, Mono.Cecil 읽기 및 SkiaSharp 픽셀 검사 통과.
- Mono 9 기반 스위치 런타임 + Mesa EGL/GL 브리지 네이티브 컴파일·링크.
- 관리 코드의 네이티브 호출 11개와 C의 내보내기 이름 일치 확인.
- ELF64 AArch64 / NRO / NACP / RomFS 검사, 패키지 파일 SHA-256 검증.

Linux에서 성공한 SkiaSharp 픽셀 검사는 Linux 네이티브 라이브러리를 사용한 것입니다.
그 Linux 라이브러리를 스위치에 넣지 않았으며, SkiaSharp의 스위치용 네이티브 렌더링은 남은 작업입니다.

## 소스와 재빌드

관리 코드: `switch/managed/`. 스위치 네이티브 진입점: `switch/runtime/main.c`.
GitHub Actions의 관리 코드 빌드는 원본 저장소의 고정 커밋에서 DLL을 읽고 해시를 검증합니다.
권한은 `contents: read`이며 비공개 릴리스 읽기를 위한 권한 확대는 사용하지 않습니다.

```sh
python3 tools/build_switch_probe.py \
  --nroforge /workspace/NroForge \
  --devkitpro /workspace/setup-tools/root/opt/devkitpro \
  --managed /workspace/twgios/build/managed-0.2 \
  --output /workspace/twgios/build/switch-probe-0.2
```

관리 코드의 `--desktop-smoke`는 Linux용 검사이며, 스위치에서는 인자 없이 실행합니다.
