// NEXT-D05·D06·IMP-07 화면(게임 미연결 단계) 검사.
// - 화면의 사전 안내 규칙(가격·입찰 한도·사용 가능 여부)이 02 규칙과 맞는지
// - 현재 계약의 SCOREBOARD를 전광판 모델로 바꿀 수 있는지
// - 각 화면이 예시 데이터로 예외 없이 만들어지는지(모양·클릭 확인은 미리보기 창에서 사람이 한다)
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Dev;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.View;
using NUnit.Framework;
using UnityEngine;

namespace BlackWhale.UI.Tests
{
    public class DFeatureScreenTests
    {
        // ------------------------------------------------------------------
        // 상점
        // ------------------------------------------------------------------

        [Test]
        public void Shop_PriceIsFloorOfRate_Minimum1_ZeroAssetsBlocked()
        {
            var amp = ItemCatalog.Get("AMPLIFIER");   // 3%
            Assert.AreEqual(373, ShopRules.Price(amp, 12450), "12450×3% = 373.5 → 373(버림)");
            Assert.AreEqual(1, ShopRules.Price(amp, 20), "20×3% = 0.6 → 최소 1");
            Assert.AreEqual(0, ShopRules.Price(amp, 0));
            Assert.AreEqual(0, ShopRules.Price(ItemCatalog.Get("FREEPASS"), 10000), "프리패스는 상점 품목 아님");
            Assert.AreEqual(8, ItemCatalog.Shop.Count(), "상점 8종(프리패스 제외)");
            Assert.AreEqual(9, ItemCatalog.All.Length, "아이템 9종");
        }

        [Test]
        public void Shop_CanBuy_OnePerTurn_Max3_Money()
        {
            var shield = ItemCatalog.Get("SHIELD");
            Assert.AreEqual(ShopBlock.None, ShopRules.CanBuy(shield, 10000, 10000, 0, 2));
            Assert.AreEqual(ShopBlock.AlreadyBought, ShopRules.CanBuy(shield, 10000, 10000, 1, 0));
            Assert.AreEqual(ShopBlock.InventoryFull, ShopRules.CanBuy(shield, 10000, 10000, 0, 3));
            Assert.AreEqual(ShopBlock.NotEnoughMoney, ShopRules.CanBuy(shield, 10000, 199, 0, 0), "가격 200");
            Assert.AreEqual(ShopBlock.NoAssets, ShopRules.CanBuy(shield, 0, 0, 0, 0));
            Assert.AreEqual(ShopBlock.NotForSale, ShopRules.CanBuy(ItemCatalog.Get("FREEPASS"), 10000, 10000, 0, 0));
        }

        // ------------------------------------------------------------------
        // 아이템 사용
        // ------------------------------------------------------------------

        [Test]
        public void Item_UsePhaseAndCategoryLimit()
        {
            var used = new HashSet<ItemCategory>();
            Assert.AreEqual(UseBlock.None, ItemUseRules.CanUse(ItemCatalog.Get("AMPLIFIER"), Protocol.Investment, used));
            Assert.AreEqual(UseBlock.WrongPhase, ItemUseRules.CanUse(ItemCatalog.Get("AMPLIFIER"), Protocol.Info, used));
            Assert.AreEqual(UseBlock.None, ItemUseRules.CanUse(ItemCatalog.Get("STEAL"), Protocol.Info, used));
            Assert.AreEqual(UseBlock.WrongPhase, ItemUseRules.CanUse(ItemCatalog.Get("BLACKBOX"), Protocol.Investment, used));
            Assert.AreEqual(UseBlock.NotUsable, ItemUseRules.CanUse(ItemCatalog.Get("FREEPASS"), "AUCTION", used));
            used.Add(ItemCategory.Attack);
            Assert.AreEqual(UseBlock.CategoryUsed, ItemUseRules.CanUse(ItemCatalog.Get("DRAIN"), Protocol.Info, used), "공격형 턴당 1개");
            Assert.AreEqual(UseBlock.None, ItemUseRules.CanUse(ItemCatalog.Get("BLACKBOX"), Protocol.Info, used), "분류가 다르면 가능");
        }

