namespace Alicia.Application.Generations;

public interface IGenerationProfileCatalogStore
{
    Task<GenerationProfileCatalog?> LoadAsync(
        GenerationProfileModelScope scope,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        GenerationProfileCatalog catalog,
        CancellationToken cancellationToken = default);
}
