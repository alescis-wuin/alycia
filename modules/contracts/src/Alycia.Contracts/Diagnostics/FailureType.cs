namespace Alycia.Contracts.Diagnostics;

public static class FailureType
{
    public static string Name(Exception error) => error.GetType().Name;
}
