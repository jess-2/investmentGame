// NEXT-D01 일봉(캔들) 그래프의 표현 모델(Unity 의존 없음).
// - 봉 1개 = 하루. 종가 = 그 날짜의 '0일차 대비' 변화율(bps), 시가 = 전날 종가(전날이 열람된 경우만).
//   세로축은 그대로 0일차 대비 %이므로 각 봉의 종가 위치가 서버가 준 값과 같다.
// - 데이터가 하루 1개 값뿐이라 장중 고가·저가가 없다. 꼬리(심지)를 지어내지 않는다.
// - 전날이 미열람이면 시가를 모르므로 몸통 없이 종가 표식만 그린다.
// - 0일차는 항상 알려진 0%(공개 기준값).
// - 미열람 날짜는 Hidden(값 없음). 0%로 바꾸지 않는다.
// - 공개(MARKET_REVEAL) 전에는 PlayerView의 visibleBundles만 쓴다. 공개 후에만 RevealedCompany 전체를 쓴다.
// - 축은 현재 열람 가능한 시가·종가만으로 자동 맞춘다. 비공개 원본과 종목 상한은 쓰지 않는다.
using System.Collections.Generic;
using BlackWhale.Shared;
using BlackWhale.UI.State;

namespace BlackWhale.UI.Presentation
{
    public enum DayBarState
    {
        Hidden,   // 아직 볼 수 없는 날짜
        Zero,     // 알려진 0% (Hidden과 다르게 표시)
        Up,       // 양수
        Down      // 음수
    }

    /// <summary>봉 방향(시가→종가). 한국 증권 관례: 양봉 빨강, 음봉 파랑.</summary>
    public enum CandleMove
    {
        Unknown,  // 시가(전날 값)를 모름 — 종가 표식만
        Rise,     // 양봉: 종가 > 시가
        Fall,     // 음봉: 종가 < 시가
        Flat      // 보합: 종가 = 시가 (0일차 포함)
    }

    public struct DayBar
    {
        public int Day;
        public int? Bps;
        public DayBarState State;
        /// <summary>PUBLIC / PURCHASED / ITEM / STOLEN / ABILITY / BASE(0일차) / REVEALED / "" (미열람)</summary>
        public string Source;

        /// <summary>시가(= 전날 종가). 0일차는 0, 전날 미열람이면 null.</summary>
        public int? OpenBps;
        public CandleMove Move;

        public float? Percent => Bps.HasValue ? Bps.Value / 100f : (float?)null;
    }

    public static class DailyBarModel
    {
        public const string BaseSource = "BASE";
        public const string RevealedSource = "REVEALED";

        /// <summary>0~30일 막대 31개. revealed가 null이면 열람값만, 있으면 31일 전체(공개 후 전용).</summary>
        public static DayBar[] Build(VisibleCompany company, RevealedCompany revealed = null)
        {
            var bars = new DayBar[GraphSeriesBuilder.DayCount];
            for (int d = 0; d < bars.Length; d++)
                bars[d] = new DayBar { Day = d, Bps = null, State = DayBarState.Hidden, Source = "" };

            Set(bars, 0, 0, BaseSource);

            if (company?.visibleBundles != null)
            {
                foreach (var b in company.visibleBundles)
                {
                    if (b?.dailyChangesBps == null) continue;
                    for (int i = 0; i < b.dailyChangesBps.Length; i++)
                    {
                        int day = b.startDay + i; // 묶음 배열 인덱스 0의 날짜는 startDay
                        if (day >= 0 && day < bars.Length) Set(bars, day, b.dailyChangesBps[i], b.source ?? "");
                    }
                }
            }

            foreach (var w in company?.visibleWindows ?? System.Array.Empty<VisibleInformationWindow>())
            {
                if (w?.dailyChangesBps == null) continue;
                for (int i = 0; i < w.dailyChangesBps.Length; i++)
                {
                    int day = w.startDay + i;
                    if (day >= 1 && day < bars.Length)
                    {
                        string sources = bars[day].Source;
                        if (!sources.Contains(w.source ?? "")) sources = sources.Length == 0 ? w.source : sources + " / " + w.source;
                        Set(bars, day, w.dailyChangesBps[i], sources);
                    }
                }
            }
            if (revealed?.dailyChangesBps != null)
            {
                for (int d = 0; d < bars.Length && d < revealed.dailyChangesBps.Length; d++)
                    if (bars[d].State == DayBarState.Hidden) Set(bars, d, revealed.dailyChangesBps[d], RevealedSource);
            }
            FillCandles(bars);
            var opens = InformationWindowRules.KnownOpens(company);
            for (int d = 1; d < bars.Length; d++)
                if (bars[d].Bps.HasValue && opens[d].HasValue)
                {
                    bars[d].OpenBps = opens[d];
                    bars[d].Move = bars[d].Bps > opens[d] ? CandleMove.Rise : bars[d].Bps < opens[d] ? CandleMove.Fall : CandleMove.Flat;
                }
            return bars;
        }

