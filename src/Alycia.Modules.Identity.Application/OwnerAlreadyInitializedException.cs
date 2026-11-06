namespace Alycia.Modules.Identity.Application;

public sealed class OwnerAlreadyInitializedException()
    : InvalidOperationException("The owner is already initialized.");
