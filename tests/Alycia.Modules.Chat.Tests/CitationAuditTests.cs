using Alycia.Modules.Retrieval;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Tests;

public sealed class CitationAuditTests
{
    [Fact]
    public void RecognizesOnlySuppliedPassagesAndDoesNotTreatReferencesAsTruth()
    {
        var id = Guid.NewGuid(); var reference = SourceCoordinates.Reference(id, "hash", 20, 40);
        var source = new SourceExcerpt(id, "src/A.cs", 20, true, "code", reference, 2, 3, "hash", 20, 40);
        var report = CitationAudit.Check($"Affirmation fausse [{reference}] puis [{reference.ToLowerInvariant()}] [S000000000000].", [source], []);
        Assert.Equal("unknown_references", report.Status);
        Assert.Equal([reference], report.Recognized); Assert.Equal(["S000000000000"], report.Unknown);
        Assert.Equal("sources_uncited", CitationAudit.Check("Sans référence", [source], []).Status);
        Assert.Equal("no_sources", CitationAudit.Check("Aucune source", [], []).Status);
        Assert.Equal(20, report.Supplied[0].Start);
    }

    [Fact]
    public void RefusedToolReadsCannotValidateACitation()
    {
        var evidence = new ToolEvidence("src/A.cs", "hash", 0, 30, true, "S123456789ABC", Guid.NewGuid(), 30, 1, 2);
        var denied = new ToolObservation(1, "project_read", "{}", false, 0, evidence);
        Assert.Single(CitationAudit.Check("[S123456789ABC]", [], [denied]).Unknown);
        var valid = CitationAudit.Check("[S123456789ABC]", [], [denied with { Succeeded = true }]);
        Assert.Empty(valid.Unknown); Assert.Single(valid.Recognized);
        Assert.Equal("tool", valid.Supplied[0].Origin);
        var removed = denied with { Succeeded = true, RetainedInFinalPrompt = false };
        var compacted = CitationAudit.Check("[S123456789ABC]", [], [removed]);
        Assert.Single(compacted.Unknown); Assert.Equal("tool_compacted", Assert.Single(compacted.Supplied).Origin);
        Assert.Single(CitationAudit.Check("[S123456789ABC]", [], [removed, denied with { Succeeded = true }]).Recognized);
    }

    [Fact]
    public void ImportedMalformedDiagnosticArraysAreNotTrustedByTheUi()
    {
        Assert.Null(TurnDiagnostics.Read("{\"usage\":{\"calls\":null}}"));
        Assert.Null(TurnDiagnostics.Read("{\"citations\":{\"supplied\":[null]}}"));
        Assert.Null(TurnDiagnostics.Read("{\"retrieval\":{\"omissions\":[],\"classification\":{\"candidates\":null}}}"));
        Assert.NotNull(TurnDiagnostics.Read("{}"));
    }
}
