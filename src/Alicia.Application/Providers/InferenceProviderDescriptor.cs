namespace Alicia.Application.Providers;

public sealed record InferenceProviderDescriptor
{
    public InferenceProviderDescriptor(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Provider identifier cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Provider name cannot be empty.", nameof(name));
        }

        Id = id.Trim();
        Name = name.Trim();
    }

    public string Id { get; }

    public string Name { get; }
}
