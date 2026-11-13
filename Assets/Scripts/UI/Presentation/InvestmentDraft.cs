// 주문창 입력 초안(클라이언트 전용). 확정 전에는 서버에 보내지 않는다.
// 배열 순서 = 첫 기업/둘째 기업. 비중은 첫 기업 기준(5000/6000/7000), 둘째는 10000 - 첫 기업.
// 화면 정렬 때문에 순서가 바뀌지 않도록, 순서 변경은 SwapCompanies()로만 한다.
using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.State;

namespace BlackWhale.UI.Presentation
{
    public sealed class InvestmentDraft
    {
        public string FirstCompanyId { get; private set; }
        public string SecondCompanyId { get; private set; }
        public int FirstWeightBps { get; private set; } = 5000;
        public int SecondWeightBps => UiRules.TotalWeightBps - FirstWeightBps;
        public bool EntryAtOpen { get; set; }

        private List<Interval> _firstIntervals = new List<Interval>();
        private List<Interval> _secondIntervals = new List<Interval>();

        public IReadOnlyList<Interval> FirstIntervals => _firstIntervals;
        public IReadOnlyList<Interval> SecondIntervals => _secondIntervals;

        public InvestmentDraft(string firstCompanyId, string secondCompanyId)
        {
            FirstCompanyId = firstCompanyId;
            SecondCompanyId = secondCompanyId;
        }

        /// <summary>선택 순서대로 초안을 만든다. 선택이 2개가 아니면 null.</summary>
        public static InvestmentDraft FromSelections(IReadOnlyList<string> ownSelections, TradePlanBook plans = null)
        {
            if (ownSelections == null || ownSelections.Count != UiRules.CompaniesPerOrder) return null;
            var draft = new InvestmentDraft(ownSelections[0], ownSelections[1]);
            draft.EntryAtOpen = plans?.EntryAtOpen ?? false;
            if (plans?.WeightedCompanies is Investment[] weighted && weighted.Length == 2 &&
                weighted.All(i => ownSelections.Contains(i.companyId)))
            {
                draft = new InvestmentDraft(weighted[0].companyId, weighted[1].companyId) { EntryAtOpen = plans.EntryAtOpen };
                draft.SetFirstWeight(weighted[0].weightBps);
            }
            if (plans != null)
                foreach (string company in ownSelections)
                    foreach (var interval in plans.Get(company).Intervals)
                        draft.TryAddInterval(company, interval.entryDay, interval.exitDay);
            return draft;
        }

        public bool SetFirstWeight(int weightBps)
        {
            if (!UiRules.FirstCompanyWeightOptionsBps.Contains(weightBps)) return false;
            FirstWeightBps = weightBps;
            return true;
        }

        public void SwapCompanies()
        {
            (FirstCompanyId, SecondCompanyId) = (SecondCompanyId, FirstCompanyId);
            (_firstIntervals, _secondIntervals) = (_secondIntervals, _firstIntervals);
        }

        public List<Interval> IntervalsOf(string companyId)
        {
            if (companyId == FirstCompanyId) return _firstIntervals;
            if (companyId == SecondCompanyId) return _secondIntervals;
            throw new ArgumentException("선택하지 않은 기업: " + companyId);
        }

        /// <summary>구간 추가. 사전검사에서 걸리면 추가하지 않고 오류 코드를 돌려준다.</summary>
        public string TryAddInterval(string companyId, int entryDay, int exitDay)
        {
            var list = IntervalsOf(companyId);
            var candidate = list.Select(DtoCopy.Copy).ToList();
            candidate.Add(new Interval { entryDay = entryDay, exitDay = exitDay });
            var error = OrderInputValidator.ValidateIntervals(candidate, EntryAtOpen);
            if (error.Length == 0) list.Add(new Interval { entryDay = entryDay, exitDay = exitDay });
            return error;
        }

