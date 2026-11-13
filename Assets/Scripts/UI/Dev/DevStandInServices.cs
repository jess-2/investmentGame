// 개발 전용: 실제 C 서비스에 실패 응답을 주입하는 래퍼. B는 MarketService를 직접 사용한다.
using BlackWhale.Shared;

namespace BlackWhale.UI.Dev
{
    // Development-only error injection around the real C service; no duplicate coin/purchase rules.
    public sealed class DevInformationService : IInformationService
    {
        private readonly IInformationService inner;
        public string ForceNextBuyError { get; set; }
        public DevInformationService(IMarketDataSource source)
            => inner = new BlackWhale.Rules.Information.SharedInformationService(source);

        public void GrantTurnCoins(RequestContext context, string[] playerIds)
            => inner.GrantTurnCoins(context, playerIds);

        public RuleResult BuyInformation(RequestContext context, string companyId, int bundleIndex)
        {
            if (!string.IsNullOrEmpty(ForceNextBuyError))
            {
                var forced = ForceNextBuyError;
                ForceNextBuyError = null;
                return new RuleResult(forced);
            }
            return inner.BuyInformation(context, companyId, bundleIndex);
        }

        public PlayerView BuildPlayerView(RequestContext context, OwnInvestmentState ownState)
            => inner.BuildPlayerView(context, ownState);
    }
}
