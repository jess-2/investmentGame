using System;
using System.Collections.Generic;
using System.Linq;

namespace InvestmentGame.Rules.Information
{
    /// <summary>
    /// 정보와 코인 상태의 최종 변경 책임을 가지는 순수 C# 규칙 서비스이다.
    /// UI, GameObject, RPC를 직접 참조하지 않아 A가 서버 흐름에 연결하고 D가 결과를 표시할 수 있다.
    /// </summary>
    public sealed class InformationService
    {
        /// <summary>
        /// 한 플레이어의 서버 상태이다. 코인, 턴별 구매 원장, 재전송 캐시를 플레이어별로 분리한다.
        /// </summary>
        private sealed class PlayerState
        {
            // C가 최종 변경하는 현재 황금 코인 잔액이다.
            public int Coins;
            public readonly Dictionary<int, int> CoinsByTurn = new Dictionary<int, int>();
            // 이미 코인을 지급한 턴을 기록해 같은 턴의 재호출을 멱등적으로 처리한다.
            public readonly HashSet<string> GrantedTurns = new HashSet<string>();
            // 구매 원장을 턴별로 분리해 이전 턴의 열람 권한이 새 시장으로 넘어가지 않게 한다.
            public readonly Dictionary<int, HashSet<BundleKey>> PurchasedByTurn = new Dictionary<int, HashSet<BundleKey>>();
            // 턴/requestId별 결과를 보관해 다음 턴의 같은 ID와 구분한다.
            public readonly Dictionary<string, CachedRequest> Requests = new Dictionary<string, CachedRequest>();
            public readonly Dictionary<string, string> Sources = new Dictionary<string, string>();
            public readonly HashSet<string> Rewards = new HashSet<string>();
        }

        /// <summary>요청 ID와 요청 내용 지문, 당시 결과를 함께 보관하는 재전송 캐시 항목이다.</summary>
        private sealed class CachedRequest
        {
            public CachedRequest(string fingerprint, InformationBuyResponse response)
            {
                Fingerprint = fingerprint;
                Response = response;
            }

            public string Fingerprint { get; }
            public InformationBuyResponse Response { get; }
        }

        /// <summary>
        /// 기업과 묶음 번호를 하나의 값 키로 묶는다.
        /// HashSet에서 같은 턴의 동일 묶음 중복 구매를 확인하기 위해 사용한다.
        /// </summary>
        private struct BundleKey : IEquatable<BundleKey>
        {
            public BundleKey(string companyId, int bundleIndex)
            {
                CompanyId = companyId;
                BundleIndex = bundleIndex;
            }

            public string CompanyId { get; }
            public int BundleIndex { get; }

            public bool Equals(BundleKey other) => BundleIndex == other.BundleIndex && CompanyId == other.CompanyId;
            public override bool Equals(object obj) => obj is BundleKey && Equals((BundleKey)obj);
            public override int GetHashCode() => (CompanyId?.GetHashCode() ?? 0) * 397 ^ BundleIndex;
        }

        // 여러 요청이 동시에 들어와도 코인 차감과 구매 기록이 분리되지 않도록 잠금에 사용한다.
        private readonly object sync = new object();
        // B의 시장 원본을 읽는 의존성이다. C는 시장을 생성하지 않는다.
        private readonly IMarketInformationReader market;
        // 구매 요청으로 허용할 기업 ID 목록이다.
        private readonly IReadOnlyList<string> companyIds;
        // 플레이어 ID를 개인 상태로 연결하는 서버 메모리 저장소이다.
        private readonly Dictionary<string, PlayerState> players = new Dictionary<string, PlayerState>();

        /// <summary>
        /// 시장 조회기와 기업 목록, 필요하면 fixture 또는 복원 상태의 플레이어를 주입한다.
        /// </summary>
        public InformationService(IMarketInformationReader market, IEnumerable<string> companyIds,
            IEnumerable<PlayerInformationSeed> initialPlayers = null)
        {
            this.market = market ?? throw new ArgumentNullException(nameof(market));
            this.companyIds = new List<string>(companyIds ?? throw new ArgumentNullException(nameof(companyIds))).AsReadOnly();
            if (this.companyIds.Count == 0) throw new ArgumentException("At least one company is required.", nameof(companyIds));

            if (initialPlayers != null)
            {
                // 초기화도 AddPlayer를 사용해 일반 등록과 복원 규칙을 한 곳으로 유지한다.
                foreach (var seed in initialPlayers) AddPlayer(seed);
            }
        }

