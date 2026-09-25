namespace WholeGameStateAudit.Tests;

internal static class Check
{
    public static int Passed { get; private set; }

    public static List<string> Failures { get; } = [];

    public static string Current { get; set; } = string.Empty;

    public static void True(bool condition, string what)
    {
        if (condition)
        {
            Passed++;
        }
        else
        {
            Failures.Add($"{Current}: {what}");
        }
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
        {
            Passed++;
        }
        else
        {
            Failures.Add($"{Current}: {what}: expected {expected}, got {actual}");
        }
    }

    public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string what)
    {
        var left = expected.ToArray();
        var right = actual.ToArray();
        if (left.SequenceEqual(right))
        {
            Passed++;
        }
        else
        {
            Failures.Add($"{Current}: {what}: expected [{string.Join(", ", left)}], got [{string.Join(", ", right)}]");
        }
    }
}
