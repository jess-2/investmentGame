using System;
using BlackWhale.Shared;

namespace BlackWhale.Flow
{
    // Immutable server configuration. Fixture timing and game length are explicit opt-ins.
    public sealed class GameSettings
    {
        public double InfoSeconds { get; }
        public double PublicInformationPickSeconds => Math.Min(10, InfoSeconds / 5);
        public int Turns { get; }
        public double? AbilitySelectionSeconds { get; }
        public bool EnableProgressionFeatures { get; }
        public GameSettings(double infoSeconds = 90, int turns = 10,
            double? abilitySelectionSeconds = null, bool enableProgressionFeatures = true)
        {
            if (double.IsNaN(infoSeconds) || double.IsInfinity(infoSeconds) || infoSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(infoSeconds));
            if (turns < 1 || turns > 10) throw new ArgumentOutOfRangeException(nameof(turns));
            if (abilitySelectionSeconds.HasValue && (double.IsNaN(abilitySelectionSeconds.Value) ||
                double.IsInfinity(abilitySelectionSeconds.Value) || abilitySelectionSeconds.Value <= 0))
                throw new ArgumentOutOfRangeException(nameof(abilitySelectionSeconds));
            InfoSeconds = infoSeconds; Turns = turns;
            AbilitySelectionSeconds = abilitySelectionSeconds;
            EnableProgressionFeatures = enableProgressionFeatures;
        }
        public static GameSettings Fixture => new GameSettings(50, 1, enableProgressionFeatures: false);
        public static GameSettings StockGame => new GameSettings(30, StockRules.Turns, 20, enableProgressionFeatures: false);
    }
}
