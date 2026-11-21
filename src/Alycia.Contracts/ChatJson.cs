using System.Text.Json;

namespace Alycia.Contracts.Chat;

public static class ChatJson
{
    public static readonly JsonSerializerOptions Archive = new(JsonSerializerDefaults.Web) { WriteIndented = true, MaxDepth = 16 };
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
