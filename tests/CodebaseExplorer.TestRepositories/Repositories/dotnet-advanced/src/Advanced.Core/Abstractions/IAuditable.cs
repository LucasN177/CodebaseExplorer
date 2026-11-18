namespace Advanced.Core.Abstractions;

public interface IAuditable
{
    DateTimeOffset ModifiedAt { get; }
}
