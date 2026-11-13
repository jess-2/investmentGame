using System;
using System.Collections.Generic;
using System.Linq;

namespace BlackWhale.Rules
{
    // Server-only. Turn-derived seeds make generation independent of lookup order.
    public sealed class SeededMarketProvider
    {
        private readonly int seed;
        private readonly Dictionary<int, MarketData> cache = new Dictionary<int, MarketData>();
        public SeededMarketProvider(int seed) { this.seed = seed; }
        public MarketData Get(int turn)
        {
            if (turn < 1 || turn > 10) throw new ArgumentOutOfRangeException(nameof(turn));
            if (!cache.TryGetValue(turn, out var data))
            {
                var random = new Random(unchecked(seed * 397 ^ turn));
                var ids = InvestmentRules.CompanyIds;
                data = new MarketData { turn = turn, companies = ids.Select(id =>
                {
                    int daily = id == "BANK" ? 80 : id == "FOOD" ? 120 : id == "AUTO" ? 350 : id == "BIO" ? 1500 : 800;
                    int cap = id == "BANK" ? 600 : id == "FOOD" ? 800 : id == "AUTO" ? 2000 : id == "BIO" ? 8000 : 5000;
                    daily = daily * (100 + 5 * (turn - 1)) / 100;
                    var values = new int[31];
                    for (int d = 1; d <= 30; d++) values[d] = Math.Max(-cap, Math.Min(cap, values[d - 1] + random.Next(-daily, daily + 1)));
                    int first = random.Next(6), second = random.Next(5); if (second >= first) second++;
                    return new CompanyMarketData { companyId = id, dailyChangesBps = values, publicBundleIndices = new[] { first, second } };
                }).ToArray() };
                cache.Add(turn, data);
            }
            return new MarketData { turn = data.turn, companies = data.companies.Select(c => new CompanyMarketData {
                companyId = c.companyId, dailyChangesBps = (int[])c.dailyChangesBps.Clone(),
                publicBundleIndices = (int[])c.publicBundleIndices.Clone() }).ToArray() };
        }
    }
}