        [Test]
        public void Item_DrainTargets_TopTwoExceptMe()
        {
            var assets = new Dictionary<string, long> { { "P1", 13200 }, { "P2", 14100 }, { "P3", 11800 }, { "P4", 12900 } };
            CollectionAssert.AreEqual(new[] { "P1" }, ItemUseRules.DrainTargets(assets, "P2"), "상위 2명(P2,P1) 중 나 제외");
            CollectionAssert.AreEqual(new[] { "P2", "P1" }, ItemUseRules.DrainTargets(assets, "P3"), "내가 상위권이 아니면 2명");
        }

        // ------------------------------------------------------------------
        // 입찰
        // ------------------------------------------------------------------

        [Test]
        public void Auction_RangeScoreAndPreference()
        {
            Assert.AreEqual(1245, AuctionRules.MaxBid(12450), "floor(S×10%)");
            Assert.AreEqual(0, AuctionRules.MaxBid(0));
            Assert.AreEqual("", AuctionRules.Validate(1245, 12450));
            Assert.AreNotEqual("", AuctionRules.Validate(1246, 12450));
            Assert.AreNotEqual("", AuctionRules.Validate(-1, 12450));
            Assert.AreEqual(1000 * 125, AuctionRules.Score(1000, true), "프리패스: ×125");
            Assert.AreEqual(1000 * 100, AuctionRules.Score(1000, false));
            CollectionAssert.AreEqual(new[] { "A", "C", "B", "D" }, AuctionRules.MoveUp(new[] { "A", "B", "C", "D" }, 2));
            CollectionAssert.AreEqual(new[] { "C", "A", "B", "D" }, AuctionRules.NormalizePreference(new[] { "C", "X", "C" }), "잘못/중복 제거 후 A→B→C→D로 채움");
            Assert.AreEqual(AuctionKind.BlackBox, AuctionCatalog.KindOf(3));
            Assert.AreEqual(AuctionKind.FreePass, AuctionCatalog.KindOf(6));
            Assert.AreEqual(AuctionKind.SeatPriority, AuctionCatalog.KindOf(9));
            Assert.IsNull(AuctionCatalog.KindOf(10), "10턴은 재입찰 없음");
        }

        // ------------------------------------------------------------------
        // 전광판 모델
        // ------------------------------------------------------------------

        [Test]
        public void Scoreboard_FromCurrentContract()
        {
            var board = new Scoreboard
            {
                turn = 2,
                rows = new[]
                {
                    new ScoreRow { playerId = "P1", rank = 2, totalAssets = 10500, turnReturnBps = -300, cumulativeReturnBps = 500,
                        returnHistory = new[] { new ReturnPoint { turn = 2, returnBps = -300 }, new ReturnPoint { turn = 1, returnBps = 800 } } },
                    new ScoreRow { playerId = "P2", rank = 1, totalAssets = 11000, turnReturnBps = 600, cumulativeReturnBps = 1000 }
                }
            };
            var state = new PublicState { turn = 2, phase = Protocol.Result, players = new[] { new Player { playerId = "P1", displayName = "민준" }, new Player { playerId = "P2" } } };
            var m = ScoreboardModel.FromScoreboard(board, state, "P2", new Dictionary<string, int> { { "P1", 1 }, { "P2", 2 } });
            Assert.AreEqual(2, m.Turn);
            Assert.IsFalse(m.Final);
            var p1 = m.Players.First(p => p.Id == "P1");
            Assert.AreEqual("민준", p1.Name);
            Assert.AreEqual(new[] { 1, 2 }, p1.History.Select(h => h.Key).ToArray(), "턴 순서로 정렬");
            Assert.AreEqual(-1, p1.RankDelta, "1위 → 2위 = 하락");
            Assert.AreEqual(800, p1.BestTurnBps);
            var p2 = m.Players.First(p => p.Id == "P2");
            Assert.AreEqual("P2", p2.Name, "이름이 없으면 ID");
            Assert.IsTrue(p2.IsMe);
            Assert.AreEqual(1, p2.RankDelta);
            Assert.AreEqual("P2", m.Ranked.First().Id);
        }

