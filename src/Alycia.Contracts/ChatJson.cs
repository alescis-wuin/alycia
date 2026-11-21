using System.Text.Json;

namespace Alycia.Contracts.Chat;

public static class ChatJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
