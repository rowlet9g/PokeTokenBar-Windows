using System.Globalization;
using System.Numerics;

namespace PokeTokenBar.Core;

public static class TokenFormatter
{
    public static string Compact(long value)
    {
        var absolute = Math.Abs((double)value);
        var sign = value < 0 ? "-" : string.Empty;

        return absolute switch
        {
            < 1_000 => value.ToString(CultureInfo.InvariantCulture),
            < 1_000_000 => sign + FormatTrimmed(absolute / 1_000, 1) + "K",
            < 1_000_000_000 => sign + FormatTrimmed(absolute / 1_000_000, 1) + "M",
            _ => sign + FormatTrimmed(absolute / 1_000_000_000, 2) + "B",
        };
    }

    public static string Grouped(long value, CultureInfo? culture = null) =>
        value.ToString("N0", culture ?? CultureInfo.CurrentCulture);

    public static string Cost(double usd) =>
        "$" + FormatFixed(usd, 2);

    public static string CostCompact(double usd) => usd switch
    {
        < 100 => "$" + FormatFixed(usd, 1),
        < 10_000 => "$" + FormatFixed(usd, 0),
        _ => "$" + FormatFixed(usd / 1_000, 1) + "K",
    };

    public static string Percent(double value) =>
        value == Math.Truncate(value)
            ? FormatFixed(value, 0) + "%"
            : FormatFixed(value, 1) + "%";

    private static string FormatTrimmed(double value, int decimals) =>
        FormatFixed(value, decimals).TrimEnd('0').TrimEnd('.');

    // Swift's String(format:) rounds the exact binary floating-point value.
    // .NET's numeric formatter uses different midpoint behavior for values such
    // as 88.35, so reproduce printf-style round-to-nearest-even explicitly.
    private static string FormatFixed(double value, int decimals)
    {
        if (!double.IsFinite(value))
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        var rawBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        var negative = (rawBits >> 63) != 0;
        var exponentBits = (int)((rawBits >> 52) & 0x7ff);
        var mantissa = rawBits & 0x000f_ffff_ffff_ffff;

        ulong significand;
        int binaryExponent;
        if (exponentBits == 0)
        {
            significand = mantissa;
            binaryExponent = -1074;
        }
        else
        {
            significand = mantissa | (1UL << 52);
            binaryExponent = exponentBits - 1023 - 52;
        }

        var numerator = new BigInteger(significand) * BigInteger.Pow(10, decimals);
        var denominator = BigInteger.One;
        if (binaryExponent >= 0)
        {
            numerator <<= binaryExponent;
        }
        else
        {
            denominator <<= -binaryExponent;
        }

        var rounded = BigInteger.DivRem(numerator, denominator, out var remainder);
        var midpointComparison = (remainder << 1).CompareTo(denominator);
        if (midpointComparison > 0 || (midpointComparison == 0 && !rounded.IsEven))
        {
            rounded += BigInteger.One;
        }

        var digits = rounded.ToString(CultureInfo.InvariantCulture);
        if (decimals > 0)
        {
            digits = digits.PadLeft(decimals + 1, '0');
            digits = digits.Insert(digits.Length - decimals, ".");
        }

        return negative ? "-" + digits : digits;
    }
}
