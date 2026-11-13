using System;
using System.Collections.Generic;

namespace BlackWhale.Rules
{
    // Diagnostic metadata stays in the server/editor assembly, never in Shared/network DTOs.
    public enum StockPatternKind { Rectangle, DoubleTop, DoubleBottom, Flag, AscendingTriangle, DescendingTriangle, RoundingBottom, RoundingTop, MeasuredMoveUp, MeasuredMoveDown, BumpAndRunTop, HeadAndShouldersTop, Pennant, ThreeRisingValleys, VBottom, VTop }
    public enum StockPatternOutcome { Completed, Damaged, Unstructured }
    public sealed class StockPatternSample
    {
        public int Seed, Turn, StartDay, EndDay;
        public string CompanyId;
        public StockPatternKind Pattern;
        public StockPatternOutcome Outcome;
        public int[] DailyChangesBps;
    }
    // Server only. Metadata and the room seed never belong in a player snapshot.
    // RandomMarketGenerator remains the v2 comparison generator for frozen fixtures.
    public sealed class PatternMarketGenerator
    {
        private readonly int seed;
        private readonly InvestmentRules rules;
        private readonly Dictionary<int, MarketData> cache = new Dictionary<int, MarketData>();
        private readonly object gate = new object();
        public PatternMarketGenerator(int seed, InvestmentRules rules = null)
        { this.seed = seed; this.rules = (rules ?? new InvestmentRules()).Copy(); }

        public MarketData ForTurn(int turn)
        {
            lock (gate)
            {
                if (!cache.TryGetValue(turn, out var data)) cache.Add(turn, data = Generate(seed, turn, rules));
                var companies = new CompanyMarketData[data.companies.Length];
                for (int c = 0; c < companies.Length; c++) companies[c] = new CompanyMarketData {
                    companyId = data.companies[c].companyId,
                    dailyChangesBps = (int[])data.companies[c].dailyChangesBps.Clone(), publicBundleIndices = new int[0] };
                return new MarketData { turn = turn, companies = companies };
            }
        }

        public static MarketData Generate(int seed, int turn, InvestmentRules rules = null)
        {
            if (turn < 1 || turn > 10) throw new ArgumentOutOfRangeException(nameof(turn));
            rules = rules ?? new InvestmentRules();
            if (rules.daysPerTurn != 30) throw new ArgumentException("Pattern markets require 30 days.");
            var companies = new CompanyMarketData[InvestmentRules.CompanyIds.Length];
            for (int c = 0; c < companies.Length; c++)
            {
                string id = InvestmentRules.CompanyIds[c];
                var rng = new Stream(unchecked((ulong)(uint)seed * 0x9E3779B97F4A7C15UL ^
                    (ulong)(uint)turn * 0xBF58476D1CE4E5B9UL ^ (ulong)(c + 1) * 0x94D049BB133111EBUL));
                companies[c] = new CompanyMarketData { companyId = id, publicBundleIndices = new int[0],
                    dailyChangesBps = Path(id, turn, rng) };
            }
            return new MarketData { turn = turn, companies = companies };
        }

        // Rectangle, DoubleTop, DoubleBottom, Flag. Each company changes probabilities, not outcomes.
        private static int PickPattern(string id, Stream rng)
        {
            int[][] weights = { new[] {4, 4, 1, 1}, new[] {4, 1, 4, 1}, new[] {1, 4, 4, 1},
                new[] {1, 4, 1, 4}, new[] {4, 1, 1, 4}, new[] {1, 3, 3, 3} };
            int group = id == "BANK" ? 0 : id == "FOOD" ? 1 : id == "AUTO" ? 2 : id == "AI" ? 3 : id == "SPACE" ? 4 : 5;
            var w = weights[group];
            int roll = rng.Int(0, 19);
            if (roll >= 10) return (int)StockPatternKind.AscendingTriangle + group * 2 + (roll >= 15 ? 1 : 0);
            for (int p = 0; p < w.Length; p++) { roll -= w[p]; if (roll < 0) return p; }
            return 3;
        }