        /// <summary>툴팁 문구. 미열람은 값 대신 '미열람'으로만 표시한다.</summary>
        public static string Tooltip(DayBar bar)
        {
            string head = bar.Day + "일";
            if (!bar.Bps.HasValue) return head + " · 미열람";
            string value = DisplayFormat.Percent(bar.Bps.Value);
            string candle = bar.Day == 0 ? ""
                : bar.OpenBps.HasValue
                    ? " · 시가 " + DisplayFormat.Percent(bar.OpenBps.Value) + " → 종가 " + value
                      + " (" + MoveWord(bar.Move) + " " + SignedPoints(bar.Bps.Value - bar.OpenBps.Value) + ")"
                    : " · 전일 미열람";
            string src = bar.Source == BundleSources.Purchased ? " · 구매"
                : bar.Source == BundleSources.Public ? " · 공개"
                : bar.Source == RevealedSource ? " · 시장 공개"
                : bar.Source == BaseSource ? " · 기준일"
                : "";
            return head + " · " + value + " (0일차 대비)" + candle + src;
        }

        private static string MoveWord(CandleMove m) =>
            m == CandleMove.Rise ? "양봉" : m == CandleMove.Fall ? "음봉" : "보합";

        private static string SignedPoints(int bps)
        {
            string pct = DisplayFormat.Percent(bps); // "+20%" / "-20%" / "0%"
            return pct.EndsWith("%") ? pct.Substring(0, pct.Length - 1) + "%p" : pct;
        }

        /// <summary>시가 = 전날 종가. 전날을 모르면 Unknown. 0일차는 기준일이므로 보합.</summary>
        private static void FillCandles(DayBar[] bars)
        {
            for (int d = 0; d < bars.Length; d++)
            {
                bars[d].OpenBps = null;
                bars[d].Move = CandleMove.Unknown;
                if (!bars[d].Bps.HasValue) continue;
                if (d == 0) { bars[d].OpenBps = bars[d].Bps; bars[d].Move = CandleMove.Flat; continue; }
                var open = bars[d - 1].Bps;
                if (!open.HasValue) continue;
                bars[d].OpenBps = open;
                int close = bars[d].Bps.Value;
                bars[d].Move = close > open.Value ? CandleMove.Rise : close < open.Value ? CandleMove.Fall : CandleMove.Flat;
            }
        }

        /// <summary>구간 수익률(진입→청산). 일별 막대 높이와 다른 값이다: 10일 -40%, 15일 +30% → +70%p.</summary>
        public static int? IntervalReturnBps(DayBar[] bars, int entryDay, int exitDay, bool entryAtOpen = false)
        {
            if (bars == null || entryDay < 0 || exitDay >= bars.Length || entryDay >= bars.Length || exitDay < 0) return null;
            var a = entryAtOpen ? bars[entryDay].OpenBps : bars[entryDay].Bps;
            var b = bars[exitDay].Bps;
            if (!a.HasValue || !b.HasValue) return null;
            return b.Value - a.Value;
        }

        public static List<int> HiddenDays(DayBar[] bars)
        {
            var list = new List<int>();
            foreach (var b in bars) if (b.State == DayBarState.Hidden) list.Add(b.Day);
            return list;
        }

        private static void Set(DayBar[] bars, int day, int bps, string source)
        {
            bars[day].Bps = bps;
            bars[day].Source = source;
            bars[day].State = bps > 0 ? DayBarState.Up : bps < 0 ? DayBarState.Down : DayBarState.Zero;
        }
    }

    /// <summary>날짜(0~30) ↔ 그래프 사각형 안의 로컬 x 좌표. 막대 그리기와 클릭 판정이 같은 규칙을 쓴다.
    /// 날짜 d는 폭을 31등분한 d번째 칸 전체이므로 0일·30일 막대와 클릭 영역이 양 끝에서 잘리지 않는다.</summary>
    public static class DayAxis
    {
        public const int Count = GraphSeriesBuilder.DayCount;

        public static float SlotWidth(float width) => width / Count;

        public static float CenterX(int day, float xMin, float width) => xMin + (day + 0.5f) * SlotWidth(width);

        public static float LeftX(int day, float xMin, float width) => xMin + day * SlotWidth(width);

        /// <summary>로컬 x → 날짜. 사각형 밖이면 null(가장자리로 끌어붙이지 않음).</summary>
        public static int? DayAt(float localX, float xMin, float width)
        {
            if (width <= 0) return null;
            float t = (localX - xMin) / width;
            if (t < 0f || t > 1f) return null;
            int day = (int)(t * Count);
            if (day >= Count) day = Count - 1; // 오른쪽 끝(t=1) 포함
            return day;
        }
    }
}
