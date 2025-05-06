namespace GamaEdtech.Common.Security
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    public static class Cryptography
    {
        public static string EncryptAes(string plainText, string secret)
        {
            var key = Encoding.UTF8.GetBytes(secret);

            // Set up the encryption objects
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            // Encrypt the input plaintext using the AES algorithm
            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            return Convert.ToBase64String(cipherBytes);
        }

        public static string DecryptAes(string encryptedText, string secret)
        {
            var key = Encoding.UTF8.GetBytes(secret);

            // Set up the encryption objects
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            // Decrypt the input ciphertext using the AES algorithm
            using var decryptor = aes.CreateDecryptor();
            var cipherBytes = Convert.FromBase64String(encryptedText);
            var decryptedBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

            return Encoding.UTF8.GetString(decryptedBytes);
        }
    }
}
