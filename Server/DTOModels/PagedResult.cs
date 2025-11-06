namespace Server.DTOModels;

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    
    // Constructor for in-memory pagination (kept for backward compatibility)
    public PagedResult(List<T> orderedItems, int page, int pageSize) {
        if (page < 1)
        {
            throw new ArgumentException("Page number must be greater than 0");
        }
        
        if (pageSize is < 1 or > 100)
        {
            throw new ArgumentException("Page size must be between 1 and 100");
        }
        
        Items = orderedItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        TotalCount = orderedItems.Count;
        Page = page;
        PageSize = pageSize;
        TotalPages = (int)Math.Ceiling((double)TotalCount / pageSize);
        
        // throw and error if page is out of range
        if (page > TotalPages && TotalPages != 0) {
            throw new ArgumentException("Page number out of range");
        }
    }
    
    // Parameterless constructor for direct assignment
    public PagedResult()
    {
    }
}