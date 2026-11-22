namespace Alycia.Contracts.Inference;

public interface ITextTokenCounter
{
    string Name { get; }
    int Count(string text);
}
