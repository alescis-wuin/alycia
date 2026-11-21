using System.Net;
using System.Text;
using System.Text.Json;
using Alycia.Api.Chat;
using Alycia.Modules.Chat.Application;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;

namespace Alycia.Modules.Chat.Tests;

public sealed class ProjectMcpClientTests
{
    [Theory]
    [InlineData("http://0.0.0.0:5020", "127.0.0.1", true)]
    [InlineData("http://localhost:5020", "localhost", false)]
    [InlineData("http://[::1]:5020", "[::1]", false)]
    [InlineData("http://[::]:5020", "[::1]", false)]
    public async Task NegotiatesOnceUsesLocalAddressAndPreservesEvidenceAndRevocation(string address, string host, bool container)
    {
        using var handler = new Replies(); using var http = new HttpClient(handler); using var server = new Listener(address);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ALYCIA_CHAT_CONTAINER_MODE"] = container.ToString(), ["ALYCIA_CHAT_ACCESS_TOKEN"] = new string('a', 64) }).Build();
        var client = new ProjectMcpClient(http, server, config, null!); // Only the transport is exercised here.
        var conversation = Guid.NewGuid();
        var args = JsonSerializer.SerializeToElement(new { fileId = Guid.NewGuid(), offset = 200001 });
        var first = await client.CallAsync(conversation, "project_read", args, 2048, default);
        var second = await client.CallAsync(conversation, "project_read", args, 2048, default);
        Assert.False(first.IsError); Assert.True(second.IsError);
        Assert.Equal("src/A.cs", first.Evidence!.Path); Assert.Equal(200001, first.Evidence.Offset);
        Assert.Equal(new[] { "initialize", "notifications/initialized", "tools/list", "tools/call", "tools/call" }, handler.Methods);
        Assert.All(handler.Seen, r =>
        {
            Assert.Equal(host, r.Uri.Host); Assert.Equal($"/api/chat/conversations/{conversation}/mcp", r.Uri.AbsolutePath);
            Assert.Equal(container ? "Bearer " + new string('a', 64) : null, r.Auth);
            Assert.Contains("application/json", r.Accept); Assert.Contains("text/event-stream", r.Accept);
            Assert.Equal("2025-11-25", r.Version);
        });
        Assert.Equal(2048, handler.Budget);
    }
    [Fact]
    public async Task WrongRpcIdPreventsExecution()
    {
        using var handler = new Replies { WrongId = true }; using var http = new HttpClient(handler);
        using var server = new Listener("http://127.0.0.1:5020");
        var client = new ProjectMcpClient(http, server, new ConfigurationBuilder().Build(), null!);
        await Assert.ThrowsAsync<ChatModelException>(() => client.CallAsync(Guid.NewGuid(), "project_list", JsonSerializer.SerializeToElement(new { }), 2048, default));
        Assert.Equal(new[] { "initialize" }, handler.Methods);
    }
    private sealed class Listener : IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();
        public Listener(string url) { var f = new ServerAddressesFeature(); f.Addresses.Add(url); Features.Set<IServerAddressesFeature>(f); }
        public void Dispose() { }
        public Task StartAsync<TContext>(IHttpApplication<TContext> app, CancellationToken ct) where TContext : notnull => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
    private sealed record Request(Uri Uri, string? Auth, string Accept, string Version);
    private sealed class Replies : HttpMessageHandler
    {
        public bool WrongId { get; init; }
        public List<string> Methods { get; } = [];
        public List<Request> Seen { get; } = [];
        public int Budget;
        private int _calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add(new(request.RequestUri!, request.Headers.Authorization?.ToString(), request.Headers.Accept.ToString(), request.Headers.GetValues("MCP-Protocol-Version").Single()));
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = document.RootElement; var method = root.GetProperty("method").GetString()!; Methods.Add(method);
            if (method == "notifications/initialized") return new(HttpStatusCode.Accepted);
            object result;
            if (method == "initialize") result = new { protocolVersion = "2025-11-25" };
            else if (method == "tools/list") result = new { tools = ProjectToolCatalog.All.Select(t => new { name = t.Name }) };
            else
            {
                Budget = root.GetProperty("params").GetProperty("_meta").GetProperty("io.alycia/maxResultBytes").GetInt32();
                result = new
                {
                    content = new[] { new { type = "text", text = "return 42;" } },
                    isError = ++_calls > 1,
                    _meta = new Dictionary<string, object> { ["io.alycia/evidence"] = new ToolEvidence("src/A.cs", new string('a', 64), 200001, null, true) }
                };
            }
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                { jsonrpc = "2.0", id = root.GetProperty("id").GetInt32() + (WrongId ? 1 : 0), result }, ProjectToolCatalog.Json), Encoding.UTF8, "application/json")
            };
        }
    }
}
