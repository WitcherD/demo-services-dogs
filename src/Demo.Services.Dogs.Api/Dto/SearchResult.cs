namespace Demo.Services.Dogs.Api.Dto;

public record SearchResult<T> where T : class
{
    public int TotalCount { get; init; }

    public IEnumerable<T> Values { get; init; }
}