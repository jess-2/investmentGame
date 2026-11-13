// D 화면 미리보기용 예시 데이터. 실제 게임·서버 값이 아니며 화면 모양 확인에만 쓴다.
// 플레이어: P1 민준, P2 서연(나), P3 수아, P4 지호.
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;

namespace BlackWhale.UI.Dev
{
    public static class PreviewData
    {
        public const string Me = "P2";

        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "P1", "민준" }, { "P2", "서연" }, { "P3", "수아" }, { "P4", "지호" }
        };

        public static string NameOf(string id) => Names.TryGetValue(id, out var n) ? n : id;

        // 턴별 수익률(bps) 1~10턴
        private static readonly Dictionary<string, int[]> Returns = new Dictionary<string, int[]>
        {
            { "P1", new[] { 1200, -800, 2500, 400, -1500, 3100, -600, 900, 1800, -400 } },
            { "P2", new[] { 600, 1100, -300, 1900, 800, -1200, 2200, 1500, -500, 2600 } },
            { "P3", new[] { -400, 2300, 900, -1100, 2700, 500, -900, -1300, 3200, 700 } },
            { "P4", new[] { 1500, 300, -1800, 2200, -200, 1400, 1000, -700, 600, 1100 } }
        };

        private static BoardPlayer PlayerAt(string id, int turn)
        {
            var r = Returns[id];
            long assets = 10000;
            var history = new List<KeyValuePair<int, int>>();
            for (int t = 1; t <= turn; t++)
            {
                assets = assets * (10000 + r[t - 1]) / 10000;
                history.Add(new KeyValuePair<int, int>(t, r[t - 1]));
            }
            return new BoardPlayer
            {
                Id = id,
                Name = NameOf(id),
                TotalAssets = assets,
                TurnReturnBps = r[turn - 1],
                CumulativeReturnBps = (int)((assets - 10000) * 10000 / 10000),
                CumulativeProfit = assets - 10000,
                History = history,
                IsMe = id == Me
            };
        }

        private static List<BoardPlayer> Ranked(int turn)
        {
            var now = Names.Keys.Select(id => PlayerAt(id, turn)).OrderByDescending(p => p.TotalAssets).ThenBy(p => p.Id).ToList();
            var before = turn > 1
                ? Names.Keys.Select(id => PlayerAt(id, turn - 1)).OrderByDescending(p => p.TotalAssets).ThenBy(p => p.Id).Select(p => p.Id).ToList()
                : null;
            for (int i = 0; i < now.Count; i++)
            {
                now[i].Rank = i + 1;
                now[i].PrevRank = before == null ? 0 : before.IndexOf(now[i].Id) + 1;
                now[i].PrevTotalAssets = turn > 1 ? PlayerAt(now[i].Id, turn - 1).TotalAssets : 10000;
            }
            return now;
        }

        private static List<BoardEvent> Events(int upToTurn)
        {
            var all = new List<BoardEvent>
            {
                new BoardEvent { Turn = 3, Kind = BoardEventKind.Auction, Text = "블랙박스 분석권 낙찰: 지호" },
                new BoardEvent { Turn = 3, Kind = BoardEventKind.Bonus, Text = "3턴 수익률 1위 민준 · 코인 1개" },
                new BoardEvent { Turn = 4, Kind = BoardEventKind.Ability, Text = "수아(정치인)가 공격을 막았습니다" },
                new BoardEvent { Turn = 5, Kind = BoardEventKind.Attack, Text = "민준 → 수아 · 정보 훔치기" },
                new BoardEvent { Turn = 6, Kind = BoardEventKind.Auction, Text = "블랙웨일 프리패스 낙찰: 서연" },
                new BoardEvent { Turn = 7, Kind = BoardEventKind.Attack, Text = "지호 → 민준 · 수익률 감소" },
                new BoardEvent { Turn = 9, Kind = BoardEventKind.Auction, Text = "9·10턴 자리: A 수아 · B 서연 · C 민준 · D 지호" },
                new BoardEvent { Turn = 10, Kind = BoardEventKind.System, Text = "10턴 정산 완료 · 게임 종료" }
            };
            return all.Where(e => e.Turn <= upToTurn).ToList();
        }

        public static ScoreboardModel LiveBoard(int turn = 7)
        {
            turn = System.Math.Max(1, System.Math.Min(10, turn));
            return new ScoreboardModel { Turn = turn, Players = Ranked(turn), Events = Events(turn) };
        }

        public static ScoreboardModel FinalBoard()
            => new ScoreboardModel { Turn = 10, Final = true, Players = Ranked(10), Events = Events(10) };

        // ------------------------------------------------------------------
        // 입찰
        // ------------------------------------------------------------------

        public static AuctionModel Auction(AuctionKind kind)
        {
            return new AuctionModel
            {
                Kind = kind,
                TurnStartAssets = kind == AuctionKind.BlackBox ? 12450 : kind == AuctionKind.FreePass ? 13870 : 16320,
                Bid = 0,
                SubmittedCount = 1,
                SecondsLeft = 12,
                HasFreePass = kind == AuctionKind.SeatPriority,
                UseFreePass = false
            };
        }

        public static AuctionResultModel BlackBoxResult()
            => new AuctionResultModel { WinnerId = "P4", WinnerName = NameOf("P4"), WinningBid = 870 };

        public static AuctionResultModel FreePassNoWinner()
            => new AuctionResultModel { NoWinner = true };

        public static AuctionResultModel SeatResult()
            => new AuctionResultModel
            {
                Seats = new List<AuctionSeatResult>
                {
                    new AuctionSeatResult { Seat = "A", PlayerId = "P3", Name = NameOf("P3"), Paid = 1400 },
                    new AuctionSeatResult { Seat = "B", PlayerId = "P2", Name = NameOf("P2"), Paid = 1200, UsedFreePass = true },
                    new AuctionSeatResult { Seat = "C", PlayerId = "P1", Name = NameOf("P1"), Paid = 500 },
                    new AuctionSeatResult { Seat = "D", PlayerId = "P4", Name = NameOf("P4"), Paid = 0 }
                }
            };

        // ------------------------------------------------------------------
        // 능력
        // ------------------------------------------------------------------

        public static AbilityPickModel Ability()
            => new AbilityPickModel { Candidates = new[] { "POLITICIAN", "INFOBROKER", "GAMBLER" }, SecondsLeft = 18 };

        public static AbilityStatusModel StatusFor(string abilityId)
        {
            switch (abilityId)
            {
                case "POLITICIAN": return new AbilityStatusModel { AbilityId = abilityId, State = "방어 가능", Detail = "다음 공격 1회를 자동으로 막습니다." };
                case "INFOBROKER": return new AbilityStatusModel { AbilityId = abilityId, State = "이번 턴 무료 1회 남음", Detail = "처음 사는 정보 1묶음이 무료입니다." };
                case "GAMBLER": return new AbilityStatusModel { AbilityId = abilityId, State = "항상 적용", Detail = "AI·SPACE·BIO 손익 ×1.2" };
                default: return new AbilityStatusModel { AbilityId = abilityId, State = "", Detail = "" };
            }
        }

        // ------------------------------------------------------------------
        // 상점·인벤토리
        // ------------------------------------------------------------------

        public static ShopModel Shop()
            => new ShopModel { TurnStartAssets = 12450, Money = 12450, BoughtThisTurn = 0, Inventory = new List<string> { "SHIELD" }, Phase = Protocol.Info };

        public static InventoryModel Inventory()
        {
            var m = new InventoryModel
            {
                Items = new List<string> { "AMPLIFIER", "STEAL", "DRAIN" },
                Phase = Protocol.Info,
                Turn = 7
            };
            m.OwnCompanies.Add(new TargetOption { Key = "AI", Label = CompanyCatalog.Get("AI").DisplayName + " (AI)" });
            m.OwnCompanies.Add(new TargetOption { Key = "BANK", Label = CompanyCatalog.Get("BANK").DisplayName + " (BANK)" });
            m.OwnIntervals.Add(new TargetOption { Key = "AI:0", Label = "뉴럴코어 10→15일" });
            m.OwnIntervals.Add(new TargetOption { Key = "BANK:0", Label = "새벽은행 5→10일" });
            foreach (var id in new[] { "AI", "SPACE", "FOOD" })
                m.Companies.Add(new TargetOption { Key = id, Label = CompanyCatalog.Get(id).DisplayName });
            m.Players.Add(new TargetOption { Key = "P1", Label = NameOf("P1") });
            m.Players.Add(new TargetOption { Key = "P3", Label = NameOf("P3") });
            m.Players.Add(new TargetOption { Key = "P4", Label = NameOf("P4") });

            // 수익률 감소: 턴 시작 총자산 상위 2명 중 나를 뺀 사람만 선택 가능
            var assets = new Dictionary<string, long> { { "P1", 13200 }, { "P2", 14100 }, { "P3", 11800 }, { "P4", 12900 } };
            var allowed = ItemUseRules.DrainTargets(assets, Me);
            foreach (var id in new[] { "P1", "P3", "P4" })
                m.TopPlayers.Add(new TargetOption
                {
                    Key = id,
                    Label = NameOf(id),
                    Enabled = allowed.Contains(id),
                    Reason = allowed.Contains(id) ? "" : "상위 2명 아님"
                });
            return m;
        }
    }
}
