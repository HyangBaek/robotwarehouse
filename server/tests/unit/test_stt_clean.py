"""음성 인식 실패를 인식 결과처럼 보내지 않는지 (Whisper 가 무음에서 지어내는 문장)."""
from types import SimpleNamespace

from services.stt import INITIAL_PROMPT, clean_transcript, is_non_speech


def test_hallucinations_are_empty():
    for t in ["시청해 주셔서 감사합니다.", "감사합니다", "  구독과 좋아요!  ", "", " . ", INITIAL_PROMPT]:
        assert clean_transcript(t) == ""


def test_real_sentence_kept():
    s = "랙 8줄, 통로 폭 3m, 입하 도크 2개, 출하 도크 1개"
    assert clean_transcript(s) == s
    assert clean_transcript("랙 6줄로 하고 감사합니다") == "랙 6줄로 하고 감사합니다"   # 문장 일부면 그대로


def test_non_speech_segment():
    assert is_non_speech(SimpleNamespace(no_speech_prob=0.9, avg_logprob=-1.5))
    assert not is_non_speech(SimpleNamespace(no_speech_prob=0.9, avg_logprob=-0.3))
    assert not is_non_speech(SimpleNamespace(no_speech_prob=0.1, avg_logprob=-1.5))
