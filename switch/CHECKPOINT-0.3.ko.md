# WogSwitch 0.3 이미지·한글 확인

SkiaSharp 3.119의 실제 C API를 devkitA64로 정적 빌드하고 Mono의 네이티브
라이브러리 조회에 연결한 중간 확인판입니다. CPU에서 한글과 이미지를 그린 뒤
RGBA 비트맵을 스위치 GPU 텍스처로 올려 표시합니다.
게임 시작, 게임 자료 로딩, 음향·저장 기능은 다음 단계입니다.

## 설치와 확인

1. ZIP을 풀어 `switch/WogSwitchProbe` 폴더 전체를 SD카드의 같은 위치에 덮어씁니다.
   NRO뿐 아니라 `managed`, `assets`, 런타임 파일도 함께 복사해야 합니다.
2. 0.2를 실행했던 full application / title takeover 방식으로 실행합니다.
3. `창세기전 스위치 포팅`, `한글 이미지 렌더링 테스트`와 흰색·초록색 체크 이미지가
   함께 보이는지 확인합니다. A를 누르는 동안 위쪽 배너가 빨간색으로 바뀝니다.
4. PLUS로 종료한 뒤 `switch/WogSwitchProbe/probe.log`와 `render.png`를 전달합니다.
   `render.png`는 최초 한글·이미지 합성 결과입니다. 로그는 실행할 때 덮어씁니다.

이번 확인에 원본 게임 자료는 필요 없습니다. 0.3 실기 성공 여부는 실행 결과를 받아야
확정할 수 있습니다. Linux 검사와 스위치용 컴파일 성공은 실기 검증과 구분합니다.

## 검증 내용

- 명시적으로 동봉한 NanumGothic 글꼴에서 검사 문장의 한글 글리프를 찾습니다.
- 실제 한글 래스터 영역의 배경 외 픽셀 수를 세어 빈 결과를 거부합니다.
- 체크 이미지를 PNG로 인코딩하고 디코딩하여 크기와 픽셀을 비교합니다.
- 전체 화면을 PNG로 저장합니다.
- GPU 텍스처 출력 후 프레임버퍼의 샘플 픽셀을 CPU 원본과 비교합니다.
- 기존 0.2의 원본 Wog DLL 메서드 실행, Mono.Cecil 읽기, EGL과 A 입력 검사를 유지합니다.

## 구현 범위와 재빌드

Linux용 `libSkiaSharp.so`는 SD 패키지에 넣지 않습니다. Skia 커밋
`30abf4a0dcd3006f3a555e46906151e486f87b6e`의 CPU 렌더러, FreeType 및 이미지 코덱을
사용합니다. Horizon에 없는 mmap은 Skia의 파일 읽기 대체 경로를 사용하고,
동적 OS 로더와 Linux ARM 기능 조회는 빌드에서 제외합니다. Skia Ganesh/Graphite
GPU 컨텍스트는 비활성화했습니다. 실제 게임의 GPU 사용 경로는 별도 통합 대상입니다.
폰트와 라이브러리의 라이선스는 패키지에 포함합니다.

```sh
python3 tools/build_skia_native.py --work work/skia --output output/skia-native
python3 tools/build_switch_probe.py \
  --nroforge /workspace/NroForge \
  --devkitpro /workspace/setup-tools/root/opt/devkitpro \
  --managed /workspace/twgios/build/managed-0.3 \
  --skia /workspace/twgios/build/skia-native-0.3/output/skia-native \
  --output /workspace/twgios/build/switch-probe-0.3
```

첫 명령은 `.github/workflows/wogswitch-skia.yml`의 고정 devkitPro 컨테이너에서
실행합니다. 관리 코드 빌드는 `.github/workflows/wogswitch-managed.yml`을 사용합니다.

## 빌드 검사 결과

- Linux 관리 코드 실행: 원본 Wog 메서드·Mono.Cecil·한글·PNG 검사 통과.
- 실제 한글 래스터 영역에서 배경 외 픽셀 3,157개 확인.
- 동일 화면 셰이더로 GLES 3와 GL 3에서 1280×720 전체 RGBA 픽셀 일치 확인.
- devkitA64 Skia 네이티브 C API 내보내기 921개, 관리 DLL의 Skia 호출 877개 모두 존재.
- 관리 체크포인트의 네이티브 호출 13개 모두 C 조회표에 등록.
- 스위치 ELF 전체 링크 성공, 미해결 심볼 없음. NRO/NACP/RomFS 형식 검사 통과.
- 스위치 실기 렌더링 검증은 사용자 실행 결과 대기.
