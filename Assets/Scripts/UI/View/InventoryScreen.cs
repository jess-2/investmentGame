// NEXT-D06 보유 아이템(최대 3칸)과 사용 화면.
// - 칸을 누르면 오른쪽에 설명·사용 가능 여부·대상 선택이 나온다.
// - 금융형: 투자 결정 단계에 내 기업(대폭발은 내 구간)에, 정보·공격형: 정보 탐색 단계에. 턴당 분류별 1개.
// - 공격 대상 목록에는 대상의 구매 목록·코인 같은 비공개 정보를 보여 주지 않는다(이름만).
// - '사용 확정'은 의도만 알린다. 소모·효과 판정은 서버.
using System;
using BlackWhale.UI.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public sealed class InventoryScreen
    {
        private readonly RectTransform _root;

        public Action<int> SelectSlot;
        public Action<string> SelectTarget;
        public Action Use;

        public InventoryScreen(RectTransform root) => _root = root;

        public void Show(InventoryModel m)
        {
            UiKit.Clear(_root);
            if (m == null) return;

            ScreenKit.Title(_root, "<b>보유 아이템</b>   <size=14><color=#6B7280>" + m.Items.Count + "/" + ShopRules.InventoryLimit + "칸 · 지금: "
                + PhaseNames.Korean(m.Phase) + " 단계</color></size>");

            // 이번 턴 분류별 사용 여부
            var used = UiKit.Rect(_root, "Used");
            UiKit.Place(used, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-4, -6), new Vector2(330, 24));
            var cats = new[] { ItemCategory.Finance, ItemCategory.Info, ItemCategory.Attack };
            for (int i = 0; i < cats.Length; i++)
            {
                bool done = m.UsedThisTurn.Contains(cats[i]);
                ScreenKit.Chip(used, ItemCatalog.CategoryName(cats[i]) + (!m.UsageEnabled ? " 준비 중" : done ? " 사용함" : " 가능"),
                    done ? Palette.Disabled : ScreenKit.CategoryColor(cats[i]), done ? Palette.DisabledText : Color.white,
                    new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * 110, 0), 104, 22);
            }

            var slots = UiKit.Rect(_root, "Slots");
            UiKit.Anchor(slots, 0, 0, 0.40f, 1);
            slots.offsetMax = new Vector2(-6, -44);
            for (int i = 0; i < ShopRules.InventoryLimit; i++)
            {
                var cell = UiKit.Rect(slots, "Slot" + i);
                UiKit.Anchor(cell, 0, 1f - (i + 1) / 3f, 1, 1f - i / 3f, 0, 5);
                BuildSlot(cell, i, m);
            }

            var detail = UiKit.Rect(_root, "Detail");
            UiKit.Anchor(detail, 0.40f, 0, 1, 1);
            detail.offsetMin = new Vector2(6, 0);
            detail.offsetMax = new Vector2(0, -44);
            BuildDetail(detail, m);
        }

        private void BuildSlot(RectTransform cell, int index, InventoryModel m)
        {
            string id = index < m.Items.Count ? m.Items[index] : null;
            var item = id == null ? null : ItemCatalog.Get(id);
            bool selected = m.SelectedSlot == index && item != null;

            if (item == null)
            {
                var emptyFrame = ScreenKit.Card(cell, "Empty", Palette.CardBorder, Palette.Screen, out var emptyInner);
                UiKit.Stretch(emptyFrame);
                var t = UiKit.Label(emptyInner, "빈 칸", 14, Palette.DisabledText);
                UiKit.Stretch(t.rectTransform);
                return;
            }

            var color = ScreenKit.CategoryColor(item.Category);
            var frame = ScreenKit.Card(cell, "Item", selected ? ScreenKit.Amber : Palette.CardBorder, selected ? ScreenKit.AmberSoft : Palette.Card, out var inner, selected ? 3f : 1f);
            UiKit.Stretch(frame);
            var hit = UiKit.Button(inner, "", 12, Color.clear, Palette.Text, () => SelectSlot?.Invoke(index));
            UiKit.Stretch((RectTransform)hit.transform);
            hit.name = "Slot_" + index;

            var bar = UiKit.Panel(inner, "Bar", color, rounded: true);
            bar.rectTransform.anchorMin = new Vector2(0, 0);
            bar.rectTransform.anchorMax = new Vector2(0, 1);
            bar.rectTransform.offsetMin = new Vector2(0, 0);
            bar.rectTransform.offsetMax = new Vector2(6, 0);

            ScreenKit.Chip(inner, ItemCatalog.CategoryName(item.Category), color, Color.white, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -10), 44, 20);
            var text = UiKit.Label(inner, "<size=17><b>" + item.Name + "</b></size>\n<size=12><color=#6B7280>" + item.Short + " · " + ItemCatalog.WhenUsable(item.Category) + "</color></size>",
                13, Palette.Text, TextAnchor.MiddleLeft);
            UiKit.Stretch(text.rectTransform, 16, 30, 10, 6);
        }

        private void BuildDetail(RectTransform area, InventoryModel m)
        {
            var frame = ScreenKit.Card(area, "DetailCard", out var inner);
            UiKit.Stretch(frame);

            ItemDef item = m.SelectedSlot >= 0 && m.SelectedSlot < m.Items.Count ? ItemCatalog.Get(m.Items[m.SelectedSlot]) : null;
            if (item == null)
            {
                var t = UiKit.Label(inner, m.Items.Count == 0 ? "보유한 아이템이 없습니다. 상점에서 사거나 입찰·보상으로 얻을 수 있습니다." : "왼쪽에서 사용할 아이템을 고르세요.",
                    15, Palette.SubText);
                UiKit.Stretch(t.rectTransform, 20, 20, 20, 20);
                return;
            }

            var head = UiKit.Label(inner, "<size=22><b>" + item.Name + "</b></size>   <size=13><color=#" + ColorUtility.ToHtmlStringRGB(ScreenKit.CategoryColor(item.Category)) + ">"
                + ItemCatalog.CategoryName(item.Category) + "형 · " + ItemCatalog.WhenUsable(item.Category) + "</color></size>\n<size=13>" + item.Effect + "</size>",
                14, Palette.Text, TextAnchor.UpperLeft);
            var headRt = ScreenKit.TopStrip(inner, "Head", 14, 80, 18, 18);
            head.rectTransform.SetParent(headRt, false);
            UiKit.Stretch(head.rectTransform);

            var block = ItemUseRules.CanUse(item, m.Phase, m.UsedThisTurn);
            var targets = m.TargetsFor(item);
            if (block == UseBlock.None && item.Target != ItemTarget.None && !targets.Exists(o => o.Enabled)) block = UseBlock.NoTarget;

            float y = 104;
            if (item.Target != ItemTarget.None)
            {
                var tt = UiKit.Label(inner, "<b>" + TargetTitle(item.Target) + "</b>", 14, Palette.Text, TextAnchor.MiddleLeft);
                var ttRt = ScreenKit.TopStrip(inner, "TargetTitle", y, 22, 18, 18);
                tt.rectTransform.SetParent(ttRt, false);
                UiKit.Stretch(tt.rectTransform);
                y += 28;

                var row = ScreenKit.TopStrip(inner, "Targets", y, 40, 18, 18);
                for (int i = 0; i < targets.Count; i++)
                {
                    var opt = targets[i];
                    bool on = m.SelectedTarget == opt.Key;
                    var b = UiKit.Button(row, opt.Label, 13, on ? ScreenKit.Amber : Palette.Card, on ? Color.white : (opt.Enabled ? Palette.Text : Palette.DisabledText),
                        () => SelectTarget?.Invoke(opt.Key));
                    UiKit.Place((RectTransform)b.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * 150, 0), new Vector2(142, 38));
                    b.interactable = opt.Enabled && block == UseBlock.None;
                    if (!opt.Enabled && !string.IsNullOrEmpty(opt.Reason))
                    {
                        var r = UiKit.Label(inner, opt.Reason, 10, Palette.SubText, TextAnchor.UpperCenter);
                        UiKit.Place(r.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18 + i * 150, -(y + 42)), new Vector2(142, 14));
                    }
                }
                y += 64;
            }

            bool ready = m.UsageEnabled && block == UseBlock.None && (item.Target == ItemTarget.None || targets.Exists(o => o.Key == m.SelectedTarget && o.Enabled));
            string why = !m.UsageEnabled ? m.UnavailableReason : block != UseBlock.None ? ItemUseRules.Explain(block, item)
                : !ready ? "대상을 고르세요."
                : "확정하면 아이템이 사라집니다. 효과가 없거나 방어돼도 돌려받지 않습니다.";
            var msg = UiKit.Label(inner, (block != UseBlock.None ? "<color=#DC2626>" + why + "</color>" : why)
                    + (string.IsNullOrEmpty(m.LastMessage) ? "" : "\n<color=#047857><b>" + m.LastMessage + "</b></color>"),
                13, Palette.SubText, TextAnchor.UpperLeft);
            var msgRt = ScreenKit.TopStrip(inner, "Message", y, 44, 18, 18);
            msg.rectTransform.SetParent(msgRt, false);
            UiKit.Stretch(msg.rectTransform);

            var use = UiKit.Button(inner, "사용 확정", 16, ready ? Palette.Accent : Palette.Disabled, ready ? Color.white : Palette.DisabledText, () => Use?.Invoke());
            use.name = "UseItem";
            use.interactable = ready;
            UiKit.Place((RectTransform)use.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-18, 16), new Vector2(220, 46));
        }

        private static string TargetTitle(ItemTarget t)
        {
            switch (t)
            {
                case ItemTarget.OwnCompany: return "적용할 내 기업";
                case ItemTarget.OwnInterval: return "지정할 내 구간";
                case ItemTarget.Company: return "열어 볼 기업";
                case ItemTarget.Player: return "대상 플레이어";
                case ItemTarget.TopPlayer: return "대상 (턴 시작 총자산 상위 2명 중 나를 뺀 사람)";
                default: return "";
            }
        }
    }
}
