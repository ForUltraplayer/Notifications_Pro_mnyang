// @file SoundRule.cs
// @author: rudals252
// @brief 기본·앱별 WAV 규칙 모델(사용 여부, 선택 방식, 파일 목록, 연속 중복 방지, 명시적 무음). 이번 단계는 모델·직렬화·이관만 다룬다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

namespace NotificationsPro.Models;

/// <summary>
/// Sound rule for the default sound or one app. Intended meaning (playback is implemented in P2):
/// - App rule absent or <see cref="Enabled"/> = false: inherit the default rule.
/// - <see cref="ExplicitSilence"/> = true: this app is silent and never falls back to the default.
/// - Empty <see cref="Files"/>: silent (an empty Single list plays nothing).
/// Files hold managed assets as portable relative paths ("sounds/x.wav"), trusted Windows
/// sounds as absolute paths, and files directly inside a local
/// <see cref="AppSettings.SoundLibraryFolders"/> entry as absolute paths.
/// </summary>
public class SoundRule
{
    public bool Enabled { get; set; } = true;
    public SoundSelectionMode Mode { get; set; } = SoundSelectionMode.Single;
    public List<string> Files { get; set; } = new();
    public bool AvoidRepeat { get; set; }
    public bool ExplicitSilence { get; set; }

    public static SoundRule CreateSingle(string? file)
    {
        var rule = new SoundRule { Mode = SoundSelectionMode.Single };
        if (!string.IsNullOrWhiteSpace(file))
            rule.Files.Add(file);
        return rule;
    }

    public SoundRule Clone()
    {
        return new SoundRule
        {
            Enabled = Enabled,
            Mode = Mode,
            Files = new List<string>(Files ?? new List<string>()),
            AvoidRepeat = AvoidRepeat,
            ExplicitSilence = ExplicitSilence,
        };
    }
}