        public void RemoveInterval(string companyId, int index)
        {
            var list = IntervalsOf(companyId);
            if (index >= 0 && index < list.Count) list.RemoveAt(index);
        }
        public string TryReplaceInterval(string companyId, int index, int entryDay, int exitDay)
        {
            var list = IntervalsOf(companyId);
            if (index < 0 || index >= list.Count) return ErrorCodes.MissingInterval;
            var candidate = list.Select(DtoCopy.Copy).ToArray(); candidate[index] = new Interval { entryDay = entryDay, exitDay = exitDay };
            string error = OrderInputValidator.ValidateIntervals(candidate, EntryAtOpen);
            if (error.Length == 0) list[index] = candidate[index];
            return error;
        }

        public Investment[] ToInvestments()
        {
            return new[]
            {
                new Investment
                {
                    companyId = FirstCompanyId,
                    weightBps = FirstWeightBps,
                    intervals = _firstIntervals.Select(DtoCopy.Copy).ToArray()
                },
                new Investment
                {
                    companyId = SecondCompanyId,
                    weightBps = SecondWeightBps,
                    intervals = _secondIntervals.Select(DtoCopy.Copy).ToArray()
                }
            };
        }

        public string Validate()
            => OrderInputValidator.Validate(ToInvestments(), new[] { FirstCompanyId, SecondCompanyId }, EntryAtOpen);
    }

    public sealed class CompanyTradePlan
    {
        public bool Favorite;
        public int Rank;
        public Interval[] Intervals = Array.Empty<Interval>();
    }

