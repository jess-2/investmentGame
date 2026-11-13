// 일별 그래프 데이터 생성.
// - 0일차는 항상 0% (전원 공개 기준값)
// - 묶음 k의 날짜 = startDay + i (5개 배열의 인덱스 0은 0일차가 아니다)
// - 보이지 않는 날짜는 '값 없음'(null). 0%로 채우지 않는다.
// - 연속으로 값이 있는 날짜끼리만 선으로 잇는다. 숨은 구간을 건너뛰어 잇지 않는다.
// - 차트 축 범위는 현재 열람한 값만으로 자동 맞춘다. 종목 상한은 표시 범위에 쓰지 않는다.
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.UI.Presentation
{
    public struct GraphPoint
    {
        public int Day;
        public int Bps;
        public GraphPoint(int day, int bps) { Day = day; Bps = bps; }
        public float Percent => Bps / 100f;
        public override string ToString() => Day + ":" + Bps;
    }

    public struct LockedRange
    {
        public int BundleIndex;
        public int StartDay;
        public int EndDay;
    }

    public static class GraphSeriesBuilder
    {
        public const int DayCount = UiRules.MaxDay + 1; // 0~30

        public static int BundleStartDay(int bundleIndex) => bundleIndex * UiRules.BundleSizeDays + 1;
        public static int BundleEndDay(int bundleIndex) => BundleStartDay(bundleIndex) + UiRules.BundleSizeDays - 1;

        /// <summary>날짜(0~30)별 알려진 값. 모르면 null.</summary>
        public static int?[] KnownValues(VisibleCompany company)
        {
            return InformationWindowRules.KnownDays(company);
        }

        /// <summary>선으로 이을 연속 구간 목록. 점이 1개뿐인 구간은 점으로만 그린다.</summary>
        public static List<List<GraphPoint>> Segments(VisibleCompany company)
        {
            var values = KnownValues(company);
            var segments = new List<List<GraphPoint>>();
            List<GraphPoint> current = null;

            for (int day = 0; day < DayCount; day++)
            {
                if (values[day].HasValue)
                {
                    if (current == null)
                    {
                        current = new List<GraphPoint>();
                        segments.Add(current);
                    }
                    current.Add(new GraphPoint(day, values[day].Value));
                }
                else
                {
                    current = null;
                }
            }
            return segments;
        }

        /// <summary>시장 공개(MARKET_REVEAL) 후 전체 31일 값을 하나의 구간으로. 공개 전에는 호출하지 않는다.</summary>
        public static List<GraphPoint> RevealedSeries(RevealedCompany revealed)
        {
            var list = new List<GraphPoint>();
            if (revealed?.dailyChangesBps == null) return list;
            for (int day = 0; day < revealed.dailyChangesBps.Length && day < DayCount; day++)
                list.Add(new GraphPoint(day, revealed.dailyChangesBps[day]));
            return list;
        }

        /// <summary>잠금 표시할 묶음(아직 열람하지 않은 5일 구간).</summary>
        public static List<LockedRange> LockedRanges(VisibleCompany company)
        {
            if (company?.visibleWindows == null)
            {
                var visible = new HashSet<int>(company?.visibleBundles?.Select(b => b.bundleIndex) ?? Enumerable.Empty<int>());
                return Enumerable.Range(0, UiRules.BundleCount).Where(k => !visible.Contains(k)).Select(k =>
                    new LockedRange { BundleIndex = k, StartDay = BundleStartDay(k), EndDay = BundleEndDay(k) }).ToList();
            }
            var values = KnownValues(company);
            var result = new List<LockedRange>();
            for (int d = 1; d < DayCount; d++)
            {
                if (values[d].HasValue) continue;
                int start = d;
                while (d + 1 < DayCount && !values[d + 1].HasValue) d++;
                result.Add(new LockedRange { BundleIndex = -1, StartDay = start, EndDay = d });
            }
            return result;
        }

        /// <summary>v2 비교군의 공개 규칙 한도. 현재 차트 표시는 ChartVerticalRange의 열람값 자동 범위를 쓴다.</summary>
        public static int AxisLimitBps(string companyId) => UiRules.AbsoluteChangeCapBps(companyId);

        public static bool TryGetSource(VisibleCompany company, int bundleIndex, out string source)
        {
            var b = company?.visibleBundles?.FirstOrDefault(x => x.bundleIndex == bundleIndex);
            source = b?.source;
            return b != null;
        }
    }
}
