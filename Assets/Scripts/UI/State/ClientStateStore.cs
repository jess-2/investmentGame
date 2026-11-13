// 클라이언트 화면 캐시. A의 ServerMessage를 받아 보관만 한다(A·B·C가 확정한 값).
// - 코인·자산을 스스로 계산하지 않는다. 응답의 remainingCoins / PLAYER_VIEW 값을 그대로 쓴다.
// - 같은 응답이 다시 와도 한 번만 반영한다(D-response-replay).
// - 현재 턴보다 이전 턴의 응답·상태는 무시한다(D-stale-response).
// - 실패 응답은 오류만 기록하고 잔액·정보는 그대로 둔다(D-error-state).
// - 메시지 구분은 반드시 type으로 한다. JsonUtility로 받은 메시지는 쓰지 않는 필드도 null이 아닌
//   기본 객체로 채워지므로 'state != null' 같은 검사로 종류를 판단하면 안 된다.
using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.UI.State
{
    public enum ApplyOutcome
    {
        Applied,
        Rejected,   // 서버가 실패로 응답함(오류 표시)
        Duplicate,  // 이미 반영한 응답
        Stale,      // 이전 턴 또는 더 오래된 상태
        NoState,    // 기준 상태가 아직 없음
        Ignored     // D가 처리하지 않는 메시지
    }

    public sealed class ClientError
    {
        public readonly string ResultType;
        public readonly string RequestId;
        public readonly string ErrorCode;

        public ClientError(string resultType, string requestId, string errorCode)
        {
            ResultType = resultType;
            RequestId = requestId;
            ErrorCode = errorCode;
        }
    }

    public sealed class ClientStateStore
    {
        public BlackWhale.UI.Presentation.TradePlanBook TradePlans { get; } = new BlackWhale.UI.Presentation.TradePlanBook();
        public ClientStateStore() { TradePlans.Changed += Raise; }
        private sealed class PendingRequest
        {
            public string Type;
            public int Turn;
            public string Key; // 구매: "AI:1", 선택: "AI", 그 외: ""
        }

        private readonly Dictionary<string, PendingRequest> _pending = new Dictionary<string, PendingRequest>();
        private readonly HashSet<string> _applied = new HashSet<string>();
        private readonly List<string> _ownSelections = new List<string>();
        private readonly HashSet<string> _completedPhases = new HashSet<string>();
        private readonly Dictionary<string, string> _completeRequestPhase = new Dictionary<string, string>();

        public string LocalPlayerId { get; private set; } = "";
        public PublicState Public { get; private set; }
        /// <summary>기업 선택 차례 순서(관찰 기반). 화면 표시 전용.</summary>
        public BlackWhale.UI.Presentation.SelectionOrderTracker SelectionOrder { get; } = new BlackWhale.UI.Presentation.SelectionOrderTracker();
        public PlayerView View { get; private set; }
        public Scoreboard Scoreboard { get; private set; }
        // Read-only browsing data never replaces the current turn's trading state.
        public HistorySnapshot History { get; private set; }
        /// <summary>주문 잠금 후 A가 공개한 전체 시장(MARKET_REVEAL). 그 전에는 null.</summary>
        public RevealedCompany[] Revealed { get; private set; }
        public int RevealedTurn { get; private set; }
        public ClientError LastError { get; private set; }
        /// <summary>ROOM_ENDED / JOIN_REJECTED / DISCONNECTED 사유. 정상 진행 중이면 빈 문자열.</summary>
        public string EndReason { get; private set; } = "";
        public bool OrderLockedByResult { get; private set; }

        public event Action Changed;
        public event Action<ClientError> ErrorReceived;
        /// <summary>시간 초과로 서버가 대신 처리한 행동(자동 선택·기본 주문). 인자 = 결과 type.</summary>
        public event Action<string> AutomaticActionReceived;

        public int CurrentTurn => Math.Max(Public?.turn ?? 0, View?.turn ?? 0);

        /// <summary>이번 턴 내 선택 기업(선택 순서 유지). 개인뷰의 selectedCompanies가 기준.</summary>
        public IReadOnlyList<string> OwnSelections
        {
            get
            {
                var fromView = View?.selectedCompanies;
                if (fromView != null && fromView.Length > 0 && View.turn == CurrentTurn) return fromView;
                return _ownSelections;
            }
        }

        public bool OrderLocked
            => OrderLockedByResult || (View?.ownInvestments != null && View.ownInvestments.Length > 0 && View.turn == CurrentTurn);

        // ------------------------------------------------------------------
        // 대기 상태
        // ------------------------------------------------------------------

        public void MarkPending(string requestId, string type, int turn, string key = "", bool notify = true)
        {
            _pending[requestId] = new PendingRequest { Type = type, Turn = turn, Key = key ?? "" };
            if (notify) Raise();
        }

        public bool IsPending(string requestId) => _pending.ContainsKey(requestId);

        public bool HasPending(string type, string key = null)
            => _pending.Values.Any(p => p.Type == type && (key == null || p.Key == key));

        public static string PurchaseKey(string companyId, int bundleIndex) => companyId + ":" + bundleIndex;

        public bool IsPurchasePending(string companyId, int bundleIndex)
            => HasPending(CommandTypes.BuyInformation, PurchaseKey(companyId, bundleIndex));

        /// <summary>이번 단계에 COMPLETE_PHASE를 보냈거나 성공했는지.</summary>
        public bool IsPhaseCompleted(int turn, string phase) => _completedPhases.Contains(turn + "|" + phase);

        public void ClearError()
        {
            LastError = null;
            Raise();
        }

        // ------------------------------------------------------------------
        // 메시지 입구
        // ------------------------------------------------------------------

        public ApplyOutcome Apply(ServerMessage message)
        {
            if (message == null) return ApplyOutcome.Ignored;
            switch (message.type)
            {
                case "HISTORY_VIEW":
                    var history = message.history;
                    if (history?.playerView == null || history.turn > CurrentTurn || history.playerView.recipientPlayerId != LocalPlayerId)
                        return ApplyOutcome.Ignored;
                    History = DtoCopy.Copy(history); Raise(); return ApplyOutcome.Applied;
                case ServerMessageTypes.Welcome:
                    LocalPlayerId = message.session?.playerId ?? "";
                    Raise();
                    return ApplyOutcome.Applied;
                case ServerMessageTypes.PublicState: return SetPublicState(message.state);
                case ServerMessageTypes.PlayerView: return SetPlayerView(message.playerView);
                case ServerMessageTypes.CommandResult: return ApplyResult(message.response);
                case ServerMessageTypes.MarketReveal: return SetRevealed(message.revealedMarket);
                case ServerMessageTypes.Scoreboard: return SetScoreboard(message.scoreboard);
                case ServerMessageTypes.RoomEnded:
                case ServerMessageTypes.JoinRejected:
                case ServerMessageTypes.Disconnected:
                    EndReason = string.IsNullOrEmpty(message.errorCode) ? message.type : message.errorCode;
                    _pending.Clear();
                    Raise();
                    return ApplyOutcome.Applied;
                default:
                    return ApplyOutcome.Ignored;
            }
        }

        // ------------------------------------------------------------------
        // 전체 스냅샷
        // ------------------------------------------------------------------

        public ApplyOutcome SetPublicState(PublicState state)
        {
            if (state == null) return ApplyOutcome.NoState;
            if (state.turn < CurrentTurn) return ApplyOutcome.Stale;
            if (Public != null && state.turn == Public.turn && state.turn > 0
                && state.serverElapsedSeconds < Public.serverElapsedSeconds)
                return ApplyOutcome.Stale;

            BeginTurnIfNewer(state.turn);
            Public = DtoCopy.Copy(state);
            SelectionOrder.Observe(Public); // 선택 차례 기록(몇 번째 = 누구)
            Raise();
            return ApplyOutcome.Applied;
        }

        public ApplyOutcome SetPlayerView(PlayerView view)
        {
            if (view == null) return ApplyOutcome.NoState;
            if (view.turn < CurrentTurn) return ApplyOutcome.Stale;
            if (!string.IsNullOrEmpty(LocalPlayerId) && !string.IsNullOrEmpty(view.recipientPlayerId)
                && view.recipientPlayerId != LocalPlayerId)
                return ApplyOutcome.Ignored; // 다른 사람의 개인뷰는 받지도 쓰지도 않는다

            BeginTurnIfNewer(view.turn);
            View = DtoCopy.Copy(view);
            TradePlans.EntryAtOpen = view.entryAtOpen;
            TradePlans.RestoreReservations(view.reservedInvestments);
            if (string.IsNullOrEmpty(LocalPlayerId)) LocalPlayerId = view.recipientPlayerId ?? "";
            Raise();
            return ApplyOutcome.Applied;
        }

        public ApplyOutcome SetScoreboard(Scoreboard board)
        {
            if (board == null) return ApplyOutcome.NoState;
            if (board.turn < CurrentTurn) return ApplyOutcome.Stale;
            Scoreboard = DtoCopy.Copy(board);
            Raise();
            return ApplyOutcome.Applied;
        }

        public ApplyOutcome SetRevealed(RevealedCompany[] market)
        {
            if (market == null) return ApplyOutcome.NoState;
            Revealed = market.Select(DtoCopy.Copy).ToArray();
            RevealedTurn = CurrentTurn;
            Raise();
            return ApplyOutcome.Applied;
        }

        // ------------------------------------------------------------------
        // 개인 결과
        // ------------------------------------------------------------------

        public ApplyOutcome ApplyResult(CommandResult result)
        {
            if (result == null) return ApplyOutcome.NoState;
            var gate = Gate(result);
            if (gate.HasValue) return gate.Value;

            if (!result.success) return Reject(result);

            var p = result.payload;
            if (p != null && p.automatic) AutomaticActionReceived?.Invoke(result.type);

            if (result.type == CommandTypes.ResultOf(CommandTypes.BuyInformation))
            {
                if (View == null || p == null) return ApplyOutcome.NoState;
                View.coins = p.remainingCoins; // 대입. 'coins -= 1' 금지.
                UpsertBundle(p.companyId, new VisibleBundle
                {
                    bundleIndex = p.bundleIndex,
                    startDay = p.startDay,
                    source = string.IsNullOrEmpty(p.source) ? BundleSources.Purchased : p.source,
                    dailyChangesBps = (int[])p.dailyChangesBps?.Clone()
                });
            }
            else if ((result.type == CommandTypes.ResultOf(CommandTypes.BuyInformationWindow)
                || result.type == CommandTypes.ResultOf(CommandTypes.RevealInformationWindow)
                || result.type == CommandTypes.ResultOf(CommandTypes.UseInformationItem)) && p?.dailyChangesBps != null && View != null)
            {
                View.coins = p.remainingCoins;
                var company = View.companies?.FirstOrDefault(c => c.companyId == p.companyId);
                if (company != null && !(company.visibleWindows ?? new VisibleInformationWindow[0]).Any(w => w.recordId == p.recordId))
                    company.visibleWindows = (company.visibleWindows ?? new VisibleInformationWindow[0]).Concat(new[] {
                        new VisibleInformationWindow { recordId = p.recordId, startDay = p.startDay, source = p.source,
                            paidCoins = p.paidCoins, dailyChangesBps = (int[])p.dailyChangesBps.Clone(), dailyOpenBps = (int[])p.dailyOpenBps?.Clone() } }).ToArray();
            }
            else if (result.type == CommandTypes.ResultOf(CommandTypes.SelectCompany))
            {
                var companyId = p?.companyId;
                if (!string.IsNullOrEmpty(companyId) && !_ownSelections.Contains(companyId)) _ownSelections.Add(companyId);
            }
            else if (result.type == CommandTypes.ResultOf(CommandTypes.SubmitInvestment))
            {
                if (p != null && p.locked)
                {
                    OrderLockedByResult = true;
                    if (View != null && p.investments != null && p.investments.Length > 0)
                        View.ownInvestments = DtoCopy.Copy(p.investments);
                }
            }

            Raise();
            return ApplyOutcome.Applied;
        }

        /// <summary>COMPLETE_PHASE를 보낸 즉시 '완료 대기'로 기록한다. 실패 응답이 오면 해제한다.
        /// (전원 완료 시 서버가 다음 단계 상태를 결과보다 먼저 보낼 수 있어, 보낸 시점의 단계를 기억해 둔다.)</summary>
        public void MarkPhaseCompleted(string requestId, int turn, string phase)
        {
            var key = turn + "|" + phase;
            _completedPhases.Add(key);
            if (requestId != null) _completeRequestPhase[requestId] = key;
            Raise();
        }

        // ------------------------------------------------------------------
        // 내부
        // ------------------------------------------------------------------

        /// <summary>공통 검사: 이전 턴/중복. 통과하면 null.</summary>
        private ApplyOutcome? Gate(CommandResult r)
        {
            if (r.turn < CurrentTurn)
            {
                // 이전 턴 응답: 대기 표시만 정리하고 화면 값은 건드리지 않는다.
                if (r.requestId != null && _pending.TryGetValue(r.requestId, out var p) && p.Turn == r.turn)
                    _pending.Remove(r.requestId);
                Raise();
                return ApplyOutcome.Stale;
            }

            var key = r.type + "|" + r.turn + "|" + r.requestId;
            if (_applied.Contains(key)) return ApplyOutcome.Duplicate;

            _applied.Add(key);
            if (r.requestId != null) _pending.Remove(r.requestId);
            return null;
        }

        private ApplyOutcome Reject(CommandResult r)
        {
            if (r.type == CommandTypes.ResultOf(CommandTypes.CompletePhase)
                && r.requestId != null && _completeRequestPhase.TryGetValue(r.requestId, out var key))
                _completedPhases.Remove(key);

            LastError = new ClientError(r.type, r.requestId, r.errorCode ?? "");
            ErrorReceived?.Invoke(LastError);
            Raise();
            return ApplyOutcome.Rejected;
        }

        private void BeginTurnIfNewer(int turn)
        {
            if (turn <= CurrentTurn) return;
            // Public and private snapshots arrive separately. Never render a new turn with
            // the preceding turn's holdings, visible prices or locked investment controls.
            if (View != null && View.turn < turn) View = null;
            if (Public != null && Public.turn < turn) Public = null;
            TradePlans.BeginTurn(turn);
            _pending.Clear();
            _ownSelections.Clear();
            OrderLockedByResult = false;
            _completedPhases.Clear();
            _completeRequestPhase.Clear();
            LastError = null;
            Revealed = null;
            RevealedTurn = 0;
        }

        private void UpsertBundle(string companyId, VisibleBundle bundle)
        {
            var companies = (View.companies ?? new VisibleCompany[0]).ToList();
            var company = companies.FirstOrDefault(c => c.companyId == companyId);
            if (company == null)
            {
                company = new VisibleCompany { companyId = companyId, visibleBundles = new VisibleBundle[0] };
                companies.Add(company);
                View.companies = companies.ToArray();
            }

            var bundles = (company.visibleBundles ?? new VisibleBundle[0]).ToList();
            // 이미 보이는 묶음(무료 공개 등)은 덮어쓰지 않는다. 다른 기업·다른 묶음은 유지한다.
            if (bundles.Any(b => b.bundleIndex == bundle.bundleIndex)) return;
            bundles.Add(bundle);
            company.visibleBundles = bundles.OrderBy(b => b.bundleIndex).ToArray();
        }

        private void Raise() => Changed?.Invoke();
    }
}
