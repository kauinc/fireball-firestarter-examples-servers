using System.Collections.Generic;

namespace DoofTroop.Paytable
{
    public class BalancingConfig
    {
        public decimal Rtp { get; set; }
        public Dictionary<string, ScopeMultipliers> Standard { get; set; }
        public Dictionary<string, decimal> Special { get; set; }
    }

    public class ScopeMultipliers
    {
        public decimal Top3 { get; set; }
        public decimal Top2 { get; set; }
        public decimal First { get; set; }

        public decimal Get(string scope) => scope switch
        {
            "top3" => Top3,
            "top2" => Top2,
            _ => First
        };
    }
}
