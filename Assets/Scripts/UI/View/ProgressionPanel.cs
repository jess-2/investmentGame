using System;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    // Uses real public/private snapshots; only unsubmitted drafts belong to this screen.
    public sealed class ProgressionPanel : MonoBehaviour
    {
        private enum Page { None, Board, Ability, Auction, Shop, Inventory, Difficulty }
        private GameClientController controller;
        private LobbyActions lobby;
        private RectTransform modal, content;
        private Text feedback;
        private Button close, complete;
        private Page page;
        private string phaseKey = "", signature, abilityDraft = "", endReason = "";
        private int selectedSlot = -1;
        private AuctionModel auctionDraft, lastAuction;
        private AbilityPickScreen abilityScreen;
        private AuctionScreen auctionScreen;
        private readonly PhaseClock clock = new PhaseClock();
        private PublicState synced;
        private bool refreshRequested = true;
        private Scoreboard shownBoard;
        public static ProgressionPanel Create(RectTransform parent, GameClientController controller, LobbyActions lobby = null)
        {
            var root = UiKit.Rect(parent, "ProgressionPanel"); UiKit.Stretch(root);
            var panel = root.gameObject.AddComponent<ProgressionPanel>();
            panel.controller = controller; panel.lobby = lobby; panel.Build(root);
            controller.Store.Changed += panel.RequestRefresh; panel.Refresh();
            return panel;
        }
        private void OnDestroy() { if (controller != null) controller.Store.Changed -= RequestRefresh; }
        private void RequestRefresh() => refreshRequested = true;
        private void Build(RectTransform root)
        {
            var nav = UiKit.Rect(root, "GameScreens");
            nav.anchorMin = new Vector2(0, 1); nav.anchorMax = Vector2.one;
            nav.offsetMin = new Vector2(148, -33); nav.offsetMax = new Vector2(-48, -2);
            string[] labels = { "전광판", "입찰 결과", "내 능력", "상점", "보유 아이템", "BOT 난이도" };
            Page[] pages = { Page.Board, Page.Auction, Page.Ability, Page.Shop, Page.Inventory, Page.Difficulty };
            if (controller.Store.Public?.stockSystem == true)
            { labels = new[] { "전광판", "주식 현황", "내 증강", "BOT 난이도" }; pages = new[] { Page.Board, Page.Auction, Page.Ability, Page.Difficulty }; }
            for (int i = 0; i < labels.Length; i++)
            {
                Page target = pages[i];
                var button = UiKit.Button(nav, labels[i], 12, Palette.Card, Palette.Text, () => Open(target));
                button.name = "Open" + target;
                UiKit.Anchor((RectTransform)button.transform, i / (float)labels.Length, 0, (i + 1) / (float)labels.Length, 1, 3, 1);
            }
            modal = UiKit.Panel(root, "LiveGameScreen", Palette.Screen, rounded: true, raycast: true).rectTransform;
            UiKit.Stretch(modal, 42, 40, 42, 40);
            content = UiKit.Rect(modal, "LiveFeatureContent"); UiKit.Stretch(content, 16, 12, 16, 68);
            feedback = UiKit.Label(modal, "", 12, Palette.SubText, TextAnchor.MiddleLeft);
            UiKit.Place(feedback.rectTransform, Vector2.zero, Vector2.zero, new Vector2(16, 12), new Vector2(710, 42));
            complete = UiKit.Button(modal, "완료", 14, Palette.Accent, Color.white, () => controller.FinishPhase());
            UiKit.Place((RectTransform)complete.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-154, 14), new Vector2(220, 38));
            close = UiKit.Button(modal, "게임 화면으로", 13, Palette.Card, Palette.Text, () => { page = Page.None; ForceRefresh(); });
            close.name = "CloseGameScreen";
            UiKit.Place((RectTransform)close.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 14), new Vector2(130, 38));
        }
        public void OpenShop() => Open(Page.Shop);
        public void OpenInventory() => Open(Page.Inventory);
        public void OpenAbility() => Open(Page.Ability);
        private void Open(Page target)
        {
            refreshRequested = true;
            Refresh();
            string phase = controller.Store.Public?.phase;
            bool connected = string.IsNullOrEmpty(controller.Store.EndReason);
            page = connected && phase == Protocol.AbilitySelection ? Page.Ability : connected && phase == Protocol.Auction ? Page.Auction : target;
            ForceRefresh();
        }
        private void ForceRefresh() { signature = null; refreshRequested = true; Refresh(); }
        private void Update()
        {
            if (controller == null) return;
            Refresh();
            int seconds = clock.RemainingSeconds(controller.Store.Public, Time.realtimeSinceStartup);
            abilityScreen?.UpdateSeconds(seconds); auctionScreen?.UpdateSeconds(seconds);
        }
        private void Refresh()
        {
            var store = controller.Store; var state = store.Public;
            if (state == null) { modal.gameObject.SetActive(false); return; }
            if (!ReferenceEquals(synced, state)) { synced = state; clock.Sync(state.serverElapsedSeconds, Time.realtimeSinceStartup); }
            if (!refreshRequested) return;
            refreshRequested = false;
            string nextPhase = state.turn + "|" + state.phase;
            if (phaseKey != nextPhase)
            {
                if (state.phase != Protocol.Auction) lastAuction = LiveScreenModels.LastAuction(state, store.View, auctionDraft ?? lastAuction);
                phaseKey = nextPhase; abilityDraft = ""; selectedSlot = -1;
                page = state.phase == Protocol.AbilitySelection ? Page.Ability : state.phase == Protocol.Auction ? Page.Auction
                    : state.phase == Protocol.Result || state.phase == Protocol.Ended ? Page.Board : Page.None;
                if (state.phase == Protocol.Auction) auctionDraft = null;
            }
            var view = store.View;
            if (store.EndReason != endReason)
            { endReason = store.EndReason; if (!string.IsNullOrEmpty(endReason)) page = Page.Board; }
            string nextSignature = nextPhase + "|" + page + "|" + view?.turn + "|" + view?.ability?.selectedId + "|" + view?.ability?.freeInformationCredits
                + "|" + view?.auction?.confirmed + "|" + state.botDifficulty + "|" + string.Join(",", view?.inventory ?? new string[0])
                + "|" + store.HasPending(CommandTypes.SelectAbility) + "|" + store.HasPending(CommandTypes.SubmitBid)
                + "|" + controller.IsDoneForCurrentPhase + "|" + store.LastError?.RequestId + "|" + state.events?.Length
                + "|" + lobby?.Busy + "|" + state.paused
                + "|" + endReason
                + "|" + string.Join(",", view?.ability?.candidates ?? new string[0]);
            if (state.stockSystem) nextSignature += "|" + string.Join(",", view?.portfolio?.augments ?? new string[0]) + "|" + state.selectionIndex;
            bool newBoard = !ReferenceEquals(shownBoard, store.Scoreboard);
            if (nextSignature == signature && !newBoard) return;
            signature = nextSignature; shownBoard = store.Scoreboard;
            modal.gameObject.SetActive(page != Page.None);
            if (page == Page.None) return;
            bool connected = string.IsNullOrEmpty(endReason);
            bool mandatory = connected && (state.phase == Protocol.AbilitySelection || state.phase == Protocol.Auction);
            close.interactable = !mandatory;
            complete.gameObject.SetActive(connected && ((page == Page.Board && state.phase == Protocol.Result)
                || (page == Page.Ability && state.phase == Protocol.AbilitySelection && controller.IsHost)));
            complete.name = state.phase == Protocol.AbilitySelection ? "DefaultAbilities" : "CompleteResult";
            UiKit.SetButton(complete, state.phase == Protocol.AbilitySelection ? "미선택자는 첫 후보로 진행" : "정산 확인 · 완료",
                controller.CanFinishPhase(), Palette.Accent, Color.white);
            feedback.text = endReason == "GAME_COMPLETE" ? (state.totalTurns > 0 ? state.totalTurns : 10) + "턴 게임이 종료되었습니다. 서버가 확정한 최종 결과입니다."
                : !connected ? ErrorMessages.ToKorean(endReason) : store.LastError != null ? ErrorMessages.ToKorean(store.LastError.ErrorCode)
                : mandatory ? "선택과 확정은 서버가 처리합니다. 다른 참가자의 개인 정보는 표시하지 않습니다." : "실제 게임 상태 · 화면을 닫아도 게임은 계속 진행됩니다.";
            UiKit.Clear(content); abilityScreen = null; auctionScreen = null;
            if (((page == Page.Ability && state.phase == Protocol.AbilitySelection) || (page == Page.Auction && state.phase == Protocol.Auction))
                && (view == null || view.turn != state.turn)) { Message("내 게임 정보를 받아오는 중입니다."); return; }
            int seconds = clock.RemainingSeconds(state, Time.realtimeSinceStartup);
            switch (page)
            {
                case Page.Ability:
                    if (state.stockSystem)
                    {
                        Message("<b>내 증강</b>\n\n" + string.Join("\n\n", (view?.portfolio?.augments ?? new string[0]).Select(id => "<b>" + StockRules.Name(id) + "</b>\n" + StockRules.Description(id))) + "\n\n증강은 1·3·5턴 정보 확인 후 선택합니다.");
                        break;
                    }
                    var ability = LiveScreenModels.Ability(state, view, abilityDraft, store.HasPending(CommandTypes.SelectAbility), seconds);
                    if (state.phase != Protocol.AbilitySelection) { ability.Candidates = ability.Selected.Length > 0 ? new[] { ability.Selected } : new string[0]; ability.Confirmed = true; }
                    abilityScreen = new AbilityPickScreen(content);
                    abilityScreen.Picked = id => { abilityDraft = id; ForceRefresh(); };
                    abilityScreen.Confirm = () => { store.ClearError(); controller.SelectAbility(LiveScreenModels.ServerAbilityId(abilityDraft)); };
                    abilityScreen.Show(ability, ability.Confirmed ? LiveScreenModels.AbilityStatus(view) : null);
                    break;
                case Page.Auction:
                    if (state.stockSystem)
                    {
                        Message("<b>기업별 주식 보유 현황</b>\n\n" + string.Join("\n\n", (state.shareMarket ?? new CompanyShareState[0]).Select(c =>
                            "<b>" + c.companyId + "</b>" + (c.companyId == "BANK" ? " · 자동 예금\n" : " · 잔여 ■ ×" + (c.totalShares - c.owners.Sum(v => v.shares + v.reserved)) + "\n") + string.Join("   ", c.owners.Select(v => v.playerId + " " + v.shares + "주")))));
                        break;
                    }
                    if (state.phase == Protocol.Auction)
                        auctionDraft = LiveScreenModels.Auction(state, view, auctionDraft, store.HasPending(CommandTypes.SubmitBid), seconds);
                    else lastAuction = LiveScreenModels.LastAuction(state, view, lastAuction);
                    var auction = state.phase == Protocol.Auction ? auctionDraft : lastAuction;
                    if (auction == null) { Message("입찰은 3·6·9턴에 진행됩니다. 아직 공개된 입찰 결과가 없습니다."); break; }
                    auctionScreen = new AuctionScreen(content);
                    auctionScreen.BidChanged = bid => auctionDraft.Bid = bid;
                    auctionScreen.PreferenceChanged = order => { auctionDraft.SeatPreference = order; ForceRefresh(); };
                    auctionScreen.FreePassToggled = on => { auctionDraft.UseFreePass = on; ForceRefresh(); };
                    auctionScreen.Confirm = () => { store.ClearError(); controller.SubmitBid(auctionDraft.Bid, auctionDraft.SeatPreference, auctionDraft.UseFreePass); };
                    auctionScreen.Show(auction);
                    break;
                case Page.Board:
                    if (store.Scoreboard?.rows?.Length > 0) new ScoreboardScreen(content).Show(LiveScreenModels.Board(store.Scoreboard, state, controller.LocalPlayerId));
                    else Message("아직 정산된 턴이 없습니다. 첫 턴 정산 후 전광판이 표시됩니다.");
                    break;
                case Page.Shop:
                    new ShopScreen(content).Show(LiveScreenModels.Shop(view, state.phase));
                    feedback.text = "상점 구매 기능은 준비 중입니다. 구매 버튼은 사용할 수 없습니다.";
                    break;
                case Page.Inventory:
                    var inventory = new InventoryScreen(content);
                    inventory.SelectSlot = index => { selectedSlot = index; ForceRefresh(); };
                    inventory.Show(LiveScreenModels.Inventory(view, state.phase, selectedSlot));
                    feedback.text = state.windowInformation
                        ? "정보 아이템은 공용 공개 후 정보 구매 창의 확대 화면에서 사용합니다. 프리패스는 9턴 입찰에서 사용합니다."
                        : "서버가 지급한 보유 아이템입니다. 프리패스는 9턴 입찰 화면에서 사용합니다.";
                    break;
                case Page.Difficulty:
                    Enum.TryParse(state.botDifficulty, out BotDifficulty difficulty);
                    var panel = new BotDifficultyPanel(content);
                    panel.Changed = value => { if (lobby?.Busy != true && lobby?.SetDifficulty != null) lobby.SetDifficulty(value); };
                    panel.Show(difficulty, connected && controller.IsHost && lobby?.SetDifficulty != null && lobby.Busy == false, state.phase == Protocol.Lobby);
                    break;
            }
        }
        private void Message(string text)
        { var label = UiKit.Label(content, text, 18, Palette.SubText); UiKit.Stretch(label.rectTransform, 24, 24, 24, 24); }
    }
}
