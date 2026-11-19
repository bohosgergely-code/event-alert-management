using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Filters;

namespace EventAlertManagement.Core.Matching;

public static class RuleValidator
{
    public const int MaxCooldownSeconds = 30 * 24 * 3600;
    public const int MaxChannelsPerRule = 5;      // one event can create (rules x channels) messages, so this bounds the fan-out
    public const int MaxListItems = 50;           // keywords per list
    public const int MaxShortListItems = 20;      // tickers, event types, source keys
    public const int MaxItemLength = 100;
    public const int MaxFiltersJsonLength = 16_000;

    public static IReadOnlyList<string> Validate(AlertRule rule)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(rule.Name)) problems.Add("Name is required.");
        if (rule.Name?.Length > 100) problems.Add("Name must be 100 characters or fewer.");
        if (!Enum.IsDefined(rule.Category)) problems.Add("Category is not valid.");
        if (rule.MinSeverity is < 1 or > 5) problems.Add("Minimum severity must be between 1 and 5.");
        if (rule.CooldownSeconds < 0) problems.Add("Cooldown cannot be negative.");
        if (rule.CooldownSeconds > MaxCooldownSeconds) problems.Add("Cooldown cannot be longer than 30 days.");
        if (rule.Channels.Count == 0) problems.Add("At least one channel is required.");
        if (rule.Channels.Count > MaxChannelsPerRule) problems.Add($"A rule can send to at most {MaxChannelsPerRule} channels.");
        if ((rule.FiltersJson?.Length ?? 0) > MaxFiltersJsonLength) problems.Add("The filters are too large.");

        if (!Enum.IsDefined(rule.Category)) return problems;

        try
        {
            switch (RuleFilterSerializer.Parse(rule.Category, rule.FiltersJson))
            {
                case MarketFilters m:
                    if (m.Tickers.Count == 0 || m.Tickers.Any(string.IsNullOrWhiteSpace))
                        problems.Add("Markets rules need at least one ticker.");
                    CheckList(problems, "tickers", m.Tickers, MaxShortListItems);
                    if (m.MinPercentMove < 0) problems.Add("Minimum percentage move cannot be negative.");
                    break;
                case DisasterFilters d:
                    if (d.MinMagnitude is < 0 or > 10) problems.Add("Minimum magnitude must be between 0 and 10.");
                    if ((d.Region?.Length ?? 0) > MaxItemLength) problems.Add($"The region can be at most {MaxItemLength} characters.");
                    CheckList(problems, "event types", d.EventTypes, MaxShortListItems);
                    break;
                case NewsFilters n:
                    if (n.IncludeKeywords.Any(string.IsNullOrWhiteSpace) || n.ExcludeKeywords.Any(string.IsNullOrWhiteSpace))
                        problems.Add("Keywords cannot be blank.");
                    CheckList(problems, "keywords", n.IncludeKeywords, MaxListItems);
                    CheckList(problems, "excluded keywords", n.ExcludeKeywords, MaxListItems);
                    CheckList(problems, "sources", n.SourceKeys, MaxShortListItems);
                    break;
            }
        }
        catch (FormatException ex)
        {
            problems.Add(ex.Message);
        }

        return problems;
    }

    private static void CheckList(List<string> problems, string what, List<string> items, int max)
    {
        if (items.Count > max) problems.Add($"Too many {what}: at most {max} are allowed.");
        else if (items.Any(i => i.Length > MaxItemLength)) problems.Add($"Each of the {what} can be at most {MaxItemLength} characters.");
    }
}
