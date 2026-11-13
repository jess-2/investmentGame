// IMP-07 BOT 난이도 설정 패널(대기실용). 방 전체 설정 1개, 방장만 변경, 대기실에서만 변경(05 기본안).
// - 이 패널은 선택값을 Changed로 알리기만 한다. 실제 BOT 생성에 값을 넘기는 연결(DevTabletBootstrap)은 A 담당.
using System;
using BlackWhale.UI.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public sealed class BotDifficultyPanel
    {
        private readonly RectTransform _root;

        public Action<BotDifficulty> Changed;

        public BotDifficultyPanel(RectTransform root) => _root = root;

        public void Show(BotDifficulty selected, bool isHost, bool inLobby = true)
        {
            UiKit.Clear(_root);
            bool editable = isHost && inLobby;

            ScreenKit.Title(_root, "<b>BOT 난이도</b>   <size=14><color=#6B7280>방 전체에 하나 · 대기실에서 방장만 변경</color></size>");

            var row = UiKit.Rect(_root, "Options");
            UiKit.Stretch(row, 0, 48, 0, 70);
            var values = new[] { BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard };
            for (int i = 0; i < values.Length; i++)
            {
                var d = values[i];
                bool on = d == selected;
                var cell = UiKit.Rect(row, "Difficulty_" + d);
                UiKit.Anchor(cell, i / 3f, 0, (i + 1) / 3f, 1, 8, 0);
                var frame = ScreenKit.Card(cell, "Frame", on ? Palette.Accent : Palette.CardBorder, on ? Palette.AccentSoft : Palette.Card, out var inner, on ? 3f : 1f);
                UiKit.Stretch(frame);

                var hit = UiKit.Button(inner, "", 12, Color.clear, Palette.Text, () => Changed?.Invoke(d));
                UiKit.Stretch((RectTransform)hit.transform);
                hit.name = "BotDifficulty_" + d;
                hit.interactable = editable;
                var colors = hit.colors;
                colors.disabledColor = Color.clear;
                hit.colors = colors;

                // 난이도 막대(1~3칸)
                for (int k = 0; k < 3; k++)
                {
                    var pip = UiKit.Panel(inner, "Pip" + k, k <= i ? (on ? Palette.Accent : Palette.SubText) : Palette.Disabled, rounded: true);
                    UiKit.Place(pip.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2((k - 1) * 26, -28), new Vector2(20, 20));
                }

                var text = UiKit.Label(inner, "<size=24><b>" + BotDifficultyInfo.Name(d) + "</b></size>" + (on ? "\n<size=12><color=#2563EB><b>선택됨</b></color></size>" : "\n ")
                        + "\n\n" + BotDifficultyInfo.Describe(d),
                    14, Palette.Text, TextAnchor.UpperCenter);
                UiKit.Stretch(text.rectTransform, 18, 62, 18, 12);
            }

            string note = !inLobby ? "게임이 시작되면 난이도를 바꿀 수 없습니다."
                : !isHost ? "방장만 바꿀 수 있습니다. 현재 설정: " + BotDifficultyInfo.Name(selected)
                : "현재 방의 모든 BOT과 새로 추가할 BOT에 적용됩니다. 어떤 난이도도 숨은 시세를 보지 않고, 자기에게 공개된 정보만 씁니다.";
            var foot = UiKit.Label(_root, note, 13, Palette.SubText, TextAnchor.MiddleLeft);
            UiKit.Place(foot.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(8, 20), new Vector2(1000, 40));
        }
    }
}
