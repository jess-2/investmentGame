// NEXT-D06 3·6·9턴 입찰 화면(서버 계약 전: 모델만 그린다).
// - 3턴 블랙박스 분석권 / 6턴 블랙웨일 프리패스: 비밀 최고가, 낙찰자만 지불, 모두 0이면 유찰
// - 9턴 자리 순위: 입찰액 순서로 A~D 배정, 각자 자기 입찰액 지불, 자리 선호 순서 제출, 프리패스 사용 여부
// - 금액은 0 ~ floor(S×10%). 확정하면 바꿀 수 없다. 다른 사람 금액은 마감 전까지 보이지 않는다(제출 인원만).
// - 버튼은 의도(BidChanged/Confirm 등)만 알린다. 실제 전송·판정은 계약이 생기면 컨트롤러가 맡는다.
using System;
using BlackWhale.UI.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public sealed class AuctionScreen
    {
        private readonly RectTransform _root;
        private AuctionModel _m;
        private Text _validation, _score;
        private Button _confirm;
        private Text _clock;
        private bool _validInput = true;

        public Action<long> BidChanged;
        public Action Confirm;
        public Action<bool> FreePassToggled;
        public Action<string[]> PreferenceChanged;

        public AuctionScreen(RectTransform root) => _root = root;

        public void Show(AuctionModel m)
        {
            _m = m;
            _validation = null;
            _score = null;
            _confirm = null;
            UiKit.Clear(_root);
            if (m == null) return;

            ScreenKit.Title(_root, "<b>" + m.Turn + "턴 입찰</b>   <size=16>" + AuctionCatalog.Title(m.Kind) + "</size>");
            _validInput = true;
            _clock = ScreenKit.Chip(_root, m.Result != null ? "마감" : "남은 시간 " + DisplayFormat.Clock(m.SecondsLeft),
                m.Result != null ? Palette.Disabled : (m.SecondsLeft <= 5 ? Palette.Up : ScreenKit.Ink), m.Result != null ? Palette.SubText : Color.white,
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-4, -4), 130, 28, 14).GetComponentInChildren<Text>();
            var count = UiKit.Label(_root, m.Result != null ? "공개된 입찰 결과" : m.SubmittedCount < 0 ? (m.Submitted ? "내 입찰 제출됨 · 금액 비공개" : "내 입찰 미제출 · 금액 비공개")
                : "제출 " + m.SubmittedCount + "/" + m.PlayerCount + "명 <size=11>(금액은 비공개)</size>", 13, Palette.SubText, TextAnchor.MiddleRight);
            UiKit.Place(count.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-142, -4), new Vector2(260, 28));

            var left = UiKit.Rect(_root, "Prize");
            UiKit.Anchor(left, 0, 0, 0.44f, 1);
            left.offsetMax = new Vector2(-6, -44);
            BuildPrize(left, m);

            var right = UiKit.Rect(_root, "Bid");
            UiKit.Anchor(right, 0.44f, 0, 1, 1);
            right.offsetMin = new Vector2(6, 0);
            right.offsetMax = new Vector2(0, -44);
            if (m.Result != null) BuildResult(right, m);
            else BuildBidForm(right, m);
        }

        private static void BuildPrize(RectTransform area, AuctionModel m)
        {
            var frame = ScreenKit.Card(area, "PrizeCard", ScreenKit.Gold, ScreenKit.AmberSoft, out var inner, 2f);
            UiKit.Stretch(frame);

            string badge = m.Kind == AuctionKind.SeatPriority ? "A B C D" : m.Kind == AuctionKind.BlackBox ? "BOX" : "PASS";
            var icon = UiKit.Panel(inner, "Icon", ScreenKit.Ink, rounded: true);
            UiKit.Place(icon.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(150, 90));
            var iconText = UiKit.Label(icon.transform, badge, 26, ScreenKit.Amber, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(iconText.rectTransform);

            string what;
            if (m.Kind == AuctionKind.SeatPriority)
                what = "9턴과 10턴에 기업을 고르는 자리(A가 가장 먼저)를 정합니다.";
            else
                what = ItemCatalog.Get(m.Kind == AuctionKind.BlackBox ? "BLACKBOX" : "FREEPASS")?.Effect ?? "";

            var text = UiKit.Label(inner,
                "<size=20><b>" + AuctionCatalog.Title(m.Kind) + "</b></size>\n\n" + what + "\n\n" +
                "<size=13><color=#374151>" +
                "· " + AuctionCatalog.Rule(m.Kind) + "\n" +
                (m.ShowBidLimit ? "· 입찰 범위: 0 ~ 턴 시작 자산의 10% (" + DisplayFormat.Money(m.MaxBid) + ")\n" : "") +
                "· 한 번 확정하면 바꿀 수 없고, 마감 전에는 다른 사람 금액을 볼 수 없습니다.\n" +
                "· 금액이 같으면 시작 자산이 낮은 사람이 앞섭니다.\n" +
                "· 입찰 비용은 이번 턴 투자금에서 빠집니다.</color></size>",
                15, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(text.rectTransform, 20, 126, 20, 12);
        }

        private void BuildBidForm(RectTransform area, AuctionModel m)
        {
            var frame = ScreenKit.Card(area, "BidCard", out var inner);
            UiKit.Stretch(frame);

            var info = UiKit.Label(inner, "턴 시작 자산 <b>" + DisplayFormat.Money(m.TurnStartAssets) + "</b>   ·   입찰 가능 <b>0 ~ " + DisplayFormat.Money(m.MaxBid) + "</b>",
                15, Palette.Text, TextAnchor.MiddleLeft);
            var infoRt = ScreenKit.TopStrip(inner, "Info", 14, 26, 18, 18);
            info.rectTransform.SetParent(infoRt, false);
            UiKit.Stretch(info.rectTransform);

            // 금액 입력 + 빠른 버튼
            var row = ScreenKit.TopStrip(inner, "Amount", 50, 44, 18, 18);
            var field = ScreenKit.MoneyInput(row, m.Bid.ToString(), 20);
            field.name = "BidAmount";
            UiKit.Place((RectTransform)field.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(200, 44));
            field.interactable = !m.Submitted;
            field.onValueChanged.AddListener(OnTyped);

            var quick = new[] { ("0", 0L), ("25%", m.MaxBid / 4), ("50%", m.MaxBid / 2), ("최대", m.MaxBid) };
            for (int i = 0; i < quick.Length; i++)
            {
                long value = quick[i].Item2;
                var b = UiKit.Button(row, quick[i].Item1, 13, Palette.Card, Palette.Text, () =>
                {
                    field.text = value.ToString(); // onValueChanged가 검사·알림
                });
                UiKit.Place((RectTransform)b.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(212 + i * 70, 0), new Vector2(64, 36));
                b.interactable = !m.Submitted;
            }

            _validation = UiKit.Label(inner, "", 13, Palette.Up, TextAnchor.MiddleLeft);
            var valRt = ScreenKit.TopStrip(inner, "Validation", 98, 22, 18, 18);
            _validation.rectTransform.SetParent(valRt, false);
            UiKit.Stretch(_validation.rectTransform);

            float y = 128;
            if (m.Kind == AuctionKind.SeatPriority)
            {
                // 자리 선호 순서
                var prefTitle = UiKit.Label(inner, "<b>원하는 자리 순서</b>  <size=11><color=#6B7280>입찰 순위대로 이 순서에서 남은 자리를 배정 · ▲로 순서 변경</color></size>", 14, Palette.Text, TextAnchor.MiddleLeft);
                var ptRt = ScreenKit.TopStrip(inner, "PrefTitle", y, 22, 18, 18);
                prefTitle.rectTransform.SetParent(ptRt, false);
                UiKit.Stretch(prefTitle.rectTransform);
                y += 26;

                var pref = AuctionRules.NormalizePreference(m.SeatPreference);
                for (int i = 0; i < pref.Length; i++)
                {
                    int index = i;
                    var pr = ScreenKit.TopStrip(inner, "Pref" + i, y, 30, 18, 18);
                    var cardFrame = ScreenKit.Card(pr, "Seat", i == 0 ? Palette.Accent : Palette.CardBorder, i == 0 ? Palette.AccentSoft : Palette.Card, out var seatInner);
                    UiKit.Place(cardFrame, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(240, 28));
                    var lab = UiKit.Label(seatInner, (i + 1) + "순위   <b>자리 " + pref[i] + "</b>" + (pref[i] == "A" ? "  <size=11>(가장 먼저 고름)</size>" : ""), 13, Palette.Text, TextAnchor.MiddleLeft);
                    UiKit.Stretch(lab.rectTransform, 10, 0, 4, 0);
                    if (i > 0)
                    {
                        var up = UiKit.Button(pr, "▲", 12, Palette.Card, Palette.Text, () => PreferenceChanged?.Invoke(AuctionRules.MoveUp(pref, index)));
                        UiKit.Place((RectTransform)up.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(248, 0), new Vector2(34, 28));
                        up.interactable = !m.Submitted;
                    }
                    y += 33;
                }

                if (m.HasFreePass)
                {
                    y += 6;
                    var pr = ScreenKit.TopStrip(inner, "FreePass", y, 34, 18, 18);
                    var toggle = UiKit.Button(pr, m.UseFreePass ? "프리패스 사용함 (점수 +25%)" : "프리패스 사용 안 함", 13,
                        m.UseFreePass ? ScreenKit.Gold : Palette.Card, m.UseFreePass ? Color.white : Palette.Text,
                        () => FreePassToggled?.Invoke(!m.UseFreePass));
                    UiKit.Place((RectTransform)toggle.transform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(240, 32));
                    toggle.interactable = !m.Submitted;
                    y += 38;
                }

                _score = UiKit.Label(inner, "", 13, Palette.SubText, TextAnchor.MiddleLeft);
                var scRt = ScreenKit.TopStrip(inner, "Score", y, 22, 18, 18);
                _score.rectTransform.SetParent(scRt, false);
                UiKit.Stretch(_score.rectTransform);
            }
            else _score = null;

            // 확정
            _confirm = UiKit.Button(inner, m.Submitted ? "확정됨 · 결과 대기 중" : "입찰 확정 (확정 후 변경 불가)", 16,
                Palette.Accent, Color.white, () => Confirm?.Invoke());
            _confirm.name = "ConfirmBid";
            UiKit.Place((RectTransform)_confirm.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-18, 16), new Vector2(280, 46));

            var hint = UiKit.Label(inner, m.Submitted ? "<color=#047857><b>입찰을 확정했습니다.</b></color> 마감 후 결과가 공개됩니다."
                    : "시간 안에 확정하지 않으면 0으로 처리됩니다.", 12, Palette.SubText, TextAnchor.MiddleLeft);
            UiKit.Place(hint.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(18, 26), new Vector2(360, 26));

            Revalidate(m.Bid);
        }

        private void OnTyped(string text)
        {
            if (_m == null || _m.Submitted) return;
            _validInput = long.TryParse(string.IsNullOrEmpty(text) ? "0" : text, out var bid);
            Revalidate(bid);
            if (_validInput) { _m.Bid = bid; BidChanged?.Invoke(bid); }
        }

        private void Revalidate(long bid)
        {
            if (_m == null) return;
            string error = !_validInput ? "유효한 정수 금액을 입력하세요." : bid < 0 ? "0 이상으로 입력하세요."
                : bid > _m.MaxBid ? "최대 " + DisplayFormat.Money(_m.MaxBid) + "까지 입찰할 수 있습니다." : "";
            if (_validation != null) _validation.text = error;
            if (_score != null)
                _score.text = "입찰 점수 " + ((decimal)bid * (_m.UseFreePass ? 1.25m : 1m)).ToString("N0")
                              + (_m.UseFreePass ? " (입찰액 × 1.25)" : " (= 입찰액)") + " · 실제 비용은 입찰액 그대로";
            if (_confirm != null)
            {
                bool can = !_m.Submitted && error.Length == 0;
                UiKit.SetButton(_confirm, _m.Submitted ? "확정됨 · 결과 대기 중" : "입찰 확정 (확정 후 변경 불가)", can,
                    can ? Palette.Accent : Palette.Disabled, can ? Color.white : Palette.DisabledText);
            }
        }

        public void UpdateSeconds(int seconds)
        { if (_clock != null && _m?.Result == null) _clock.text = "남은 시간 " + DisplayFormat.Clock(seconds); }

        private static void BuildResult(RectTransform area, AuctionModel m)
        {
            var frame = ScreenKit.Card(area, "ResultCard", out var inner);
            UiKit.Stretch(frame);
            var r = m.Result;

            string myLine = m.OwnBidKnown ? "내 입찰: <b>" + (m.Submitted ? DisplayFormat.Money(m.Bid) : "미제출(0)") + "</b>" : "이전 입찰 금액은 표시하지 않습니다.";
            if (m.Kind != AuctionKind.SeatPriority)
            {
                string body = r.NoWinner
                    ? "<size=28><b>유찰</b></size>\n\n모두 0을 불러 아무도 받지 않았습니다. 비용도 없습니다."
                    : "<size=14><color=#6B7280>낙찰</color></size>\n<size=30><b>" + r.WinnerName + "</b></size>\n<size=18>"
                      + (r.AmountsPublic ? DisplayFormat.Money(r.WinningBid) + " 지불" : "낙찰 금액 비공개") + "</size>";
                var text = UiKit.Label(inner, body + "\n\n<size=13>" + myLine + "</size>", 16, Palette.Text, TextAnchor.MiddleCenter);
                UiKit.Stretch(text.rectTransform, 20, 20, 20, 20);
                return;
            }

            var title = UiKit.Label(inner, "<b>9·10턴 자리 배정</b>   <size=12><color=#6B7280>모두 자기 입찰액을 냅니다</color></size>", 16, Palette.Text, TextAnchor.MiddleLeft);
            var tRt = ScreenKit.TopStrip(inner, "Title", 14, 26, 18, 18);
            title.rectTransform.SetParent(tRt, false);
            UiKit.Stretch(title.rectTransform);

            float y = 50;
            foreach (var s in r.Seats)
            {
                var row = ScreenKit.TopStrip(inner, "Seat" + s.Seat, y, 46, 18, 18);
                var bg = UiKit.Panel(row, "Bg", Palette.Screen, rounded: true);
                UiKit.Stretch(bg.rectTransform);
                var seat = UiKit.Label(row, "<size=22><b>" + s.Seat + "</b></size>", 14, ScreenKit.Gold, TextAnchor.MiddleCenter);
                UiKit.Place(seat.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(40, 40));
                var band = UiKit.Panel(row, "PlayerColor", Palette.Player(s.PlayerId));
                UiKit.Place(band.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(52, 0), new Vector2(4, 28));
                var name = UiKit.Label(row, "<b>" + s.Name + "</b>" + (s.UsedFreePass ? "  <size=11><color=#CA8A04>프리패스 사용</color></size>" : ""), 15, Palette.Text, TextAnchor.MiddleLeft);
                UiKit.Stretch(name.rectTransform, 64, 0, 160, 0);
                var paid = UiKit.Label(row, r.AmountsPublic ? DisplayFormat.Money(s.Paid) + " 지불" : "금액 비공개", 14, Palette.Text, TextAnchor.MiddleRight);
                UiKit.Stretch(paid.rectTransform, 0, 0, 12, 0);
                y += 52;
            }
            var mine = UiKit.Label(inner, myLine + " · 선택 순서는 A→B→C→D→D→C→B→A", 13, Palette.SubText, TextAnchor.LowerLeft);
            UiKit.Stretch(mine.rectTransform, 18, 0, 18, 14);
        }
    }
}