        /// <summary>
        /// 새로운 플레이어의 코인·지급·구매 원장을 등록한다.
        /// 기존 플레이어를 덮어쓰지 않는 이유는 코인과 구매 기록이 사라지는 것을 막기 위해서다.
        /// </summary>
        public void AddPlayer(PlayerInformationSeed seed)
        {
            if (seed == null || string.IsNullOrWhiteSpace(seed.PlayerId)) throw new ArgumentException("A player ID is required.", nameof(seed));
            lock (sync)
            {
                if (players.ContainsKey(seed.PlayerId)) throw new InvalidOperationException("Player already exists.");
                var state = new PlayerState { Coins = seed.Coins };
                // 복원된 지급 원장을 먼저 등록해 초기 로드 후 중복 지급을 막는다.
                foreach (var turn in seed.AlreadyGrantedTurns ?? Array.Empty<int>()) state.GrantedTurns.Add(GrantKey(turn));
                if (seed.AlreadyGrantedTurns.Count > 0) state.CoinsByTurn[seed.AlreadyGrantedTurns.Max()] = seed.Coins;
                // 구매 원장은 턴별 HashSet으로 복원해 해당 턴의 공개 범위를 그대로 재현한다.
                foreach (var purchased in seed.PurchasedBundles ?? Array.Empty<PurchasedInformationRecord>())
                {
                    if (!state.PurchasedByTurn.TryGetValue(purchased.Turn, out var records))
                    {
                        records = new HashSet<BundleKey>();
                        state.PurchasedByTurn.Add(purchased.Turn, records);
                    }
                    records.Add(new BundleKey(purchased.CompanyId, purchased.BundleIndex));
                }
                players.Add(seed.PlayerId, state);
            }
        }

        /// <summary>
        /// 턴 시작 코인을 지급한다.
        /// 같은 턴을 다시 호출해도 지급 원장이 이미 있으면 잔액을 변경하지 않는다.
        /// </summary>
        public CoinGrantResult GrantTurnCoins(int turn, IEnumerable<string> playerIds, bool perDay = false, int? coinGrant = null)
        {
            if (turn < 1) throw new ArgumentOutOfRangeException(nameof(turn));
            if (coinGrant.HasValue && (coinGrant < 0 || coinGrant > 999)) throw new ArgumentOutOfRangeException(nameof(coinGrant));
            if (playerIds == null) throw new ArgumentNullException(nameof(playerIds));
            var ids = playerIds.ToList();
            lock (sync)
            {
                CoinGrantResult last = null;
                foreach (var playerId in ids)
                {
                    var state = GetPlayer(playerId);
                    var key = GrantKey(turn);
                    // HashSet.Add가 true인 최초 호출에서만 지급한다.
                    var granted = state.GrantedTurns.Add(key);
                    // rules-v2에 따라 6턴부터 5개, 그 전에는 3개를 지급한다.
                    if (granted) state.Coins += coinGrant ?? (turn >= InformationConstants.LateGrantStartsAtTurn
                        ? (perDay ? InformationConstants.DailyLateTurnCoinGrant : InformationConstants.LateTurnCoinGrant)
                        : (perDay ? InformationConstants.DailyEarlyTurnCoinGrant : InformationConstants.EarlyTurnCoinGrant));
                    if (granted) state.CoinsByTurn[turn] = state.Coins;
                    last = new CoinGrantResult(playerId, turn, state.Coins, granted);
                }
                return last;
            }
        }

        /// <summary>여러 플레이어의 지급 결과를 플레이어 ID별로 반환하는 편의 메서드이다.</summary>
        public IReadOnlyDictionary<string, CoinGrantResult> GrantTurnCoinsForAll(int turn, IEnumerable<string> playerIds)
        {
            var result = new Dictionary<string, CoinGrantResult>();
            foreach (var playerId in playerIds)
            {
                result[playerId] = GrantTurnCoins(turn, new[] { playerId });
            }
            return result;
        }

