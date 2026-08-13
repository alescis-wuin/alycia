namespace Alicia.Presentation.ViewModels;

public enum ConversationConfigurationGateState
{
    Hidden,
    NoProvider,
    ProviderNotDetected,
    ProviderDetecting,
    ProviderMissing,
    ProviderInstalling,
    ProviderUnsupported,
    ProviderFaulted,
    ModelConfigurationRequired,
    ProviderReadyToStart,
    ProviderStarting,
    ProviderStopping,
}
