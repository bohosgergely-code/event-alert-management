using EventAlertManagement.Api.Auth;
using EventAlertManagement.Api.Dtos;
using EventAlertManagement.Api.Services;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Matching;
using EventAlertManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Api.Controllers;

[ApiController]
[Route("api/rules")]
[Authorize(Roles = Roles.User)]
public class RulesController(AppDbContext db, RuleBuilder builder, IEventMatcher matcher) : ControllerBase
{
    private IQueryable<AlertRule> Own() =>
        db.AlertRules.Include(r => r.Channels).Where(r => r.UserId == User.UserId());

    [HttpGet]
    public async Task<List<RuleResponse>> List(CancellationToken ct) =>
        (await Own().AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct)).Select(Mapping.ToResponse).ToList();

    [HttpGet("presets")]
    public IReadOnlyList<PresetResponse> Presets() => RulePresets.All;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RuleResponse>> Get(Guid id, CancellationToken ct)
    {
        var rule = await Own().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return rule is null ? NotFound() : Mapping.ToResponse(rule);
    }

    [HttpPost]
    public async Task<ActionResult<RuleResponse>> Create(RuleRequest request, CancellationToken ct)
    {
        var uid = User.UserId();
        if (await db.AlertRules.CountAsync(r => r.UserId == uid, ct) >= RuleBuilder.MaxRulesPerUser)
            return Invalid("name", $"You can have at most {RuleBuilder.MaxRulesPerUser} rules.");

        var built = await builder.BuildAsync(request, uid, requireChannels: true, ct);
        if (built.Rule is null) return Invalid("rule", built.Problems);

        db.AlertRules.Add(built.Rule);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = built.Rule.Id }, Mapping.ToResponse(built.Rule));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RuleResponse>> Update(Guid id, RuleRequest request, CancellationToken ct)
    {
        var existing = await Own().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (existing is null) return NotFound();

        var built = await builder.BuildAsync(request, User.UserId(), requireChannels: true, ct);
        if (built.Rule is null) return Invalid("rule", built.Problems);

        existing.Name = built.Rule.Name;
        existing.Category = built.Rule.Category;
        existing.MinSeverity = built.Rule.MinSeverity;
        existing.FiltersJson = built.Rule.FiltersJson;
        existing.CooldownSeconds = built.Rule.CooldownSeconds;
        existing.IsEnabled = built.Rule.IsEnabled;
        existing.Channels = built.Rule.Channels;
        await db.SaveChangesAsync(ct);
        return Mapping.ToResponse(existing);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var rule = await Own().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();
        db.AlertRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>"Which recent events would this rule have matched?" Nothing is saved or sent.</summary>
    [HttpPost("preview")]
    public async Task<ActionResult<PreviewResponse>> Preview(RuleRequest request, CancellationToken ct)
    {
        var built = await builder.BuildAsync(request, User.UserId(), requireChannels: false, ct);
        if (built.Rule is null) return Invalid("rule", built.Problems);

        var since = DateTime.UtcNow.AddDays(-7);
        var recent = await db.Events.AsNoTracking()
            .Where(e => e.Category == built.Rule.Category && e.OccurredAt >= since)
            .OrderByDescending(e => e.OccurredAt).Take(500).ToListAsync(ct);

        built.Rule.IsEnabled = true; // Preview is about the filters, not whether the rule is switched on.
        var matches = recent.Where(e => matcher.Matches(built.Rule, e)).ToList();
        return new PreviewResponse(recent.Count, matches.Count, matches.Take(20).Select(Mapping.ToSummary).ToList());
    }

    private ActionResult Invalid(string field, IEnumerable<string> messages) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = messages.ToArray() }));
    private ActionResult Invalid(string field, string message) => Invalid(field, new[] { message });
}
