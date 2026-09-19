namespace Alicia.Application.Providers;

public interface IInferenceModelLibraryStore
{
    Task<InferenceModelLibrary?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        InferenceModelLibrary library,
        CancellationToken cancellationToken = default);
}
