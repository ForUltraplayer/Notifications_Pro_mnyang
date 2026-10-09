// @file SoundSelectionMode.cs
// @author: rudals252
// @brief 소리 규칙의 파일 선택 방식(단일/랜덤/순차) 저장 값. 재생 로직은 P2에서 구현한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Text.Json.Serialization;
using NotificationsPro.Helpers;

namespace NotificationsPro.Models;

/// <summary>How a <see cref="SoundRule"/> picks a file. Stored as the member name; Single is the fallback.</summary>
[JsonConverter(typeof(SafeEnumJsonConverter<SoundSelectionMode>))]
public enum SoundSelectionMode
{
    Single = 0,
    Random = 1,
    Sequential = 2,
}
