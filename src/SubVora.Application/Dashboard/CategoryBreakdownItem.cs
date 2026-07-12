namespace SubVora.Application.Dashboard;

public class CategoryBreakdownItem
{
    public Guid? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal MonthlyAmount { get; set; }
}
