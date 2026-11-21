using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Contracts.Chat;
using Avalonia.Platform.Storage;

namespace Alycia.Client.Avalonia;

internal sealed record DocumentUploadResult(int Stored, int Skipped, int Excluded);

internal static class DocumentUpload
{
    internal static async Task<DocumentUploadResult?> RunAsync(ChatApiClient client, Guid owner, bool project, IEnumerable<IStorageItem> selected,
        ImportLimits limits, bool includeExcluded, Action<int> uploading, CancellationToken ct)
    {
        var files = new List<UploadFile>();
        var excluded = 0;
        ulong totalBytes = 0;
        var seenFolders = new HashSet<string>(StringComparer.Ordinal);
        async Task VisitAsync(IStorageItem item, string prefix, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (depth > limits.FolderDepth) throw new ChatApiException($"Les dossiers sont limités à {limits.FolderDepth} niveaux.");
            var path = prefix + item.Name;
            if (!includeExcluded && ImportPolicy.Excluded(path)) { excluded++; return; }
            var local = item.TryGetLocalPath();
            if (local is not null && (File.GetAttributes(local) & FileAttributes.ReparsePoint) != 0)
                throw new ChatApiException("Les liens symboliques ne sont pas importés. Sélectionnez leur cible explicitement.");
            if (item is IStorageFolder folder)
            {
                if (!seenFolders.Add(folder.Path.ToString())) return;
                await foreach (var child in folder.GetItemsAsync()) await VisitAsync(child, path + "/", depth + 1);
            }
            else if (item is IStorageFile file)
            {
                if (files.Count >= limits.BatchEntries) throw new ChatApiException($"Import limité à {limits.BatchEntries} fichiers par lot.");
                var properties = await file.GetBasicPropertiesAsync();
                if (properties.Size > (ulong)limits.FileBytes) throw new ChatApiException($"{file.Name} dépasse la limite de {limits.FileMiB} Mio.");
                totalBytes += properties.Size ?? 0;
                if (totalBytes > (ulong)limits.BatchBytes) throw new ChatApiException($"Lot limité à {limits.BatchMiB} Mio.");
                files.Add(new(path, await file.OpenReadAsync(), properties.DateCreated, properties.DateModified));
            }
        }
        try
        {
            foreach (var item in selected) await VisitAsync(item, "", 0);
            if (files.Count == 0) return null;
            uploading(files.Count);
            var result = await client.UploadAsync(owner, Guid.NewGuid(), files, ct, project);
            return new(result.Length, excluded, result.Count(file => file.ContextMode == "excluded"));
        }
        finally
        {
            foreach (var file in files) await file.Content.DisposeAsync();
        }
    }
}
