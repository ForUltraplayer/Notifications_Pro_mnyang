// @file WavHeaderStatus.cs
// @author: rudals252
// @brief WAV 헤더 검사 상태 값(정상, 없음, 빈 파일, RIFF/WAVE 아님, fmt 없음, 오디오 데이터 없음, 미지원 형식, 읽기 오류).
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

public enum WavHeaderStatus
{
    Valid = 0,
    Missing = 1,
    Empty = 2,
    NotRiffWave = 3,
    NoFormatChunk = 4,
    NoAudioData = 5,
    UnsupportedFormat = 6,
    ReadError = 7,
}
