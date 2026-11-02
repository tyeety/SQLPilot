// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

#nullable disable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
namespace SqlPilot.UI
{
    // Shared by the SSMS dialogs and the standalone installer. Frosted surfaces are local,
    // deterministic WPF visuals; no OS-wide transparency or theme changes are required.
    public static class Design
    {
        public static Brush Ink => new SolidColorBrush(Color.FromRgb(47, 31, 67));
        public static Brush Muted => new SolidColorBrush(Color.FromRgb(116, 93, 142));
        public static Brush Accent => new SolidColorBrush(Color.FromRgb(119, 63, 201));
        public static ResourceDictionary Resources() => (ResourceDictionary)XamlReader.Parse(@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
<Style TargetType='Button'><Setter Property='Background' Value='#F1EAFB'/><Setter Property='Foreground' Value='#2F1F43'/><Setter Property='Padding' Value='15,9'/><Setter Property='MinHeight' Value='36'/><Setter Property='BorderBrush' Value='#DECCE9'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='Shell' CornerRadius='9' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Shell' Property='Opacity' Value='0.82'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Shell' Property='BorderBrush' Value='#773FC9'/><Setter TargetName='Shell' Property='BorderThickness' Value='2'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType='TextBox'><Setter Property='Padding' Value='10,7'/><Setter Property='Background' Value='#FDF9FF'/><Setter Property='Foreground' Value='#2F1F43'/><Setter Property='BorderBrush' Value='#DCC6EB'/><Setter Property='BorderThickness' Value='1'/><Setter Property='MinHeight' Value='34'/><Setter Property='VerticalContentAlignment' Value='Center'/></Style>
<Style TargetType='ComboBox'><Setter Property='Padding' Value='8,6'/><Setter Property='MinHeight' Value='34'/></Style>
<Style TargetType='ListBox'><Setter Property='Background' Value='#BFFFFFFF'/><Setter Property='BorderBrush' Value='#DDCCEE'/><Setter Property='Padding' Value='4'/><Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/></Style>
<Style TargetType='ListBoxItem'><Setter Property='Padding' Value='8,6'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/></Style>
<Style TargetType='CheckBox'><Setter Property='Margin' Value='0,7,0,7'/><Setter Property='VerticalContentAlignment' Value='Center'/></Style>
</ResourceDictionary>");
        public static Border Card(UIElement content) => new Border { Child = content, Padding = new Thickness(20), CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromArgb(222, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)), BorderThickness = new Thickness(1), Effect = new DropShadowEffect { Color = Color.FromRgb(98, 57, 139), BlurRadius = 24, ShadowDepth = 5, Opacity = .10 } };
        public static Window Window(string title, UIElement content, double width = 920, double height = 660, string subtitle = null)
        {
            var window = new Window { Title = title, Width = width, Height = height, MinWidth = Math.Min(760, width), MinHeight = Math.Min(560, height), WindowStartupLocation = WindowStartupLocation.CenterScreen, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Foreground = Ink, FlowDirection = FlowDirection.LeftToRight };
            Apply(window, title, content, subtitle);
            return window;
        }
        public static void Apply(Window window, string heading, UIElement content, string subtitle = null)
        {
            window.Resources = Resources();
            window.Background = new LinearGradientBrush(Color.FromRgb(235, 221, 255), Color.FromRgb(248, 242, 255), 35);
            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            var header = new StackPanel { Margin = new Thickness(4, 0, 0, 18) };
            header.Children.Add(new TextBlock { Text = "SQLPILOT", Foreground = Accent, FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) });
            header.Children.Add(new TextBlock { Text = heading, FontSize = 25, FontWeight = FontWeights.SemiBold });
            if (subtitle != null)
                header.Children.Add(new TextBlock { Text = subtitle, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0) });
            root.Children.Add(header);
            var card = Card(content);
            Grid.SetRow(card, 1);
            root.Children.Add(card);
            window.Content = root;
        }
        public static Button Primary(string label)
        {
            return new Button { Content = label, Background = Accent, Foreground = Brushes.White, BorderBrush = Accent, IsDefault = true };
        }
    }
    // Shared popup lifecycle keeps editor hover stable without capturing the mouse.
    internal sealed class HoverCard : IDisposable
    {
        readonly System.Windows.Controls.Primitives.Popup popup = new System.Windows.Controls.Primitives.Popup
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
            StaysOpen = true,
            AllowsTransparency = true,
            Focusable = false
        };
        readonly System.Windows.Threading.DispatcherTimer leave = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        string identity;
        Rect viewport;
        bool onTarget;
        internal bool IsOpen => popup.IsOpen;
        internal bool IsPointerOver => popup.Child?.IsMouseOver == true;
        internal UIElement Content => popup.Child;
        internal HoverCard() => leave.Tick += LeaveTick;
        void LeaveTick(object sender, EventArgs e) => CloseIfOutside();
        internal bool Show(string key, Func<UIElement> build, UIElement anchor, Rect target, Rect currentViewport)
        {
            leave.Stop();
            onTarget = true;
            if (popup.IsOpen && identity == key && popup.PlacementTarget == anchor)
                return false;
            Dismiss();
            onTarget = true;
            var content = build();
            content.MouseEnter += EnterCard;
            content.MouseLeave += LeaveCard;
            content.PreviewKeyDown += Key;
            popup.Child = content;
            popup.PlacementTarget = anchor;
            popup.HorizontalOffset = Math.Max(0, Math.Min(target.Left, currentViewport.Width - 540));
            popup.VerticalOffset = target.Bottom + 4;
            viewport = currentViewport;
            identity = key;
            popup.IsOpen = true;
            return true;
        }
        void Key(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Dismiss();
                e.Handled = true;
            }
        }
        void EnterCard(object sender, System.Windows.Input.MouseEventArgs e) => leave.Stop();
        void LeaveCard(object sender, System.Windows.Input.MouseEventArgs e) => TrackPointer(false);
        internal void TrackPointer(bool overTarget)
        {
            onTarget = overTarget;
            if (overTarget || IsPointerOver)
                leave.Stop();
            else if (popup.IsOpen && !leave.IsEnabled)
                leave.Start();
        }
        internal void CloseIfOutside()
        {
            leave.Stop();
            if (!onTarget && !IsPointerOver)
                Dismiss();
        }
        internal void Layout(Rect currentViewport)
        {
            if (popup.IsOpen && viewport != currentViewport)
                Dismiss();
        }
        internal void Dismiss()
        {
            leave.Stop();
            popup.IsOpen = false;
            identity = null;
            onTarget = false;
            if (popup.Child != null)
            {
                popup.Child.MouseEnter -= EnterCard;
                popup.Child.MouseLeave -= LeaveCard;
                popup.Child.PreviewKeyDown -= Key;
                popup.Child = null;
            }
        }
        public void Dispose()
        {
            Dismiss();
            leave.Tick -= LeaveTick;
            popup.PlacementTarget = null;
        }
    }

}
