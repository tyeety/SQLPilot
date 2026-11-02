// Copyright (c) 2026 Arash Ghasemi Rad. All rights reserved.
// Licensed under the SqlPilot Source-Available Use License. See LICENSE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SqlPilot.Core;

namespace SqlPilot.Ssms
{
    internal sealed class MetadataHover : IDisposable
    {
        readonly IWpfTextView view;
        readonly Func<IEnumerable<DbObject>> catalog;
        readonly SqlPilot.UI.HoverCard card = new SqlPilot.UI.HoverCard();
        ITextSnapshot hoveredSnapshot;
        Span hoveredSpan;
        Rect target;
        Rect Viewport => new Rect(view.ViewportLeft, view.ViewportTop, view.ViewportWidth, view.ViewportHeight);
        internal MetadataHover(IWpfTextView view, Func<IEnumerable<DbObject>> catalog)
        {
            this.view = view;
            this.catalog = catalog;
            view.MouseHover += Hover;
            view.VisualElement.MouseLeave += Leave;
            view.VisualElement.MouseMove += Move;
            view.VisualElement.PreviewKeyDown += Key;
            view.TextBuffer.Changed += Changed;
            view.LostAggregateFocus += LostFocus;
            view.LayoutChanged += Layout;
        }
        void Hover(object sender, MouseHoverEventArgs args)
        {
            try
            {
                if (view.IsClosed || !view.HasAggregateFocus || Mouse.LeftButton != MouseButtonState.Released)
                    return;
                var snapshot = view.TextBuffer.CurrentSnapshot;
                if (card.IsOpen && hoveredSnapshot == snapshot && hoveredSpan.Contains(args.Position))
                {
                    card.TrackPointer(true);
                    return;
                }
                var description = Engine.DescribeAt(snapshot.GetText(), args.Position, catalog());
                if (description == null)
                {
                    Dismiss();
                    return;
                }
                var span = new SnapshotSpan(snapshot, description.Start, description.Length);
                var bounds = view.TextViewLines.GetNormalizedTextBounds(span).FirstOrDefault();
                if (bounds == null)
                {
                    Dismiss();
                    return;
                }
                target = new Rect(bounds.Left - view.ViewportLeft, bounds.Top - view.ViewportTop, Math.Max(2, bounds.Width), bounds.Height);
                hoveredSnapshot = snapshot;
                hoveredSpan = span.Span;
                var panel = new StackPanel { MaxWidth = 510, Resources = SqlPilot.UI.Design.Resources() };
                panel.Children.Add(new TextBlock { Text = description.Title, FontWeight = FontWeights.SemiBold, Foreground = SqlPilot.UI.Design.Accent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 7) });
                panel.Children.Add(new TextBlock { Text = description.Body, Foreground = SqlPilot.UI.Design.Ink, TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12 });
                card.Show(snapshot.Version.VersionNumber + ":" + description.Start + ":" + description.Length,
                    () => SqlPilot.UI.Design.Card(new ScrollViewer { Content = panel, MaxWidth = 510, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }),
                    view.VisualElement, target, Viewport);
            }
            catch (Exception ex) { Dismiss(); Diagnostics.Write("Metadata hover: " + ex.GetType().Name); }
        }
        void Move(object sender, MouseEventArgs e) => card.TrackPointer(target.Contains(e.GetPosition(view.VisualElement)));
        void Leave(object sender, MouseEventArgs e) => card.TrackPointer(false);
        void Key(object sender, KeyEventArgs e) => Dismiss();
        void Changed(object sender, TextContentChangedEventArgs e) => Dismiss();
        void LostFocus(object sender, EventArgs e)
        {
            if (!card.IsPointerOver)
                Dismiss();
        }
        void Layout(object sender, TextViewLayoutChangedEventArgs e) => card.Layout(Viewport);
        internal void Dismiss()
        {
            card.Dismiss();
            hoveredSnapshot = null;
        }
        public void Dispose()
        {
            card.Dispose();
            view.MouseHover -= Hover;
            view.VisualElement.MouseLeave -= Leave;
            view.VisualElement.MouseMove -= Move;
            view.VisualElement.PreviewKeyDown -= Key;
            view.TextBuffer.Changed -= Changed;
            view.LostAggregateFocus -= LostFocus;
            view.LayoutChanged -= Layout;
        }
    }
}
