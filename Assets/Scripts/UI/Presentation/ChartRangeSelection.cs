using System;
using BlackWhale.Shared;

namespace BlackWhale.UI.Presentation
{
    // Mouse selection is a draft. Prices/coins never change here.
    public sealed class ChartRangeSelection
    {
        private enum DragKind { Create, Move, Start, End, Public }
        private DragKind kind;
        private int anchor, originalStart, originalEnd;
        private bool awaitingEnd;
        public int Start { get; private set; }
        public int End { get; private set; }
        public int SelectedIndex { get; private set; } = -1;
        public bool HasSelection { get; private set; }
        public bool ChangedByDrag { get; private set; }
        public bool FixedPublicWindow { get; set; }
        public void Begin(int day, Interval[] intervals)
        {
            day = Clamp(day); anchor = day; ChangedByDrag = false;
            if (FixedPublicWindow)
            { kind = DragKind.Public; SelectedIndex = -1; HasSelection = true; Start = Math.Max(1, Math.Min(26, day)); End = Start + 4; awaitingEnd = false; return; }
            for (int i = 0; i < (intervals?.Length ?? 0); i++)
                if (day >= intervals[i].entryDay && day <= intervals[i].exitDay)
                {
                    SelectInterval(i, intervals[i].entryDay, intervals[i].exitDay);
                    originalStart = Start; originalEnd = End;
                    kind = Start == End ? DragKind.Create : day == Start ? DragKind.Start : day == End ? DragKind.End : DragKind.Move;
                    awaitingEnd = false; return;
                }
            kind = DragKind.Create; SelectedIndex = -1;
            if (awaitingEnd && HasSelection) { anchor = Start; Drag(day); awaitingEnd = false; }
            else { Start = End = day; HasSelection = true; }
        }
        public void Drag(int day)
        {
            day = Clamp(day); ChangedByDrag |= day != anchor;
            switch (kind)
            {
                case DragKind.Public: Start = Math.Max(1, Math.Min(26, Math.Min(anchor, day))); End = Start + 4; break;
                case DragKind.Move:
                    int delta = Math.Max(-originalStart, Math.Min(30 - originalEnd, day - anchor));
                    Start = originalStart + delta; End = originalEnd + delta; break;
                case DragKind.Start: Start = Math.Min(day, originalEnd); End = originalEnd; break;
                case DragKind.End: Start = originalStart; End = Math.Max(day, originalStart); break;
                default: Start = Math.Min(anchor, day); End = Math.Max(anchor, day); break;
            }
        }
        public void Finish(int day) { Drag(day); awaitingEnd = !FixedPublicWindow && SelectedIndex < 0 && Start == End; }
        public void SelectInterval(int index, int start, int end)
        { SelectedIndex = index; Start = start; End = end; HasSelection = true; awaitingEnd = false; }
        public void SetRange(int start, int end)
        { SelectedIndex = -1; Start = Clamp(Math.Min(start, end)); End = Clamp(Math.Max(start, end)); HasSelection = true; awaitingEnd = false; }
        public void Restore(Interval[] intervals)
        { if (SelectedIndex >= 0 && SelectedIndex < intervals.Length) SelectInterval(SelectedIndex, intervals[SelectedIndex].entryDay, intervals[SelectedIndex].exitDay); }
        public void Clear() { HasSelection = awaitingEnd = ChangedByDrag = false; SelectedIndex = -1; }
        private static int Clamp(int value) => Math.Max(0, Math.Min(30, value));
    }
}
