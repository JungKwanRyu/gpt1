# WogSwitch 0.5.1 — 초기 실행 실패 수정

사용자 제공 0.5 로그에서 Wog DLL 기본 호출과 Mono.Cecil 읽기까지 통과한 뒤,
예외 문자열 변환의 System.SR 오류가 최초 예외를 가리는 것을 확인했습니다.
추가로 실제 스위치 SDK의 .NET 9 BCL과 메서드 참조를 비교해 아래 결함을 찾았습니다.

- 설정 파서가 .NET 10의 `MemoryExtensions.Contains(ReadOnlySpan, 값, comparer)`를
  호출했습니다. SDK9에 존재하는 명시적 IEnumerable/LINQ 호출로 교체했습니다.
- 로더와 WogPatcher가 .NET 10의 배열용 `Enumerable.Reverse(T[])`를 호출했습니다.
  SDK9의 `Enumerable.Reverse(IEnumerable)`로 연결했습니다.
- 예외 기록은 `Exception.ToString()`을 사용하지 않고 타입·HResult·메시지·스택·
  내부 예외를 각각 기록합니다. 메시지나 스택 출력 자체가 실패해도 내부 예외를 보존합니다.
- 임베디드 런타임의 리소스 키 설정과 단계별 `STEP` 로그를 추가했습니다.

현재 관리 코드, WogShim, 호환 처리한 WogPatcher, 패치한 게임 DLL 두 개의
System.* 메서드 참조가 고정된 SDK9에서 모두 해석되는 것을 CI에서 확인했습니다.
이는 실제 스위치에서 모든 메서드가 정상 실행된다는 뜻은 아닙니다.

## 설치와 재확인

1. ZIP의 `switch/WogSwitchProbe` 폴더 전체를 SD카드의 같은 위치에 덮어씁니다.
   **managed 폴더도 함께 복사해야 합니다.** 이번 수정은 실행기 DLL과 WogPatcher에 포함됩니다.
2. 이미 넣은 `release`, `original`, `save`, `launcher.ini`는 그대로 사용합니다.
   PC판 게임 파일이나 원본 자료를 다시 받거나 옮길 필요는 없습니다.
3. 기존과 같은 full application / title takeover 방식으로 실행합니다.
4. 로딩 확인 화면에 도달하면 X로 시작을 시도합니다. 안내창은 방향키·A·B로 조작합니다.
5. 종료 후 `switch/WogSwitchProbe/probe.log`를 전달합니다.
   생성됐다면 `load-report.txt`, `game-start.txt`도 함께 전달합니다.

로그 첫 줄은 `WogSwitch checkpoint 0.5.1`이어야 하며 관리 코드도 0.5.1을 표시합니다.
실패하면 마지막 STEP과 Exception[0], Exception[1] 등의 원인 기록을 확인합니다.

## 기본 파일 위치

```text
switch/WogSwitchProbe/
├─ WogSwitchProbe.nro
├─ managed/                    ← 이번 ZIP의 전체 폴더
├─ assets/NanumGothic.ttf
├─ release/
│  ├─ WarOfGenesis.dll
│  ├─ WarOfGenesis.Assets.dll
│  └─ assets/                  ← PC판에 동봉된 폴더 전체
└─ original/
   ├─ part2/                   ← TXR/Txr.dat, Chr, Obs 등 원본 전체
   └─ part1/                   ← Chars, Obss, Btls 등 원본 전체
```

경로를 바꾸려면 `launcher.ini.example`을 `launcher.ini`로 복사해 편집합니다.
기존 설정을 덮어쓰지 않도록 SD 배포 폴더에는 예제만 포함합니다.

## 검증과 범위

- 손상된 Message/StackTrace/ToString을 가진 중첩 예외에서도 원인이 유지되는 회귀 검사 통과.
- 실제 SDK9 메서드 참조 검사: 실행기·WogShim·WogPatcher·게임 DLL 두 개 모두 미해결 0개.
- PC판 v1.0.86 패치 및 로딩, 90→0 호환성 검사, 캐시 검사, 실제 Main의 원본 폴더 안내·취소 복귀 통과.
- WogShim은 공개 원본 그대로입니다. WogPatcher는 해시로 검증한 원본에 배열 Reverse
  호출 한 개만 호환 처리했습니다. 원본·변경 후 SHA-256과 변경 내용은
  `runtime-compatibility.json`에 기록합니다. 사용자가 넣은 PC 게임 DLL은 읽기만 합니다.
- PCM 음향 출력과 실제 원본 자료로 타이틀·전투·저장/불러오기·성능 검증은 아직 남아 있습니다.
- 0.5.1 수정판의 스위치 실기 성공 여부는 재실행 로그로 확인해야 합니다.
