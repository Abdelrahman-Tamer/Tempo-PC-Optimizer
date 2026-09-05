using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Tempo.Services;
using Xunit;

namespace Tempo.Tests
{
    public class SecurityGuardsTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void IsSafePath_RejectsNullOrWhitespace(string? path)
        {
            Assert.False(CleanupService.IsSafePath(path!));
        }

        [Fact]
        public void IsSafePath_RejectsSystemAndSystem32Roots()
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string sys32 = Path.Combine(winDir, "System32");

            Assert.False(CleanupService.IsSafePath(winDir));
            Assert.False(CleanupService.IsSafePath(sys32));
            Assert.False(CleanupService.IsSafePath(Path.Combine(sys32, "cmd.exe")));
        }

        [Fact]
        public void IsSafePath_RejectsUserPersonalDirectories()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string downloads = Path.Combine(userProfile, "Downloads");

            Assert.False(CleanupService.IsSafePath(desktop));
            Assert.False(CleanupService.IsSafePath(docs));
            Assert.False(CleanupService.IsSafePath(pics));
            Assert.False(CleanupService.IsSafePath(downloads));
            Assert.False(CleanupService.IsSafePath(Path.Combine(desktop, "important.docx")));
        }

        [Fact]
        public void IsSafePath_RejectsDriveRoots()
        {
            Assert.False(CleanupService.IsSafePath(@"C:\"));
            Assert.False(CleanupService.IsSafePath(@"C:"));
            Assert.False(CleanupService.IsSafePath(@"D:\"));
        }

        [Fact]
        public void IsSafePath_RejectsAppBaseDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            Assert.False(CleanupService.IsSafePath(baseDir));
            Assert.False(CleanupService.IsSafePath(Path.Combine(baseDir, "Tempo.dll")));
        }

        [Theory]
        [InlineData(@"C:\Temp\project\file.cs")]
        [InlineData(@"C:\Temp\build\App.csproj")]
        [InlineData(@"C:\Temp\repo\Solution.sln")]
        [InlineData(@"C:\Temp\code\.git\config")]
        public void IsSafePath_RejectsSourceCodeFiles(string path)
        {
            Assert.False(CleanupService.IsSafePath(path));
        }

        [Fact]
        public void IsSafePath_AllowsWindowsTempAndUserTempSubpaths()
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string winTempFile = Path.Combine(winDir, "Temp", "sample_junk_file.tmp");
            string userTempFile = Path.Combine(Path.GetTempPath(), "tempo_unit_test.tmp");

            Assert.True(CleanupService.IsSafePath(winTempFile));
            Assert.True(CleanupService.IsSafePath(userTempFile));
        }

        [Fact]
        public void SafeEnumerateFiles_HandlesNonExistentDirectorySafely()
        {
            var nonExistent = new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
            var files = CleanupService.SafeEnumerateFiles(nonExistent, apply24HourCutoff: false);

            Assert.Empty(files);
        }

        [Fact]
        public void SafeEnumerateFiles_FiltersFilesBy24HourCutoff()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TempoTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string oldFile = Path.Combine(tempDir, "old.tmp");
                string freshFile = Path.Combine(tempDir, "fresh.tmp");

                File.WriteAllText(oldFile, "old content");
                File.WriteAllText(freshFile, "fresh content");

                // Set oldFile to 48 hours ago for both write and creation time
                File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddHours(-48));
                File.SetCreationTimeUtc(oldFile, DateTime.UtcNow.AddHours(-48));
                // freshFile remains written just now

                var di = new DirectoryInfo(tempDir);
                var filtered = CleanupService.SafeEnumerateFiles(di, apply24HourCutoff: true);

                var fileNames = new System.Collections.Generic.List<string>();
                foreach (var f in filtered)
                {
                    fileNames.Add(f.Name);
                }

                Assert.Contains("old.tmp", fileNames);
                Assert.DoesNotContain("fresh.tmp", fileNames);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void VerifySha256_ValidatesCorrectAndMismatchedHashes()
        {
            string tempFile = Path.GetTempFileName();
            try
            {
                byte[] content = Encoding.UTF8.GetBytes("Tempo PC Optimizer Cryptographic Verification 2026");
                File.WriteAllBytes(tempFile, content);

                string expectedHash;
                using (var sha256 = SHA256.Create())
                {
                    byte[] hashBytes = sha256.ComputeHash(content);
                    expectedHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
                }

                Assert.True(UpdateService.VerifySha256(tempFile, expectedHash));
                Assert.True(UpdateService.VerifySha256(tempFile, expectedHash.ToUpperInvariant())); // case insensitive
                Assert.False(UpdateService.VerifySha256(tempFile, "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void VerifySha256_ReturnsFalseOnMissingFile()
        {
            string nonExistent = Path.Combine(Path.GetTempPath(), "non_existent_" + Guid.NewGuid().ToString("N") + ".exe");
            Assert.False(UpdateService.VerifySha256(nonExistent, "deadbeef"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("!")]
        [InlineData("1")]
        [InlineData("XYZ")]
        [InlineData("C:")]
        [InlineData("C:\\")]
        public void SsdReTrim_RejectsInvalidLettersSafely(string letter)
        {
            var svc = new CleanupService();
            var res = svc.SsdReTrim(letter);

            // Must fail gracefully without throwing
            if (letter == "C:" || letter == "C:\\")
            {
                // Note: "C:" or "C:\" gets trimmed to "C", which proceeds to fixed drive check
                // For actual drive C:, on developer machine it may require admin
                Assert.NotNull(res.Message);
            }
            else
            {
                Assert.False(res.Success);
                Assert.True(res.Message.Contains("غير صالح", StringComparison.OrdinalIgnoreCase) ||
                            res.Message.Contains("Invalid drive letter", StringComparison.OrdinalIgnoreCase));
            }
        }

        [Fact]
        public void ResolveSignedDotnet_ExecutesWithoutCrashing()
        {
            string? dotnetPath = CleanupService.ResolveSignedDotnet();
            if (dotnetPath != null)
            {
                Assert.True(File.Exists(dotnetPath));
                Assert.EndsWith("dotnet.exe", dotnetPath, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
