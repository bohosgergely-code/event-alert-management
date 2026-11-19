using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Filters;

namespace EventAlertManagement.Core.Matching;

public static class RuleValidator
{
    public static IReadOnlyList<string> Validate(AlertRule rule)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(rule.Name)) problems.Add("Name is required.");
        if (rule.Name?.Length > 100) problems.Add("Name must be 100 characters or fewer.");
        if (!Enum.IsDefined(rule.Category)) problems.Add("Category is not valid.");
        if (rule.MinSeverity is < 1 or > 5) problems.Add("Minimum severity must be between 1 and 5.");
        if (rule.CooldownSeconds < 0) problems.Add("Cooldown cannot be negative.");
        if (rule.Channels.Count == 0) problems.Add("At least one channel is required.");

        if (!Enum.IsDefined(rule.Category)) return problems;

        try
        {
            switch (RuleFilterSerializer.Parse(rule.Category, rule.FiltersJson))
            {
                case MarketFilters m:
                    if (m.Tickers.Count == 0 || m.Tickers.Any(string.IsNullOrWhiteSpace))
                        problems.Add("Markets rules need at least one ticker.");
                    if (m.MinPercentMove < 0) problems.Add("Minimum percentage move cannot be negative.");
                    break;
                case DisasterFilters d:
                    if (d.MinMagnitude is < 0 or > 10) problems.Add("Minimum magnitude must be between 0 and 10.");
                    break;
                case NewsFilters n:
                    if (n.IncludeKeywords.Any(string.IsNullOrWhiteSpace) || n.ExcludeKeywords.Any(string.IsNullOrWhiteSpace))
                        problems.Add("Keywords cannot be blank.");
                    break;
            }
        }
        catch (FormatException ex)
        {
            problems.Add(ex.Message);
        }

        return problems;
    }
}
