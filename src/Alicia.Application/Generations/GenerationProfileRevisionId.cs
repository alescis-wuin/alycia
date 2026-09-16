namespace Alicia.Application.Generations;

public readonly record struct GenerationProfileRevisionId(Guid Value)
{
    public bool IsEmpty => Value == Guid.Empty;

    public static GenerationProfileRevisionId New()
    {
        return new GenerationProfileRevisionId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}
