// [개발 전용] 다른 참가자 대신 움직이는 간단한 봇. 사람과 똑같이 GameClientController로 명령을 보낸다.
// 대기실 준비 → (P1이면) 시작 → 정보 단계 완료 → 자기 차례에 기업 선택 → 주문 제출 → 공개·결과 완료.
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;

namespace BlackWhale.UI.Dev
{
    public sealed class FixtureReplayBot
    {
        private readonly GameClientController _c;
        private readonly float _thinkSeconds;
        private string _stateKey = "";
        private float _since;

        public IGameClient Client { get; }
        public GameClientController Controller => _c;

        public FixtureReplayBot(IGameClient client, float thinkSeconds)
        {
            Client = client;
            _c = new GameClientController(client);
            _thinkSeconds = thinkSeconds;
        }

        public void Update(float now)
        {
            var s = _c.Store.Public;
            if (s == null || !string.IsNullOrEmpty(_c.Store.EndReason)) return;

            string key = s.turn + "|" + s.phase + "|" + s.selectionIndex + "|" + s.currentPlayerId;
            if (key != _stateKey)
            {
                _stateKey = key;
                _since = now;
            }
            if (now - _since < _thinkSeconds) return;

            switch (s.phase)
            {
                case Protocol.Lobby:
                    if (!_c.IsLocalReady && !_c.Store.HasPending(CommandTypes.SetReady)) _c.SetReady(true);
                    else if (_c.IsHost && _c.AllPlayersReady && !_c.Store.HasPending(CommandTypes.StartGame)) _c.StartGame();
                    break;

                case Protocol.Info:
                case Protocol.Reveal:
                case Protocol.Result:
                    if (_c.CanFinishPhase()) _c.FinishPhase();
                    break;

                case Protocol.Selection:
                    if (_c.IsMyTurnToSelect() && !_c.Store.HasPending(CommandTypes.SelectCompany))
                    {
                        string wanted = s.selectionIndex >= 0 && s.selectionIndex < DevFixtures.ReplayPicks.Length
                            ? DevFixtures.ReplayPicks[s.selectionIndex] : null;
                        string pick = wanted != null && _c.CanSelect(wanted)
                            ? wanted
                            : new[] { "BANK", "FOOD", "AUTO", "AI", "SPACE", "BIO" }.FirstOrDefault(_c.CanSelect);
                        if (pick != null) _c.SelectCompany(pick);
                        _since = now; // 같은 차례에 중복 요청하지 않도록
                    }
                    break;

                case Protocol.Investment:
                    if (!_c.Store.OrderLocked && !_c.Store.HasPending(CommandTypes.SubmitInvestment)
                        && _c.Store.OwnSelections.Count == UiRules.CompaniesPerOrder)
                    {
                        var draft = InvestmentDraft.FromSelections(_c.Store.OwnSelections);
                        draft.SetFirstWeight(6000);
                        draft.TryAddInterval(draft.FirstCompanyId, 10, 15);
                        draft.TryAddInterval(draft.SecondCompanyId, 5, 10);
                        _c.SubmitInvestment(draft.ToInvestments());
                        _since = now;
                    }
                    break;
            }
        }
    }
}
