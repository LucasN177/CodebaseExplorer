using Advanced.Core.Abstractions;

namespace Advanced.Core.Entities;

public partial class Customer : Entity<Guid>, IAuditable
{
    public Customer(Guid id, string name)
        : base(id)
    {
        Name = name;
    }

    public string Name { get; private set; }

    public DateTimeOffset ModifiedAt { get; private set; }
}
