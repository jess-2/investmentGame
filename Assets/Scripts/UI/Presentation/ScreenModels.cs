// NEXT-D05·D06·IMP-07 화면이 그리는 값(화면 객체 의존 없음).
// - LiveScreenModels가 실제 서버 DTO를 표시 모델로 변환한다. 예시 데이터는 ScreenGallery에서만 사용한다.
// - 서버가 지원하지 않는 상점 구매·일반 아이템 사용은 실제 게임에서 비활성화한다.
// - 전광판은 현재 계약(Scoreboard·PublicState)에서 바로 만들 수 있다(FromScoreboard).
// - 타인의 코인·구매 정보·투자 기업·금액은 모델에 넣지 않는다(공개 범위 유지).
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.UI.Presentation
{
    // ------------------------------------------------------------------
    // 전광판(D05)
    // ------------------------------------------------------------------

    public enum BoardEventKind { Auction, Ability, Attack, Bonus, System }

    public sealed class BoardEvent
    {
        public int Turn;
        public BoardEventKind Kind;
        public string Text;

        public static string KindName(BoardEventKind k)
        {
            switch (k)
            {
                case BoardEventKind.Auction: return "입찰";
                case BoardEventKind.Ability: return "능력";
                case BoardEventKind.Attack: return "공격";
                case BoardEventKind.Bonus: return "보상";
                default: return "알림";
            }
        }
    }

    public sealed class BoardPlayer
    {
        public string Id, Name;
        public int Rank, PrevRank;              // PrevRank 0 = 이전 순위 모름
        public long TotalAssets;
        public long? PrevTotalAssets;            // 직전 턴 총자산(연출용 숫자 세기). 모르면 null
        public int TurnReturnBps, CumulativeReturnBps;
        public long CumulativeProfit;
        public List<KeyValuePair<int, int>> History = new List<KeyValuePair<int, int>>(); // (턴, 턴 수익률 bps)
        public bool IsMe;

        public int RankDelta => PrevRank <= 0 ? 0 : PrevRank - Rank; // +면 상승

        public int? BestTurnBps => History.Count == 0 ? (int?)null : History.Max(h => h.Value);
    }

    public sealed class ScoreboardModel
    {
        public const int TotalTurns = 10;
        public int GameTurns = 10;
        public bool StockSystem;
        public int Turn;
        public bool Final;
        public string WinnerId = "";
        public List<BoardPlayer> Players = new List<BoardPlayer>();
        public List<BoardEvent> Events = new List<BoardEvent>();
        public string FinalNote = "";   // 최종 동점 처리 근거 등(서버가 준 문구)

        public IEnumerable<BoardPlayer> Ranked => Players.OrderBy(p => p.Rank).ThenBy(p => p.Id);

        public static bool IsAuctionTurn(int turn) => turn == 3 || turn == 6 || turn == 9;

        /// <summary>현재 계약의 SCOREBOARD로 만든다. prevRanks·prevAssets는 화면이 기억해 둔 직전 턴 순위·총자산(정산 연출용, 없으면 null).</summary>
        public static ScoreboardModel FromScoreboard(Scoreboard board, PublicState state, string localPlayerId,
            IDictionary<string, int> prevRanks = null, IDictionary<string, long> prevAssets = null)
        {
            var model = new ScoreboardModel { Turn = board?.turn ?? state?.turn ?? 0,
                GameTurns = state?.totalTurns > 0 ? state.totalTurns : 10, StockSystem = state?.stockSystem == true };
            if (board?.rows == null) return model;
            foreach (var r in board.rows)
            {
                if (r == null) continue;
                var name = state?.players?.FirstOrDefault(p => p.playerId == r.playerId)?.displayName;
                int prev = 0;
                if (prevRanks != null && prevRanks.TryGetValue(r.playerId, out var pr)) prev = pr;
                long? prevAsset = null;
                if (prevAssets != null && prevAssets.TryGetValue(r.playerId, out var pa)) prevAsset = pa;
                model.Players.Add(new BoardPlayer
                {
                    Id = r.playerId,
                    Name = string.IsNullOrEmpty(name) ? r.playerId : name,
                    Rank = r.rank,
                    PrevRank = prev,
                    TotalAssets = r.totalAssets,
                    PrevTotalAssets = prevAsset,
                    TurnReturnBps = r.turnReturnBps,
                    CumulativeReturnBps = r.cumulativeReturnBps,
                    CumulativeProfit = r.cumulativeProfit,
                    History = (r.returnHistory ?? new ReturnPoint[0]).Where(h => h != null)
                        .OrderBy(h => h.turn).Select(h => new KeyValuePair<int, int>(h.turn, h.returnBps)).ToList(),
                    IsMe = r.playerId == localPlayerId
                });
            }
            model.Final = board.gameComplete;
            model.WinnerId = board.winnerPlayerId ?? "";
            return model;
        }
    }

    // ------------------------------------------------------------------
    // 입찰(D06)
    // ------------------------------------------------------------------

    public sealed class AuctionSeatResult
    {
        public string Seat, PlayerId, Name;
        public long Paid;
        public bool UsedFreePass;
    }

    public sealed class AuctionResultModel
    {
        public bool AmountsPublic = true;
        public bool NoWinner;                 // 3·6턴 유찰(모두 0)
        public string WinnerId = "", WinnerName = "";
        public long WinningBid;
        public List<AuctionSeatResult> Seats = new List<AuctionSeatResult>(); // 9턴
    }

    public sealed class AuctionModel
    {
        public AuctionKind Kind;
        public long TurnStartAssets;           // S
        public long Bid;                       // 입력 중/확정한 금액
        public bool Submitted;                 // 확정(변경 불가)
        public int SubmittedCount, PlayerCount = 4;   // 제출 인원(금액은 비공개)
        public int SecondsLeft = 15;
        public bool HasFreePass, UseFreePass;
        public bool OwnBidKnown = true, ShowBidLimit = true;
        public string[] SeatPreference = AuctionRules.Seats.ToArray();
        public AuctionResultModel Result;      // null = 아직 마감 전

        public long? ServerMaximumBid;
        public long MaxBid => ServerMaximumBid ?? AuctionRules.MaxBid(TurnStartAssets);
        public int Turn => AuctionCatalog.TurnOf(Kind);
    }

    // ------------------------------------------------------------------
    // 고유능력 선택(D06)
    // ------------------------------------------------------------------

    public sealed class AbilityPickModel
    {
        public string[] Candidates = new string[0];   // 무작위 3개(서버 제공)
        public string Selected = "";                  // 로컬 후보
        public bool Confirmed;
        public bool Pending;
        public bool TimeLimited = true;
        public string Notice = "";
        public int SecondsLeft = 20;

        /// <summary>미선택 시 서버가 적용하는 기본값: 제시된 첫 후보.</summary>
        public string DefaultPick => Candidates.Length > 0 ? Candidates[0] : "";
    }

    public sealed class AbilityStatusModel
    {
        public string AbilityId = "";
        public string State = "";      // 예: "방어 가능", "재사용 대기"
        public string Detail = "";     // 예: "7턴 시작부터 다시 방어"
    }

    // ------------------------------------------------------------------
    // 상점·인벤토리(D06)
    // ------------------------------------------------------------------

    public sealed class ShopModel
    {
        public bool Enabled = true;
        public string UnavailableReason = "";
        public long TurnStartAssets;   // S(가격 기준)
        public long Money;             // 지금 쓸 수 있는 돈
        public int BoughtThisTurn;
        public List<string> Inventory = new List<string>();
        public string Phase = Protocol.Info;
    }

    public sealed class TargetOption
    {
        public string Key, Label;
        public bool Enabled = true;
        public string Reason = "";
    }

    public sealed class InventoryModel
    {
        public bool UsageEnabled = true;
        public string UnavailableReason = "";
        public List<string> Items = new List<string>();
        public string Phase = Protocol.Info;
        public int Turn = 1;
        public HashSet<ItemCategory> UsedThisTurn = new HashSet<ItemCategory>();
        public int SelectedSlot = -1;
        public string SelectedTarget = "";
        public string LastMessage = "";

        // 대상 후보(화면이 그대로 버튼으로 만든다). 서버 계약이 생기면 개인뷰에서 채운다.
        public List<TargetOption> OwnCompanies = new List<TargetOption>();
        public List<TargetOption> OwnIntervals = new List<TargetOption>();
        public List<TargetOption> Companies = new List<TargetOption>();
        public List<TargetOption> Players = new List<TargetOption>();
        public List<TargetOption> TopPlayers = new List<TargetOption>();

        public List<TargetOption> TargetsFor(ItemDef item)
        {
            if (item == null) return new List<TargetOption>();
            switch (item.Target)
            {
                case ItemTarget.OwnCompany: return OwnCompanies;
                case ItemTarget.OwnInterval: return OwnIntervals;
                case ItemTarget.Company: return Companies;
                case ItemTarget.Player: return Players;
                case ItemTarget.TopPlayer: return TopPlayers;
                default: return new List<TargetOption>();
            }
        }
    }

    // Converts only public snapshots and the receiving player's private view.
    public static class LiveScreenModels
    {
        public static string AbilityId(string serverId)
            => serverId == "SYNDICATE" ? "OPERATOR" : serverId == "INFORMATION_BROKER" ? "INFOBROKER"
                : serverId == "CORPORATE_RAIDER" ? "RAIDER" : serverId ?? "";
        public static string ServerAbilityId(string screenId)
            => screenId == "OPERATOR" ? "SYNDICATE" : screenId == "INFOBROKER" ? "INFORMATION_BROKER"
                : screenId == "RAIDER" ? "CORPORATE_RAIDER" : screenId ?? "";
        public static string ItemId(string serverId)
            => serverId == "INFORMATION_STEAL" ? "STEAL" : serverId == "FREE_PASS" ? "FREEPASS" : serverId;
        public static string Safe(string text) => (text ?? "").Replace("<", "‹").Replace(">", "›");
        public static string Name(PublicState state, string id)
        {
            string name = state?.players?.FirstOrDefault(p => p.playerId == id)?.displayName;
            return Safe(string.IsNullOrEmpty(name) ? id : name);
        }

        public static AbilityPickModel Ability(PublicState state, PlayerView view, string draft, bool pending, int seconds)
        {
            var selected = AbilityId(view?.ability?.selectedId);
            var candidates = (view?.ability?.candidates ?? new string[0]).Select(AbilityId).ToArray();
            return new AbilityPickModel {
                Candidates = candidates, Selected = selected.Length > 0 ? selected : candidates.Contains(draft) ? draft : "",
                Confirmed = selected.Length > 0, Pending = pending, SecondsLeft = seconds,
                TimeLimited = state?.phaseEndsAtServerSeconds > 0,
                Notice = "정보상 첫 정보 무료·작전 세력 초기 아이템이 적용됩니다. 그 외 능력 효과는 준비 중입니다."
            };
        }
        public static AbilityStatusModel AbilityStatus(PlayerView view)
        {
            string id = AbilityId(view?.ability?.selectedId);
            return new AbilityStatusModel { AbilityId = id, State = id.Length == 0 ? "선택 대기" : "선택 완료",
                Detail = id == "INFOBROKER" ? "이번 턴 무료 정보 구매 " + view.ability.freeInformationCredits + (view.entryAtOpen ? "일" : "회") + " 남음 · 추가 능력 효과 준비 중"
                    : id == "OPERATOR" ? "초기 정보 훔치기 아이템 지급 · 공격 효과 준비 중" : "능력 효과 준비 중" };
        }
        public static AuctionModel Auction(PublicState state, PlayerView view, AuctionModel draft, bool pending, int seconds)
        {
            var bid = view?.auction;
            bool locked = bid?.confirmed == true;
            return new AuctionModel {
                Kind = AuctionCatalog.KindOf(state.turn) ?? AuctionKind.BlackBox,
                TurnStartAssets = view?.assets ?? 0, ServerMaximumBid = bid?.maximumBid ?? 0,
                Bid = locked ? bid.bidAmount : draft?.Bid ?? 0, Submitted = locked || pending,
                SubmittedCount = -1, PlayerCount = state.players?.Length ?? 0, SecondsLeft = seconds,
                HasFreePass = view?.inventory?.Contains("FREE_PASS") == true,
                UseFreePass = locked ? bid.useFreePass : draft?.UseFreePass == true,
                SeatPreference = (locked ? bid.seatPreferences : draft?.SeatPreference ?? bid?.seatPreferences ?? AuctionRules.Seats).ToArray()
            };
        }
        public static ShopModel Shop(PlayerView view, string phase)
            => new ShopModel { Enabled = false, UnavailableReason = "상점 구매 기능은 준비 중입니다.",
                TurnStartAssets = view?.assets ?? 0, Money = view?.assets ?? 0, Phase = phase,
                Inventory = (view?.inventory ?? new string[0]).Select(ItemId).ToList() };
        public static AuctionModel LastAuction(PublicState state, PlayerView view, AuctionModel remembered = null)
        {
            var all = (state?.events ?? new PublicEvent[0]).Where(e => e.kind?.StartsWith("AUCTION") == true).ToArray();
            if (all.Length == 0) return remembered;
            int turn = all.Max(e => e.turn);
            var events = all.Where(e => e.turn == turn).ToArray();
            var model = remembered?.Turn == turn ? remembered : new AuctionModel {
                Kind = AuctionCatalog.KindOf(turn) ?? AuctionKind.BlackBox, OwnBidKnown = false, SubmittedCount = -1 };
            if (view?.turn == turn && view.auction != null)
            {
                model.OwnBidKnown = true; model.Bid = view.auction.bidAmount; model.Submitted = view.auction.confirmed;
            }
            model.ShowBidLimit = false;
            model.Result = new AuctionResultModel { AmountsPublic = false, NoWinner = events.Any(e => e.kind == "AUCTION_UNSOLD") };
            var winner = events.FirstOrDefault(e => e.kind == "AUCTION_WINNER");
            if (winner != null) { model.Result.WinnerId = winner.playerId; model.Result.WinnerName = Name(state, winner.playerId); }
            foreach (var e in events.Where(e => e.kind == "AUCTION_SEAT").OrderBy(e => e.detail))
                model.Result.Seats.Add(new AuctionSeatResult { Seat = Safe(e.detail), PlayerId = e.playerId, Name = Name(state, e.playerId) });
            return model;
        }
        public static InventoryModel Inventory(PlayerView view, string phase, int selectedSlot)
        {
            var items = (view?.inventory ?? new string[0]).Select(ItemId).ToList();
            return new InventoryModel { Items = items, Turn = view?.turn ?? 0, Phase = phase,
                SelectedSlot = selectedSlot >= 0 && selectedSlot < items.Count ? selectedSlot : -1,
                UsageEnabled = false, UnavailableReason = "일반 아이템 사용은 준비 중입니다. 프리패스는 9턴 입찰에서 사용할 수 있습니다." };
        }
        public static ScoreboardModel Board(Scoreboard board, PublicState state, string playerId)
        {
            var model = ScoreboardModel.FromScoreboard(board, state, playerId);
            foreach (var player in model.Players) player.Name = Safe(player.Name);
            foreach (var e in state?.events ?? new PublicEvent[0])
            {
                string who = Name(state, e.playerId);
                string text = e.kind == "AUCTION_WINNER" ? who + " · " + (ItemCatalog.Get(ItemId(e.detail))?.Name ?? "아이템") + " 낙찰"
                    : e.kind == "AUCTION_UNSOLD" ? "입찰 유찰"
                    : e.kind == "AUCTION_SEAT" ? who + " · " + Safe(e.detail) + " 자리 배정" : Safe(e.detail);
                model.Events.Add(new BoardEvent { Turn = e.turn, Kind = e.kind?.StartsWith("AUCTION") == true ? BoardEventKind.Auction : BoardEventKind.System, Text = text });
            }
            if (model.Final) model.FinalNote = "서버 확정 우승: " + Name(state, model.WinnerId) + " · 자산 → 코인 → 최고 단일 턴 수익률 → 시작 추첨 순서";
            return model;
        }
    }
}
