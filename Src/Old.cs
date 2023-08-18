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
