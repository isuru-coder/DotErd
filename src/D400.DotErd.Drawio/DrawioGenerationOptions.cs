namespace D400.DotErd.Drawio;

/// <summary>
/// Configures draw.io XML generation.
/// </summary>
/// <param name="PageName">The draw.io diagram page name.</param>
/// <param name="GridColumns">The number of table columns in the deterministic grid layout.</param>
/// <param name="TableWidth">The table vertex width.</param>
/// <param name="HeaderHeight">The rendered table header height.</param>
/// <param name="ColumnHeight">The rendered column row height.</param>
/// <param name="HorizontalSpacing">The horizontal grid spacing.</param>
/// <param name="VerticalSpacing">The vertical grid spacing.</param>
public sealed record DrawioGenerationOptions(
    string PageName = "ERD",
    int GridColumns = 3,
    int TableWidth = 280,
    int HeaderHeight = 34,
    int ColumnHeight = 24,
    int HorizontalSpacing = 360,
    int VerticalSpacing = 260);

