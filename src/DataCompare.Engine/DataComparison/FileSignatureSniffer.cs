namespace DataCompare.Engine.DataComparison
{

    /// <summary>
    /// Best-effort file-type detection from a byte array's leading magic-number bytes. Used when
    /// writing a large-content column's value to disk for manual inspection (planning.md §18) — reads
    /// the actual bytes rather than trusting a sibling column's stated meaning (e.g. an "IsFrontPage"
    /// flag), since the whole point of sniffing is to catch cases where that assumption doesn't hold.
    /// Only recognizes the two formats known to appear in this project's data today (PDF and a
    /// ZIP-based Office format); anything else falls back to a generic extension.
    /// </summary>
    public static class FileSignatureSniffer
    {
        private static readonly byte[] PdfSignature = "%PDF"u8.ToArray();
        private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

        /// <summary>
        /// Checks whether the content's leading bytes match the PDF magic number.
        /// </summary>
        /// <param name="bytes">a System.Byte array holding the content to check</param>
        /// <returns>returns a System.Boolean that is true when the content starts with the PDF signature</returns>
        public static bool LooksLikePdf(byte[] bytes) => StartsWith(bytes, PdfSignature);

        /// <summary>
        /// Checks whether the content's leading bytes match the ZIP magic number used by all
        /// ZIP-based Office formats (xlsx, docx, pptx).
        /// </summary>
        /// <param name="bytes">a System.Byte array holding the content to check</param>
        /// <returns>returns a System.Boolean that is true when the content starts with the ZIP signature</returns>
        public static bool LooksLikeZipPackage(byte[] bytes) => StartsWith(bytes, ZipSignature);

        /// <summary>
        /// Guesses a file extension (including the leading dot) from the content's magic bytes.
        /// </summary>
        /// <param name="bytes">a System.Byte array holding the content to inspect</param>
        /// <returns>returns a System.String file extension — ".pdf" for PDF content, ".xlsx" for
        /// ZIP-based content (this project's only known ZIP-based format today), or ".bin" when
        /// neither signature matches</returns>
        public static string DetectExtension(byte[] bytes)
        {
            if (LooksLikePdf(bytes))
            {
                return ".pdf";
            }

            if (LooksLikeZipPackage(bytes))
            {
                return ".xlsx";
            }

            return ".bin";
        }

        /// <summary>
        /// Checks whether a byte array begins with the given signature bytes.
        /// </summary>
        /// <param name="bytes">a System.Byte array to check</param>
        /// <param name="signature">a System.Byte array holding the magic-number bytes to match at the start of <paramref name="bytes"/></param>
        /// <returns>returns a System.Boolean that is true when <paramref name="bytes"/> is at least as long as <paramref name="signature"/> and starts with it</returns>
        private static bool StartsWith(byte[] bytes, byte[] signature) =>
            bytes.Length >= signature.Length && bytes.AsSpan(0, signature.Length).SequenceEqual(signature);
    }
}
