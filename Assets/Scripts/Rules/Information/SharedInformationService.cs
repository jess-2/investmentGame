using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using InvestmentGame.Rules.Information;

namespace BlackWhale.Rules.Information
{
    // A and D use Shared DTOs. C's domain models remain inside this server adapter.
    public sealed partial class SharedInformationService : IInformationService, IProgressionService, IWindowInformationService
    {
        // 방 하나에 속한 C 정보 서비스의 상태를 묶는다. 방별로 Service를 따로 만들기
        // 때문에 같은 플레이어 ID가 다른 방에서 사용되어도 코인과 구매 내역이 섞이지 않는다.
        private sealed class RoomState
        {
            // 해당 방의 실제 C 규칙을 수행하는 내부 서비스다.
            public InformationService Service;

            // 마지막으로 처리한 턴이다. 늦게 도착한 이전 턴의 시작 신호가 현재 상태를
            // 되돌리지 못하도록 비교에 사용한다.
            public int CurrentTurn;

            // 이 방에 등록된 플레이어 목록이다. 플레이어를 한 번만 등록해 중복된 초기화가
            // 발생하지 않도록 한다.
            public readonly HashSet<string> Players = new HashSet<string>();
        }

        // 여러 네트워크 요청이 동시에 들어와도 방 조회와 C 상태 변경이 한 번에 처리되도록
        // 사용하는 잠금 객체다. 구매 차감과 구매 기록 사이에 다른 요청이 끼지 않게 한다.
        private readonly object gate = new object();

        // B가 제공하는 서버 측 시장 데이터 원본이다. BOT 전략이나 클라이언트에는 전달하지
        // 않고, 이 클래스가 공개/개인 정보 DTO를 만들 때만 읽는다.
        private readonly IMarketDataSource source;

        // RoomId를 기준으로 C 정보 서비스 상태를 분리한다. 방이 다르면 같은 P1이라도
        // 서로 다른 코인과 구매 내역을 가진다.
        private readonly Dictionary<string, RoomState> rooms = new Dictionary<string, RoomState>();

        // 시장 데이터 원본 없이는 공개 정보와 구매 결과를 만들 수 없으므로 생성 시 즉시
        // 오류를 발견한다.
        public SharedInformationService(IMarketDataSource source)
        { this.source = source ?? throw new ArgumentNullException(nameof(source)); }

        // 새 턴의 시작 때 플레이어별 코인을 지급한다. 같은 턴의 재호출은 내부 서비스가
        // 멱등적으로 처리하며, 더 오래된 턴의 신호는 현재 코인을 변경하지 않는다.
        public void GrantTurnCoins(RequestContext context, string[] playerIds)
        {
            lock (gate)
            {
                // 아직 생성되지 않은 방이면 이 시점에 방 전용 C 서비스를 만든다.
                if (!rooms.TryGetValue(context.RoomId, out var room))
                {
                    room = new RoomState { Service = new InformationService(new MarketReader(source), source.GetCompanyIds()) };
                    rooms.Add(context.RoomId, room);
                }

                // 네트워크 지연으로 이전 턴의 시작 메시지가 늦게 도착할 수 있다. 이를 무시해야
                // 이미 지급된 현재 턴의 코인과 구매 상태가 되돌아가지 않는다.
                if (context.Turn < room.CurrentTurn) return; // late turn-start replay must not change current coins

                // 새로 발견된 플레이어만 내부 C 상태에 등록한다. 이미 등록된 플레이어를
                // 다시 추가하지 않으므로 동일한 시작 메시지의 재전송에도 중복 상태가 없다.
                foreach (var id in playerIds)
                    if (room.Players.Add(id)) room.Service.AddPlayer(new PlayerInformationSeed(id, 0));

                // 실제 코인 지급의 규칙과 턴 멱등성은 InformationService가 담당한다.
                int? grant = roomSettings.TryGetValue(context.RoomId, out var options)
                    ? (context.Turn >= InformationConstants.LateGrantStartsAtTurn ? options.lateTurnCoins : options.earlyTurnCoins) : (int?)null;
                room.Service.GrantTurnCoins(context.Turn, playerIds, UsesWindowInformation, grant);
                room.CurrentTurn = context.Turn;
            }
        }

        // 정보 구매 요청을 C 내부 규칙에 전달하고, 성공 결과만 Shared DTO로 변환한다.
        // BOT도 이 메서드를 직접 또는 A의 동일한 요청 경로를 통해 사용해야 하며,
        // BOT 전용 우회 규칙은 없으며, 능력에 따른 무료 구매권은 사람과 BOT에 동일하게 적용한다.
        public RuleResult BuyInformation(RequestContext context, string companyId, int bundleIndex)
        {
            if (UsesWindowInformation) return new RuleResult("USE_WINDOW_REQUEST");
            lock (gate)
            {
                var room = rooms[context.RoomId];
                var feature = FeaturePlayer(context.RoomId, context.PlayerId);
                bool free = feature != null && feature.FreeCredits > 0;
                int beforeCoins = room.Service.GetCoins(context.PlayerId);
                var result = room.Service.BuyInformation(context.PlayerId, room.CurrentTurn, context.Phase,
                    new InformationRequest(context.RequestId, context.Turn, companyId, bundleIndex), free);
                if (!result.Success) return new RuleResult(result.ErrorCode);
                // The domain cache can replay a previous response. Consume a credit only once.
                string purchaseKey = context.Turn + ":" + context.RequestId;
                if (free && result.RemainingCoins == beforeCoins && feature.FreePurchases.Add(purchaseKey)) feature.FreeCredits--;
                return new RuleResult(payload: new ResultPayload
                {
                    companyId = result.CompanyId, bundleIndex = result.BundleIndex,
                    startDay = result.StartDay, remainingCoins = result.RemainingCoins,
                    dailyChangesBps = result.DailyChangesBps.ToArray(),
                    source = feature != null && feature.FreePurchases.Contains(purchaseKey) ? "ABILITY" : "PURCHASED"
                });
            }
        }

