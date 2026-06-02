namespace Ordering.API.Domain.SeedWork;

public abstract class Entity
{
    public int Id { get; protected set; }

    private List<IDomainEvent>? _domainEvents;
    public IReadOnlyCollection<IDomainEvent> DomainEvents =>
        _domainEvents?.AsReadOnly() ?? new List<IDomainEvent>().AsReadOnly();

    protected void AddDomainEvent(IDomainEvent eventItem)
    {
        _domainEvents ??= [];
        _domainEvents.Add(eventItem);
    }

    public void ClearDomainEvents() => _domainEvents?.Clear();
}