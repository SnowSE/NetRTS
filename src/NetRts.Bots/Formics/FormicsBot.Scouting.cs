using NetRts.Protocol;

namespace NetRts.Bots.Formics;

public sealed partial class FormicsBot
{
    private int _scoutId;
    private List<(int X, int Y)> _tour = [];
    private int _tourStop;
    private bool _scouted;

    /// <summary>
    /// Early on, one worker walks a loop between our base and the middle of the map to find the grub fields there
    /// (on a mirrored map each one found is found in every quadrant), then goes back to digging.
    /// </summary>
    private void Scout(HiveTick hive)
    {
        var view = hive.View;
        if (_scouted || view.Workers.Count < _tune.ScoutAtWorkers)
        {
            return;
        }

        if (_scoutId == 0)
        {
            _tour = ScoutTour(hive);
            var pick = view.Workers.Where(w => !hive.Turn.IsClaimed(w.Id) && !_memory.IsBuilder(w.Id) && w.Activity != UnitActivity.Building)
                .OrderBy(w => w.Carrying).ThenBy(w => Cheb(w.X, w.Y, _tour[0].X, _tour[0].Y)).FirstOrDefault();
            if (pick is null)
            {
                return;
            }

            _scoutId = pick.Id;
        }

        var scout = view.Workers.FirstOrDefault(w => w.Id == _scoutId);
        if (scout is null)
        {
            _scouted = true;
            return;
        }

        // Next stop once there, or as near as it gets (a stop can be on rock).
        while (_tourStop < _tour.Count
               && Cheb(scout.X, scout.Y, _tour[_tourStop].X, _tour[_tourStop].Y) is var gap
               && (gap <= 2 || (gap <= 6 && scout.Activity == UnitActivity.Idle)))
        {
            _tourStop++;
        }

        if (_tourStop >= _tour.Count)
        {
            _scouted = true;
            if (BestDeposit(hive, scout.X, scout.Y, null) is { } spot)
            {
                hive.Turn.Claim(scout.Id);
                hive.Turn.Add(new CommandRequest { Type = CommandType.Gather, UnitIds = [scout.Id], TargetId = spot.Id });
            }

            return;
        }

        hive.Turn.Claim(scout.Id);
        var stop = _tour[_tourStop];
        if (scout.Activity == UnitActivity.Idle || scout.Destination is not { } d || d.X != stop.X || d.Y != stop.Y)
        {
            hive.Turn.Add(new CommandRequest { Type = CommandType.Move, UnitIds = [scout.Id], X = stop.X, Y = stop.Y });
        }
    }

    /// <summary>Stops on the way from our base to the middle of the map, swinging out to either side.</summary>
    private static List<(int X, int Y)> ScoutTour(HiveTick hive)
    {
        var cc = hive.View.CommandCenter!;
        var (w, h) = (hive.View.Map.Width, hive.View.Map.Height);
        var (dx, dy) = (((w - 1) / 2.0) - cc.X, ((h - 1) / 2.0) - cc.Y); // the exact middle, so mirrored bases scout mirrored tours
        var length = Math.Max(1, Math.Sqrt((dx * dx) + (dy * dy)));
        var (px, py) = (-dy / length * length * 0.2, dx / length * length * 0.2);
        (int X, int Y) At(double f, double side) =>
            (Math.Clamp((int)Math.Round(cc.X + (dx * f) + (px * side)), 1, w - 2), Math.Clamp((int)Math.Round(cc.Y + (dy * f) + (py * side)), 1, h - 2));
        return [At(0.45, 1), At(0.45, 0), At(0.45, -1), At(0.75, -1), At(0.75, 0), At(0.75, 1), At(0.95, 0)];
    }
}
