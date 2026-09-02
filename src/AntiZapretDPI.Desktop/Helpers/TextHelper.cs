namespace AntiZapretDPI.Helpers
{
    public static class TextHelper
    {
        public static string Truncate(string? text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text ?? string.Empty;
            }

            int contentLength = maxLength - 3;
            return text.Substring(0, contentLength > 0 ? contentLength : 0) + "...";
        }
    }
}
