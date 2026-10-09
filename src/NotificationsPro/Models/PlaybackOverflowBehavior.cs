// @file PlaybackOverflowBehavior.cs
// @author: rudals252
// @brief 재생 중 새 알림음이 왔을 때 대기열에 넣을지 건너뛸지 정하는 저장 값. 동작은 P2에서 구현한다.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Text.Json.Serialization;
using NotificationsPro.Helpers;

namespace NotificationsPro.Models;

/// <summary>What to do with a new sound while another one is playing. Stored as the member name; Queue is the fallback.</summary>
[JsonConverter(typeof(SafeEnumJsonConverter<PlaybackOverflowBehavior>))]
public enum PlaybackOverflowBehavior
{
    Queue = 0,
    Skip = 1,
}
