using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using InvestmentGame.Rules.Information;

namespace BlackWhale.Rules.Information
{
    // C's progression ledger. No assets are duplicated here: all costs are committed by B.
    public sealed partial class SharedInformationService
    {
        private sealed class FeatureState
        {
            public string[] Candidates;
            public string Ability = "";
            public int ReusableAtTurn = 1, FreeCredits;
            public readonly List<string> Inventory = new List<string>();
            public readonly HashSet<string> FreePurchases = new HashSet<string>();
            public AuctionState Bid;
        }
        private sealed class FeatureRoom
        {
            public string[] InitialOrder;
            public int Turn;
            public readonly Dictionary<string, FeatureState> Players = new Dictionary<string, FeatureState>();
            public readonly HashSet<int> ClosedAuctions = new HashSet<int>();
            public readonly HashSet<int> RewardedTurns = new HashSet<int>();
            public readonly List<PublicEvent> Events = new List<PublicEvent>();
            public string[] FinalSeats;
        }
        private static readonly string[] Abilities = { "POLITICIAN", "SYNDICATE", "INFORMATION_BROKER", "CORPORATE_RAIDER", "GAMBLER" };
        private static readonly string[] SeatNames = { "A", "B", "C", "D" };
        private readonly Dictionary<string, FeatureRoom> features = new Dictionary<string, FeatureRoom>();

        private FeatureState FeaturePlayer(string roomId, string id)
            => features.TryGetValue(roomId, out var room) && room.Players.TryGetValue(id, out var player) ? player : null;

