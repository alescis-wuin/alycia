using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Alicia.Application.Providers;

namespace Alicia.Application.Generations;

public sealed class GenerationProfileRevision
{
    public GenerationProfileRevision(
        GenerationProfileRevisionId id,
        GenerationProfileRevisionId? parentRevisionId,
        DateTimeOffset createdAt,
        GenerationProfile profile)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException(
                "Generation-profile revision identifier cannot be empty.",
                nameof(id));
        }

        if (parentRevisionId is { IsEmpty: true })
        {
            throw new ArgumentException(
                "Parent generation-profile revision identifier cannot be empty.",
                nameof(parentRevisionId));
        }

        if (parentRevisionId == id)
        {
            throw new ArgumentException(
                "A generation-profile revision cannot be its own parent.",
                nameof(parentRevisionId));
        }

        ArgumentNullException.ThrowIfNull(profile);

        if (profile.IsDefault)
        {
            throw new ArgumentException(
                "The built-in default generation profile does not produce user revisions.",
                nameof(profile));
        }

        Id = id;
        ParentRevisionId = parentRevisionId;
        CreatedAtUtc = createdAt.ToUniversalTime();
        Profile = GenerationProfileSnapshot.Copy(profile);
        PayloadHash = GenerationProfileSnapshot.ComputePayloadHash(Profile);
    }

    public GenerationProfileRevisionId Id { get; }

    public GenerationProfileId ProfileId => Profile.Id;

    public GenerationProfileRevisionId? ParentRevisionId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public GenerationProfile Profile { get; }

    public string PayloadHash { get; }
}

internal static class GenerationProfileSnapshot
{
    private const string PayloadHashSchema = "alicia-generation-profile-payload-v1";

    public static GenerationProfile Copy(GenerationProfile source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.IsDefault)
        {
            return GenerationProfile.CreateDefault(source.Id);
        }

        return new GenerationProfile(
            source.Id,
            source.Name,
            source.BaseSystemInstructions,
            source.GenerationOptions,
            source.InitialSuggestions);
    }

    public static string ComputePayloadHash(GenerationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        StringBuilder canonical = new();
        AppendCanonical(canonical, PayloadHashSchema);
        AppendCanonical(canonical, profile.Id.ToString());
        AppendCanonical(canonical, profile.Name);
        AppendCanonical(canonical, profile.IsDefault ? "1" : "0");
        AppendCanonical(canonical, profile.BaseSystemInstructions);
        AppendCanonical(
            canonical,
            FormatNullable(profile.GenerationOptions.MaxOutputTokens));
        AppendCanonical(
            canonical,
            FormatNullable(profile.GenerationOptions.Temperature));
        AppendCanonical(
            canonical,
            FormatNullable(profile.GenerationOptions.TopP));
        AppendCanonical(
            canonical,
            FormatNullable(profile.GenerationOptions.TopK));
        AppendCanonical(
            canonical,
            FormatNullable(profile.GenerationOptions.Seed));
        AppendCanonical(
            canonical,
            FormatNullable(profile.GenerationOptions.ReasoningEnabled));
        AppendCanonical(
            canonical,
            FormatNullable(profile.GenerationOptions.ReasoningBudgetTokens));
        AppendCanonical(
            canonical,
            profile.InitialSuggestions.Count.ToString(CultureInfo.InvariantCulture));

        foreach (string suggestion in profile.InitialSuggestions)
        {
            AppendCanonical(canonical, suggestion);
        }

        byte[] payload = Encoding.UTF8.GetBytes(canonical.ToString());
        byte[] digest = SHA256.HashData(payload);
        return Convert.ToHexString(digest);
    }

    private static void AppendCanonical(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append("-1:;");
            return;
        }

        builder
            .Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append(';');
    }

    private static string? FormatNullable(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }

    private static string? FormatNullable(double? value)
    {
        return value?.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string? FormatNullable(bool? value)
    {
        return value switch
        {
            true => "1",
            false => "0",
            null => null,
        };
    }
}
