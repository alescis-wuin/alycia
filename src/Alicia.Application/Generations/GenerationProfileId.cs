namespace Alicia.Application.Generations;

public readonly record struct GenerationProfileId(Guid Value)
{
    public bool IsEmpty => Value == Guid.Empty;

    public static GenerationProfileId New()
    {
        return new GenerationProfileId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}
