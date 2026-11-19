using System.Text.Json;
using EventAlertManagement.Api.Dtos;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Filters;
using EventAlertManagement.Core.Matching;
using EventAlertManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Api.Services;

public sealed record RuleBuildResult(AlertRule? Rule, IReadOnlyList<string> Problems);

/// <summary>Turns a request into a valid rule. Used for create, update and preview so they cannot disagree.</summary>
public sealed class RuleBuilder(AppDbContext db)
{
    public const int MaxRulesPerUser = 50;

    public async Task<RuleBuildResult> BuildAsync(RuleRequest req, Guid userId, bool requireChannels, CancellationToken ct)
    {
        if (!Enum.IsDefined(req.Category)) return new RuleBuildResult(null, new[] { "Category is not valid." });

        var problems = new List<string>();
        var rule = new AlertRule
        {
            UserId = userId, Name = (req.Name ?? "").Trim(), Category = req.Category, MinSeverity = req.MinSeverity,
            CooldownSeconds = req.CooldownSeconds, IsEnabled = req.IsEnabled,
        };

        // Canonical filter JSON: parsed strictly into the category's own filter type, then written back.
        try
        {
            if (req.Filters is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Undefined or JsonValueKind.Null) })
                problems.Add("Filters must be a JSON object.");
            var raw = req.Filters is { ValueKind: JsonValueKind.Object } f ? f.GetRawText() : "{}";
            rule.FiltersJson = RuleFilterSerializer.Serialize(RuleFilterSerializer.Parse(req.Category, raw));
        }
        catch (FormatException ex)
        {
            problems.Add(ex.InnerException is JsonException je ? $"Filters are not valid for this category: {je.Message}" : ex.Message);
        }

        // Only the caller's own channels count. Someone else's id looks exactly like a missing one.
        var wanted = (req.ChannelIds ?? new()).Distinct().ToList();
        if (wanted.Count > 0)
        {
            var own = await db.ChannelConfigs.Where(c => c.UserId == userId && wanted.Contains(c.Id)).ToListAsync(ct);
            if (own.Count != wanted.Count) problems.Add("One or more channels do not exist.");
            rule.Channels = own;
        }

        var validation = requireChannels
            ? RuleValidator.Validate(rule)
            : RuleValidator.Validate(WithPlaceholderChannel(rule));
        problems.AddRange(validation.Where(v => !problems.Contains(v)));

        return problems.Count > 0 ? new RuleBuildResult(null, problems) : new RuleBuildResult(rule, problems);
    }

    // Preview does not need channels, but the shared validator insists on one.
    private static AlertRule WithPlaceholderChannel(AlertRule r)
    {
        if (r.Channels.Count == 0) r.Channels = new() { new ChannelConfig() };
        return r;
    }
}
