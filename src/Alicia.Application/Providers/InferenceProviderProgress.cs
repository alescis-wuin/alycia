namespace Alicia.Application.Providers;

public sealed record InferenceProviderProgress
{
    public InferenceProviderProgress(
        string stage,
        string detail,
        double? fraction = null)
    {
        if (string.IsNullOrWhiteSpace(stage))
        {
            throw new ArgumentException("Provider progress stage cannot be empty.", nameof(stage));
        }

        if (string.IsNullOrWhiteSpace(detail))
        {
            throw new ArgumentException("Provider progress detail cannot be empty.", nameof(detail));
        }

        if (fraction is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fraction),
                "Provider progress fraction must be between 0 and 1.");
        }

        Stage = stage.Trim();
        Detail = detail.Trim();
        Fraction = fraction;
    }

    public string Stage { get; }

    public string Detail { get; }

    public double? Fraction { get; }
}
