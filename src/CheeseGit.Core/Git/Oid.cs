using System.Runtime.InteropServices;
using System.Text;

namespace CheeseGit.Core.Git;

/// <summary>
/// A git object id. Holds up to 32 bytes so both SHA-1 (20) and SHA-256 (32)
/// repositories work; SHA-1 ids are zero-padded.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Oid : IEquatable<Oid>, IComparable<Oid>
{
    private readonly ulong a, b, c, d;
    private readonly byte length;

    private Oid(ulong a, ulong b, ulong c, ulong d, byte length)
    {
        this.a = a;
        this.b = b;
        this.c = c;
        this.d = d;
        this.length = length;
    }

    /// <summary>Number of raw bytes: 20 for SHA-1, 32 for SHA-256, 0 for <c>default</c>.</summary>
    public int ByteLength => this.length;

    public bool IsEmpty => this.length == 0;

    /// <summary>Parses 40 or 64 lowercase/uppercase hex characters.</summary>
    public static Oid ParseHex(ReadOnlySpan<byte> hex) =>
        TryParseHex(hex, out var oid) ? oid : throw new FormatException($"Invalid object id '{Encoding.ASCII.GetString(hex)}'.");

    public static Oid ParseHex(string hex) => ParseHex(Encoding.ASCII.GetBytes(hex));

    public static bool TryParseHex(ReadOnlySpan<byte> hex, out Oid oid)
    {
        oid = default;
        if (hex.Length is not (40 or 64)) return false;
        Span<byte> raw = stackalloc byte[32];
        raw.Clear();
        for (var i = 0; i < hex.Length / 2; i++)
        {
            var hi = HexValue(hex[2 * i]);
            var lo = HexValue(hex[2 * i + 1]);
            if (hi < 0 || lo < 0) return false;
            raw[i] = (byte)((hi << 4) | lo);
        }
        var words = MemoryMarshal.Cast<byte, ulong>(raw);
        oid = new Oid(words[0], words[1], words[2], words[3], (byte)(hex.Length / 2));
        return true;
    }

    public static bool TryParseHex(string hex, out Oid oid) => TryParseHex(Encoding.ASCII.GetBytes(hex), out oid);

    public string ToHex()
    {
        Span<ulong> words = [this.a, this.b, this.c, this.d];
        return Convert.ToHexStringLower(MemoryMarshal.AsBytes(words)[..this.length]);
    }

    /// <summary>A short form for display only; never use it to address an object.</summary>
    public string ToShortHex(int chars = 7) => this.ToHex()[..Math.Min(chars, this.length * 2)];

    public bool Equals(Oid other) =>
        this.a == other.a && this.b == other.b && this.c == other.c && this.d == other.d && this.length == other.length;

    public override bool Equals(object? obj) => obj is Oid other && this.Equals(other);

    // Object ids are uniformly distributed already; the first word is a fine hash.
    public override int GetHashCode() => (int)this.a ^ (int)(this.a >> 32);

    public int CompareTo(Oid other) => string.CompareOrdinal(this.ToHex(), other.ToHex());

    public static bool operator ==(Oid left, Oid right) => left.Equals(right);

    public static bool operator !=(Oid left, Oid right) => !left.Equals(right);

    public override string ToString() => this.ToHex();

    private static int HexValue(byte ch) => ch switch
    {
        >= (byte)'0' and <= (byte)'9' => ch - '0',
        >= (byte)'a' and <= (byte)'f' => ch - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => ch - 'A' + 10,
        _ => -1,
    };
}
