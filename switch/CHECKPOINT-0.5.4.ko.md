# WogSwitch 0.5.4 — X 시작 시 네이티브 로그 출력 충돌 수정

0.5.3 실기에서 DLL 두 개의 패치·호환성 재검사·로딩이 완료됐습니다.
X 입력 후 DuelDx.Program.Main을 호출한 직후 네이티브 충돌이 발생했습니다.
충돌 보고서의 모듈 Build ID는 배포한 0.5.3 ELF와 정확히 일치합니다.
같은 ELF로 주소를 해석한 호출 경로는 다음과 같습니다.

```
SystemNative_SysLog -> printf -> _write_r -> consolePrintChar
-> ConsoleSwRenderer_drawChar (해제된 콘솔의 NULL 프레임버퍼)
```

그래픽 전환 때 텍스트 콘솔을 해제한 뒤, SDK의 SysLog가 printf로
그 콘솔에 다시 출력했습니다. Mono 로그 핸들러와 wog_log의 그래픽 상태 검사만으로는
이 직접 출력 경로를 보호할 수 없었습니다.

0.5.4는 고정된 NroForge SDK의 파일 출력 기능으로 stdout/stderr를 runtime.log에
연결합니다. 이전 probe.ini가 남아 있어도 네이티브 시작 코드가 연결 상태를 검사하고
보완합니다. 안전한 출력 연결 없이는 EGL로 전환하지 않습니다.

game-start.txt도 게임 호출 전에 만들고 각 기록을 즉시 파일에 추가합니다.
이전 버전은 finally에서만 파일을 저장해 네이티브 충돌 때 기록이 남지 않았습니다.
이제 Main 호출 직전 기록을 먼저 닫아 저장한 뒤 로그 콜백과 게임 코드를 실행합니다.

## 설치와 확인

1. ZIP의 switch/WogSwitchProbe 폴더 전체를 SD카드 같은 위치에 덮어씁니다.
   **NRO, managed 폴더와 probe.ini를 함께 갱신**합니다.
2. 기존 release, original, save, launcher.ini를 계속 사용합니다.
3. full application / title takeover 방식으로 실행하고 0.5.4 표시를 확인합니다.
4. DLL 패치·로딩 확인 완료 후 X로 시작을 시도합니다.
5. 결과 확인에 필요한 파일은 같은 폴더의 probe.log, runtime.log, game-start.txt,
   load-report.txt입니다. 시스템 오류가 재발하면 최신 Atmosphère 충돌 보고서도 확인합니다.

runtime.log는 각 실행의 `--- starting log ---` 구분과 함께 누적됩니다.
NRO만 교체하면 관리 코드의 기록 개선은 적용되지 않습니다.

## 검증 범위와 남은 작업

SDK의 실제 stdio 출력 함수와 콜백을 추출한 Linux 시험에서 콘솔 해제 뒤의 기존
출력 경로는 SIGSEGV를 재현합니다. 파일로 연결한 같은 출력은 stdout/stderr 기록,
즉시 종료 시 기록 보존과 다음 실행의 추가 기록을 통과합니다.
실제 PC판 v1.0.86 DLL 패치·로딩·Main의 원본 폴더 안내 후 취소 복귀와,
Main 호출 전 game-start.txt 저장 여부도 검사합니다.
실행기·패처·shim·게임 DLL 두 개는 고정된 SDK9 메서드 참조 검사 대상입니다.
구체적인 결과는 verification.json과 reference의 시험 기록에 있습니다.

0.5.4의 스위치 실기 결과는 아직 받지 않았습니다. 이 수정이 타이틀 화면 진입까지
보장하지는 않습니다. 실제 원본 자료로 타이틀·전투·저장/불러오기·성능 검증이 필요하며,
PCM 음향 출력도 미구현입니다. 사용자 원본 로그와 PC 게임 DLL은 이 패키지에 포함하지 않습니다.