        public void PrepareGame(string roomId, string[] initialOrder)
        {
            lock (gate)
            {
                if (features.ContainsKey(roomId)) return;
                if (!(source is IMarketProgression)) throw new InvalidOperationException("B cost ledger is required.");
                if (initialOrder == null || initialOrder.Length != 4 || initialOrder.Distinct().Count() != 4)
                    throw new ArgumentException("Four distinct players required.");
                var feature = new FeatureRoom { InitialOrder = (string[])initialOrder.Clone() };
                var random = new Random(BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0));
                foreach (var id in initialOrder)
                {
                    var candidates = (string[])Abilities.Clone();
                    for (int i = candidates.Length - 1; i > 0; i--)
                    { int j = random.Next(i + 1); var temp = candidates[i]; candidates[i] = candidates[j]; candidates[j] = temp; }
                    feature.Players.Add(id, new FeatureState { Candidates = candidates.Take(3).ToArray() });
                }
                if (!rooms.TryGetValue(roomId, out var room))
                {
                    room = new RoomState { Service = new InformationService(new MarketReader(source), source.GetCompanyIds()) };
                    rooms.Add(roomId, room);
                }
                foreach (var id in initialOrder)
                    if (room.Players.Add(id)) room.Service.AddPlayer(new PlayerInformationSeed(id, 0));
                features.Add(roomId, feature);
            }
        }
        public RuleResult SelectAbility(RequestContext context, string abilityId)
        {
            lock (gate)
            {
                var p = FeaturePlayer(context.RoomId, context.PlayerId);
                if (context.Phase != Protocol.AbilitySelection) return new RuleResult("WRONG_PHASE");
                if (context.Turn != 0) return new RuleResult("STALE_TURN");
                if (p == null || !p.Candidates.Contains(abilityId)) return new RuleResult("INVALID_ABILITY");
                if (p.Ability != "") return new RuleResult("ABILITY_LOCKED");
                SetAbility(p, abilityId);
                return new RuleResult();
            }
        }
        private static void SetAbility(FeatureState p, string id)
        {
            p.Ability = id;
            if (id == "SYNDICATE") p.Inventory.Add("INFORMATION_STEAL");
        }
        public bool AllAbilitiesSelected(string roomId)
        { lock (gate) return features[roomId].Players.Values.All(p => p.Ability != ""); }
        public void CompleteAbilitySelection(string roomId)
        {
            lock (gate) foreach (var p in features[roomId].Players.Values)
                if (p.Ability == "") SetAbility(p, p.Candidates[0]);
        }
        public void BeginTurn(RequestContext context)
        {
            lock (gate)
            {
                var room = features[context.RoomId];
                if (context.Turn <= room.Turn) return;
                room.Turn = context.Turn;
                foreach (var p in room.Players)
                {
                    p.Value.FreeCredits = p.Value.Ability == "INFORMATION_BROKER" ? (UsesWindowInformation ? 5 : 1) : 0;
                    p.Value.Bid = new AuctionState { maximumBid = ((IMarketProgression)source).GetTurnStartAssets(p.Key) / 10,
                        seatPreferences = (string[])SeatNames.Clone() };
                }
            }
        }
        public RuleResult SubmitBid(RequestContext context, CommandPayload payload)
        {
            lock (gate)
            {
                var room = features[context.RoomId];
                if (context.Turn != room.Turn) return new RuleResult("STALE_TURN");
                if (context.Phase != Protocol.Auction || (context.Turn != 3 && context.Turn != 6 && context.Turn != 9))
                    return new RuleResult("WRONG_PHASE");
                if (payload == null || !room.Players.TryGetValue(context.PlayerId, out var p)) return new RuleResult("INVALID_REQUEST");
                if (p.Bid.confirmed) return new RuleResult("BID_LOCKED");
                if (payload.bidAmount < 0 || payload.bidAmount > p.Bid.maximumBid) return new RuleResult("INVALID_BID");
                if (payload.useFreePass && (context.Turn != 9 || !p.Inventory.Contains("FREE_PASS"))) return new RuleResult("INVALID_ITEM");
                var prefs = payload.seatPreferences;
                p.Bid = new AuctionState { confirmed = true, bidAmount = payload.bidAmount, maximumBid = p.Bid.maximumBid,
                    useFreePass = payload.useFreePass,
                    seatPreferences = prefs != null && prefs.Length == 4 && prefs.Distinct().Count() == 4 && prefs.All(SeatNames.Contains)
                        ? (string[])prefs.Clone() : (string[])SeatNames.Clone() };
                return new RuleResult();
            }
        }
        public bool AllBidsConfirmed(RequestContext context)
        { lock (gate) return features[context.RoomId].Players.Values.All(p => p.Bid.confirmed); }
        public string[] CloseAuction(RequestContext context)
        {
            lock (gate)
            {
                var room = features[context.RoomId];
                if (room.ClosedAuctions.Contains(context.Turn)) return room.FinalSeats == null ? null : (string[])room.FinalSeats.Clone();
                if (context.Turn != room.Turn || context.Phase != Protocol.Auction ||
                    (context.Turn != 3 && context.Turn != 6 && context.Turn != 9)) throw new InvalidOperationException("Not an auction.");
                var economy = (IMarketProgression)source;
                // decimal avoids overflow of long bid amounts multiplied by 125.
                var ranked = room.InitialOrder.OrderByDescending(id => (decimal)room.Players[id].Bid.bidAmount *
                        (room.Players[id].Bid.useFreePass ? 125 : 100))
                    .ThenBy(economy.GetTurnStartAssets).ThenBy(id => Array.IndexOf(room.InitialOrder, id)).ToArray();
                var payers = context.Turn == 9 ? ranked : room.Players[ranked[0]].Bid.bidAmount == 0 ? Array.Empty<string>() : ranked.Take(1).ToArray();
                foreach (var id in payers)
                {
                    var result = economy.CommitCost(new RequestContext(context.RoomId, context.Turn, context.Phase, id, "auction-cost"), room.Players[id].Bid.bidAmount);
                    if (!result.Success) throw new InvalidOperationException("B rejected validated auction cost: " + result.ErrorCode);
                }
                if (context.Turn == 9)
                {
                    room.FinalSeats = new string[4];
                    foreach (var id in ranked)
                    {
                        var bid = room.Players[id].Bid;
                        int seat = bid.seatPreferences.Select(s => Array.IndexOf(SeatNames, s)).First(i => room.FinalSeats[i] == null);
                        room.FinalSeats[seat] = id;
                        if (bid.useFreePass) room.Players[id].Inventory.Remove("FREE_PASS");
                        room.Events.Add(new PublicEvent { turn = context.Turn, kind = "AUCTION_SEAT", playerId = id, detail = SeatNames[seat] });
                    }
                }
                else if (payers.Length == 1)
                {
                    string item = context.Turn == 3 ? "BLACKBOX" : "FREE_PASS";
                    var p = room.Players[payers[0]];
                    if (p.Inventory.Count < 3) p.Inventory.Add(item);
                    else rooms[context.RoomId].Service.RewardCoins(payers[0], context.Turn, "auction-overflow", 1);
                    room.Events.Add(new PublicEvent { turn = context.Turn, kind = "AUCTION_WINNER", playerId = payers[0], detail = item });
                }
                else room.Events.Add(new PublicEvent { turn = context.Turn, kind = "AUCTION_UNSOLD" });
                room.ClosedAuctions.Add(context.Turn);
                return room.FinalSeats == null ? null : (string[])room.FinalSeats.Clone();
            }
        }
        public void RewardTurn(RequestContext context, Scoreboard board)
        {
            lock (gate)
            {
                var room = features[context.RoomId];
                if (!room.RewardedTurns.Add(context.Turn) || (context.Turn != 3 && context.Turn != 6 && context.Turn != 9)) return;
                int best = board.rows.Max(r => r.turnReturnBps);
                foreach (var r in board.rows.Where(r => r.turnReturnBps == best))
                    rooms[context.RoomId].Service.RewardCoins(r.playerId, context.Turn, "turn-performance", 1);
            }
        }
        public void PopulateView(RequestContext context, PlayerView view)
        {
            lock (gate)
            {
                var p = FeaturePlayer(context.RoomId, context.PlayerId);
                if (p == null) return;
                view.ability = new AbilityState { candidates = (string[])p.Candidates.Clone(), selectedId = p.Ability,
                    reusableAtTurn = p.ReusableAtTurn, freeInformationCredits = p.FreeCredits };
                view.inventory = p.Inventory.ToArray();
                if (p.Bid != null) view.auction = new AuctionState { maximumBid = p.Bid.maximumBid, bidAmount = p.Bid.bidAmount,
                    confirmed = p.Bid.confirmed, useFreePass = p.Bid.useFreePass, seatPreferences = (string[])p.Bid.seatPreferences.Clone() };
            }
        }
        public PublicEvent[] GetPublicEvents(string roomId)
        {
            lock (gate) return features.TryGetValue(roomId, out var room) ? room.Events.Select((e, index) => new PublicEvent {
                eventId = roomId + ":event:" + (index + 1), turn = e.turn, kind = e.kind,
                playerId = e.playerId, targetPlayerId = e.targetPlayerId, detail = e.detail }).ToArray() : Array.Empty<PublicEvent>();
        }
        public int GetCoins(string roomId, string playerId)
        { lock (gate) return rooms[roomId].Service.GetCoins(playerId); }
    }
}
