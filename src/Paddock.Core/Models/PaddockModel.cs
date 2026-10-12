namespace Paddock.Core.Models;

public class PaddockModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "New Pad";
    public TitleVisibility TitleVisibility { get; set; } = TitleVisibility.Hover;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 250;
    public PaddockStyle Style { get; set; } = new();
    public List<IconEntry> Icons { get; set; } = new();
    public bool Locked { get; set; }
    public bool IsRolledUp { get; set; }
    public bool ExcludeFromQuickHide { get; set; }
    public SortField SortBy { get; set; } = SortField.Name;
    public bool SortAscending { get; set; } = true;
}

public enum TitleVisibility
{
    Always,
    Hover,
    Hidden
}

public enum SortField
{
    Name,
    DateModified,
    FileType,
    FileSize
}
