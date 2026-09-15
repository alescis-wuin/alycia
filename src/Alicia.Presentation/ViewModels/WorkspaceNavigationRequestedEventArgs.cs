namespace Alicia.Presentation.ViewModels;

internal sealed class WorkspaceNavigationRequestedEventArgs : EventArgs
{
    public WorkspaceNavigationRequestedEventArgs(WorkspaceSection section)
    {
        Section = section;
    }

    public WorkspaceSection Section { get; }
}
