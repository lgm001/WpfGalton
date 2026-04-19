using System.Windows.Media;

namespace WpfGalton;

internal readonly record struct Vec2(double X, double Y);

internal readonly record struct LayoutWall(Vec2 A, Vec2 B, double Restitution, bool IsFloor);

internal sealed class Marble
{
    public double X, Y, Vx, Vy;
    public double Radius;
    public Color Color;
}

internal sealed class GaltonSimulation
{
    private readonly List<Marble> _marbles = new();
    private readonly List<Vec2> _pegs = new();
    private readonly List<LayoutWall> _walls = new();
    private readonly List<(Vec2 A, Vec2 B)> _segmentDraw = new();
    private readonly Random _rng = new();

    private double _width;
    private double _height;
    private double _pegRadius;
    private double _floorY;
    private double _spawnY;
    private double _spawnHalfWidth;
    private int _rows;
    private double _horizontalSpacing;
    private double _gravity = 980;
    private double _restitutionPeg = 0.48;
    private double _restitutionBall = 0.92;
    private double _restitutionWall = 0.75;
    private double _bumperRestitution = 0.9;

    private static readonly Color[] Palette =
    [
        Color.FromRgb(255, 99, 132),
        Color.FromRgb(54, 162, 235),
        Color.FromRgb(255, 206, 86),
        Color.FromRgb(75, 192, 192),
        Color.FromRgb(153, 102, 255),
        Color.FromRgb(255, 159, 64),
        Color.FromRgb(46, 204, 113),
        Color.FromRgb(231, 76, 60),
        Color.FromRgb(52, 152, 219),
        Color.FromRgb(155, 89, 182),
        Color.FromRgb(241, 196, 15),
        Color.FromRgb(26, 188, 156),
    ];

    public IReadOnlyList<Marble> Marbles => _marbles;
    public IReadOnlyList<Vec2> Pegs => _pegs;
    public IReadOnlyList<(Vec2 A, Vec2 B)> Segments => _segmentDraw;
    public double PegRadius => _pegRadius;

    public void Resize(double width, double height)
    {
        _width = width;
        _height = height;

        _pegs.Clear();
        _walls.Clear();
        _segmentDraw.Clear();

        if (width < 200 || height < 200)
            return;

        var marginX = width * 0.04;
        var marginTop = height * 0.05;
        var floorMargin = height * 0.06;

        var centerX = width * 0.5;
        var funnelTopY = marginTop + 20;
        var funnelBottomY = marginTop + height * 0.14;
        var funnelTopHalfWidth = Math.Min(width * 0.22, 220);
        var funnelBottomHalfWidth = Math.Min(width * 0.045, 48);

        _spawnY = funnelBottomY + 8;
        _spawnHalfWidth = funnelBottomHalfWidth * 0.55;

        _floorY = height - floorMargin;

        _horizontalSpacing = Math.Clamp(width / 28.0, 26, 44);
        _rows = (int)Math.Clamp((funnelBottomY + height * 0.62 - funnelBottomY) / 36.0, 10, 16);
        _pegRadius = Math.Clamp(_horizontalSpacing * 0.11, 3.2, 5.5);

        var pegStartY = funnelBottomY + height * 0.05;
        for (var r = 0; r < _rows; r++)
        {
            for (var k = 0; k <= r; k++)
            {
                var x = centerX + (k - r * 0.5) * _horizontalSpacing;
                var y = pegStartY + r * (_horizontalSpacing * 0.92);
                _pegs.Add(new Vec2(x, y));
            }
        }

        var lastPegY = pegStartY + (_rows - 1) * (_horizontalSpacing * 0.92);
        var binTopY = lastPegY + _horizontalSpacing * 0.75;

        var numBins = _rows + 1;
        var binHalfSpan = (_rows * 0.5) * _horizontalSpacing + _horizontalSpacing * 0.55;
        var binLeft = centerX - binHalfSpan;
        var binRight = centerX + binHalfSpan;
        var binWidth = (binRight - binLeft) / numBins;

        AddWall(
            new Vec2(centerX - funnelTopHalfWidth, funnelTopY),
            new Vec2(centerX - funnelBottomHalfWidth, funnelBottomY),
            _restitutionWall,
            false);
        AddWall(
            new Vec2(centerX + funnelTopHalfWidth, funnelTopY),
            new Vec2(centerX + funnelBottomHalfWidth, funnelBottomY),
            _restitutionWall,
            false);

        var rowDy = _horizontalSpacing * 0.92;
        var lastR = _rows - 1;
        var w = _horizontalSpacing;
        var yBumperTop = funnelBottomY - 4;
        var yBumperBot = _floorY;
        var clearance = _pegRadius + Math.Max(11, w * 0.28);

        if (lastR >= 1 && Math.Abs(lastR * rowDy) > 1e-6)
        {
            var vxl = -lastR * w * 0.5;
            var vyl = lastR * rowDy;
            var lenL = Math.Sqrt(vxl * vxl + vyl * vyl);
            var uxl = vxl / lenL;
            var uyl = vyl / lenL;
            var nlx = -uyl;
            var nly = uxl;
            var ax = centerX + nlx * clearance;
            var ay = pegStartY + nly * clearance;
            if (Math.Abs(uyl) > 1e-5)
            {
                var topX = ax + uxl * ((yBumperTop - ay) / uyl);
                var botX = ax + uxl * ((yBumperBot - ay) / uyl);
                AddWall(new Vec2(topX, yBumperTop), new Vec2(botX, yBumperBot), _bumperRestitution, false);
            }

            var vxr = lastR * w * 0.5;
            var vyr = lastR * rowDy;
            var lenR = Math.Sqrt(vxr * vxr + vyr * vyr);
            var uxr = vxr / lenR;
            var uyr = vyr / lenR;
            var nrx = uyr;
            var nry = -uxr;
            var arx = centerX + nrx * clearance;
            var ary = pegStartY + nry * clearance;
            if (Math.Abs(uyr) > 1e-5)
            {
                var topRx = arx + uxr * ((yBumperTop - ary) / uyr);
                var botRx = arx + uxr * ((yBumperBot - ary) / uyr);
                AddWall(new Vec2(topRx, yBumperTop), new Vec2(botRx, yBumperBot), _bumperRestitution, false);
            }
        }

        var funnelGuardInset = Math.Clamp(marginX * 0.35, 10, 36);
        AddWall(new Vec2(funnelGuardInset, 0), new Vec2(funnelGuardInset, funnelTopY + 40), _restitutionWall, false);
        AddWall(new Vec2(width - funnelGuardInset, 0), new Vec2(width - funnelGuardInset, funnelTopY + 40), _restitutionWall, false);

        AddWall(new Vec2(binLeft, binTopY), new Vec2(binLeft, _floorY), _restitutionWall, false);
        AddWall(new Vec2(binRight, binTopY), new Vec2(binRight, _floorY), _restitutionWall, false);

        for (var i = 1; i < numBins; i++)
        {
            var x = binLeft + i * binWidth;
            AddWall(new Vec2(x, binTopY), new Vec2(x, _floorY), _restitutionWall, false);
        }

        AddWall(new Vec2(binLeft, _floorY), new Vec2(binRight, _floorY), _restitutionWall, true);

        foreach (var m in _marbles)
        {
            if (m.Y > _floorY - m.Radius)
                m.Y = _floorY - m.Radius - 0.01;
        }
    }

