using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Alycia.Client.Avalonia;

public sealed record ProjectToolsInfo(bool Enabled, bool ModelSupportsTools, int MaxCalls, string[] Tools, string ProtocolVersion);

public sealed partial class ChatApiClient
{
    public Task<ProjectToolsInfo> ProjectToolsAsync(Guid id, CancellationToken ct) => SendAsync<ProjectToolsInfo>(HttpMethod.Get, $"/conversations/{id}/tools", null, ct);
    public string McpEndpoint(Guid id)
    {
        using var request = CreateRequest(HttpMethod.Post, $"/conversations/{id}/mcp", null);
        return request.RequestUri!.AbsoluteUri;
    }
}
