using System.Text.Json.Serialization;

namespace Alycia.Contracts.Chat;

public sealed record ImportLimits(int FileMiB = 25, int BatchMiB = 100, int BatchEntries = 500,
    int StoredFiles = 500, int StoredMiB = 1024, int ArchiveDepth = 4, int FolderDepth = 16)
{
    public const int HardFileBytes = 256 * 1024 * 1024;
    public const int HardBatchBytes = 1024 * 1024 * 1024;
    [JsonIgnore] public int FileBytes => FileMiB * 1024 * 1024;
    [JsonIgnore] public int BatchBytes => BatchMiB * 1024 * 1024;
    [JsonIgnore] public long StoredBytes => (long)StoredMiB * 1024 * 1024;
    public void Validate()
    {
        if (FileMiB is < 1 or > 256 || BatchMiB < FileMiB || BatchMiB > 1024
            || BatchEntries is < 1 or > 10000 || StoredFiles is < 0 or > 1000000 || StoredMiB is < 0 or > 8388608
            || ArchiveDepth is < 1 or > 16 || FolderDepth is < 1 or > 64)
            throw new ArgumentException("Limites invalides : fichier 1–256 Mio, lot jusqu’à 1024 Mio / 10 000 entrées, archives 1–16 niveaux, dossiers 1–64. Stockage : 0 désactive le quota global.");
    }
}
