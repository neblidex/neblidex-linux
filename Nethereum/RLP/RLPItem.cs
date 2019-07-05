namespace Nethereum.RLP
{
    public class RLPItem : IRLPElement
    {
        private readonly byte[] rlpData;

        public RLPItem(byte[] rlpData)
        {
            this.rlpData = rlpData;
        }

        public byte[] RLPData { get { return GetRLPData();}}

        private byte[] GetRLPData()
        {
            return rlpData.Length == 0 ? null : rlpData;
        }
    }
}