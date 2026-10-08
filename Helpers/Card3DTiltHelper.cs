using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MinecraftLauncher.Helpers
{
    /// <summary>
    /// Эффект 3D-наклона и глянцевого блика для карточек.
    ///
    /// Две проблемы в прежней версии:
    ///  1. States был обычным словарём с сильными ссылками на FrameworkElement.
    ///     Запись удалялась только в ветке, где Enable3DTilt явно ставят в False,
    ///     а в XAML его один раз ставят в True и больше не меняют. В итоге каждая
    ///     загруженная карточка жила до конца процесса вместе со своим визуальным
    ///     деревом, биндингами и ViewModel.
    ///  2. CompositionTarget.Rendering подписывался навсегда и каждый кадр
    ///     обходил весь словарь, включая уже выгруженные элементы.
    ///
    /// Здесь словарь держит слабые ссылки и чистится по Unloaded, а хук рендеринга
    /// снимается, когда активных элементов не осталось.
    /// </summary>
    public static class Card3DTiltHelper
    {
        public static readonly DependencyProperty Enable3DTiltProperty =
            DependencyProperty.RegisterAttached(
                "Enable3DTilt",
                typeof(bool),
                typeof(Card3DTiltHelper),
                new PropertyMetadata(false, OnEnable3DTiltChanged));

        public static bool GetEnable3DTilt(DependencyObject obj) => (bool)obj.GetValue(Enable3DTiltProperty);
        public static void SetEnable3DTilt(DependencyObject obj, bool value) => obj.SetValue(Enable3DTiltProperty, value);

        private class ElementGlowState
        {
            public bool IsHovered;
            public double TargetGlowOpacity;
            public double CurrentGlowOpacity;
            public double TargetScale = 1.0;
            public double CurrentScale = 1.0;
            public RadialGradientBrush? LightGlow;
            public Point TargetGlowCenter;
            public Point CurrentGlowCenter;
        }

        private sealed class Entry
        {
            public WeakReference<FrameworkElement> Element { get; }
            public readonly ElementGlowState State = new();

            public Entry(FrameworkElement element) => Element = new WeakReference<FrameworkElement>(element);
        }

        private static readonly List<Entry> States = new();
        private static readonly object _gate = new();
        private static bool _isHookedRendering;
        private static int _activeCount;

        private static void OnEnable3DTiltChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element) return;

            if ((bool)e.NewValue)
            {
                element.Loaded += Element_Loaded;
                element.Unloaded += Element_Unloaded;
                element.MouseMove += Element_MouseMove;
                element.MouseEnter += Element_MouseEnter;
                element.MouseLeave += Element_MouseLeave;
                element.PreviewMouseDown += Element_MouseDown;
                element.PreviewMouseUp += Element_MouseUp;

                lock (_gate) { _activeCount++; }
                EnsureRenderingHook();
            }
            else
            {
                element.Loaded -= Element_Loaded;
                element.Unloaded -= Element_Unloaded;
                element.MouseMove -= Element_MouseMove;
                element.MouseEnter -= Element_MouseEnter;
                element.MouseLeave -= Element_MouseLeave;
                element.PreviewMouseDown -= Element_MouseDown;
                element.PreviewMouseUp -= Element_MouseUp;

                RemoveElement(element);

                lock (_gate)
                {
                    _activeCount = Math.Max(0, _activeCount - 1);
                    if (_activeCount == 0) ReleaseRenderingHook();
                }
            }
        }

        private static void EnsureRenderingHook()
        {
            if (_isHookedRendering) return;

            CompositionTarget.Rendering += OnRendering;
            _isHookedRendering = true;
        }

        private static void ReleaseRenderingHook()
        {
            if (!_isHookedRendering) return;

            CompositionTarget.Rendering -= OnRendering;
            _isHookedRendering = false;
        }

        private static Entry GetOrAddState(FrameworkElement element)
        {
            lock (_gate)
            {
                foreach (var entry in States)
                {
                    if (entry.Element.TryGetTarget(out var existing) && ReferenceEquals(existing, element))
                    {
                        return entry;
                    }
                }

                var created = new Entry(element);
                States.Add(created);
                return created;
            }
        }

        private static ElementGlowState? FindState(FrameworkElement element)
        {
            lock (_gate)
            {
                foreach (var entry in States)
                {
                    if (entry.Element.TryGetTarget(out var existing) && ReferenceEquals(existing, element))
                    {
                        return entry.State;
                    }
                }
            }

            return null;
        }

        private static void RemoveElement(FrameworkElement element)
        {
            lock (_gate)
            {
                States.RemoveAll(entry =>
                {
                    if (!entry.Element.TryGetTarget(out var existing)) return true;
                    return ReferenceEquals(existing, element);
                });
            }
        }

        private static void Element_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element) return;

            var entry = GetOrAddState(element);
            var state = entry.State;

            // Раньше XAML-объявленный RenderTransform молча заменялся на ScaleTransform.
            // Теперь используем наш, но только если в разметке не задано ничего
            // (иначе анимация из XAML потерялась бы).
            if (element.RenderTransform is null)
            {
                element.RenderTransformOrigin = new Point(0.5, 0.5);
                element.RenderTransform = new ScaleTransform(1, 1);
            }

            if (element is Border border && border.Child is Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is Border b && b.Name == "__GlossGlowLayer__") return;
                }

                var glowLayer = new Border
                {
                    Name = "__GlossGlowLayer__",
                    IsHitTestVisible = false,
                    CornerRadius = border.CornerRadius,
                    Margin = new Thickness(-border.Padding.Left, -border.Padding.Top, -border.Padding.Right, -border.Padding.Bottom),
                    Opacity = 0.0
                };

                var radialBrush = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.5),
                    GradientOrigin = new Point(0.5, 0.5),
                    RadiusX = 1.4,
                    RadiusY = 1.4
                };

                radialBrush.GradientStops.Add(new GradientStop(Color.FromArgb(75, 255, 255, 255), 0.0));
                radialBrush.GradientStops.Add(new GradientStop(Color.FromArgb(25, 255, 255, 255), 0.55));
                radialBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0));

                glowLayer.Background = radialBrush;
                state.LightGlow = radialBrush;

                Grid.SetRowSpan(glowLayer, 99);
                Grid.SetColumnSpan(glowLayer, 99);
                grid.Children.Add(glowLayer);
            }
        }

        /// <summary>
        /// Элемент выгружен из визуального дерева — убираем его состояние.
        /// Раньше этого события не было вовсе, поэтому навигация между страницами
        ///Mods/Modpacks постоянно увеличивала утечку.
        /// </summary>
        private static void Element_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                RemoveElement(element);
            }
        }

        private static void Element_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element && FindState(element) is { } state)
            {
                state.IsHovered = true;
                state.TargetGlowOpacity = 1.0;
                state.TargetScale = 1.015;
            }
        }

        private static void Element_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element && FindState(element) is { } state)
            {
                double width = element.ActualWidth;
                double height = element.ActualHeight;
                if (width <= 0 || height <= 0) return;

                Point pos = e.GetPosition(element);
                state.TargetGlowCenter = new Point(pos.X / width, pos.Y / height);
            }
        }

        private static void Element_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element && FindState(element) is { } state)
            {
                state.IsHovered = false;
                state.TargetGlowOpacity = 0.0;
                state.TargetScale = 1.0;
            }
        }

        private static void Element_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && FindState(element) is { } state)
            {
                state.TargetScale = 0.98;
            }
        }

        private static void Element_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && FindState(element) is { } state && state.IsHovered)
            {
                state.TargetScale = 1.015;
            }
        }

        private static void OnRendering(object? sender, EventArgs e)
        {
            // Снимок списка под локом: элементы добавляются и удаляются
            // во время обхода, особенно после добавления наших обработчиков.
            Entry[] snapshot;
            lock (_gate)
            {
                snapshot = States.ToArray();
            }

            foreach (var entry in snapshot)
            {
                // Слабая ссылка могла сброситься, пока обход шёл.
                if (!entry.Element.TryGetTarget(out var element)) continue;
                if (!element.IsLoaded) continue;

                var state = entry.State;

                const double smoothFactor = 0.15;
                state.CurrentScale += (state.TargetScale - state.CurrentScale) * smoothFactor;
                state.CurrentGlowOpacity += (state.TargetGlowOpacity - state.CurrentGlowOpacity) * smoothFactor;

                state.CurrentGlowCenter = new Point(
                    state.CurrentGlowCenter.X + (state.TargetGlowCenter.X - state.CurrentGlowCenter.X) * smoothFactor,
                    state.CurrentGlowCenter.Y + (state.TargetGlowCenter.Y - state.CurrentGlowCenter.Y) * smoothFactor
                );

                if (element.RenderTransform is ScaleTransform scale)
                {
                    scale.ScaleX = state.CurrentScale;
                    scale.ScaleY = state.CurrentScale;
                }

                if (state.LightGlow != null)
                {
                    state.LightGlow.Center = state.CurrentGlowCenter;
                    state.LightGlow.GradientOrigin = state.CurrentGlowCenter;
                }

                if (element is Border border && border.Child is Grid grid)
                {
                    foreach (var child in grid.Children)
                    {
                        if (child is Border glowLayer && glowLayer.Name == "__GlossGlowLayer__")
                        {
                            glowLayer.Opacity = state.CurrentGlowOpacity;
                            break;
                        }
                    }
                }
            }
        }
    }
}