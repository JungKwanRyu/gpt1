# WogSwitch 0.5.5 — 게임 시작 시 홈 폴더 조회 수정과 패치 진행 표시

최근 0.5.4 실기 로그에서 DLL 재패치·로딩과 GPU 검사는 완료됐습니다.
초기 화면은 패치 진행 출력이 파일로만 전환되어 정지처럼 보였습니다.
1분 미만에 종료한 앞선 로그만으로 정지 여부를 판단할 수는 없었습니다.
뒤이어 받은 로그는 X 이후의 실제 실패 원인도 보여 줍니다.

```
Main invoked: yes — DuelDx.Program.Main
Unable to get home directory
PersistedFiles.TryGetHomeDirectoryFromPasswd
WogShim.BclRedirects.GetFolderPath
WarOfGenesis.Assets.OriginalGame..cctor()
```

원본 패처가 Environment.GetFolderPath를 WogShim 호출로 바꾼 뒤,
스위치 플랫폼 패치가 그 호출을 놓쳤습니다. SDK가 스위치에 없는 passwd 조회를
시도하고 디버그 단언 실패로 프로세스를 종료했습니다.

0.5.5는 두 종류의 폴더 조회를 모두 앱 경로로 연결합니다.
ApplicationData/LocalApplicationData/MyDocuments/CommonApplicationData는 save,
UserProfile은 home, Fonts는 앱의 assets입니다. 지원하지 않는 특수 폴더는 빈 경로를
반환하며 OS 조회로 넘기지 않습니다. Mono 초기화 전 HOME과 XDG 경로도
앱의 home 아래로 지정해 BCL 내부의 홈 조회에 대비합니다.

stdout/stderr는 계속 runtime.log에 기록됩니다. 앱이 직접 출력하는 패치 상태만
그래픽 전환 전의 살아 있는 콘솔에 표시합니다. 원본 패치 로직은 유지하면서
DLL별 Retarget, RewritePInvokes, RedirectCalls, Verify 단계와 경과 시간을 표시합니다.
진행률 백분율이나 예상 남은 시간은 계산하지 않습니다.
load-report.txt는 실행 시작부터 새로 기록하므로 이전 성공 보고서가 남지 않습니다.

## 설치와 확인

1. ZIP의 switch/WogSwitchProbe 폴더 전체를 같은 SD카드 위치에 덮어씁니다.
   NRO·managed·probe.ini를 함께 갱신합니다.
2. 기존 release, original, save, launcher.ini는 유지합니다. 새 home 폴더는 자동 생성합니다.
3. full application / title takeover로 실행하고 0.5.5 표시를 확인합니다.
4. 처음에는 캐시를 새로 패치합니다. 화면의 PATCH 단계와 경과 시간을 확인합니다.
   첫 패치가 실제 스위치에서 완료되기까지의 시간은 아직 측정하지 못했습니다.
5. DLL 패치·로딩 완료 후 X로 시작을 시도합니다.
6. 종료 또는 정지 시 같은 폴더의 probe.log, runtime.log, load-report.txt,
   생성됐다면 game-start.txt가 진단 대상입니다.

## 검증과 한계

실제 PC판 v1.0.86 DLL로 패치 단계 콜백, 캐시 생성·복구, 로딩과 Main 안내 후 취소
복귀를 검사합니다. 패치한 게임 DLL 두 개와 실행기에 OS 특수 폴더 조회가 남지
않았는지, SDK9에 없는 메서드 호출이 있는지도 검사합니다.
지원하는 모든 SpecialFolder 값은 앱 내부 경로나 빈 경로를 반환하는지 확인합니다.
현재 실행 보고서가 로깅 콜백 전에 저장되는지 검사합니다.

네이티브 진행 표시는 메인 스레드의 살아 있는 콘솔에서만 동작하며,
EGL 사용 중·콘솔 해제 후·다른 스레드에서는 그리기를 하지 않는지 시험합니다.
구체적인 검사 결과와 CI 번호는 verification.json과 reference 기록을 참고합니다.

0.5.5의 스위치 실기 결과와 실제 원본 자료를 사용한 타이틀·전투·저장/불러오기·성능
검증은 남아 있습니다. PCM 음향 출력도 미구현입니다.
사용자 원본 로그나 PC 게임 DLL은 이 배포 패키지에 포함하지 않습니다.
