using Advanced.Core.Entities;

namespace Advanced.Core.Abstractions;

public interface IRepository<T>
    where T : Entity<Guid>
{
    T? Find(Guid id);

    void Add(T item);
}
