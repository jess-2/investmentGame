// 기업 선택 차례 표시(화면 객체 의존 없음).
// - 서버(A)의 선택 순서는 '자리 순서 + 역순'(뱀 순서)이다. 예: 4명이면 1→2→3→4→4→3→2→1 (8번).
// - 현재 공통 계약(PublicState)에는 전체 순서가 없고 currentPlayerId·selectionIndex만 온다.
//   그래서 차례가 넘어갈 때마다 '몇 번째 = 누구'를 기록하고, 뱀 순서 규칙으로 짝 칸(마지막에서 같은 거리)도 채운다.
//   앞 절반에서 한 명만 남으면 그 사람이 남은 칸이다. 모르는 칸은 '?'로 둔다(추측해서 채우지 않음).
// - A가 PublicState에 전체 순서를 넣어 주면 SetKnownOrder로 그대로 쓰면 된다.
// - 표시하는 것은 차례(누가 몇 번째)뿐이다. 누가 어느 기업을 골랐는지는 여기서 다루지 않는다.
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.UI.Presentation
{
    public enum PickSlotState
    {
        Done,      // 이미 지나간 차례
        Current,   // 지금 고르는 중
        Upcoming   // 아직 안 온 차례
    }

    public struct PickSlot
    {
        public int Index;          // 0부터. 화면에는 Index+1번째로 표시
        public string PlayerId;    // 모르면 null
        public string Name;        // 표시 이름(모르면 "?")
        public bool IsMine;
        public PickSlotState State;
    }

    public sealed class SelectionOrderView
    {
        public PickSlot[] Slots = new PickSlot[0];
        public int CurrentIndex = -1;           // 선택 단계가 아니면 -1
        public string CurrentPlayerId = "";
        public string CurrentName = "";
        public bool IsMyTurn;
        public List<int> MyIndices = new List<int>();   // 내가 고르는 칸(알려진 것만)
        public int? MyNextIndex;                         // 앞으로 남은 내 차례 중 가장 빠른 칸
        public bool MyPositionUnknown;                   // 아직 내 자리를 알 수 없음

        /// <summary>내 다음 차례 전까지 남은 다른 사람 차례 수(지금 차례 포함). 모르면 null.</summary>
        public int? TurnsBeforeMine
            => MyNextIndex.HasValue && CurrentIndex >= 0 && !IsMyTurn ? MyNextIndex.Value - CurrentIndex : (int?)null;

        /// <summary>선택 화면 상단 안내 문구(리치 텍스트).</summary>
        public string Summary()
        {
            if (Slots.Length == 0) return "";
            if (IsMyTurn)
            {
                int left = MyIndices.Count(i => i > CurrentIndex);
                return "<color=#DC2626><b>지금 내 차례입니다</b></color> (" + (CurrentIndex + 1) + "/" + Slots.Length + "번째)"
                       + (left > 0 ? " · 이번 뒤에 내 차례 " + left + "번 더" : " · 이번이 마지막 내 차례");
            }

            string now = CurrentIndex >= 0
                ? "<b>" + CurrentName + "</b> 님이 고르는 중 (" + (CurrentIndex + 1) + "/" + Slots.Length + "번째)"
                : "선택 대기";
            string mine;
            if (MyNextIndex.HasValue)
            {
                int wait = TurnsBeforeMine ?? 0;
                mine = "내 다음 차례: <color=#2563EB><b>" + (MyNextIndex.Value + 1) + "번째</b></color>"
                       + (wait > 1 ? " (내 앞에 " + wait + "번 남음)" : wait == 1 ? " (<b>바로 다음</b>)" : "");
            }
            else if (MyPositionUnknown)
                mine = "내 차례: 앞사람들이 고르면 표시됩니다";
            else
                mine = "내 선택 차례는 모두 끝났습니다";
            return now + "   ·   " + mine;
        }
    }

    /// <summary>턴마다 관찰한 '몇 번째 = 누구'를 모아 전체 선택 순서를 만든다.</summary>
    public sealed class SelectionOrderTracker
    {
        private int _turn = -1;
        private string[] _order = new string[0];

        public int Turn => _turn;
        public IReadOnlyList<string> Order => _order;

        /// <summary>PUBLIC_STATE를 받을 때마다 호출. 선택 단계의 현재 차례를 기록한다.</summary>
        public void Observe(PublicState s)
        {
            if (s == null) return;
            int players = s.players?.Length ?? 0;
            if (s.turn != _turn)
            {
                _turn = s.turn;
                _order = new string[players * 2];
            }
            if (_order.Length != players * 2)
            {
                var resized = new string[players * 2];
                for (int i = 0; i < resized.Length && i < _order.Length; i++) resized[i] = _order[i];
                _order = resized;
            }
            if (s.phase != Protocol.Selection || string.IsNullOrEmpty(s.currentPlayerId)) return;
            Record(s.selectionIndex, s.currentPlayerId, (s.players ?? new Player[0]).Select(p => p.playerId));
        }

        /// <summary>서버가 전체 순서를 알려 줄 때(향후 계약) 그대로 사용.</summary>
        public void SetKnownOrder(int turn, string[] order)
        {
            if (order == null) return;
            _turn = turn;
            _order = (string[])order.Clone();
        }

        private void Record(int index, string playerId, IEnumerable<string> allPlayers)
        {
            int n = _order.Length;
            if (index < 0 || index >= n) return;
            _order[index] = playerId;
            _order[n - 1 - index] = playerId; // 뱀 순서: 처음에서 k번째 = 끝에서 k번째

            // 앞 절반에서 한 칸만 비었고 아직 안 나온 사람이 한 명이면 그 사람이다.
            int half = n / 2;
            var seen = new HashSet<string>(_order.Take(half).Where(x => x != null));
            var missingSlots = Enumerable.Range(0, half).Where(i => _order[i] == null).ToList();
            var missingPlayers = allPlayers.Where(p => !string.IsNullOrEmpty(p) && !seen.Contains(p)).Distinct().ToList();
            if (missingSlots.Count == 1 && missingPlayers.Count == 1)
            {
                _order[missingSlots[0]] = missingPlayers[0];
                _order[n - 1 - missingSlots[0]] = missingPlayers[0];
            }
        }

        public SelectionOrderView Build(PublicState s, string localPlayerId)
        {
            var view = new SelectionOrderView();
            if (s == null || s.turn != _turn || _order.Length == 0) return view;

            bool selecting = s.phase == Protocol.Selection;
            view.CurrentIndex = selecting ? s.selectionIndex : -1;
            view.CurrentPlayerId = selecting ? s.currentPlayerId ?? "" : "";
            view.CurrentName = NameOf(s, view.CurrentPlayerId);
            view.IsMyTurn = selecting && !string.IsNullOrEmpty(localPlayerId) && view.CurrentPlayerId == localPlayerId;

            view.Slots = new PickSlot[_order.Length];
            for (int i = 0; i < _order.Length; i++)
            {
                string id = _order[i];
                var state = !selecting ? (s.phase == Protocol.Info || s.phase == Protocol.Lobby ? PickSlotState.Upcoming : PickSlotState.Done)
                    : i < s.selectionIndex ? PickSlotState.Done
                    : i == s.selectionIndex ? PickSlotState.Current
                    : PickSlotState.Upcoming;
                bool mine = id != null && id == localPlayerId;
                view.Slots[i] = new PickSlot { Index = i, PlayerId = id, Name = id == null ? "?" : NameOf(s, id), IsMine = mine, State = state };
                if (mine) view.MyIndices.Add(i);
            }

            int from = selecting ? s.selectionIndex : 0;
            bool selectionOver = !selecting && s.phase != Protocol.Info && s.phase != Protocol.Lobby;
            if (!selectionOver)
            {
                var next = view.MyIndices.Where(i => i >= from).OrderBy(i => i).ToList();
                if (next.Count > 0) view.MyNextIndex = next[0];
                else view.MyPositionUnknown = !string.IsNullOrEmpty(localPlayerId)
                                              && view.Slots.Skip(from).Any(x => x.PlayerId == null);
            }
            return view;
        }

        private static string NameOf(PublicState s, string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return "";
            var p = s.players?.FirstOrDefault(x => x.playerId == playerId);
            return string.IsNullOrEmpty(p?.displayName) ? playerId : p.displayName;
        }
    }
}
