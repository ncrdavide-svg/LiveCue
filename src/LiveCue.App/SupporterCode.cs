// Same file in every app of the suite; the .NET 9 apps have nullable annotations on, this code doesn't use them.
#if NETCOREAPP
#nullable disable
#endif
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LiveCue.App
{
    /// <summary>
    /// Supporter codes of the LIVE suite: whoever has a valid one doesn't see the Buy Me a Coffee window.
    /// A code is a line "Name|YYYY-MM-DD|signature", signed (ECDSA P-256, SHA-256, raw r||s in base64url)
    /// with a private key that only the author has; the apps hold just the public key, so codes can't be
    /// made up. The same file, written the same way, is in every app of the suite: one sostenitore.txt
    /// in %APPDATA%\LIVE unlocks all of them. Plain C# 5 because LiveCount is built with the compiler
    /// that ships with .NET Framework.
    /// </summary>
    public static class SupporterCode
    {
        const string PublicKey = "qWmJhOgcKcEv2Av8MvBb0zZLxkIjETwayCjUu60-4ZEUWYXDq22FBtcNAKXaj2PPMo12cUjzuhRPyinPaUrv3g";
        public const string FileName = "sostenitore.txt";

        /// <summary>Shared by all the apps of the suite, and writable without admin rights.</summary>
        public static string SharedFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LIVE"); }
        }

        /// <summary>Name of the supporter, from sostenitore.txt next to the exe or in %APPDATA%\LIVE; null if none.</summary>
        public static string FindSupporter()
        {
            foreach (var dir in new[] { AppDomain.CurrentDomain.BaseDirectory, SharedFolder })
            {
                try
                {
                    var file = Path.Combine(dir, FileName);
                    if (!File.Exists(file)) continue;
                    var name = CheckText(File.ReadAllText(file, Encoding.UTF8));
                    if (name != null) return name;
                }
                catch { /* unreadable file: as if it weren't there */ }
            }
            return null;
        }

        /// <summary>Name in the first valid code of the text (one code per line), or null.</summary>
        public static string CheckText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            foreach (var line in text.Split('\n'))
            {
                var name = CheckLine(line);
                if (name != null) return name;
            }
            return null;
        }

        /// <summary>Saves a pasted code to %APPDATA%\LIVE\sostenitore.txt, for every app; returns the name, or null if not valid.</summary>
        public static string Save(string pasted)
        {
            var name = CheckText(pasted);
            if (name == null) return null;
            Directory.CreateDirectory(SharedFolder);
            File.WriteAllText(Path.Combine(SharedFolder, FileName),
                "Codice sostenitore della suite LIVE: grazie per il caff\u00e8!\r\n" + pasted.Trim() + "\r\n", Encoding.UTF8);
            return name;
        }

        static string CheckLine(string line)
        {
            line = line.Trim().TrimStart((char)0xFEFF);
            var cut = line.LastIndexOf('|');
            if (cut <= 0) return null;
            var message = line.Substring(0, cut);
            var first = message.IndexOf('|');
            if (first <= 0) return null;
            try
            {
                return Verify(Encoding.UTF8.GetBytes(message), FromBase64Url(line.Substring(cut + 1).Trim()))
                    ? message.Substring(0, first).Trim()
                    : null;
            }
            catch
            {
                return null; // malformed signature, or no CNG: no code
            }
        }

        static bool Verify(byte[] data, byte[] signature)
        {
            if (signature.Length != 64) return false;
            var q = FromBase64Url(PublicKey);
            // BCRYPT_ECCPUBLIC_BLOB: magic "ECS1", key length, X, Y.
            var blob = new byte[8 + 64];
            BitConverter.GetBytes(0x31534345).CopyTo(blob, 0);
            BitConverter.GetBytes(32).CopyTo(blob, 4);
            q.CopyTo(blob, 8);
            using (var key = CngKey.Import(blob, CngKeyBlobFormat.EccPublicBlob))
            using (var ecdsa = new ECDsaCng(key))
                return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }

        static byte[] FromBase64Url(string text)
        {
            var s = text.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(s + new string('=', (4 - s.Length % 4) % 4));
        }
    }
}
