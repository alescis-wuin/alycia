namespace Alycia.Contracts.Inference;

public interface IForegroundActivity
{
    bool ForegroundRequested { get; }
    IDisposable PreferForeground();
}
