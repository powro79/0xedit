using System;
using System.Globalization;
using System.Text;

namespace ZeroXEdit
{
    internal static class ByteTools
    {
        internal static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        internal static byte[] ParseHex(string text)
        {
            var digits = new StringBuilder();
            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch))
                    continue;
                if (!Uri.IsHexDigit(ch))
                    throw new FormatException("Use hexadecimal digits 0–9 and A–F.");
                digits.Append(ch);
            }
            if (digits.Length % 2 != 0)
                throw new FormatException("Enter two hexadecimal digits per byte.");
            var result = new byte[digits.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = byte.Parse(digits.ToString(2 * i, 2), NumberStyles.HexNumber, Invariant);
            return result;
        }

        internal static string Hex(byte[] data)
        {
            return BitConverter.ToString(data).Replace('-', ' ');
        }
        internal static string Bits(byte value)
        {
            string bits = Convert.ToString(value, 2).PadLeft(8, '0');
            return bits.Substring(0, 4) + " " + bits.Substring(4);
        }
        internal static string Size(long length)
        {
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };
            double size = length;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return size.ToString("0.##", Invariant) + " " + units[unit];
        }
        internal static long ParseOffset(string text, bool hex, long length)
        {
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hex = true;
                text = text.Substring(2);
            }
            long offset;
            if (!long.TryParse(text, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, Invariant, out offset) ||
                offset < 0 || offset > length)
                throw new FormatException("Enter an offset from 0 to " +
                                          (hex ? "0x" + length.ToString("X") : length.ToString(Invariant)) + ".");
            return offset;
        }

        internal static string Interpret(byte[] data, int width, bool bigEndian, char type)
        {
            if (data.Length < width)
                return "—";
            var bytes = new byte[width];
            Array.Copy(data, bytes, width);
            if (bigEndian == BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            if (type == 'f')
                return width == 4 ? BitConverter.ToSingle(bytes, 0).ToString("G9", Invariant)
                                  : BitConverter.ToDouble(bytes, 0).ToString("G17", Invariant);
            if (type == 'u')
            {
                ulong value = width == 1   ? bytes[0]
                              : width == 2 ? BitConverter.ToUInt16(bytes, 0)
                              : width == 4 ? BitConverter.ToUInt32(bytes, 0)
                                           : BitConverter.ToUInt64(bytes, 0);
                return value.ToString(Invariant);
            }
            long signed = width == 1   ? unchecked((sbyte)bytes[0])
                          : width == 2 ? BitConverter.ToInt16(bytes, 0)
                          : width == 4 ? BitConverter.ToInt32(bytes, 0)
                                       : BitConverter.ToInt64(bytes, 0);
            return signed.ToString(Invariant);
        }
    }

    // Scan bounded chunks on each UI tick. KMP carries partial matches across reads.
    internal sealed class PatternSearch
    {
        private readonly BinaryDocument document;
        private readonly byte[] pattern, buffer = new byte[65536];
        private readonly int[] prefix;
        private readonly bool backward;
        private readonly long secondLow, secondHigh;
        private long cursor, remaining;
        private int matched;
        private bool second;
        internal long Result { get; private set; }

        internal PatternSearch(BinaryDocument document, byte[] bytes, long start, bool backward)
        {
            if (bytes.Length == 0)
                throw new ArgumentException("Enter a search value.");
            this.document = document;
            this.backward = backward;
            pattern = (byte[])bytes.Clone();
            if (backward)
                Array.Reverse(pattern);
            prefix = new int[pattern.Length];
            for (int i = 1, n = 0; i < pattern.Length; i++)
            {
                while (n > 0 && pattern[i] != pattern[n])
                    n = prefix[n - 1];
                if (pattern[i] == pattern[n])
                    n++;
                prefix[i] = n;
            }
            Result = -1;
            long last = document.Length - bytes.Length;
            if (backward)
            {
                long split = Math.Max(-1, Math.Min(start, last));
                SetRange(0, split);
                secondLow = split + 1;
                secondHigh = last;
            }
            else
            {
                long split = Math.Max(0, Math.Min(start, last + 1));
                SetRange(split, last);
                secondLow = 0;
                secondHigh = split - 1;
            }
        }
        private void SetRange(long low, long high)
        {
            remaining = high < low ? 0 : high - low + pattern.Length;
            cursor = backward && remaining > 0 ? high + pattern.Length - 1 : low;
            matched = 0;
        }
        // With a visitor, continue past each full match and consume its bytes.
        internal bool Step(int budget, Action<long> visitMatch = null)
        {
            while (budget > 0)
            {
                if (remaining == 0)
                {
                    if (second)
                        return true;
                    second = true;
                    SetRange(secondLow, secondHigh);
                    continue;
                }
                int n = (int)Math.Min(Math.Min(buffer.Length, budget), remaining);
                document.Read(backward ? cursor - n + 1 : cursor, buffer, 0, n);
                for (int i = 0; i < n; i++)
                {
                    byte value = buffer[backward ? n - i - 1 : i];
                    while (matched > 0 && pattern[matched] != value)
                        matched = prefix[matched - 1];
                    if (pattern[matched] == value)
                        matched++;
                    if (matched == pattern.Length)
                    {
                        Result = backward ? cursor - i : cursor + i - pattern.Length + 1;
                        if (visitMatch == null)
                            return true;
                        visitMatch(Result);
                        matched = 0;
                    }
                }
                cursor += backward ? -n : n;
                remaining -= n;
                budget -= n;
            }
            return false;
        }
    }
}
