using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace MinecraftLauncher.Helpers
{
    /// <summary>
    /// Эффект масштабирования и отклика при наведении/нажатии для карточек в Avalonia UI.
    /// </summary>
    public static class Card3DTiltHelper
    {
        public static readonly AttachedProperty<bool> Enable3DTiltProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>(
                "Enable3DTilt",
                typeof(Card3DTiltHelper),
                defaultValue: false);

        static Card3DTiltHelper()
        {
            Enable3DTiltProperty.Changed.AddClassHandler<Control>(OnEnable3DTiltChanged);
        }

        public static bool GetEnable3DTilt(Control obj) => obj.GetValue(Enable3DTiltProperty);
        public static void SetEnable3DTilt(Control obj, bool value) => obj.SetValue(Enable3DTiltProperty, value);

        private static void OnEnable3DTiltChanged(Control control, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                control.AttachedToVisualTree += OnAttached;
                control.PointerEntered += OnPointerEnter;
                control.PointerExited += OnPointerExit;
                control.PointerPressed += OnPointerDown;
                control.PointerReleased += OnPointerUp;
            }
            else
            {
                control.AttachedToVisualTree -= OnAttached;
                control.PointerEntered -= OnPointerEnter;
                control.PointerExited -= OnPointerExit;
                control.PointerPressed -= OnPointerDown;
                control.PointerReleased -= OnPointerUp;
            }
        }

        private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is Control c && c.RenderTransform is null)
            {
                c.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                c.RenderTransform = new ScaleTransform(1, 1);
            }
        }

        private static void OnPointerEnter(object? sender, PointerEventArgs e)
        {
            if (sender is Control c && c.RenderTransform is ScaleTransform s)
            {
                s.ScaleX = 1.015;
                s.ScaleY = 1.015;
            }
        }

        private static void OnPointerExit(object? sender, PointerEventArgs e)
        {
            if (sender is Control c && c.RenderTransform is ScaleTransform s)
            {
                s.ScaleX = 1.0;
                s.ScaleY = 1.0;
            }
        }

        private static void OnPointerDown(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control c && c.RenderTransform is ScaleTransform s)
            {
                s.ScaleX = 0.98;
                s.ScaleY = 0.98;
            }
        }

        private static void OnPointerUp(object? sender, PointerReleasedEventArgs e)
        {
            if (sender is Control c && c.RenderTransform is ScaleTransform s)
            {
                s.ScaleX = c.IsPointerOver ? 1.015 : 1.0;
                s.ScaleY = c.IsPointerOver ? 1.015 : 1.0;
            }
        }
    }
}
