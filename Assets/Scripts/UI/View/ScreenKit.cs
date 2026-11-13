// NEXT-D05·D06 화면들이 함께 쓰는 작은 조각(카드·칩·띠 배치·금액 입력).
using BlackWhale.UI.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public static class ScreenKit
    {
        public static readonly Color Amber = new Color32(0xF5, 0x9E, 0x0B, 0xFF);
        public static readonly Color AmberSoft = new Color32(0xFE, 0xF3, 0xC7, 0xFF);
        public static readonly Color Gold = new Color32(0xCA, 0x8A, 0x04, 0xFF);
        public static readonly Color Ink = new Color32(0x11, 0x18, 0x27, 0xFF);

        /// <summary>테두리 카드. 반환값은 바깥(배치용), inner는 내용을 넣을 안쪽.</summary>
        public static RectTransform Card(Transform parent, string name, Color border, Color fill, out RectTransform inner, float borderWidth = 1f)
        {
            var frame = UiKit.Panel(parent, name, border, rounded: true);
            var body = UiKit.Panel(frame.transform, "Inner", fill, rounded: true);
            UiKit.Stretch(body.rectTransform, borderWidth, borderWidth, borderWidth, borderWidth);
            inner = body.rectTransform;
            return frame.rectTransform;
        }

        public static RectTransform Card(Transform parent, string name, out RectTransform inner)
            => Card(parent, name, Palette.CardBorder, Palette.Card, out inner);

        /// <summary>부모 위쪽에서 top만큼 내려와 height 높이로 가로 전체를 채우는 띠.</summary>
        public static RectTransform TopStrip(RectTransform parent, string name, float top, float height, float left = 0, float right = 0)
        {
            var rt = UiKit.Rect(parent, name);
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(left, -top - height);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>둥근 배경의 짧은 표시(분류·상태).</summary>
        public static RectTransform Chip(Transform parent, string text, Color bg, Color fg, Vector2 anchor, Vector2 pivot, Vector2 pos, float width, float height = 20f, int fontSize = 11)
        {
            var img = UiKit.Panel(parent, "Chip", bg, rounded: true);
            UiKit.Place(img.rectTransform, anchor, pivot, pos, new Vector2(width, height));
            var t = UiKit.Label(img.transform, text, fontSize, fg, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(t.rectTransform, 2, 0, 2, 0);
            return img.rectTransform;
        }

        public static Text Title(RectTransform parent, string text, float top = 0, int size = 20)
        {
            var t = UiKit.Label(parent, text, size, Palette.Text, TextAnchor.MiddleLeft);
            var rt = TopStrip(parent, "Title", top, size + 14, 4, 4);
            t.rectTransform.SetParent(rt, false);
            UiKit.Stretch(t.rectTransform);
            return t;
        }

        public static Color CategoryColor(ItemCategory c)
        {
            switch (c)
            {
                case ItemCategory.Finance: return new Color32(0x25, 0x63, 0xEB, 0xFF);
                case ItemCategory.Info: return new Color32(0x05, 0x96, 0x69, 0xFF);
                case ItemCategory.Attack: return new Color32(0xDC, 0x26, 0x26, 0xFF);
                default: return Gold;
            }
        }

        public static Color EventColor(BoardEventKind k)
        {
            switch (k)
            {
                case BoardEventKind.Auction: return Gold;
                case BoardEventKind.Ability: return new Color32(0x7C, 0x3A, 0xED, 0xFF);
                case BoardEventKind.Attack: return new Color32(0xDC, 0x26, 0x26, 0xFF);
                case BoardEventKind.Bonus: return new Color32(0x05, 0x96, 0x69, 0xFF);
                default: return Palette.SubText;
            }
        }

        /// <summary>금액 입력 칸(정수, 최대 10자리).</summary>
        public static InputField MoneyInput(Transform parent, string text, int fontSize)
        {
            var field = UiKit.NumberInput(parent, text, fontSize);
            field.characterLimit = 10;
            if (field.placeholder is Text ph) ph.text = "금액";
            return field;
        }

        public static string Signed(int bps) => "<color=#" + ColorUtility.ToHtmlStringRGB(Palette.ForBps(bps)) + ">" + DisplayFormat.Percent(bps) + "</color>";
    }
}
