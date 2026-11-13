// NEXT-D06 아이템 상점 화면.
// - 상점 8종(프리패스 제외)을 분류(금융/정보/공격)별 색으로 보여 준다. 가격 = floor(S × 비율), 최소 1.
// - 구매 버튼은 사전 안내 규칙(턴당 1개·보유 3칸·잔액·S=0)으로 미리 막고 이유를 표시한다. 최종 판정은 서버.
using System;
using BlackWhale.UI.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public sealed class ShopScreen
    {
        private readonly RectTransform _root;

        public Action<string> Buy;

        public ShopScreen(RectTransform root) => _root = root;

        public void Show(ShopModel m)
        {
            UiKit.Clear(_root);
            if (m == null) return;

            ScreenKit.Title(_root, "<b>아이템 상점</b>");
            var info = UiKit.Label(_root,
                "턴 시작 자산(S) <b>" + DisplayFormat.Money(m.TurnStartAssets) + "</b>   ·   잔액 <b>" + DisplayFormat.Money(m.Money) + "</b>   ·   "
                + "이번 턴 구매 <b>" + m.BoughtThisTurn + "/" + ShopRules.PurchasesPerTurn + "</b>   ·   보유 <b>" + m.Inventory.Count + "/" + ShopRules.InventoryLimit + "</b>",
                14, Palette.Text, TextAnchor.MiddleRight);
            UiKit.Place(info.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-4, -2), new Vector2(760, 30));

            var grid = UiKit.Rect(_root, "Items");
            UiKit.Stretch(grid, 0, 44, 0, 30);
            int i = 0;
            foreach (var item in ItemCatalog.Shop)
            {
                int col = i % 4, row = i / 4;
                var cell = UiKit.Rect(grid, "Item_" + item.Id);
                UiKit.Anchor(cell, col / 4f, row == 0 ? 0.5f : 0f, (col + 1) / 4f, row == 0 ? 1f : 0.5f, 5, 5);
                BuildItemCard(cell, item, m);
                i++;
            }

            var note = UiKit.Label(_root, "가격은 이번 턴 시작 자산 기준(버림, 최소 1) · 구매 비용은 이번 턴 투자금에서 빠짐 · 사용하면 사라짐 · 낙찰·보상으로 칸이 넘치면 코인 1개로 대체",
                11, Palette.SubText, TextAnchor.LowerLeft);
            UiKit.Stretch(note.rectTransform, 4, 0, 4, 6);
        }

        private void BuildItemCard(RectTransform cell, ItemDef item, ShopModel m)
        {
            var color = ScreenKit.CategoryColor(item.Category);
            var frame = ScreenKit.Card(cell, "Card", out var inner);
            UiKit.Stretch(frame);

            var stripe = UiKit.Panel(inner, "Stripe", color, rounded: true);
            stripe.rectTransform.anchorMin = new Vector2(0, 1);
            stripe.rectTransform.anchorMax = new Vector2(1, 1);
            stripe.rectTransform.pivot = new Vector2(0.5f, 1);
            stripe.rectTransform.offsetMin = new Vector2(0, -5);
            stripe.rectTransform.offsetMax = new Vector2(0, 0);

            ScreenKit.Chip(inner, ItemCatalog.CategoryName(item.Category), color, Color.white, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -14), 44, 20);
            int owned = 0;
            foreach (var id in m.Inventory) if (id == item.Id) owned++;
            if (owned > 0)
                ScreenKit.Chip(inner, "보유 " + owned, Palette.AccentSoft, Palette.Accent, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -14), 52, 20);

            long price = ShopRules.Price(item, m.TurnStartAssets);
            var text = UiKit.Label(inner,
                "<size=18><b>" + item.Name + "</b></size>\n<color=#" + ColorUtility.ToHtmlStringRGB(color) + "><b>" + item.Short + "</b></color>\n\n"
                + "<size=12>" + item.Effect + "</size>\n\n<size=11><color=#6B7280>사용: " + ItemCatalog.WhenUsable(item.Category)
                + " · 가격 S×" + (item.PriceRateBps / 100) + "%</color></size>",
                13, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(text.rectTransform, 12, 42, 12, 66);

            var block = ShopRules.CanBuy(item, m.TurnStartAssets, m.Money, m.BoughtThisTurn, m.Inventory.Count);
            bool can = m.Enabled && block == ShopBlock.None;
            var buy = UiKit.Button(inner, price > 0 ? DisplayFormat.Money(price) + " 구매" : "구매 불가", 14,
                can ? color : Palette.Disabled, can ? Color.white : Palette.DisabledText, () => Buy?.Invoke(item.Id));
            buy.name = "Buy_" + item.Id;
            buy.interactable = can;
            var rt = (RectTransform)buy.transform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(12, 12);
            rt.offsetMax = new Vector2(-12, 46);
            if (!can)
            {
                var reason = UiKit.Label(inner, m.Enabled ? ShopRules.Explain(block) : m.UnavailableReason, 11, Palette.Up, TextAnchor.LowerCenter);
                UiKit.Stretch(reason.rectTransform, 8, 0, 8, 48);
                reason.rectTransform.anchorMax = new Vector2(1, 0);
                reason.rectTransform.offsetMax = new Vector2(-8, 64);
            }
        }
    }
}
