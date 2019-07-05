using System.Linq;
using System.Text;
using Nethereum.Hex.HexConvertors.Extensions;
using NBitcoin.Altcoins.HashX11.Crypto.SHA3;

namespace Nethereum.Util
{
    public class Sha3Keccack
    {
        public static Sha3Keccack Current = new Sha3Keccack();

        public string CalculateHash(string value)
        {
            var input = Encoding.UTF8.GetBytes(value);
            var output = CalculateHash(input);
            return output.ToHex();
        }

        public string CalculateHashFromHex(params string[] hexValues)
        {
            var joinedHex = string.Join("", hexValues.Select(x => x.RemoveHexPrefix()).ToArray());
            return CalculateHash(joinedHex.HexToByteArray()).ToHex();
        }

        public byte[] CalculateHash(byte[] value)
        {
        	var digest = new Keccak256();
            var output = new byte[digest.BlockSize];
            digest.TransformBytes(value,0,value.Length);
            var result = digest.TransformFinal();
            output = result.GetBytes();
            return output;
        }
    }
}