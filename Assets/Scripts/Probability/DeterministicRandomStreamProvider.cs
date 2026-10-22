using System;

namespace AirportBaggageSimulator
{
    /// <summary>One owned sampler per domain; reconstruct this provider on every reset.</summary>
    public sealed class DeterministicRandomStreamProvider : IRandomStreamProvider
    {
        public const string Algorithm = "SplitMix64-open52-v1";
        private readonly IDistributionSampler[] streams = new IDistributionSampler[6];

        public DeterministicRandomStreamProvider(int seed)
        {
            // Fixed domain IDs, independent of enum ordering, string hashes and runtime Random.
            ulong[] domains = { 0xA0761D6478BD642FUL, 0xE7037ED1A0B428DBUL,
                0x8EBC6AF09C88C6E3UL, 0x589965CC75374CC3UL,
                0x1D8E4E27C47D124FUL, 0xEB44ACCAB455D165UL };
            for (int i = 0; i < streams.Length; i++)
                streams[i] = new DistributionSampler(unchecked((ulong)(long)seed) ^ domains[i]);
        }

        public IDistributionSampler Get(RandomStream stream)
        {
            switch (stream)
            {
                case RandomStream.Arrival: return streams[0];
                case RandomStream.Transport: return streams[1];
                case RandomStream.Weight: return streams[2];
                case RandomStream.Inspection: return streams[3];
                case RandomStream.Flagging: return streams[4];
                case RandomStream.Blockage: return streams[5];
                default: throw new ArgumentOutOfRangeException(nameof(stream));
            }
        }
    }
}
