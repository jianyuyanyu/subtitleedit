using System.Text;

namespace Nikse.SubtitleEdit.Core.Common
{
    public static class StringBuilderExtensions
    {
        /// <summary>
        /// Trims leading/trailing whitespace inside the builder - the "sb.ToString().Trim()"
        /// idiom allocates the whole output an extra time.
        /// </summary>
        public static void Trim(this StringBuilder sb)
        {
            while (sb.Length > 0 && char.IsWhiteSpace(sb[sb.Length - 1]))
            {
                sb.Length--;
            }

            var start = 0;
            while (start < sb.Length && char.IsWhiteSpace(sb[start]))
            {
                start++;
            }

            if (start > 0)
            {
                sb.Remove(0, start);
            }
        }

        public static bool StartsWith(this StringBuilder sb, char c)
        {
            return sb.Length > 0 && sb[0] == c;
        }

        public static bool EndsWith(this StringBuilder sb, char c)
        {
            return sb.Length > 0 && sb[sb.Length - 1] == c;
        }

        // Ordinal "sb.ToString().EndsWith(value)" without materializing the builder.
        public static bool EndsWith(this StringBuilder sb, string value)
        {
            var offset = sb.Length - value.Length;
            if (offset < 0)
            {
                return false;
            }

            for (var i = value.Length - 1; i >= 0; i--)
            {
                if (sb[offset + i] != value[i])
                {
                    return false;
                }
            }

            return true;
        }

        // Same answer as string.IsNullOrWhiteSpace(sb.ToString()) without the copy.
        // Walks the chunks directly: the StringBuilder indexer locates the chunk on every call,
        // which is cheap at the tail but quadratic when scanning from the front.
        public static bool IsNullOrWhiteSpace(this StringBuilder sb)
        {
#if NETSTANDARD2_1
            return string.IsNullOrWhiteSpace(sb.ToString());
#else
            foreach (var chunk in sb.GetChunks())
            {
                var span = chunk.Span;
                for (var i = 0; i < span.Length; i++)
                {
                    if (!char.IsWhiteSpace(span[i]))
                    {
                        return false;
                    }
                }
            }

            return true;
#endif
        }

        // Same count as scanning sb.ToString() without materializing the whole
        // accumulated text into a fresh string.
        public static int CountChar(this StringBuilder sb, char c)
        {
            var count = 0;
#if NET8_0_OR_GREATER
            foreach (var chunk in sb.GetChunks())
            {
                foreach (var ch in chunk.Span)
                {
                    if (ch == c)
                    {
                        count++;
                    }
                }
            }
#else
            // GetChunks is missing from the netstandard2.1 reference assemblies.
            for (var i = 0; i < sb.Length; i++)
            {
                if (sb[i] == c)
                {
                    count++;
                }
            }
#endif
            return count;
        }

        // Matches "{0:00}"/"{0:000}"-style formatting: sign first, then the absolute value
        // padded with leading zeros to minDigits. Negates on a long so int.MinValue does
        // not overflow.
        public static void AppendNumber(this StringBuilder sb, int value, int minDigits)
        {
            var v = (long)value;
            if (v < 0)
            {
                sb.Append('-');
                v = -v;
            }

            var digitCount = 1;
            for (var rest = v; rest >= 10; rest /= 10)
            {
                digitCount++;
            }

            for (; digitCount < minDigits; digitCount++)
            {
                sb.Append('0');
            }

            sb.Append(v);
        }
    }
}
