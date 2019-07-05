using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Model;
using Nethereum.Hex.HexConvertors.Extensions;

namespace Nethereum.Signer
{
    public abstract class SignedTransactionBase
    {
        public static RLPSigner CreateDefaultRLPSigner(byte[] rawData)
        {
           return new RLPSigner(rawData, NUMBER_ENCODING_ELEMENTS);  
        }

        //Number of encoding elements (output for transaction)
        public const int NUMBER_ENCODING_ELEMENTS = 6;
        public static readonly BigInteger DEFAULT_GAS_PRICE = BigInteger.Parse("20000000000");
        public static readonly BigInteger DEFAULT_GAS_LIMIT = BigInteger.Parse("21000");


        protected RLPSigner SimpleRlpSigner { get; set; }

        public byte[] RawHash { get { return SimpleRlpSigner.RawHash; }}
        
        public byte[] Hash { get { return SimpleRlpSigner.Hash; }}
        
        public string HashID { get { return Hash.ToHex(true); } } //The string value of the hex
        
        public string Signed_Hex { get { return SimpleRlpSigner.GetRLPEncoded().ToHex(true); } }

        /// <summary>
        ///     The counter used to make sure each transaction can only be processed once, you may need to regenerate the
        ///     transaction if is too low or too high, simples way is to get the number of transacations
        /// </summary>
        public byte[] Nonce { get { return SimpleRlpSigner.Data[0] ?? DefaultValues.ZERO_BYTE_ARRAY;}}

        public byte[] Value { get { return SimpleRlpSigner.Data[4] ?? DefaultValues.ZERO_BYTE_ARRAY;}}

        public byte[] ReceiveAddress { get { return SimpleRlpSigner.Data[3];}}

        public byte[] GasPrice { get { return SimpleRlpSigner.Data[1] ?? DefaultValues.ZERO_BYTE_ARRAY;}}

        public byte[] GasLimit { get { return SimpleRlpSigner.Data[2];}}

        public byte[] Data { get { return SimpleRlpSigner.Data[5];}}

        public EthECDSASignature Signature { get { return SimpleRlpSigner.Signature;}}

        public abstract EthECKey Key { get;  }
            

        public byte[] GetRLPEncoded()
        {
            return SimpleRlpSigner.GetRLPEncoded();
        }

        public byte[] GetRLPEncodedRaw()
        {
            return SimpleRlpSigner.GetRLPEncodedRaw();
        }

        public virtual void Sign(EthECKey key)
        {
            SimpleRlpSigner.Sign(key);
        }

        public void SetSignature(EthECDSASignature signature)
        {
            SimpleRlpSigner.SetSignature(signature);
        }

        protected static string ToHex(byte[] x)
        {
            if (x == null) return "0x";
            return x.ToHex();
        }
#if !DOTNET35
        public abstract Task SignExternallyAsync(IEthExternalSigner externalSigner);
#endif
    }
}