    private void AddWall(Vec2 a, Vec2 b, double restitution, bool isFloor)
    {
        _walls.Add(new LayoutWall(a, b, restitution, isFloor));
        _segmentDraw.Add((a, b));
    }

    public void ClearMarbles() => _marbles.Clear();

    public bool TrySpawnMarble(double marbleRadius, int maxMarbles)
    {
        if (_width < 200 || _marbles.Count >= maxMarbles)
            return false;

        var centerX = _width * 0.5;
        var x = centerX + (_rng.NextDouble() * 2 - 1) * _spawnHalfWidth;
        var y = _spawnY;

        foreach (var other in _marbles)
        {
            var dx = x - other.X;
            var dy = y - other.Y;
            var minDist = marbleRadius + other.Radius;
            if (dx * dx + dy * dy < minDist * minDist)
                return false;
        }

        var color = Palette[_rng.Next(Palette.Length)];
        _marbles.Add(new Marble
        {
            X = x,
            Y = y,
            Vx = (_rng.NextDouble() - 0.5) * 14,
            Vy = _rng.NextDouble() * 14,
            Radius = marbleRadius,
            Color = color,
        });
        return true;
    }

    public void Update(double dt)
    {
        if (_width < 200)
            return;

        const int substeps = 6;
        var h = dt / substeps;
        for (var s = 0; s < substeps; s++)
            SubStep(h);
    }

    private void SubStep(double dt)
    {
        foreach (var m in _marbles)
        {
            m.Vy += _gravity * dt;
            m.X += m.Vx * dt;
            m.Y += m.Vy * dt;

            const double damping = 0.9992;
            m.Vx *= damping;
            m.Vy *= damping;
        }

        foreach (var m in _marbles)
        {
            ResolvePegCollisions(m);

            foreach (var wall in _walls)
                ResolveSegment(m, wall.A, wall.B, wall.Restitution, wall.IsFloor);
        }

        for (var i = 0; i < _marbles.Count; i++)
        {
            for (var j = i + 1; j < _marbles.Count; j++)
                ResolveMarblePair(_marbles[i], _marbles[j]);
        }

        foreach (var m in _marbles)
        {
            if (m.Y <= _floorY - m.Radius + 1)
                continue;

            m.Y = _floorY - m.Radius;
            if (m.Vy > 0)
                m.Vy *= -0.05;
            m.Vx *= 0.88;
            if (Math.Abs(m.Vx) < 8 && Math.Abs(m.Vy) < 25)
            {
                m.Vx = 0;
                m.Vy = 0;
            }
        }

        const double maxSpeed = 720;
        foreach (var m in _marbles)
        {
            var sp = Math.Sqrt(m.Vx * m.Vx + m.Vy * m.Vy);
            if (sp > maxSpeed)
            {
                var s = maxSpeed / sp;
                m.Vx *= s;
                m.Vy *= s;
            }
        }
    }

