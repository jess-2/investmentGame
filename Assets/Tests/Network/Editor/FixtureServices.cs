using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.Tests
{
    // A-only test doubles. These are NOT B/C implementations and never enter a player build.
    public interface ITestJson
    {
        T Decode<T>(string json);
        string Encode<T>(T value);
    }
    [Serializable] public sealed class FixtureCompany
    { public string companyId; public int[] dailyChangesBps, publicBundleIndices; }
    [Serializable] public sealed class MarketFixture { public FixtureCompany[] companies; }

    public sealed class FixtureMarketService : IMarketService
    {
        private readonly MarketFixture fixture;
        private readonly Scoreboard scriptedScoreboard;
        private readonly Dictionary<string, List<string>> selected = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, Investment[]> orders = new Dictionary<string, Investment[]>();
        public int BeginCalls, SelectCalls, SubmitCalls, LockCalls, SettlementCalls;
        private bool locked;
        public FixtureMarketService(MarketFixture fixture, Scoreboard scriptedScoreboard)
        { this.fixture = fixture; this.scriptedScoreboard = scriptedScoreboard; }
        public string[] GetCompanyIds() { return fixture.companies.Select(c => c.companyId).ToArray(); }
        public ServerInformationBundle GetBundle(int turn, string companyId, int bundleIndex)
        {
            if (turn != 1 || bundleIndex < 0 || bundleIndex > 5) throw new ArgumentOutOfRangeException();
            FixtureCompany company = fixture.companies.First(c => c.companyId == companyId);
            return new ServerInformationBundle { CompanyId = companyId, BundleIndex = bundleIndex,
                StartDay = 1 + bundleIndex * 5, IsPublic = company.publicBundleIndices.Contains(bundleIndex),
                DailyChangesBps = company.dailyChangesBps.Skip(1 + bundleIndex * 5).Take(5).ToArray() };
        }
        public void BeginTurn(RequestContext context, string[] playerIds)
        {
            BeginCalls++;
            foreach (string id in playerIds) selected[id] = new List<string>();
        }
        public RuleResult ValidateAndSelectCompany(RequestContext context, string companyId)
        {
            SelectCalls++;
            if (!fixture.companies.Any(c => c.companyId == companyId)) return new RuleResult("INVALID_COMPANY");
            if (selected[context.PlayerId].Contains(companyId)) return new RuleResult("DUPLICATE_COMPANY");
            if (selected.Values.Count(s => s.Contains(companyId)) == 2) return new RuleResult("COMPANY_FULL");
            selected[context.PlayerId].Add(companyId);
            return new RuleResult(payload: new ResultPayload { companyId = companyId });
        }
        public CompanySeat[] GetCompanySeats()
        {
            return fixture.companies.Select(c => new CompanySeat { companyId = c.companyId,
                remainingSeats = 2 - selected.Values.Count(s => s.Contains(c.companyId)) }).ToArray();
        }
        public string[] GetSelectedCompanies(string playerId) { return selected[playerId].ToArray(); }
        public RuleResult SubmitInvestment(RequestContext context, Investment[] investments)
        {
            SubmitCalls++;
            if (locked || orders.ContainsKey(context.PlayerId)) return new RuleResult("ORDER_LOCKED");
            // Deliberately minimal: A tests call boundaries; B must validate ratios, days and accounting.
            if (investments == null || investments.Length != 2 || investments.Any(i => i == null ||
                !selected[context.PlayerId].Contains(i.companyId))) return new RuleResult("COMPANY_NOT_SELECTED");
            orders.Add(context.PlayerId, investments);
            return new RuleResult(payload: new ResultPayload { locked = true, investments = investments });
        }
        public bool IsOrderLocked(string playerId) { return orders.ContainsKey(playerId); }
        public void LockAllOrders(RequestContext context)
        {
            if (orders.Count != 4) throw new InvalidOperationException("Orders must be complete before reveal.");
            LockCalls++; locked = true;
        }
        public Scoreboard SettleTurn(RequestContext context)
        {
            if (!locked) throw new InvalidOperationException("Settlement before lock.");
            SettlementCalls++;
            return scriptedScoreboard; // Tests verify forwarding only; no financial computation in A.
        }
        public OwnInvestmentState GetOwnInvestmentState(string playerId)
        {
            Investment[] own;
            ScoreRow row = SettlementCalls > 0 ? scriptedScoreboard.rows.First(r => r.playerId == playerId) : null;
            return new OwnInvestmentState { TotalAssets = row?.totalAssets ?? 10000,
                SelectedCompanies = GetSelectedCompanies(playerId),
                Investments = orders.TryGetValue(playerId, out own) ? own : new Investment[0] };
        }
        public RevealedCompany[] GetRevealedMarket(RequestContext context)
        {
            if (!locked) throw new InvalidOperationException("Private market requested before lock.");
            return fixture.companies.Select(c => new RevealedCompany { companyId = c.companyId,
                dailyChangesBps = (int[])c.dailyChangesBps.Clone() }).ToArray();
        }
    }
    public sealed class FixtureInformationService : IInformationService
    {
        private readonly IMarketDataSource source;
        public readonly Dictionary<string, int> Coins = new Dictionary<string, int>();
        public readonly Dictionary<string, HashSet<string>> Purchases = new Dictionary<string, HashSet<string>>();
        public readonly HashSet<string> Grants = new HashSet<string>();
        public int GrantCalls, BuyCalls, ViewCalls;
        public FixtureInformationService(IMarketDataSource source) { this.source = source; }
        public void GrantTurnCoins(RequestContext context, string[] playerIds)
        {
            GrantCalls++;
            if (!Grants.Add(context.RoomId + ":" + context.Turn)) return;
            foreach (string id in playerIds)
            {
                if (!Coins.ContainsKey(id)) { Coins[id] = 0; Purchases[id] = new HashSet<string>(); }
                Coins[id] += context.Turn < 6 ? 3 : 5;
            }
        }
        public RuleResult BuyInformation(RequestContext context, string companyId, int bundleIndex)
        {
            BuyCalls++;
            if (!source.GetCompanyIds().Contains(companyId)) return new RuleResult("INVALID_COMPANY");
            if (bundleIndex < 0 || bundleIndex > 5) return new RuleResult("INVALID_BUNDLE");
            ServerInformationBundle bundle = source.GetBundle(context.Turn, companyId, bundleIndex);
            string key = companyId + ":" + bundleIndex;
            if (bundle.IsPublic || Purchases[context.PlayerId].Contains(key))
                return new RuleResult("ALREADY_VISIBLE");
            if (Coins[context.PlayerId] < 1) return new RuleResult("NOT_ENOUGH_COINS");
            int[] values = bundle.DailyChangesBps;
            Coins[context.PlayerId]--; Purchases[context.PlayerId].Add(key);
            return new RuleResult(payload: new ResultPayload { companyId = companyId, bundleIndex = bundleIndex,
                startDay = 1 + bundleIndex * 5, dailyChangesBps = values, remainingCoins = Coins[context.PlayerId] });
        }
        public PlayerView BuildPlayerView(RequestContext context, OwnInvestmentState ownState)
        {
            ViewCalls++;
            return new PlayerView { recipientPlayerId = context.PlayerId, turn = context.Turn, phase = context.Phase,
                assets = ownState.TotalAssets, coins = Coins[context.PlayerId], ownInvestments = ownState.Investments,
                selectedCompanies = ownState.SelectedCompanies,
                companies = source.GetCompanyIds().Select(id => new VisibleCompany { companyId = id,
                    visibleBundles = Enumerable.Range(0, 6).Select(i => source.GetBundle(context.Turn, id, i))
                        .Where(b => b.IsPublic || Purchases[context.PlayerId].Contains(id + ":" + b.BundleIndex))
                        .Select(b => new VisibleBundle { bundleIndex = b.BundleIndex, startDay = b.StartDay,
                            source = b.IsPublic ? "PUBLIC" : "PURCHASED", dailyChangesBps = b.DailyChangesBps }).ToArray() }).ToArray() };
        }
    }
}