        [Test]
        public void Animation_OutsidePlayMode_JumpsToFinalValues()
        {
            var root = NewRoot(out var go);
            try
            {
                var rt = UiKit.Rect(root, "Moving");
                UiAnim.Slide(rt, new Vector2(0, -100), new Vector2(0, -20));
                Assert.AreEqual(new Vector2(0, -20), rt.anchoredPosition, "연출이 없어도 최종 위치");
                var text = UiKit.Label(root, "", 12, Color.black);
                UiAnim.Count(text, 10000, 12345, v => DisplayFormat.Money(v));
                Assert.AreEqual(DisplayFormat.Money(12345), text.text, "연출이 없어도 최종 값(서버 값)");
                Assert.AreEqual(1f, UiAnim.EaseOut(1f), 1e-6);
                Assert.AreEqual(0f, UiAnim.EaseOut(0f), 1e-6);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------------
        // 화면 생성(예외 없이 만들어지는지)
        // ------------------------------------------------------------------

        private static RectTransform NewRoot(out GameObject go)
        {
            go = new GameObject("DFeatureRoot", typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(1240, 600);
            return rt;
        }

        [Test]
        public void Screens_BuildWithPreviewData()
        {
            var root = NewRoot(out var go);
            try
            {
                new ScoreboardScreen(UiKit.Rect(root, "A")).Show(PreviewData.LiveBoard());
                new ScoreboardScreen(UiKit.Rect(root, "B")).Show(PreviewData.FinalBoard());
                foreach (AuctionKind k in new[] { AuctionKind.BlackBox, AuctionKind.FreePass, AuctionKind.SeatPriority })
                {
                    new AuctionScreen(UiKit.Rect(root, "Auc" + k)).Show(PreviewData.Auction(k));
                    var done = PreviewData.Auction(k);
                    done.Submitted = true;
                    done.Result = k == AuctionKind.BlackBox ? PreviewData.BlackBoxResult() : k == AuctionKind.FreePass ? PreviewData.FreePassNoWinner() : PreviewData.SeatResult();
                    new AuctionScreen(UiKit.Rect(root, "AucDone" + k)).Show(done);
                }
                var ability = PreviewData.Ability();
                new AbilityPickScreen(UiKit.Rect(root, "Ab1")).Show(ability);
                ability.Selected = "GAMBLER"; ability.Confirmed = true;
                new AbilityPickScreen(UiKit.Rect(root, "Ab2")).Show(ability, PreviewData.StatusFor("GAMBLER"));
                new ShopScreen(UiKit.Rect(root, "Shop")).Show(PreviewData.Shop());
                var inv = PreviewData.Inventory();
                new InventoryScreen(UiKit.Rect(root, "Inv1")).Show(inv);
                inv.SelectedSlot = 2; // 수익률 감소: 대상 중 일부 비활성
                new InventoryScreen(UiKit.Rect(root, "Inv2")).Show(inv);
                new BotDifficultyPanel(UiKit.Rect(root, "Bot1")).Show(BotDifficulty.Hard, true);
                new BotDifficultyPanel(UiKit.Rect(root, "Bot2")).Show(BotDifficulty.Easy, false);

                Assert.IsNotNull(root.GetComponentsInChildren<UnityEngine.UI.Button>(true).FirstOrDefault(b => b.name == "ConfirmBid"));
                Assert.IsNotNull(root.GetComponentsInChildren<UnityEngine.UI.Button>(true).FirstOrDefault(b => b.name == "Buy_AMPLIFIER"));
                var drainTargets = inv.TargetsFor(ItemCatalog.Get("DRAIN"));
                Assert.AreEqual(1, drainTargets.Count(t => t.Enabled), "상위 2명 중 나 제외 = 1명만 선택 가능");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
