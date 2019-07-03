using System.Numerics;
using Nethereum.Hex.HexConvertors;
using Newtonsoft.Json;

namespace Nethereum.Hex.HexTypes
{
    [JsonConverter(typeof(HexRPCTypeJsonConverter<HexBigInteger, BigInteger>))]
    public class HexBigInteger : HexRPCType<BigInteger>
    {
        public HexBigInteger(string hex) : base(new HexBigIntegerBigEndianConvertor(), hex)
        {
        }

        public HexBigInteger(BigInteger value) : base(value, new HexBigIntegerBigEndianConvertor())
        {
        }

        public override bool Equals(object obj)
        {
            if (obj is HexBigInteger)
            {
            	HexBigInteger val = (HexBigInteger)obj;
                return val.Value == Value;
            }

            return false;
        }
        
        public override int GetHashCode()
        {
        	return base.GetHashCode();
        }

        public override string ToString()
        {
            return Value.ToString();
        }
    }
}