        /// <summary>
        /// 정보 구매를 검증하고 성공 시 코인 차감과 구매 원장을 하나의 잠금 안에서 확정한다.
        /// 검증 실패나 시장 조회 실패에서는 어떤 상태도 변경하지 않는다.
        /// </summary>
        public InformationBuyResponse BuyInformation(string playerId, int currentTurn, string phase, InformationRequest request,
            bool free = false)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            lock (sync)
            {
                var state = GetPlayer(playerId);
                // 지문은 같은 requestId로 다른 기업·묶음을 보내는 요청 변조를 감지한다.
                var fingerprint = request.Turn + "|" + request.CompanyId + "|" + request.BundleIndex;
                var requestKey = request.Turn + ":" + request.RequestId;
                if (!string.IsNullOrEmpty(request.RequestId) && state.Requests.TryGetValue(requestKey, out var cached))
                {
                    // 동일 요청 재전송은 이전 결과의 복사본만 반환하여 두 번째 차감을 막는다.
                    if (cached.Fingerprint != fingerprint) return InformationBuyResponse.Failed("REQUEST_ID_CONFLICT", state.Coins);
                    return cached.Response.CopyOf();
                }

                InformationBuyResponse response;
                // A를 거치지 않은 직접 호출에도 C의 도메인 조건이 항상 적용되도록 다시 검증한다.
                if (!string.Equals(phase, "INFO", StringComparison.Ordinal)) response = InformationBuyResponse.Failed("WRONG_PHASE", state.Coins);
                else if (request.Turn != currentTurn) response = InformationBuyResponse.Failed("STALE_TURN", state.Coins);
                else if (!companyIds.Contains(request.CompanyId)) response = InformationBuyResponse.Failed("INVALID_COMPANY", state.Coins);
                else if (request.BundleIndex < 0 || request.BundleIndex >= InformationConstants.BundleCount) response = InformationBuyResponse.Failed("INVALID_BUNDLE", state.Coins);
                else if (IsVisible(currentTurn, state, request.CompanyId, request.BundleIndex)) response = InformationBuyResponse.Failed("ALREADY_VISIBLE", state.Coins);
                else if (!free && state.Coins < InformationConstants.InformationCostCoins) response = InformationBuyResponse.Failed("NOT_ENOUGH_COINS", state.Coins);
                else if (!market.TryGetBundle(currentTurn, request.CompanyId, request.BundleIndex, out var bundle)) response = InformationBuyResponse.Failed("MARKET_UNAVAILABLE", state.Coins);
                else
                {
                    // 여기까지 모든 검사를 통과한 뒤에만 원장과 코인을 함께 변경한다.
                    if (!state.PurchasedByTurn.TryGetValue(currentTurn, out var records))
                    {
                        records = new HashSet<BundleKey>();
                        state.PurchasedByTurn.Add(currentTurn, records);
                    }
                    if (!free) state.Coins -= InformationConstants.InformationCostCoins;
                    state.CoinsByTurn[currentTurn] = state.Coins;
                    records.Add(new BundleKey(request.CompanyId, request.BundleIndex));
                    state.Sources[currentTurn + ":" + request.CompanyId + ":" + request.BundleIndex] = free ? "ABILITY" : "PURCHASED";
                    // 구매자에게 필요한 묶음만 반환하고 전체 시장 원본은 반환하지 않는다.
                    response = InformationBuyResponse.Succeeded(bundle, state.Coins);
                }

                // 실패도 캐시하여 동일 요청 재전송이 상태를 다시 평가하지 않게 한다.
                if (!string.IsNullOrEmpty(request.RequestId)) state.Requests[requestKey] = new CachedRequest(fingerprint, response.CopyOf());
                return response;
            }
        }

        /// <summary>
        /// 특정 플레이어가 특정 턴에 볼 수 있는 개인뷰를 생성한다.
        /// 공개 묶음은 모두에게 동일하게, 구매 묶음은 요청 플레이어의 해당 턴 원장에서만 가져온다.
        /// </summary>
        public PlayerInformationView BuildPlayerView(string playerId, int turn)
        {
            lock (sync)
            {
                var state = GetPlayer(playerId);
                // 이전 턴 원장은 보존하지만 현재 턴 개인뷰에는 섞지 않는다.
                var purchased = state.PurchasedByTurn.TryGetValue(turn, out var records) ? records : new HashSet<BundleKey>();
                var views = new List<CompanyInformationView>();
                foreach (var companyId in companyIds)
                {
                    var bundles = new List<VisibleInformationBundle>();
                    // 무료 공개 위치는 B의 결과를 그대로 사용하며 플레이어별로 재추첨하지 않는다.
                    foreach (var index in market.GetPublicBundleIndices(turn, companyId))
                    {
                        if (TryCopyBundle(turn, companyId, index, out var bundle)) bundles.Add(new VisibleInformationBundle(bundle, "PUBLIC"));
                    }
                    foreach (var record in purchased.Where(x => x.CompanyId == companyId).OrderBy(x => x.BundleIndex))
                    {
                        if (TryCopyBundle(turn, record.CompanyId, record.BundleIndex, out var bundle))
                            bundles.Add(new VisibleInformationBundle(bundle, state.Sources.TryGetValue(turn + ":" + record.CompanyId + ":" + record.BundleIndex,
                                out var source) ? source : "PURCHASED"));
                    }
                    views.Add(new CompanyInformationView(companyId, bundles.OrderBy(x => x.BundleIndex).ToArray()));
                }
                int coins = state.CoinsByTurn.TryGetValue(turn, out var recordedCoins) ? recordedCoins : state.Coins;
                return new PlayerInformationView(turn, coins, views);
            }
        }

