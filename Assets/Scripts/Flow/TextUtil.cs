using System.Text;

namespace Barista
{
    public static class TextUtil
    {
        /// <summary>Word-wraps text for TextMesh, which has no wrapping of its own.</summary>
        public static string Wrap(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var paragraph in text.Split('\n'))
            {
                var line = 0;
                foreach (var word in paragraph.Split(' '))
                {
                    if (line > 0 && line + word.Length + 1 > maxChars)
                    {
                        sb.Append('\n');
                        line = 0;
                    }
                    else if (line > 0)
                    {
                        sb.Append(' ');
                        line++;
                    }
                    sb.Append(word);
                    line += word.Length;
                }
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }
    }
}
