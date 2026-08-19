using DataCompare.Engine.DataComparison;

namespace DataCompare.Engine.Tests.DataComparison
{

    /// <summary>
    /// tests that DataCompare.Engine.DataComparison.FileSignatureSniffer correctly detects file
    /// types from their leading byte signature.
    /// </summary>
    public sealed class FileSignatureSnifferTests
    {
        [Fact]
        public void DetectExtension_PdfSignature_ReturnsPdf()
        {
            byte[] bytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x33]; // "%PDF-1.3"

            Assert.True(FileSignatureSniffer.LooksLikePdf(bytes));
            Assert.False(FileSignatureSniffer.LooksLikeZipPackage(bytes));
            Assert.Equal(".pdf", FileSignatureSniffer.DetectExtension(bytes));
        }

        [Fact]
        public void DetectExtension_ZipSignature_ReturnsXlsx()
        {
            byte[] bytes = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x08, 0x00];

            Assert.True(FileSignatureSniffer.LooksLikeZipPackage(bytes));
            Assert.False(FileSignatureSniffer.LooksLikePdf(bytes));
            Assert.Equal(".xlsx", FileSignatureSniffer.DetectExtension(bytes));
        }

        [Fact]
        public void DetectExtension_UnrecognizedSignature_ReturnsBin()
        {
            byte[] bytes = [0x00, 0x01, 0x02, 0x03];

            Assert.Equal(".bin", FileSignatureSniffer.DetectExtension(bytes));
        }

        [Fact]
        public void DetectExtension_ContentShorterThanSignature_ReturnsBin()
        {
            byte[] bytes = [0x25, 0x50];

            Assert.Equal(".bin", FileSignatureSniffer.DetectExtension(bytes));
        }

        [Fact]
        public void DetectExtension_EmptyContent_ReturnsBin()
        {
            Assert.Equal(".bin", FileSignatureSniffer.DetectExtension([]));
        }
    }
}