    // Per-turn personal notes. Reservations sync privately; actual orders lock at submit/deadline.
    public sealed class TradePlanBook
    {
        public bool EntryAtOpen { get; set; }
        private readonly Dictionary<string, CompanyTradePlan> companies = new Dictionary<string, CompanyTradePlan>();
        public int Turn { get; private set; }
        public event Action Changed;
        public event Action ReservationsChanged;
        public bool HasLocalReservations { get; private set; }
        public Investment[] WeightedCompanies { get; private set; } = Array.Empty<Investment>();
        public void BeginTurn(int turn)
        {
            if (turn <= Turn) return;
            Turn = turn; companies.Clear(); HasLocalReservations = false; WeightedCompanies = Array.Empty<Investment>();
        }
        public Investment[] ToReservations()
        {
            var ids = WeightedCompanies.Select(i => i.companyId).Concat(UiRules.CompanyIds).Distinct();
            return ids.Select(id => new Investment { companyId = id,
                weightBps = WeightedCompanies.FirstOrDefault(i => i.companyId == id)?.weightBps ?? 0,
                intervals = Get(id).Intervals }).ToArray();
        }
        public void RestoreReservations(Investment[] saved)
        {
            if (HasLocalReservations || saved == null || saved.Length == 0) return;
            foreach (var i in saved) Edit(i.companyId).Intervals = (i.intervals ?? Array.Empty<Interval>()).Select(DtoCopy.Copy).ToArray();
            WeightedCompanies = saved.Where(i => i.weightBps != 0).Select(DtoCopy.Copy).ToArray();
            HasLocalReservations = true;
        }
        // The investment page already refreshes itself; do not rebuild the UI inside its input callbacks.
        public void SetDraft(Investment[] draft)
        {
            bool same = WeightedCompanies.Length == draft.Length && draft.Select((i, n) =>
                WeightedCompanies[n].companyId == i.companyId && WeightedCompanies[n].weightBps == i.weightBps &&
                Get(i.companyId).Intervals.Select(v => v.entryDay + ":" + v.exitDay)
                    .SequenceEqual(i.intervals.Select(v => v.entryDay + ":" + v.exitDay))).All(v => v);
            if (same) return;
            foreach (var i in draft) Edit(i.companyId).Intervals = i.intervals.Select(DtoCopy.Copy).ToArray();
            WeightedCompanies = draft.Select(DtoCopy.Copy).ToArray();
            HasLocalReservations = true; ReservationsChanged?.Invoke();
        }
        private void ReservationEdited()
        { HasLocalReservations = true; ReservationsChanged?.Invoke(); Changed?.Invoke(); }
        private CompanyTradePlan Edit(string id)
        {
            if (!companies.TryGetValue(id, out var plan)) companies.Add(id, plan = new CompanyTradePlan());
            return plan;
        }
        public CompanyTradePlan Get(string id)
        {
            if (id == null || !companies.TryGetValue(id, out var plan)) return new CompanyTradePlan();
            return new CompanyTradePlan { Favorite = plan.Favorite, Rank = plan.Rank, Intervals = plan.Intervals.Select(DtoCopy.Copy).ToArray() };
        }
        public bool SetFavorite(string id, bool favorite)
        {
            if (Turn < 1 || !UiRules.IsKnownCompany(id)) return false;
            var plan = Edit(id); if (plan.Favorite == favorite) return false;
            plan.Favorite = favorite; Changed?.Invoke(); return true;
        }
        public bool SetRank(string id, int rank)
        {
            if (Turn < 1 || !UiRules.IsKnownCompany(id) || rank < 0 || rank > UiRules.CompanyIds.Length) return false;
            var plan = Edit(id); if (plan.Rank == rank) return false;
            if (rank > 0)
            {
                var other = companies.FirstOrDefault(p => p.Key != id && p.Value.Rank == rank).Value;
                if (other != null) other.Rank = plan.Rank;
                plan.Favorite = true;
            }
            plan.Rank = rank; Changed?.Invoke(); return true;
        }
        public string TryAddInterval(string id, int entry, int exit)
        {
            if (Turn < 1) return ErrorCodes.WrongPhase;
            if (!UiRules.IsKnownCompany(id)) return ErrorCodes.InvalidCompany;
            var plan = Get(id);
            var intervals = plan.Intervals.Concat(new[] { new Interval { entryDay = entry, exitDay = exit } }).ToArray();
            string error = OrderInputValidator.ValidateIntervals(intervals, EntryAtOpen);
            if (error.Length > 0) return error;
            Edit(id).Intervals = intervals; ReservationEdited(); return "";
        }
        public bool RemoveInterval(string id, int index)
        {
            var plan = Get(id);
            if (index < 0 || index >= plan.Intervals.Length) return false;
            Edit(id).Intervals = plan.Intervals.Where((_, i) => i != index).ToArray(); ReservationEdited(); return true;
        }
        public string RangeError(string id, int index, int entry, int exit)
        {
            var list = Get(id).Intervals.ToList();
            var range = new Interval { entryDay = entry, exitDay = exit };
            if (index >= 0 && index < list.Count) list[index] = range; else list.Add(range);
            return OrderInputValidator.ValidateIntervals(list, EntryAtOpen);
        }
        public string TryReplaceInterval(string id, int index, int entry, int exit)
        {
            var plan = Get(id);
            if (Turn < 1 || index < 0 || index >= plan.Intervals.Length) return ErrorCodes.MissingInterval;
            plan.Intervals[index] = new Interval { entryDay = entry, exitDay = exit };
            string error = OrderInputValidator.ValidateIntervals(plan.Intervals, EntryAtOpen);
            if (error.Length == 0) { Edit(id).Intervals = plan.Intervals; ReservationEdited(); }
            return error;
        }
        public string Summary(string id)
        {
            var plan = Get(id); var parts = new List<string>();
            if (plan.Favorite) parts.Add("★ 찜");
            if (plan.Rank > 0) parts.Add(plan.Rank + "순위");
            if (plan.Intervals.Length > 0) parts.Add(string.Join(", ", plan.Intervals.Select(i => i.entryDay + "→" + i.exitDay + "일")));
            return parts.Count == 0 ? "개인 계획 없음" : string.Join(" · ", parts);
        }
        public Interval[] ChartIntervals(PlayerView view, int currentTurn, string companyId, bool orderLocked)
        {
            if (orderLocked)
                return view?.turn == currentTurn
                    ? (view.ownInvestments?.FirstOrDefault(i => i.companyId == companyId)?.intervals ?? Array.Empty<Interval>()).Select(DtoCopy.Copy).ToArray()
                    : Array.Empty<Interval>();
            return Turn == currentTurn ? Get(companyId).Intervals : Array.Empty<Interval>();
        }
    }
}
