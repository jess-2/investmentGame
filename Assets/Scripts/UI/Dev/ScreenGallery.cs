// D 화면 미리보기(게임 서버와 연결하지 않음).
// 메뉴 BlackWhale > D 화면 미리보기 (게임 미연결) 로 빈 씬에서 실행된다.
// 아직 A/B/C 계약이 없는 화면(입찰·능력·상점·인벤토리·BOT 난이도)과 10턴 전광판·최종 결과를
// 예시 데이터로 띄워 모양과 조작을 확인하는 용도다. 여기서 누르는 버튼은 이 창 안의 예시 값만 바꾼다.
using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BlackWhale.UI.Dev
{
    public sealed class ScreenGallery : MonoBehaviour
    {
        private sealed class Tab
        {
            public string Name;
            public Action Build;
            public List<KeyValuePair<string, Action>> Extras = new List<KeyValuePair<string, Action>>();
        }

        private RectTransform _content, _extras;
        private readonly List<Tab> _tabs = new List<Tab>();
        private readonly List<Button> _tabButtons = new List<Button>();
        private int _current;

        // 예시 상태(이 창 안에서만 바뀜)
        private AuctionModel _auction3, _auction6, _auction9;
        private AbilityPickModel _ability;
        private ShopModel _shop;
        private InventoryModel _inventory;
        private BotDifficulty _difficulty = BotDifficulty.Normal;
        private bool _difficultyAsHost = true;
        private int _liveTurn = 7;

        private void Start()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            var go = new GameObject("GalleryCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;

            ResetModels();
            DefineTabs();
            BuildChrome((RectTransform)go.transform);
            Select(0);
        }

        // ------------------------------------------------------------------
        // 틀
        // ------------------------------------------------------------------

        private void BuildChrome(RectTransform canvas)
        {
            var bg = UiKit.Panel(canvas, "Background", Palette.Background);
            UiKit.Stretch(bg.rectTransform);

            var top = UiKit.Rect(canvas, "TopBar");
            top.anchorMin = new Vector2(0, 1);
            top.anchorMax = new Vector2(1, 1);
            top.pivot = new Vector2(0.5f, 1);
            top.offsetMin = new Vector2(10, -78);
            top.offsetMax = new Vector2(-10, -6);

            var title = UiKit.Label(top, "<b>D 화면 미리보기</b>  <size=12>게임 서버와 연결되지 않은 예시 데이터입니다. 버튼은 이 창 안의 값만 바꿉니다.</size>",
                15, Color.white, TextAnchor.UpperLeft);
            UiKit.Stretch(title.rectTransform, 2, 0, 2, 50);

            var tabs = UiKit.Rect(top, "Tabs");
            tabs.anchorMin = new Vector2(0, 0);
            tabs.anchorMax = new Vector2(1, 0);
            tabs.pivot = new Vector2(0.5f, 0);
            tabs.offsetMin = new Vector2(0, 0);
            tabs.offsetMax = new Vector2(0, 40);
            float w = 1f / _tabs.Count;
            for (int i = 0; i < _tabs.Count; i++)
            {
                int index = i;
                var b = UiKit.Button(tabs, _tabs[i].Name, 12, Palette.Card, Palette.Text, () => Select(index));
                b.name = "Tab_" + i;
                UiKit.Anchor((RectTransform)b.transform, i * w, 0, (i + 1) * w, 1, 2, 2);
                _tabButtons.Add(b);
            }

            var screen = UiKit.Panel(canvas, "Screen", Palette.Screen, rounded: true);
            UiKit.Stretch(screen.rectTransform, 10, 84, 10, 46);
            _content = UiKit.Rect(screen.transform, "Content");
            UiKit.Stretch(_content, 16, 12, 16, 12);

            _extras = UiKit.Rect(canvas, "Extras");
            _extras.anchorMin = new Vector2(0, 0);
            _extras.anchorMax = new Vector2(1, 0);
            _extras.pivot = new Vector2(0.5f, 0);
            _extras.offsetMin = new Vector2(10, 6);
            _extras.offsetMax = new Vector2(-10, 40);
        }

        private void Select(int index)
        {
            _current = index;
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                bool on = i == index;
                UiKit.SetButton(_tabButtons[i], _tabs[i].Name, true, on ? Palette.Accent : Palette.Card, on ? Color.white : Palette.Text);
            }
            Rebuild();

            UiKit.Clear(_extras);
            var label = UiKit.Label(_extras, "예시 조작:", 12, Color.white, TextAnchor.MiddleLeft);
            UiKit.Place(label.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(70, 30));
            var extras = _tabs[index].Extras;
            for (int i = 0; i < extras.Count; i++)
            {
                var action = extras[i].Value;
                var b = UiKit.Button(_extras, extras[i].Key, 12, Palette.Card, Palette.Text, () => { action(); Rebuild(); });
                UiKit.Place((RectTransform)b.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(76 + i * 176, 0), new Vector2(170, 30));
            }
        }

        private void Rebuild()
        {
            UiKit.Clear(_content);
            _tabs[_current].Build();
        }

        private RectTransform NewRoot(string name)
        {
            var rt = UiKit.Rect(_content, name);
            UiKit.Stretch(rt);
            return rt;
        }

        // ------------------------------------------------------------------
        // 탭
        // ------------------------------------------------------------------

        private void DefineTabs()
        {
            var live = new Tab { Name = "전광판", Build = () => new ScoreboardScreen(NewRoot("Scoreboard")).Show(PreviewData.LiveBoard(_liveTurn)) };
            live.Extras.Add(new KeyValuePair<string, Action>("다음 턴 정산 보기", () => _liveTurn = _liveTurn >= 10 ? 2 : _liveTurn + 1));
            live.Extras.Add(new KeyValuePair<string, Action>("정산 연출 다시 보기", () => { }));
            _tabs.Add(live);
            var final = new Tab { Name = "최종 결과", Build = () => new ScoreboardScreen(NewRoot("Final")).Show(PreviewData.FinalBoard()) };
            final.Extras.Add(new KeyValuePair<string, Action>("발표 연출 다시 보기", () => { }));
            _tabs.Add(final);

            _tabs.Add(AuctionTab("3턴 입찰", () => _auction3, PreviewData.BlackBoxResult));
            _tabs.Add(AuctionTab("6턴 입찰", () => _auction6, PreviewData.FreePassNoWinner));
            _tabs.Add(AuctionTab("9턴 자리 입찰", () => _auction9, PreviewData.SeatResult));

            var ability = new Tab { Name = "능력 선택", Build = BuildAbility };
            ability.Extras.Add(new KeyValuePair<string, Action>("처음부터 다시", () => _ability = PreviewData.Ability()));
            _tabs.Add(ability);

            var shop = new Tab { Name = "상점", Build = BuildShop };
            shop.Extras.Add(new KeyValuePair<string, Action>("다음 턴(구매 초기화)", () => _shop.BoughtThisTurn = 0));
            shop.Extras.Add(new KeyValuePair<string, Action>("자산 0으로", () => { _shop.TurnStartAssets = 0; _shop.Money = 0; }));
            shop.Extras.Add(new KeyValuePair<string, Action>("처음부터 다시", () => _shop = PreviewData.Shop()));
            _tabs.Add(shop);

            var inv = new Tab { Name = "보유 아이템", Build = BuildInventory };
            inv.Extras.Add(new KeyValuePair<string, Action>("정보 탐색 단계로", () => { _inventory.Phase = Protocol.Info; _inventory.SelectedTarget = ""; }));
            inv.Extras.Add(new KeyValuePair<string, Action>("투자 결정 단계로", () => { _inventory.Phase = Protocol.Investment; _inventory.SelectedTarget = ""; }));
            inv.Extras.Add(new KeyValuePair<string, Action>("처음부터 다시", () => _inventory = PreviewData.Inventory()));
            _tabs.Add(inv);

            var bot = new Tab { Name = "BOT 난이도", Build = BuildDifficulty };
            bot.Extras.Add(new KeyValuePair<string, Action>("방장/참가자 화면 전환", () => _difficultyAsHost = !_difficultyAsHost));
            _tabs.Add(bot);
        }

        private Tab AuctionTab(string name, Func<AuctionModel> get, Func<AuctionResultModel> result)
        {
            var tab = new Tab { Name = name };
            tab.Build = () =>
            {
                var model = get();
                var screen = new AuctionScreen(NewRoot("Auction"));
                screen.BidChanged = v => model.Bid = v;
                screen.FreePassToggled = on => { model.UseFreePass = on; Rebuild(); };
                screen.PreferenceChanged = order => { model.SeatPreference = order; Rebuild(); };
                screen.Confirm = () =>
                {
                    if (model.Submitted || AuctionRules.Validate(model.Bid, model.TurnStartAssets).Length > 0) return;
                    model.Submitted = true;
                    model.SubmittedCount = Mathf.Min(model.PlayerCount, model.SubmittedCount + 1);
                    Rebuild();
                };
                screen.Show(model);
            };
            tab.Extras.Add(new KeyValuePair<string, Action>("마감 → 결과 보기", () => { get().Result = result(); }));
            tab.Extras.Add(new KeyValuePair<string, Action>("남은 시간 3초로", () => { get().SecondsLeft = 3; }));
            tab.Extras.Add(new KeyValuePair<string, Action>("처음부터 다시", () => ResetAuction(get())));
            return tab;
        }

        private void ResetAuction(AuctionModel m)
        {
            var fresh = PreviewData.Auction(m.Kind);
            if (m.Kind == AuctionKind.BlackBox) _auction3 = fresh;
            else if (m.Kind == AuctionKind.FreePass) _auction6 = fresh;
            else _auction9 = fresh;
        }

        private void BuildAbility()
        {
            var screen = new AbilityPickScreen(NewRoot("Ability"));
            screen.Picked = id => { if (!_ability.Confirmed) { _ability.Selected = id; Rebuild(); } };
            screen.Confirm = () => { if (!string.IsNullOrEmpty(_ability.Selected)) { _ability.Confirmed = true; Rebuild(); } };
            screen.Show(_ability, _ability.Confirmed ? PreviewData.StatusFor(_ability.Selected) : null);
        }

        private void BuildShop()
        {
            var screen = new ShopScreen(NewRoot("Shop"));
            screen.Buy = id =>
            {
                var item = ItemCatalog.Get(id);
                if (ShopRules.CanBuy(item, _shop.TurnStartAssets, _shop.Money, _shop.BoughtThisTurn, _shop.Inventory.Count) != ShopBlock.None) return;
                _shop.Money -= ShopRules.Price(item, _shop.TurnStartAssets);
                _shop.BoughtThisTurn++;
                _shop.Inventory.Add(id);
                Rebuild();
            };
            screen.Show(_shop);
        }

        private void BuildInventory()
        {
            var screen = new InventoryScreen(NewRoot("Inventory"));
            screen.SelectSlot = i => { _inventory.SelectedSlot = i; _inventory.SelectedTarget = ""; _inventory.LastMessage = ""; Rebuild(); };
            screen.SelectTarget = key => { _inventory.SelectedTarget = key; Rebuild(); };
            screen.Use = () =>
            {
                int slot = _inventory.SelectedSlot;
                if (slot < 0 || slot >= _inventory.Items.Count) return;
                var item = ItemCatalog.Get(_inventory.Items[slot]);
                var target = _inventory.TargetsFor(item).FirstOrDefault(t => t.Key == _inventory.SelectedTarget);
                _inventory.Items.RemoveAt(slot);
                _inventory.UsedThisTurn.Add(item.Category);
                _inventory.SelectedSlot = -1;
                _inventory.SelectedTarget = "";
                _inventory.LastMessage = item.Name + " 사용 요청" + (target != null ? " → " + target.Label : "") + " (예시: 실제 효과는 서버 판정)";
                Rebuild();
            };
            screen.Show(_inventory);
        }

        private void BuildDifficulty()
        {
            var panel = new BotDifficultyPanel(NewRoot("Difficulty"));
            panel.Changed = d => { _difficulty = d; Rebuild(); };
            panel.Show(_difficulty, _difficultyAsHost);
        }

        private void ResetModels()
        {
            _auction3 = PreviewData.Auction(AuctionKind.BlackBox);
            _auction6 = PreviewData.Auction(AuctionKind.FreePass);
            _auction9 = PreviewData.Auction(AuctionKind.SeatPriority);
            _ability = PreviewData.Ability();
            _shop = PreviewData.Shop();
            _inventory = PreviewData.Inventory();
        }
    }
}
