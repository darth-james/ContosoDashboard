namespace ContosoDashboard.Models;

/// <summary>The fixed list of document categories. Stored as readable text in <see cref="Document.Category"/>.</summary>
public static class DocumentCategories
{
    public static readonly IReadOnlyList<string> All =
        ["Project Documents", "Team Resources", "Personal Files", "Reports", "Presentations", "Other"];

    /// <summary>True when <paramref name="value"/> is one of <see cref="All"/>, compared by ordinal.</summary>
    public static bool IsValid(string? value)
    {
        return value != null && All.Contains(value, StringComparer.Ordinal);
    }
}
