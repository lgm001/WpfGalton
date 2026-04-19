using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WpfGalton;

public partial class MainWindow : Window
{
    private readonly GaltonSimulation _world = new();
    private readonly List<Ellipse> _marbleShapes = new();
    private DispatcherTimer? _spawnTimer;
    private TimeSpan _lastRender = TimeSpan.Zero;
    private bool _renderingHooked;
    private double _marbleRadius = 8;
    private const int MaxMarbles = 160;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RebuildScene();

        _spawnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(95) };
        _spawnTimer.Tick += (_, _) => TrySpawn();
        _spawnTimer.Start();

        CompositionTarget.Rendering += OnRendering;
        _renderingHooked = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_renderingHooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
        }

        _spawnTimer?.Stop();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || e.NewSize.Width < 100 || e.NewSize.Height < 100)
            return;

        ClearMarbleVisuals();
        _world.ClearMarbles();
        RebuildScene();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void RebuildScene()
    {
        Board.Children.Clear();
        _marbleShapes.Clear();

        _world.Resize(ActualWidth, ActualHeight);
        _marbleRadius = Math.Clamp(Math.Min(ActualWidth, ActualHeight) * 0.009, 5.5, 11);

        foreach (var peg in _world.Pegs)
        {
            var dot = new Ellipse
            {
                Width = _world.PegRadius * 2,
                Height = _world.PegRadius * 2,
                Fill = new SolidColorBrush(Color.FromArgb(200, 210, 220, 240)),
                Stroke = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                StrokeThickness = 0.6,
            };
            Canvas.SetLeft(dot, peg.X - _world.PegRadius);
            Canvas.SetTop(dot, peg.Y - _world.PegRadius);
            Board.Children.Add(dot);
        }

        foreach (var seg in _world.Segments)
        {
            var line = new Line
            {
                X1 = seg.A.X,
                Y1 = seg.A.Y,
                X2 = seg.B.X,
                Y2 = seg.B.Y,
                Stroke = new SolidColorBrush(Color.FromArgb(140, 180, 190, 220)),
                StrokeThickness = 2.2,
                StrokeEndLineCap = PenLineCap.Round,
            };
            Board.Children.Add(line);
        }
    }

    private void TrySpawn()
    {
        if (!_world.TrySpawnMarble(_marbleRadius, MaxMarbles))
            return;

        var m = _world.Marbles[^1];
        var marble = CreateMarbleShape(m);
        Board.Children.Add(marble);
        _marbleShapes.Add(marble);
    }

    private static Ellipse CreateMarbleShape(Marble m)
    {
        var d = m.Radius * 2;
        var fill = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.35, 0.35),
            Center = new Point(0.45, 0.45),
            RadiusX = 0.9,
            RadiusY = 0.9,
        };
        fill.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0));
        fill.GradientStops.Add(new GradientStop(m.Color, 0.35));
        fill.GradientStops.Add(new GradientStop(Darken(m.Color, 0.55), 1));

        return new Ellipse
        {
            Width = d,
            Height = d,
            Fill = fill,
            Stroke = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            StrokeThickness = 0.7,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
    }

    private static Color Darken(Color c, double factor)
    {
        static byte Scale(byte v, double f) => (byte)Math.Clamp(v * f, 0, 255);
        return Color.FromRgb(Scale(c.R, factor), Scale(c.G, factor), Scale(c.B, factor));
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args)
            return;

        if (_lastRender == TimeSpan.Zero)
        {
            _lastRender = args.RenderingTime;
            return;
        }

        var dt = (args.RenderingTime - _lastRender).TotalSeconds;
        _lastRender = args.RenderingTime;
        if (dt <= 0 || dt > 0.12)
            dt = 1.0 / 60.0;

        _world.Update(dt);

        var marbles = _world.Marbles;
        for (var i = 0; i < marbles.Count && i < _marbleShapes.Count; i++)
        {
            var m = marbles[i];
            var el = _marbleShapes[i];
            Canvas.SetLeft(el, m.X - m.Radius);
            Canvas.SetTop(el, m.Y - m.Radius);

            var spinDegrees = (m.Vx + m.Vy * 0.12) * dt * 0.09;
            if (el.RenderTransform is not RotateTransform rt)
            {
                rt = new RotateTransform();
                el.RenderTransform = rt;
            }

            rt.Angle += spinDegrees;
        }
    }

    private void ClearMarbleVisuals()
    {
        foreach (var el in _marbleShapes)
            Board.Children.Remove(el);
        _marbleShapes.Clear();
    }
}
