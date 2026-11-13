using BlackWhale.Shared;

namespace BlackWhale.Flow
{
    public sealed partial class GameRoom
    {
        private IStockMarketSystem Stock => market as IStockMarketSystem;
        private bool StockMode => Stock?.StockEnabled == true;
        private static bool IsStockCommand(string type) => type == StockRules.Position || type == StockRules.Trade || type == StockRules.Pass || type == StockRules.Choose || type == StockRules.Priority;
        private void SendAllViews() { foreach (var m in members) SendView(m.Id); }
        private void RevealStockPortfolio(double now)
        {
            Stock.LockPortfolios(); ordersLocked = true;
            Enter(Protocol.Reveal, now, 10);
            Broadcast(new ServerMessage { type = "MARKET_REVEAL", revealedMarket = market.GetRevealedMarket(Context()) });
            SendAllViews();
        }
        private CommandResult ExecuteStockCommand(string player, Command command, double now)
        {
            if (command.type == StockRules.Position)
            {
                if (phase != Protocol.Investment) return CommandResult.Fail(command, "WRONG_PHASE");
                var orders = command.payload?.shareOrders;
                if (orders?.Length != 1) return CommandResult.Fail(command, "INVALID_SHARE_ORDER");
                var adjusted = Stock.SetPosition(Context(player, command.requestId), orders[0]);
                if (!adjusted.Success) return CommandResult.Fail(command, adjusted.ErrorCode);
                PublishState(); SendAllViews(); return Success(command);
            }
            if (command.type == StockRules.Choose)
            {
                if (phase != StockRules.AugmentPhase) return CommandResult.Fail(command, "WRONG_PHASE");
                if (command.payload == null) return CommandResult.Fail(command, "INVALID_REQUEST");
                var result = Stock.ChooseAugment(Context(player, command.requestId), command.payload.augmentId, command.payload.companyId);
                if (!result.Success) return CommandResult.Fail(command, result.ErrorCode);
                if (Stock.AllAugmentsChosen) { Enter(Protocol.Info, now, settings.InfoSeconds); SendAllViews(); } else SendView(player);
                return Success(command);
            }
            return CommandResult.Fail(command, "WRONG_PHASE");
        }
    }
}
