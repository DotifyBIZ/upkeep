namespace Upkeep.App.Core.Cleanup;

/// <summary>
/// Finds what could be cleaned. Scanning is read-only by contract: it produces evidence for the
/// preview and nothing else (ADR-0006). System-scope categories are scanned by the elevated
/// helper, not here.
/// </summary>
public interface IJunkScanner
{
    /// <summary>Scans every category the signed-in user can clean without elevation.</summary>
    Task<IReadOnlyList<JunkCategoryScan>> ScanUserCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Scans one category. Throws for system-scope categories, which belong to the helper.</summary>
    Task<JunkCategoryScan> ScanCategoryAsync(JunkCategoryId categoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans the user's own rules. Separate from the categories above because the rules come from
    /// settings rather than from the catalog, and an empty list is the normal case.
    /// </summary>
    Task<JunkCategoryScan> ScanCustomRulesAsync(IReadOnlyList<CustomCleanupRule> rules, CancellationToken cancellationToken = default);
}
