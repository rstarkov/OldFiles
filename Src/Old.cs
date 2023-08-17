using RT.Util;
using RT.Util.ExtensionMethods;

namespace OldFiles;

interface IOldItem
{
    double Age { get; }
    double Spacing { get; }
    State State { get; set; }
}

enum State { Undecided, Old, Keep }

static class Old
{
    public static void ApplySpacing(IEnumerable<IOldItem> items)
    {
        items = items.OrderBy(item => item.Age);
        IOldItem prevUndecided = null, prevKeep = null;
        foreach (var cur in items)
        {
            if (cur.State == State.Old)
                continue;
            if (prevKeep == null)
            {
                cur.State = State.Keep;
                prevKeep = cur;
            }
            else
            {
                if (prevUndecided != null)
                {
                    var gap = cur.Age - prevKeep.Age;
                    if (gap > prevKeep.Spacing || gap > cur.Spacing)
                    {
                        prevUndecided.State = State.Keep;
                        prevKeep = prevUndecided;
                    }
                    else
                    {
                        prevUndecided.State = State.Old;
                    }
                    prevUndecided = null;
                }
                if (cur.State == State.Keep)
                    prevKeep = cur;
                else
                    prevUndecided = cur;
            }
        }
        if (prevUndecided != null)
            prevUndecided.State = State.Keep;
    }
}

#if DEBUG
static class OldFilesTests
{
    private class tItem : IOldItem
    {
        public double Age { get; set; }
        public double Spacing { get; set; }
        private State _state = OldFiles.State.Undecided;
        public State State
        {
            get { return _state; }
            set
            {
                if (value == _state)
                    return;
                if (_state != OldFiles.State.Undecided)
                    throw new Exception();
                _state = value;
                Modified = true;
            }
        }
        public bool Modified;
        public tItem() { }
        public tItem(double age, double spacing, State state = OldFiles.State.Undecided) { Age = age; Spacing = spacing; _state = state; }
        public override string ToString() { return "Age={0:0.00}; Spacing={1:0.00}; State={2}".Fmt(Age, Spacing, State); }
    }