    private void ResolvePegCollisions(Marble m)
    {
        const int maxIters = 14;
        for (var iter = 0; iter < maxIters; iter++)
        {
            if (!TryResolveDeepestPeg(m))
                break;
        }
    }

    private bool TryResolveDeepestPeg(Marble m)
    {
        var bestPen = 0.0;
        Vec2 best = default;
        var minD = m.Radius + _pegRadius;
        var minDSq = minD * minD;

        foreach (var peg in _pegs)
        {
            var dx = m.X - peg.X;
            var dy = m.Y - peg.Y;
            var distSq = dx * dx + dy * dy;
            if (distSq >= minDSq || distSq < 1e-10)
                continue;

            var dist = Math.Sqrt(distSq);
            var pen = minD - dist;
            if (pen > bestPen)
            {
                bestPen = pen;
                best = peg;
            }
        }

        if (bestPen <= 0)
            return false;

        ResolveCircle(m, best.X, best.Y, _pegRadius, _restitutionPeg, pegTangentDamp: 0.88);
        return true;
    }

    private void ResolveMarblePair(Marble a, Marble b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var distSq = dx * dx + dy * dy;
        var minDist = a.Radius + b.Radius;
        if (distSq < 1e-8 || distSq > minDist * minDist)
            return;

        var dist = Math.Sqrt(distSq);
        var nx = dx / dist;
        var ny = dy / dist;
        var overlap = minDist - dist;

        a.X -= nx * (overlap * 0.5);
        a.Y -= ny * (overlap * 0.5);
        b.X += nx * (overlap * 0.5);
        b.Y += ny * (overlap * 0.5);

        var dvx = b.Vx - a.Vx;
        var dvy = b.Vy - a.Vy;
        var vn = dvx * nx + dvy * ny;
        if (vn >= 0)
            return;

        var e = _restitutionBall;
        var impulse = -(1 + e) * vn / 2.0;

        a.Vx -= impulse * nx;
        a.Vy -= impulse * ny;
        b.Vx += impulse * nx;
        b.Vy += impulse * ny;
    }

    private void ResolveCircle(Marble m, double cx, double cy, double radius, double restitution, double? pegTangentDamp = null)
    {
        var dx = m.X - cx;
        var dy = m.Y - cy;
        var distSq = dx * dx + dy * dy;
        var minDist = m.Radius + radius;
        if (distSq < 1e-10 || distSq >= minDist * minDist)
            return;

        var dist = Math.Sqrt(distSq);
        var nx = dx / dist;
        var ny = dy / dist;
        var overlap = minDist - dist;
        m.X += nx * overlap;
        m.Y += ny * overlap;

        var vn = m.Vx * nx + m.Vy * ny;
        if (vn >= 0)
            return;

        m.Vx -= (1 + restitution) * vn * nx;
        m.Vy -= (1 + restitution) * vn * ny;

        if (pegTangentDamp is { } damp && damp > 0)
        {
            var vDotN = m.Vx * nx + m.Vy * ny;
            var tx = m.Vx - vDotN * nx;
            var ty = m.Vy - vDotN * ny;
            m.Vx = vDotN * nx + tx * damp;
            m.Vy = vDotN * ny + ty * damp;
        }
    }

    private void ResolveSegment(Marble m, Vec2 a, Vec2 b, double restitution, bool isFloor)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var abLenSq = abx * abx + aby * aby;
        if (abLenSq < 1e-10)
            return;

        var t = ((m.X - a.X) * abx + (m.Y - a.Y) * aby) / abLenSq;
        t = Math.Clamp(t, 0, 1);
        var cx = a.X + t * abx;
        var cy = a.Y + t * aby;

        var dx = m.X - cx;
        var dy = m.Y - cy;
        var distSq = dx * dx + dy * dy;
        var minDist = m.Radius;
        if (distSq >= minDist * minDist || distSq < 1e-10)
            return;

        var dist = Math.Sqrt(distSq);
        var nx = dx / dist;
        var ny = dy / dist;
        m.X += nx * (minDist - dist);
        m.Y += ny * (minDist - dist);

        var vn = m.Vx * nx + m.Vy * ny;
        if (vn >= 0)
            return;

        m.Vx -= (1 + restitution) * vn * nx;
        m.Vy -= (1 + restitution) * vn * ny;

        if (isFloor && Math.Abs(ny) > 0.65)
        {
            m.Vx *= 0.86;
            if (Math.Abs(m.Vy) < 120)
                m.Vy *= 0.35;
        }
    }
}
