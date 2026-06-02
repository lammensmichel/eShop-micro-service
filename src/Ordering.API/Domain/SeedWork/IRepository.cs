namespace Ordering.API.Domain.SeedWork;

public interface IRepository<T> where T : IAggregateRoot
{
    Task<T?> GetAsync(int id);
    T Add(T aggregate);
    void Update(T aggregate);
    Task<int> SaveChangesAsync();
}