        /// <summary>현재 서버 코인 잔액을 읽기 전용으로 반환한다.</summary>
        public int GetCoins(string playerId)
        {
            lock (sync) return GetPlayer(playerId).Coins;
        }

        // Called while the adapter holds its room transaction lock, after all window validation.
        internal bool SpendWindowCoins(string playerId, int turn, int amount)
        {
            lock (sync)
            {
                var state = GetPlayer(playerId);
                if (amount < 0 || state.Coins < amount) return false;
                state.Coins -= amount;
                state.CoinsByTurn[turn] = state.Coins;
                return true;
            }
        }

        /// <summary>특정 플레이어·턴의 구매 원장 개수를 반환한다.</summary>
        public int GetPurchasedBundleCount(string playerId, int turn)
        {
            lock (sync) return players[playerId].PurchasedByTurn.TryGetValue(turn, out var records) ? records.Count : 0;
        }

        /// <summary>
        /// 공개 묶음 또는 같은 턴에 본인이 구매한 묶음이면 이미 보이는 정보로 판정한다.
        /// 이 검사가 있어 공개 정보를 구매하거나 같은 묶음을 중복 차감하지 않는다.
        /// </summary>
        public void RewardCoins(string playerId, int turn, string key, int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            lock (sync)
            {
                var state = GetPlayer(playerId);
                if (!state.Rewards.Add(turn + ":" + key)) return;
                state.Coins += amount; state.CoinsByTurn[turn] = state.Coins;
            }
        }
        private bool IsVisible(int turn, PlayerState state, string companyId, int bundleIndex)
        {
            if (market.GetPublicBundleIndices(turn, companyId).Contains(bundleIndex)) return true;
            return state.PurchasedByTurn.TryGetValue(turn, out var records) && records.Contains(new BundleKey(companyId, bundleIndex));
        }

        /// <summary>시장 원본을 복사해 개인뷰에 넣고, 조회 실패 묶음은 추가하지 않는다.</summary>
        private bool TryCopyBundle(int turn, string companyId, int bundleIndex, out MarketInformationBundle bundle)
        {
            if (market.TryGetBundle(turn, companyId, bundleIndex, out var source))
            {
                bundle = source.CopyOf();
                return true;
            }
            bundle = null;
            return false;
        }

        /// <summary>등록되지 않은 플레이어의 접근을 즉시 차단한다.</summary>
        private PlayerState GetPlayer(string playerId)
        {
            if (!players.TryGetValue(playerId, out var state)) throw new KeyNotFoundException("Unknown player: " + playerId);
            return state;
        }

        /// <summary>지급 원장에 사용하는 턴 키를 일관된 형식으로 만든다.</summary>
        private static string GrantKey(int turn) => "TURN:" + turn;
    }

    /// <summary>
    /// 서비스 시작 시 플레이어 상태를 복원하거나 fixture를 주입하기 위한 입력 모델이다.
    /// 이미 지급된 턴과 구매 기록을 함께 넘겨야 재로드 후 중복 지급이 발생하지 않는다.
    /// </summary>
    public sealed class PlayerInformationSeed
    {
        public PlayerInformationSeed(string playerId, int coins, IEnumerable<int> alreadyGrantedTurns = null,
            IEnumerable<PurchasedInformationRecord> purchasedBundles = null)
        {
            PlayerId = playerId;
            Coins = coins;
            AlreadyGrantedTurns = (alreadyGrantedTurns ?? Array.Empty<int>()).ToArray();
            PurchasedBundles = (purchasedBundles ?? Array.Empty<PurchasedInformationRecord>()).ToArray();
        }

        public string PlayerId { get; }
        public int Coins { get; }
        public IReadOnlyList<int> AlreadyGrantedTurns { get; }
        public IReadOnlyList<PurchasedInformationRecord> PurchasedBundles { get; }
    }

    /// <summary>특정 턴에 특정 기업의 묶음을 구매했다는 최소 원장 기록이다.</summary>
    public sealed class PurchasedInformationRecord
    {
        public PurchasedInformationRecord(int turn, string companyId, int bundleIndex)
        {
            Turn = turn;
            CompanyId = companyId;
            BundleIndex = bundleIndex;
        }

        public int Turn { get; }
        public string CompanyId { get; }
        public int BundleIndex { get; }
    }
}
