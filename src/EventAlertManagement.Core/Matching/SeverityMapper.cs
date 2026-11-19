namespace EventAlertManagement.Core.Matching;

/// <summary>
/// Starting values for converting source facts into a 1-5 severity.
/// These are assumptions to be tuned against real data (see docs/ARCHITECTURE.md).
/// </summary>
public static class SeverityMapper
{
    public const int NewsDefault = 2;

    public static int FromMagnitude(double magnitude) => magnitude switch
    {
        < 4.5 => 1,
        < 5.5 => 2,
        < 6.5 => 3,
        < 7.5 => 4,
        _ => 5,
    };

    public static int FromPercentMove(double changePercent) => Math.Abs(changePercent) switch
    {
        < 3 => 1,
        < 5 => 2,
        < 8 => 3,
        < 12 => 4,
        _ => 5,
    };
}
