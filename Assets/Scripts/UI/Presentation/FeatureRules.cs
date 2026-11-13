// NEXT-D06 화면의 '사전 안내'용 규칙(화면 객체 의존 없음). 최종 판정은 서버(A/B/C)가 한다.
// 규칙 출처: 02_결정_사항 6장(입찰)·7장(아이템). 버튼을 미리 막고 이유를 보여 주는 데만 쓴다.
using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.UI.Presentation
{
    public enum ShopBlock
    {
        None,
        NotForSale,        // 상점 판매 품목 아님(프리패스)
        NoAssets,          // S = 0이면 상점 이용 불가
        AlreadyBought,     // 상점 구매는 턴당 1개
        InventoryFull,     // 보유 최대 3개
        NotEnoughMoney     // 잔액 부족
    }

    public static class ShopRules
    {
        public const int InventoryLimit = 3;
        public const int PurchasesPerTurn = 1;

        /// <summary>가격 = floor(S × 비율), 최소 1. S가 0 이하이면 0(구매 불가).</summary>
        public static long Price(ItemDef item, long turnStartAssets)
        {
            if (item == null || !item.InShop || turnStartAssets <= 0) return 0;
            long price = turnStartAssets * item.PriceRateBps / 10000;
            return Math.Max(1, price);
        }

        public static ShopBlock CanBuy(ItemDef item, long turnStartAssets, long money, int boughtThisTurn, int inventoryCount)
        {
            if (item == null || !item.InShop) return ShopBlock.NotForSale;
            if (turnStartAssets <= 0) return ShopBlock.NoAssets;
            if (boughtThisTurn >= PurchasesPerTurn) return ShopBlock.AlreadyBought;
            if (inventoryCount >= InventoryLimit) return ShopBlock.InventoryFull;
            if (money < Price(item, turnStartAssets)) return ShopBlock.NotEnoughMoney;
            return ShopBlock.None;
        }

        public static string Explain(ShopBlock b)
        {
            switch (b)
            {
                case ShopBlock.NotForSale: return "상점에서 팔지 않는 아이템";
                case ShopBlock.NoAssets: return "자산이 0이면 상점을 쓸 수 없음";
                case ShopBlock.AlreadyBought: return "이번 턴 구매 완료(턴당 1개)";
                case ShopBlock.InventoryFull: return "보유 칸이 가득 참(최대 3개)";
                case ShopBlock.NotEnoughMoney: return "잔액 부족";
                default: return "";
            }
        }
    }

    public enum UseBlock
    {
        None,
        WrongPhase,      // 지금 단계에서 쓸 수 없음
        CategoryUsed,    // 이번 턴 같은 분류 이미 사용
        NotUsable,       // 직접 사용하는 아이템 아님(프리패스는 입찰 화면에서)
        NoTarget         // 고를 수 있는 대상이 없음
    }

    public static class ItemUseRules
    {
        public static string PhaseOf(ItemCategory c)
            => c == ItemCategory.Finance ? Protocol.Investment : c == ItemCategory.Special ? "AUCTION" : Protocol.Info;

        public static UseBlock CanUse(ItemDef item, string phase, ICollection<ItemCategory> usedThisTurn)
        {
            if (item == null || item.Category == ItemCategory.Special) return UseBlock.NotUsable;
            if (phase != PhaseOf(item.Category)) return UseBlock.WrongPhase;
            if (usedThisTurn != null && usedThisTurn.Contains(item.Category)) return UseBlock.CategoryUsed;
            return UseBlock.None;
        }

        public static string Explain(UseBlock b, ItemDef item)
        {
            switch (b)
            {
                case UseBlock.WrongPhase: return item == null ? "" : ItemCatalog.WhenUsable(item.Category) + "에만 사용 가능";
                case UseBlock.CategoryUsed: return "이번 턴에 " + (item == null ? "" : ItemCatalog.CategoryName(item.Category)) + "형을 이미 사용함";
                case UseBlock.NotUsable: return "9턴 입찰 화면에서 사용";
                case UseBlock.NoTarget: return "고를 수 있는 대상이 없음";
                default: return "";
            }
        }

        /// <summary>수익률 감소 대상: 턴 시작 총자산 상위 2명 중 본인을 뺀 사람(동액은 ID 순).</summary>
        public static List<string> DrainTargets(IEnumerable<KeyValuePair<string, long>> turnStartAssets, string me)
        {
            return (turnStartAssets ?? Enumerable.Empty<KeyValuePair<string, long>>())
                .OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
                .Take(2).Select(p => p.Key).Where(id => id != me).ToList();
        }
    }

    public static class AuctionRules
    {
        public static readonly string[] Seats = { "A", "B", "C", "D" };

        /// <summary>입찰 상한 = floor(S × 10%). S가 0 이하이면 0.</summary>
        public static long MaxBid(long turnStartAssets) => turnStartAssets <= 0 ? 0 : turnStartAssets / 10;

        /// <summary>문제 없으면 "", 있으면 안내 문구.</summary>
        public static string Validate(long bid, long turnStartAssets)
        {
            if (bid < 0) return "0 이상으로 입력하세요.";
            long max = MaxBid(turnStartAssets);
            if (bid > max) return "최대 " + DisplayFormat.Money(max) + "까지 입찰할 수 있습니다.";
            return "";
        }

        /// <summary>9턴 정렬 점수(정수 비교): 프리패스 사용 시 입찰액×125, 아니면 ×100.</summary>
        public static long Score(long bid, bool useFreePass) => bid * (useFreePass ? 125 : 100);

        /// <summary>자리 선호 순서에서 index 칸을 한 칸 위로. 범위 밖이면 그대로.</summary>
        public static string[] MoveUp(string[] order, int index)
        {
            var list = (order ?? Seats).ToArray();
            if (index <= 0 || index >= list.Length) return list;
            var t = list[index - 1]; list[index - 1] = list[index]; list[index] = t;
            return list;
        }

        /// <summary>잘못되거나 빠진 선호는 A→B→C→D로 채운다.</summary>
        public static string[] NormalizePreference(IEnumerable<string> order)
        {
            var result = new List<string>();
            foreach (var s in order ?? Enumerable.Empty<string>())
                if (Seats.Contains(s) && !result.Contains(s)) result.Add(s);
            foreach (var s in Seats) if (!result.Contains(s)) result.Add(s);
            return result.ToArray();
        }
    }
}
