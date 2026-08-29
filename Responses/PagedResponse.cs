namespace MdfTracker.Api.Responses;

public class PagedResponse<T>
{
    public List<T> Data { get; set; } = new();

    public PaginationMeta Meta { get; set; } = new();

    public static PagedResponse<T> Create(List<T> data, int page, int perPage, int total) => new()
    {
        Data = data,
        Meta = new PaginationMeta
        {
            Page = page,
            PerPage = perPage,
            Total = total,
            TotalPages = (int)Math.Ceiling(total / (double)perPage)
        }
    };
}

public class PaginationMeta
{
    public int Page { get; set; }

    public int PerPage { get; set; }

    public int Total { get; set; }

    public int TotalPages { get; set; }
}
