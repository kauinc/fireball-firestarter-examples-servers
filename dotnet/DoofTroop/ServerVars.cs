using System.Collections.Generic;
using Newtonsoft.Json;

namespace DoofTroop
{
    /// <summary>
    /// Fireball server_vars payload (coin / money bet ladders).
    /// </summary>
    public class ServerVars
    {
        public long core_bet_default;
        public List<long> core_bet_values;
        public List<long> coin_bets;
        public Dictionary<string, int> core_currency_rates;
        public bool core_campaign_balance_not_updating;
        public long exposure_max;

        public string ToJson() => JsonConvert.SerializeObject(this);
    }

    public class ClientVars
    {
        public Dictionary<string, int> core_currency_rates;

        public string ToJson() => JsonConvert.SerializeObject(this);
    }
}
