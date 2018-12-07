using System.Collections.Generic;
using System.Linq;

sealed class AngleRegions
{
    private List<float> _starts = new List<float>();
    private List<float> _ends = new List<float>();

    public void AddRegion(float start, float end)
    {
        start = (start % 360 + 360) % 360;
        end = end == 360 ? 360 : (end % 360 + 360) % 360;
        if (end < start)
        {
            AddRegion(start, 360);
            AddRegion(0, end);
        }
        else
        {
            _starts.Add(start);
            _ends.Add(end);
        }
    }

    public float FindNext(float angle)
    {
        angle = (angle % 360 + 360) % 360;
        var wrapped = false;
        while (true)
        {
            var intervalEnds = Enumerable.Range(0, _starts.Count).Where(ix => _starts[ix] <= angle && angle < _ends[ix]).Select(ix => _ends[ix]);
            if (!intervalEnds.Any())
                return angle;
            angle = intervalEnds.Max();
            if (angle == 360)
            {
                if (wrapped)
                    return 360;
                wrapped = true;
                angle = 0;
            }
        }
    }

    public float FindPrevious(float angle)
    {
        angle = (angle % 360 + 360) % 360;
        var wrapped = false;
        while (true)
        {
            var intervalStarts = Enumerable.Range(0, _starts.Count).Where(ix => _starts[ix] < angle && angle <= _ends[ix]).Select(ix => _starts[ix]);
            if (!intervalStarts.Any())
                return angle;
            angle = intervalStarts.Min();
            if (angle == 0)
            {
                if (wrapped)
                    return 0;
                wrapped = true;
                angle = 360;
            }
        }
    }

    public override string ToString()
    {
        return Enumerable.Range(0, _starts.Count).Select(i => string.Format(@"{0} -> {1}", _starts[i], _ends[i])).Join(", ");
    }
}
