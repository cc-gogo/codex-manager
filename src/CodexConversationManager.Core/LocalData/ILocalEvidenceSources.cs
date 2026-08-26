namespace CodexConversationManager.Core.LocalData;

public interface ISessionEvidenceSource
{
    Task<IReadOnlyList<SessionEvidence>> ScanAsync(CancellationToken cancellationToken = default);
}

public interface IStateEvidenceSource
{
    Task<IReadOnlyList<StateThreadEvidence>> ReadThreadsAsync(CancellationToken cancellationToken = default);
}

public interface ICatalogEvidenceSource
{
    Task<IReadOnlyList<CatalogThreadEvidence>> ReadCatalogAsync(CancellationToken cancellationToken = default);
}

public interface IGlobalStateEvidenceSource
{
    Task<IReadOnlyList<GlobalStateReference>> ReadReferencesAsync(CancellationToken cancellationToken = default);
}

public interface ISessionIndexEvidenceSource
{
    Task<IReadOnlyList<SessionIndexEvidence>> ReadEntriesAsync(CancellationToken cancellationToken = default);
}
