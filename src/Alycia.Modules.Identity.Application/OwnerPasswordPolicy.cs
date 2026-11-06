using System.Buffers;
using System.Globalization;
using System.Text;

namespace Alycia.Modules.Identity.Application;

public sealed class OwnerPasswordPolicy
{
    public const int MinCodePoints = 15;
    public const int MaxCodePoints = 128;

    private readonly HashSet<string> _blockedPasswords;

    public OwnerPasswordPolicy(IEnumerable<string> localBlockedPasswords)
    {
        ArgumentNullException.ThrowIfNull(localBlockedPasswords);

        _blockedPasswords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var password in localBlockedPasswords)
        {
            if (string.IsNullOrEmpty(password) || !HasValidUtf16(password))
            {
                throw new ArgumentException("Invalid local blocked-password entry.", nameof(localBlockedPasswords));
            }

            _blockedPasswords.Add(password.Normalize(NormalizationForm.FormC));
        }
    }

    public string NormalizeAndValidate(string? password)
    {
        if (password is null || !HasValidUtf16(password))
        {
            throw new ArgumentException("Password contains invalid characters.", nameof(password));
        }

        // Preserve every space and every printable code point; NFC is the only change.
        var normalized = password.Normalize(NormalizationForm.FormC);
        var count = 0;

        foreach (var rune in normalized.EnumerateRunes())
        {
            count++;
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.OtherNotAssigned)
            {
                throw new ArgumentException("Password contains non-printable characters.", nameof(password));
            }
        }

        if (count is < MinCodePoints or > MaxCodePoints)
        {
            throw new ArgumentException(
                "Password must contain 15 to 128 Unicode code points after NFC.",
                nameof(password));
        }

        if (_blockedPasswords.Contains(normalized))
        {
            throw new ArgumentException("Password appears in the local blocked-password list.", nameof(password));
        }

        return normalized;
    }

    private static bool HasValidUtf16(string value)
    {
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out _, out var consumed);
            if (status != OperationStatus.Done)
            {
                return false;
            }

            remaining = remaining[consumed..];
        }

        return true;
    }
}