        // 특정 플레이어에게 보여줄 전체 화면 상태를 만든다. 공개 정보와 해당 플레이어의
        // 개인 구매 정보만 포함하며, 다른 플레이어의 구매 내역은 C 내부에서 필터링된다.
        public PlayerView BuildPlayerView(RequestContext context, OwnInvestmentState ownState)
        {
            lock (gate)
            {
                var view = rooms[context.RoomId].Service.BuildPlayerView(context.PlayerId, context.Turn);
                var result = new PlayerView
                {
                    // 수신자와 턴/phase를 함께 넣어 A/UI가 이 뷰의 대상과 시점을 확인하게 한다.
                    recipientPlayerId = context.PlayerId, turn = context.Turn, phase = context.Phase,
                    coins = view.Coins, assets = ownState.TotalAssets, entryAtOpen = UsesWindowInformation,

                    // B가 관리하는 개인 자산 입력은 새 배열과 새 Investment/Interval 객체로
                    // 복사한다. 응답을 받은 클라이언트가 값을 바꿔도 원본 상태가 오염되지 않는다.
                    selectedCompanies = (ownState.SelectedCompanies ?? Array.Empty<string>()).ToArray(),
                    ownInvestments = (ownState.Investments ?? Array.Empty<Investment>()).Select(i => new Investment
                    {
                        companyId = i.companyId, weightBps = i.weightBps,
                        intervals = (i.intervals ?? Array.Empty<Interval>()).Select(v => new Interval
                        { entryDay = v.entryDay, exitDay = v.exitDay }).ToArray()
                    }).ToArray(),

                    // C가 계산한 회사별 공개/개인 정보도 DTO와 배열을 새로 만들어 반환한다.
                    // dailyChangesBps를 그대로 공유하지 않는 것이 핵심 불변성 규칙이다.
                    companies = view.Companies.Select(c => new VisibleCompany
                    {
                        companyId = c.CompanyId,
                        visibleBundles = c.VisibleBundles.Select(b => new VisibleBundle
                        {
                            bundleIndex = b.BundleIndex, startDay = b.StartDay,
                            source = b.Source, dailyChangesBps = b.DailyChangesBps.ToArray()
                        }).ToArray()
                    }).ToArray()
                };
                if (UsesWindowInformation) PopulateWindows(context, result);
                return result;
            }
        }

        // B의 서버 시장 데이터와 C가 이해하는 IMarketInformationReader 사이의 변환기다.
        // SharedInformationService가 외부에 B의 원본 객체를 노출하지 않도록 내부에 둔다.
        private sealed class MarketReader : IMarketInformationReader
        {
            private readonly IMarketDataSource source;
            public MarketReader(IMarketDataSource source) { this.source = source; }

            // 회사의 모든 bundle을 검사하여 공개 bundle 번호만 반환한다. 개인 bundle은
            // 플레이어가 구매한 경우에만 InformationService가 별도로 보여준다.
            public IReadOnlyList<int> GetPublicBundleIndices(int turn, string companyId)
            {
                var indices = new List<int>();
                for (int i = 0; i < InformationConstants.BundleCount; i++)
                    if (Read(turn, companyId, i) is ServerInformationBundle b && b.IsPublic) indices.Add(i);
                return indices;
            }

            // 특정 bundle의 원본을 검증한 뒤 C 내부 형식으로 변환한다. 유효하지 않은
            // 시장 데이터는 false로 처리되어 구매 실패가 되며 코인은 차감되지 않는다.
            public bool TryGetBundle(int turn, string companyId, int bundleIndex, out MarketInformationBundle bundle)
            {
                var value = Read(turn, companyId, bundleIndex);
                bundle = value == null ? null : new MarketInformationBundle(companyId, bundleIndex, value.DailyChangesBps);
                return bundle != null;
            }

            // 시장 데이터가 규칙에 맞는지 확인한다. 회사/번호/시작일/5일치 변동 배열 중
            // 하나라도 맞지 않으면 null을 반환해 MARKET_UNAVAILABLE로 이어지게 한다.
            private ServerInformationBundle Read(int turn, string companyId, int index)
            {
                ServerInformationBundle value;

                // B 데이터 원본이 누락된 회사나 잘못된 번호를 예외로 알리는 경우에도
                // 네트워크 전체를 중단하지 않고 해당 구매만 실패시킨다.
                try { value = source.GetBundle(turn, companyId, index); }
                catch (KeyNotFoundException) { return null; }
                catch (ArgumentException) { return null; }
                if (value == null || value.CompanyId != companyId || value.BundleIndex != index ||
                    value.StartDay != index * InformationConstants.BundleSizeDays + 1 ||
                    value.DailyChangesBps == null || value.DailyChangesBps.Length != InformationConstants.BundleSizeDays)
                    return null;
                return value;
            }
        }
    }
}
