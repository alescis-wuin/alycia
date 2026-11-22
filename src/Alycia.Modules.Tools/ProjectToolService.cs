using Alycia.Contracts.Indexing;
using Alycia.Contracts.Tools;
using static Alycia.Contracts.Tools.ToolArguments;
using Alycia.Contracts.Documents;
using System.Text;
using System.Text.Json;

namespace Alycia.Modules.Tools;

public sealed partial class ProjectToolService(IPassageScoring scoring, IAuthorizedToolSources sources, ISourceCommands commands, IToolConversationPolicy policy, IToolHistoryReader history, IProjectIndex index, IIndexedEmbeddings? semantic = null) : ISnapshotProjectTools
{
    public async Task<bool> HasSourcesAsync(Guid id, CancellationToken ct) =>
        await sources.HasFilesAsync(id, ct) || await policy.HasHistoryAsync(id, ct);

    public async Task<ProjectToolResult> CallAsync(Guid id, string name, JsonElement arguments, int maxResultBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (arguments.ValueKind != JsonValueKind.Undefined) arguments = arguments.Clone();
        try
        {
            var conversation = await policy.ReadAsync(id, ct) ?? throw new ConversationNotFoundException();
            if (!conversation.ToolsEnabled)
                return new(Json(new { error = "Les outils sont désactivés dans cette conversation." }), true) { Refusal = ToolRefusal.ForbiddenScope };
            Validate(name, arguments, maxResultBytes);
            return name switch
            {
                "project_read_many" => await ReadManyAsync(id, arguments, maxResultBytes, ct),
                "history_search" or "history_read" => await new HistoryTools(scoring, history, semantic,
                    conversation.HistoryEmbedding).CallAsync(id, name, arguments, maxResultBytes, ct),
                "project_search_passages" or "project_find_symbols" or "project_find_references" => await ExploreAsync(id, name, arguments, maxResultBytes, ct),
                _ => await commands.ExecuteAsync(id, name, arguments, maxResultBytes, ct)
            };
        }
        catch (ArgumentException e) { return new(Json(new { error = e.Message }), true) { Refusal = ToolRefusal.InvalidArguments }; }
    }

}
