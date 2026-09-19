using System.Globalization;
using Alicia.Application.Generations;

namespace Alicia.Presentation.ViewModels;

public sealed class GenerationProfileRevisionItemViewModel
{
    internal GenerationProfileRevisionItemViewModel(
        GenerationProfileRevision revision,
        int revisionNumber,
        bool isCurrent)
    {
        ArgumentNullException.ThrowIfNull(revision);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revisionNumber);

        Revision = revision;
        RevisionNumber = revisionNumber;
        IsCurrent = isCurrent;
    }

    internal GenerationProfileRevision Revision { get; }

    public GenerationProfileRevisionId Id => Revision.Id;

    public int RevisionNumber { get; }

    public bool IsCurrent { get; }

    public string RevisionLabel => IsCurrent
        ? $"Revision {RevisionNumber} • Current"
        : $"Revision {RevisionNumber}";

    public string CreatedAtText => Revision.CreatedAtUtc
        .ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public string ProfileName => Revision.Profile.Name;

    public string PayloadHashText => $"SHA-256 {Revision.PayloadHash[..12]}…";
}