    public static void Run()
    {
        for (int t = 0; t < 100; t++)
        {
            var items = new List<tItem>();
            for (int i = Rnd.Next(200); i >= 0; i--)
                items.Add(new tItem { Age = Rnd.NextDouble() < 0.5 ? Rnd.NextDouble(0, 50) : Rnd.NextDouble(100, 200), Spacing = Rnd.NextDouble(5, 15), State = Rnd.NextDouble() < 0.05 ? State.Keep : Rnd.NextDouble() < 0.3 ? State.Old : State.Undecided });
            foreach (var item in items)
                item.Modified = false;
            // The current algorithm only supports monotonically increasing spacing, so make it so.
            var ordered = items.OrderBy(x => x.Age);
            var prev = ordered.First();
            foreach (var item in ordered.Skip(1))
            {
                item.Spacing = Math.Max(prev.Spacing, (prev.Spacing * 3 + item.Spacing) / 4);
                prev = item;
            }
            Old.ApplySpacing(items);
            assertTestItemSpacingCorrect(items);
        }

        // The following tests are a little more strict in that they expect a particular outcome when multiple outcomes are possible without violating the spacing.
        // So they're somewhat brittle, but the advantage is that it's much more obvious what they test compared to the above.

        // gap: [2]
        test(new[] { new tItem(24, 10), new tItem(26, 10) }, State.Keep, State.Keep);

        // gaps: [3, 2, 3] -> [8]
        test(new[] { new tItem(21, 10), new tItem(24, 10), new tItem(26, 10), new tItem(29, 10) }, State.Keep, State.Old, State.Old, State.Keep);

        // gaps: [7, 2, 7] -> make it [9, 7] or [7, 9]
        test(new[] { new tItem(17, 10), new tItem(24, 10), new tItem(26, 10), new tItem(33, 10) }, State.Keep, State.Old, State.Keep, State.Keep);

        // gaps: [9, 2, 9] -> ??? -> [11, 9] or [9, 11]  -  feels like one of the inner ones should be deletable. No need though, as in real life scenarios with a spacing function that grows with age, one of them will become deletable soon anyway, without having to exceed the spacing limits
        test(new[] { new tItem(15, 10), new tItem(24, 10), new tItem(26, 10), new tItem(35, 10) }, State.Keep, State.Keep, State.Keep, State.Keep);

        // gaps: [9, 9, 9]
        test(new[] { new tItem(15, 10), new tItem(24, 10), new tItem(33, 10), new tItem(42, 10) }, State.Keep, State.Keep, State.Keep, State.Keep);

        // gaps: [2, 7, 2] -> [9, 2] or [2, 9]
        test(new[] { new tItem(10, 10), new tItem(12, 10), new tItem(19, 10), new tItem(21, 10) }, State.Keep, State.Old, State.Keep, State.Keep);

        // gaps: [2, 7, 7, 2] -> [9, 9]
        test(new[] { new tItem(10, 10), new tItem(12, 10), new tItem(19, 10), new tItem(26, 10), new tItem(28, 10) }, State.Keep, State.Old, State.Keep, State.Old, State.Keep);

        // gaps: [4, 5, 1, 1, 1] -> [9, 3] OR [4, 8]
        test(new[] { new tItem(10, 9.5), new tItem(14, 9.5), new tItem(19, 9.5), new tItem(20, 9.5), new tItem(21, 9.5), new tItem(22, 9.5) }, State.Keep, State.Old, State.Keep, State.Old, State.Old, State.Keep);

        // [4, 5, 1, 1, 1, 1, 1] -> [9, 5] (greedy from both directions) OR [4, 6, 4] OR [4, 9, 1]
        test(new[] { new tItem(10, 9.5), new tItem(14, 9.5), new tItem(19, 9.5), new tItem(20, 9.5), new tItem(21, 9.5), new tItem(22, 9.5), new tItem(23, 9.5), new tItem(24, 9.5) }, State.Keep, State.Old, State.Keep, State.Old, State.Old, State.Old, State.Old, State.Keep);

        // [4, 5, 1, 1, 1, 1, 1] -> [9, 5] (greedy from both directions) OR [4, 6, 4] OR [4, 9, 1]
        test(new[] { new tItem(10, 9.5), new tItem(14, 9.5, State.Keep), new tItem(19, 9.5), new tItem(20, 9.5), new tItem(21, 9.5), new tItem(22, 9.5), new tItem(23, 9.5), new tItem(24, 9.5) }, State.Keep, State.Keep, State.Old, State.Old, State.Old, State.Old, State.Keep, State.Keep);

        test(new[] { new tItem(20, 6), new tItem(25, 6), new tItem(35, 12) }, State.Keep, State.Keep, State.Keep);
    }

    private static void assertTestItemSpacingCorrect(List<tItem> items)
    {
        items = items.OrderBy(x => x.Age).ToList();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].State == State.Undecided)
                throw new Exception();
            if (items[i].Modified)
            {
                // This item has been specifically set to Keep or Old.
                // It should only have been set to Keep if the spacings of the closest nearby Keeps doesn't allow it to be Old. It should only have been set to Old if they do.
                if (items[i].State == State.Undecided)
                    throw new Exception();
                double lowerSpacing = 0;
                double lowerAge = -999999;
                for (int k = i - 1; k >= 0; k--)
                    if (items[k].State == State.Keep)
                    {
                        lowerSpacing = items[k].Spacing;
                        lowerAge = items[k].Age;
                        break;
                    }
                double upperSpacing = 0;
                double upperAge = 999999;
                for (int k = i + 1; k < items.Count; k++)
                    if (items[k].State == State.Keep)
                    {
                        upperSpacing = items[k].Spacing;
                        upperAge = items[k].Age;
                        break;
                    }
                var gap = upperAge - lowerAge;
                var expectedState = gap <= lowerSpacing && gap <= upperSpacing ? State.Old : State.Keep;
                if (expectedState != items[i].State)
                    throw new Exception();
            }
        }
    }

    private static void test(IEnumerable<tItem> items, params State[] expectedStates)
    {
        Old.ApplySpacing(items);
        int i = 0;
        foreach (var item in items)
        {
            if (item.State != expectedStates[i])
                throw new Exception();
            i++;
        }
        assertTestItemSpacingCorrect(items.ToList());
    }
}
#endif
