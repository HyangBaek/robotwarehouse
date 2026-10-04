# 출처 및 AI 활용 (별지2 원본)

| 구분 | 자산 | 출처 | 라이선스·이용 조건 | 사용 방식 |
|---|---|---|---|---|
| AI 모델 | LLM: gpt-oss-20b | OpenAI (Ollama `gpt-oss:20b`로 GPU 서버에서 로컬 실행) | Apache 2.0 | Agent 지시 해석 (문장 → 요구사항 JSON, 되묻기 답변 반영). 신청서의 GPT-4 API에서 변경 |
| AI 모델 | Whisper (faster-whisper `small`) | OpenAI Whisper 가중치, SYSTRAN faster-whisper (CTranslate2) | MIT | 음성 인식 (노트북 CPU 로컬 실행). 신청서의 Whisper API에서 변경 |
| AI 모델 | TTS | - | - | 사용 안 함 (FR-30 범위 제외) |
| 알고리즘 | 다중 로봇 경로 계획 | Silver(2005), Li 외(2021, IJCAI·AAAI), Sharon 외(2015), Kuhn(1955) — 상세는 `server/planner/SOURCES.md` | — | 방법 참고, 팀 직접 구현 |
| SDK | Meta XR SDK | Meta | | VR 클라이언트 |
| 데이터 | 파레트·상자 규격 | | | 격자 칸 기본값 |
| 데이터 | 물동량 | | | 시나리오 기본값 |
| 실행 환경 | Ollama | Ollama | MIT | LLM 서빙 |
| 라이브러리 | FastAPI, Uvicorn, Pydantic, httpx, NumPy, SciPy | 각 프로젝트 | MIT / BSD | Agent 서버 |
| 코딩 도구 | Claude (Anthropic) | Anthropic | 이용약관 | 코드 작성·디버깅 보조, 문서 초안 (서버·VR·경로 엔진). AI가 낸 수치는 직접 실행해 확인한 값만 사용 |
