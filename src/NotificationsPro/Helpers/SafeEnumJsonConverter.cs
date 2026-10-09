// @file SafeEnumJsonConverter.cs
// @author: rudals252
// @brief 설정 JSON의 enum 값을 이름 문자열로 저장하고, 알 수 없는 값은 첫 번째(안전한) 멤버로 읽어 설정 전체가 손상 처리되지 않게 하는 변환기.
// Derived from Notifications Pro (https://github.com/lwytch/Notifications-Pro), GPL v3.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotificationsPro.Helpers;

/// <summary>
/// Writes enum values as their (untranslated) member name. Reads names case-insensitively or
/// defined numeric values; anything else falls back to <c>default(TEnum)</c>, so every enum
/// using this converter must declare its safest value first (value 0).
/// </summary>
public sealed class SafeEnumJsonConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
            {
                var text = reader.GetString()?.Trim();
                if (!string.IsNullOrEmpty(text)
                    && !char.IsDigit(text[0])
                    && text[0] != '-'
                    && Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed)
                    && Enum.IsDefined(parsed))
                {
                    return parsed;
                }

                return default;
            }
            case JsonTokenType.Number:
            {
                if (reader.TryGetInt32(out var number))
                {
                    var candidate = (TEnum)Enum.ToObject(typeof(TEnum), number);
                    if (Enum.IsDefined(candidate))
                        return candidate;
                }

                return default;
            }
            default:
                reader.Skip();
                return default;
        }
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(Enum.IsDefined(value) ? value.ToString() : default(TEnum).ToString());
    }
}
