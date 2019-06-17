/*
 * Created by SharpDevelop.
 * User: David
 * Date: 5/7/2018
 * Time: 8:10 PM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */

using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;

//This program requires .NETFramework v4.5

namespace NebliDex_Linux
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App
	{
		//So this is how our encryption will work
		//Clients make RSA asymmetric key pair
		//They will send their public key along with a signature of the public key to validation node
		//The validation node will test the authenticity of the signature
		//It will then send it's AES symmetric key encrypted with the RSA asymmetric public key
		//Client will decrypt symmetric key with private key and send message back encrypted with symmetric key
		//validation node will decrypt message with symmetric key
			
		static bool _optimalAsymmetricEncryptionPadding = true; //RSA needs padding for security
		
        public static string AESEncrypt(string inText, string key)
        {
        	if(inText.Length == 0){return "";}
            byte[] bytesBuff = Encoding.Unicode.GetBytes(inText);
            using (Aes aes = Aes.Create())
            {
                Rfc2898DeriveBytes crypto = new Rfc2898DeriveBytes(key, new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                aes.Key = crypto.GetBytes(32);
                aes.IV = crypto.GetBytes(16);
                using (MemoryStream mStream = new MemoryStream())
                {
                    using (CryptoStream cStream = new CryptoStream(mStream, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cStream.Write(bytesBuff, 0, bytesBuff.Length);
                        cStream.Close();
                    }
                    inText = Convert.ToBase64String(mStream.ToArray(),Base64FormattingOptions.None); //No line breaks
                }
            }
            return inText;
        }
        
		//Decrypting a string
        public static string AESDecrypt(string cryptTxt,string key)
        {
        	if(cryptTxt.Length == 0){return "";}
            cryptTxt = cryptTxt.Replace(" ", "+");
            byte[] bytesBuff = Convert.FromBase64String(cryptTxt);
            using (Aes aes = Aes.Create())
            {
                Rfc2898DeriveBytes crypto = new Rfc2898DeriveBytes(key, new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                aes.Key = crypto.GetBytes(32);
                aes.IV = crypto.GetBytes(16);
                using (MemoryStream mStream = new MemoryStream())
                {
                    using (CryptoStream cStream = new CryptoStream(mStream, aes.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        cStream.Write(bytesBuff, 0, bytesBuff.Length);
                        cStream.Close();
                    }
                    cryptTxt = Encoding.Unicode.GetString(mStream.ToArray());
                }
            }
            return cryptTxt;
        }
        
        public static string[] GenerateRSAKeys(int keySize)
        {
            if (keySize % 2 != 0 || keySize < 512)
                throw new Exception("Key should be multiple of two and greater than 512.");

            string[] response = new string[2];

            using (RSACryptoServiceProvider provider = new RSACryptoServiceProvider(keySize))
            {
                string publicKey = provider.ToXmlString(false);
                string privateKey = provider.ToXmlString(true);

                string publicKeyWithSize= IncludeKeyInEncryptionString(publicKey, keySize);
                string privateKeyWithSize = IncludeKeyInEncryptionString(privateKey, keySize);

                response[0] = publicKeyWithSize;
                response[1] = privateKeyWithSize;
            }

            return response;
        }

        public static string EncryptRSAText(string text, string publicKey)
        {
        	if(text.Length == 0){return "";}
            int keySize = 0;
            string publicKeyXml = "";

            GetKeyFromEncryptionString(publicKey, out keySize, out publicKeyXml);

            byte[] encrypted = RSAEncrypt(Encoding.UTF8.GetBytes(text), keySize, publicKeyXml);
            return Convert.ToBase64String(encrypted);
        }

        private static byte[] RSAEncrypt(byte[] data, int keySize, string publicKeyXml)
        {
            if (data == null || data.Length == 0) throw new ArgumentException("Data are empty", "data");
            int maxLength = GetMaxDataLength(keySize);
            if (data.Length > maxLength) throw new ArgumentException(String.Format("Maximum data length is {0}", maxLength), "data");
            if (!IsKeySizeValid(keySize)) throw new ArgumentException("Key size is not valid", "keySize");
            if (String.IsNullOrEmpty(publicKeyXml)) throw new ArgumentException("Key is null or empty", "publicKeyXml");

            using (RSACryptoServiceProvider provider = new RSACryptoServiceProvider(keySize))
            {
                provider.FromXmlString(publicKeyXml);
                return provider.Encrypt(data, _optimalAsymmetricEncryptionPadding);
            }
        }

        public static string DecryptRSAText(string text, string privateKey)
        {
        	if(text.Length == 0){return "";}
            int keySize = 0;
            string publicAndPrivateKeyXml = "";

            GetKeyFromEncryptionString(privateKey, out keySize, out publicAndPrivateKeyXml);

            byte[] decrypted = RSADecrypt(Convert.FromBase64String(text), keySize, publicAndPrivateKeyXml);
            return Encoding.UTF8.GetString(decrypted);
        }

        private static byte[] RSADecrypt(byte[] data, int keySize, string publicAndPrivateKeyXml)
        {
            if (data == null || data.Length == 0) throw new ArgumentException("Data are empty", "data");
            if (!IsKeySizeValid(keySize)) throw new ArgumentException("Key size is not valid", "keySize");
            if (String.IsNullOrEmpty(publicAndPrivateKeyXml)) throw new ArgumentException("Key is null or empty", "publicAndPrivateKeyXml");

            using (RSACryptoServiceProvider provider = new RSACryptoServiceProvider(keySize))
            {
                provider.FromXmlString(publicAndPrivateKeyXml);
                return provider.Decrypt(data, _optimalAsymmetricEncryptionPadding);
            }
        }

        public static int GetMaxDataLength(int keySize)
        {
            if (_optimalAsymmetricEncryptionPadding)
            {
                return ((keySize - 384) / 8) + 7;
            }
            return ((keySize - 384) / 8) + 37;
        }

        public static bool IsKeySizeValid(int keySize)
        {
            return keySize >= 384 &&
                    keySize <= 16384 &&
                    keySize % 8 == 0;
        }

        private static string IncludeKeyInEncryptionString(string publicKey, int keySize)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes( keySize.ToString() + "!" + publicKey));
        }

        private static void GetKeyFromEncryptionString(string rawkey, out int keySize, out string xmlKey)
        {
            keySize = 0;
            xmlKey = "";

            if (rawkey != null && rawkey.Length > 0)
            {
                byte[] keyBytes = Convert.FromBase64String(rawkey);
                string stringKey = Encoding.UTF8.GetString(keyBytes);

                if (stringKey.Contains("!"))
                {
                	string[] splittedValues = stringKey.Split(new char[] { '!' }, 2);

                    try
                    {
                        keySize = int.Parse(splittedValues[0]);
                        xmlKey = splittedValues[1];
                    }
                    catch (Exception) { 
                    	throw;
                    }
                }
            }
        }
	}
	
}