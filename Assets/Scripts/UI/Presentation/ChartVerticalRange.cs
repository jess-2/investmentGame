using System;
using System.Collections.Generic;

namespace BlackWhale.UI.Presentation
{
    // Supply only visible opens/closes. Hidden market bounds must never enter this calculation.
    public struct ChartVerticalRange
    {
        public double MinBps, MaxBps;
        public double Span => MaxBps - MinBps;
        public double Fraction(int bps) => Math.Max(0, Math.Min(1, (bps - MinBps) / Span));
        public static ChartVerticalRange FromVisibleValues(IEnumerable<int> values)
        {
            double min = 0, max = 0; // the public day-zero baseline stays visible
            foreach (int value in values ?? new int[0]) { min = Math.Min(min, value); max = Math.Max(max, value); }
            double span = Math.Max(50, max - min), center = (min + max) * .5;
            return new ChartVerticalRange { MinBps = center - span * .62, MaxBps = center + span * .62 };
        }
    }
}
