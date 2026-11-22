namespace Alycia.Contracts.Documents;

public interface IConversationLease
{
    IDisposable Enter(Guid conversation);
}