        public static StockPatternSample Inspect(int seed, int turn, string companyId,
            StockPatternKind? pattern = null, StockPatternOutcome? outcome = null)
        {
            int company = Array.IndexOf(InvestmentRules.CompanyIds, companyId);
            if (company < 0) throw new ArgumentException("Unknown company.", nameof(companyId));
            if (turn < 1 || turn > 10) throw new ArgumentOutOfRangeException(nameof(turn));
            if (pattern.HasValue && !Enum.IsDefined(typeof(StockPatternKind), pattern.Value)) throw new ArgumentOutOfRangeException(nameof(pattern));
            if (outcome.HasValue && !Enum.IsDefined(typeof(StockPatternOutcome), outcome.Value)) throw new ArgumentOutOfRangeException(nameof(outcome));
            var rng = new Stream(unchecked((ulong)(uint)seed * 0x9E3779B97F4A7C15UL ^
                (ulong)(uint)turn * 0xBF58476D1CE4E5B9UL ^ (ulong)(company + 1) * 0x94D049BB133111EBUL));
            var sample = new StockPatternSample { Seed = seed, Turn = turn, CompanyId = companyId };
            sample.DailyChangesBps = Path(companyId, turn, rng, sample, pattern, outcome);
            return sample;
        }

        private static int[] Path(string id, int turn, Stream rng, StockPatternSample sample = null,
            StockPatternKind? forcedPattern = null, StockPatternOutcome? forcedOutcome = null)
        {
            int limit = RandomMarketGenerator.DailyLimitBps(id, turn), cap = RandomMarketGenerator.AbsoluteCapBps[id];
            int branch = rng.Int(0, 99); // 50% completed, 30% failed/damaged, 20% unstructured.
            int pattern = PickPattern(id, rng), start = rng.Int(1, 7), length = rng.Int(12, 22);
            length = Math.Min(length, 29 - start);
            if (forcedPattern.HasValue) pattern = (int)forcedPattern.Value;
            if (forcedOutcome.HasValue) branch = forcedOutcome == StockPatternOutcome.Completed ? 0 : forcedOutcome == StockPatternOutcome.Damaged ? 50 : 80;
            if (sample != null)
            {
                sample.Pattern = (StockPatternKind)pattern;
                sample.Outcome = branch < 50 ? StockPatternOutcome.Completed : branch < 80 ? StockPatternOutcome.Damaged : StockPatternOutcome.Unstructured;
                sample.StartDay = start; sample.EndDay = start + length;
            }
            double[] y;
            switch (pattern)
            {
                case 0: y = new[] {0.0, .85, -.65, .95, -.8, .75, .1}; break;
                case 1: y = new[] {0.0, 1.0, .25, rng.Int(85, 115) / 100.0, -.4, -.85}; break;
                case 2: y = new[] {0.0, -1.0, -.25, -rng.Int(85, 115) / 100.0, .4, .85}; break;
                case (int)StockPatternKind.AscendingTriangle: y = new double[] { 0,.85,-.65,.85,-.25,.85,.25,1.2 }; break;
                case (int)StockPatternKind.DescendingTriangle: y = new double[] { 0,-.85,.65,-.85,.25,-.85,-.25,-1.2 }; break;
                case (int)StockPatternKind.RoundingBottom: y = new double[] { 0,-.5,-.82,-.98,-1,-.88,-.55,0 }; break;
                case (int)StockPatternKind.RoundingTop: y = new double[] { 0,.5,.82,.98,1,.88,.55,0 }; break;
                case (int)StockPatternKind.MeasuredMoveUp: y = new double[] { 0,.4,.85,.55,.4,.85,1.25 }; break;
                case (int)StockPatternKind.MeasuredMoveDown: y = new double[] { 0,-.4,-.85,-.55,-.4,-.85,-1.25 }; break;
                case (int)StockPatternKind.BumpAndRunTop: y = new double[] { 0,.2,.4,.65,1.35,.8,.25,-.4 }; break;
                case (int)StockPatternKind.HeadAndShouldersTop: y = new double[] { 0,.75,.25,1.2,.25,.7,.2,-.5 }; break;
                case (int)StockPatternKind.Pennant: y = new double[] { 0,1,.52,.88,.62,.78,.68,1.35 }; break;
                case (int)StockPatternKind.ThreeRisingValleys: y = new double[] { 0,-.65,.28,-.38,.48,-.1,.75,1 }; break;
                case (int)StockPatternKind.VBottom: y = new double[] { 0,-.42,-.9,-1.25,-.8,-.35,.1 }; break;
                case (int)StockPatternKind.VTop: y = new double[] { 0,.42,.9,1.25,.8,.35,-.1 }; break;
                default:
                    double direction = rng.Int(0, 1) == 0 ? -1 : 1;
                    y = new[] {0.0, direction, direction * .7, direction * .8, direction * .55, direction * 1.6}; break;
            }
            if (branch >= 50 && branch < 80)
            {
                // The same initial shape can finish with a reversal, continuation or sideways tail.
                y[y.Length - 2] = y[y.Length - 3] + rng.Int(-30, 30) / 100.0;
                y[y.Length - 1] = y[y.Length - 3] + rng.Int(-55, 55) / 100.0;
            }
            var raw = new double[31];
            double amplitude = Math.Min(cap * rng.Int(35, 75) / 100.0, limit * rng.Int(20, 38) / 10.0);
            double drift = rng.Int(-35, 35) / 100.0 * limit;
            // Jitter in control-point times makes left/right shoulders asymmetric.
            var times = new double[y.Length];
            for (int k = 0; k < times.Length; k++)
                times[k] = k == 0 ? start : k == times.Length - 1 ? start + length :
                    start + length * (k + rng.Int(-20, 20) / 100.0) / (times.Length - 1);
            double walk = 0;
            for (int d = 1; d <= 30; d++)
            {
                walk += rng.Int(-100, 100) / 100.0 * limit * .24;
                double shape = 0;
                if (branch < 80 && d >= start)
                {
                    int k = 1;
                    while (k < times.Length - 1 && d > times[k]) k++;
                    shape = d >= start + length ? y[y.Length - 1] + (d - start - length) * rng.Int(-8, 8) / 100.0 :
                        y[k - 1] + (y[k] - y[k - 1]) * (d - times[k - 1]) / (times[k] - times[k - 1]);
                }
                raw[d] = shape * amplitude + drift * d + walk + rng.Int(-100, 100) / 100.0 * limit * .10;
            }
            // Scale the entire candidate rather than clipping at a ceiling for many days.
            double scale = 1;
            for (int d = 1; d <= 30; d++)
            {
                if (Math.Abs(raw[d]) > 0) scale = Math.Min(scale, cap * .96 / Math.Abs(raw[d]));
                if (Math.Abs(raw[d] - raw[d - 1]) > 0) scale = Math.Min(scale, (limit - 1) / Math.Abs(raw[d] - raw[d - 1]));
            }
            var result = new int[31];
            for (int d = 1; d <= 30; d++) result[d] = (int)Math.Round(raw[d] * scale, MidpointRounding.AwayFromZero);
            // Bounded construction: no retries, no dependence on any information selection.
            return result;
        }

        private sealed class Stream
        {
            private ulong state;
            public Stream(ulong seed) { state = seed; }
            public int Int(int min, int max)
            {
                unchecked
                {
                    ulong z = state += 0x9E3779B97F4A7C15UL;
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    z ^= z >> 31;
                    return min + (int)(z % (uint)(max - min + 1));
                }
            }
        }
    }
}
