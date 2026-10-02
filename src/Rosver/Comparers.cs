namespace Rosver;

public static class SemVerNaturalComparer
{
    public static int Compare(string? x, string? y)
    {
        if (x is null) return -1;
        if (y is null) return 1;

        Split(x, out var xCore, out var xPre);
        Split(y, out var yCore, out var yPre);

        // major.minor.patch[.n]: numeric, missing = 0
        for (int i = 0; i < Math.Max(xCore.Length, yCore.Length); i++)
        {
            var c = CompareNumeric(
                i < xCore.Length ? xCore[i] : "0",
                i < yCore.Length ? yCore[i] : "0");
            if (c != 0) return c;
        }

        // Release (no prerelease) > prerelease
        if (xPre.Length == 0 || yPre.Length == 0)
            return xPre.Length == yPre.Length ? 0 : xPre.Length == 0 ? 1 : -1;

        // Prerelease identifiers: numeric < alphanumeric; numeric by value; alpha ordinal; shorter < longer
        for (int i = 0; i < Math.Min(xPre.Length, yPre.Length); i++)
        {
            var a = xPre[i];
            var b = yPre[i];
            var aNum = IsNumeric(a);
            var bNum = IsNumeric(b);

            var c = (aNum, bNum) switch
            {
                (true, true) => CompareNumeric(a, b),
                (true, false) => -1,
                (false, true) => 1,
                _ => Math.Sign(string.CompareOrdinal(a, b)),
            };
            if (c != 0) return c;
        }

        return xPre.Length.CompareTo(yPre.Length);
    }

    private static void Split(string v, out string[] core, out string[] pre)
    {
        var plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];

        var dash = v.IndexOf('-');
        core = (dash < 0 ? v : v[..dash]).Split('.');
        pre = dash < 0 ? [] : v[(dash + 1)..].Split('.');
    }

    private static bool IsNumeric(string s) => s.Length > 0 && s.All(char.IsAsciiDigit);

    private static int CompareNumeric(string a, string b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : Math.Sign(string.CompareOrdinal(a, b));
    }
}

public sealed class SdkComparer : IComparer<string>, IComparer<SdkRoslynPair>
{
    public static SdkComparer Instance { get; } = new SdkComparer();

    public int Compare(string? x, string? y)
        => SemVerNaturalComparer.Compare(x, y);

    public int Compare(SdkRoslynPair? x, SdkRoslynPair? y)
        => Compare(x?.Sdk, y?.Sdk);
}

public sealed class RoslynComparer : IComparer<string>, IComparer<SdkRoslynPair>
{
    public static RoslynComparer Instance { get; } = new RoslynComparer();

    public int Compare(string? x, string? y)
        => SemVerNaturalComparer.Compare(x, y);

    public int Compare(SdkRoslynPair? x, SdkRoslynPair? y)
        => Compare(x?.Roslyn, y?.Roslyn);
}