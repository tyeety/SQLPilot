using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SqlPilot.Ssms;
class Program
{
    [STAThread]
    static void Main()
    {
        int n = 0;
        Action<bool, string> check = (ok, label) => { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); n++; };
        string root = Path.Combine(Path.GetTempPath(), "SqlPilot.WorkspaceTests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "session.bin");
        var tabs = new[] { new SessionTab { Title = "one.sql", Path = "Query1.sql", Sql = "SELECT N'تست';\r\n-- kept\r\n", Connection = "Server=test;Database=one;User Id=u;Password=FAKE-FIXTURE-ONLY", Line = 2, Column = 4, Active = true, Dirty = true }, new SessionTab { Title = "two.sql", Sql = "SELECT 2;", Connection = "Server=test;Database=two;Integrated Security=true" } };
        SessionFiles.Save(path, tabs);
        var loaded = SessionFiles.Load(path);
        check(loaded.Count == 2 && loaded[0].Sql == tabs[0].Sql && loaded[0].Connection == tabs[0].Connection, "DPAPI session exact Unicode SQL and connection roundtrip");
        check(loaded[0].Line == 2 && loaded[0].Column == 4 && loaded[0].Active && loaded[0].Dirty && loaded[1].Title == "two.sql", "tab order caret and active state preserved");
        check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("FAKE-FIXTURE-ONLY"), "session file does not expose credentials as plain text");
        SessionFiles.Save(path, new SessionTab[0]);
        check(SessionFiles.Load(path).Count == 0 && SessionFiles.Load(path + ".bak").Count == 2, "empty session persists and prior checkpoint is recoverable");
        File.WriteAllText(path, "corrupt");
        bool rejected = false;
        try
        {
            SessionFiles.Load(path);
        }
        catch (System.Security.Cryptography.CryptographicException) { rejected = true; }
        check(rejected, "corrupt encrypted session fails safely");
        KeyboardShortcuts.Validate(KeyboardShortcuts.Defaults);
        check(true, "all default command gestures parse");
        var keys = new Dictionary<string, string>(KeyboardShortcuts.Defaults);
        keys["Format"] = "Ctrl+Alt+F";
        KeyboardShortcuts.Validate(keys);
        check(true, "custom command gesture supported");
        keys["Format"] = "Ctrl+Shift+A";
        rejected = false;
        try
        {
            KeyboardShortcuts.Validate(keys);
        }
        catch (ArgumentException) { rejected = true; }
        check(rejected, "duplicate command gestures rejected");
        keys["Format"] = "Ctrl+V";
        rejected = false;
        try
        {
            KeyboardShortcuts.Validate(keys);
        }
        catch (ArgumentException) { rejected = true; }
        check(rejected, "editor clipboard gesture protected");
        check(SqlPilot.UI.Design.Resources().Count >= 6, "shared glass design resources parse on Framework WPF");
        check(KeyboardShortcuts.MatchHeld(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, k => k == System.Windows.Input.Key.F) == "Format", "shell accelerator maps Ctrl Shift F to Format");
        check(KeyboardShortcuts.MatchHeld(System.Windows.Input.ModifierKeys.Control, k => k == System.Windows.Input.Key.F) == null, "ordinary Find accelerator is not hijacked");
        check(KeyboardShortcuts.MatchHeld(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, k => k == System.Windows.Input.Key.A) == "Analyze", "shell accelerator preserves distinct command mappings");
        check(KeyboardShortcuts.MatchHeld(System.Windows.Input.ModifierKeys.Control, k => k == System.Windows.Input.Key.OemPeriod) == "QuickFix", "shell punctuation shortcut matches normalized WPF key");
        check(KeyboardShortcuts.MatchHeld(System.Windows.Input.ModifierKeys.Alt, k => k == System.Windows.Input.Key.Enter) == "QuickFixAlt", "Alt Enter maps to diagnostic actions");
        check(!KeyboardShortcuts.Defaults.Keys.Any(k => k.StartsWith("Codex")), "retired AI commands are not registered");
        check(KeyboardShortcuts.MatchHeld(System.Windows.Input.ModifierKeys.Control, k => k == System.Windows.Input.Key.Enter) == "ExecuteStatement", "Ctrl Enter routes to current statement execution");
        string keyboardFixture = Path.Combine(root, "keyboard.xml");
        File.WriteAllText(keyboardFixture, "<KeyboardShortcuts><Binding Command='History' Gesture='Control+Return'/><Binding Command='Format' Gesture='Ctrl+Alt+F'/></KeyboardShortcuts>");
        var migratedKeys = KeyboardShortcuts.Load(keyboardFixture);
        check(migratedKeys["ExecuteStatement"] == "" && migratedKeys["History"] == "Control+Return" && migratedKeys["Format"] == "Ctrl+Alt+F", "Existing normalized Ctrl Enter binding wins without resetting other custom shortcuts");
        File.WriteAllText(keyboardFixture, "<KeyboardShortcuts><Binding Command='ExecuteStatement' Gesture='Ctrl+Shift+E'/></KeyboardShortcuts>");
        check(KeyboardShortcuts.Load(keyboardFixture)["ExecuteStatement"] == "Ctrl+Shift+E", "Explicit execution shortcut survives loading");
        File.WriteAllText(keyboardFixture, "<KeyboardShortcuts><Binding Command='ExecuteStatement' Gesture=''/></KeyboardShortcuts>");
        check(KeyboardShortcuts.Load(keyboardFixture)["ExecuteStatement"] == "", "Execution shortcut can be disabled persistently");
        string libraryId = Guid.NewGuid().ToString("N");
        string libraryId2 = Guid.NewGuid().ToString("N");
        var libraryKeys = KeyboardShortcuts.WithLibraryShortcut(libraryId, "Ctrl+Alt+1", new Dictionary<string, string>(KeyboardShortcuts.Defaults));
        check(libraryKeys[KeyboardShortcuts.LibraryCommand(libraryId)] == "Ctrl+Alt+1", "Library query receives an independent validated key binding");
        KeyboardShortcuts.Save(libraryKeys, keyboardFixture, false);
        check(KeyboardShortcuts.Load(keyboardFixture)[KeyboardShortcuts.LibraryCommand(libraryId)] == "Ctrl+Alt+1", "Library key bindings persist and reload without changing real user settings");
        bool duplicateLibrary = false;
        try
        {
            KeyboardShortcuts.WithLibraryShortcut(libraryId2, "Control+Alt+D1", libraryKeys);
        }
        catch (ArgumentException) { duplicateLibrary = true; }
        check(duplicateLibrary, "Duplicate normalized library shortcuts are rejected");
        bool reservedLibrary = false;
        try
        {
            KeyboardShortcuts.WithLibraryShortcut(libraryId, "Ctrl+Enter", libraryKeys);
        }
        catch (ArgumentException) { reservedLibrary = true; }
        check(reservedLibrary, "Library shortcut cannot override current-statement execution");
        check(KeyboardShortcuts.WithLibraryShortcut(libraryId, "", libraryKeys)[KeyboardShortcuts.LibraryCommand(libraryId)] == "", "Library shortcut can be removed without deleting its query");
        check(!KeyboardShortcuts.IsLibraryCommand("Library:../../file") && KeyboardShortcuts.IsLibraryCommand(KeyboardShortcuts.LibraryCommand(libraryId)), "Library shortcut accepts only stable query IDs");
        string diagnosticLog = Path.Combine(root, "extension.log");
        for (int i = 0; i < 40; i++)
            SqlPilot.BoundedLog.Append(diagnosticLog, new string('a', 12), 64);
        check(new FileInfo(diagnosticLog).Length <= 64 && new FileInfo(diagnosticLog + ".1").Length <= 64, "diagnostic log and rotated copy stay bounded");
        File.WriteAllText(diagnosticLog, new string('a', 1000));
        SqlPilot.BoundedLog.Append(diagnosticLog, "fresh", 64);
        check(new FileInfo(diagnosticLog).Length <= 64 && !File.Exists(diagnosticLog + ".1"), "oversized legacy diagnostic log is discarded");
        SqlPilot.BoundedLog.Append(diagnosticLog, new string('ژ', 1000), 64);
        check(new FileInfo(diagnosticLog).Length <= 64, "large Unicode diagnostic entry stays bounded");
        AboutChecks.Run(check);
        ExplorerChecks.Run(check);
        LibraryChecks.Run(check);
        UpdateChecks.Run(check);
        CheckHoverCard(check);
        Console.WriteLine(n + " workspace checks passed.");
    }
    static void CheckHoverCard(Action<bool, string> check)
    {
        Action<int> pump = milliseconds =>
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var stop = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            stop.Tick += (_, __) => { stop.Stop(); frame.Continue = false; };
            stop.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        };
        var anchor = new System.Windows.Controls.Border();
        var window = new System.Windows.Window { Content = anchor, ShowActivated = false, WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -30000, Top = -30000, Width = 320, Height = 180 };
        var card = new SqlPilot.UI.HoverCard();
        int builds = 0, loaded = 0, unloaded = 0;
        Func<System.Windows.UIElement> build = () =>
        {
            builds++;
            var border = new System.Windows.Controls.Border { Child = new System.Windows.Controls.TextBlock { Text = "Fixture column metadata" } };
            border.Loaded += (_, __) => loaded++;
            border.Unloaded += (_, __) => unloaded++;
            return border;
        };
        var viewport = new System.Windows.Rect(0, 0, 320, 180);
        var target = new System.Windows.Rect(15, 15, 80, 18);
        try
        {
            window.Show();
            card.Show("column:one", build, anchor, target, viewport);
            pump(40);
            check(card.IsOpen && builds == 1 && loaded == 1, "Actual WPF metadata popup opens once");
            var content = card.Content;
            for (int i = 0; i < 100; i++)
            {
                card.Show("column:one", build, anchor, target, viewport);
                card.Layout(viewport);
            }
            pump(40);
            check(card.IsOpen && builds == 1 && loaded == 1 && unloaded == 0 && ReferenceEquals(content, card.Content), "Repeated hover and unchanged layouts preserve the same live popup without unload/reopen");
            card.TrackPointer(false);
            pump(60);
            check(card.IsOpen, "Crossing the short gap to the card does not immediately close it");
            card.TrackPointer(true);
            pump(300);
            check(card.IsOpen && builds == 1, "Returning to the target cancels pending dismissal without rebuilding");
            card.TrackPointer(false);
            pump(320);
            check(!card.IsOpen, "Leaving both target and metadata card dismisses after the grace period");
            card.Show("column:two", build, anchor, target, viewport);
            pump(30);
            check(card.IsOpen && builds == 2, "Hovering a different column opens its new content");
            card.Layout(new System.Windows.Rect(0, 20, 320, 180));
            check(!card.IsOpen, "Actual scrolling dismisses stale metadata instead of leaving it at an old location");
            card.Show("column:two", build, anchor, target, viewport);
            card.Dismiss();
            check(!card.IsOpen && card.Content == null, "Typing, connection refresh or explicit dismissal releases the metadata content");
            card.Show("column:two", build, anchor, target, viewport);
            var escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, System.Windows.PresentationSource.FromVisual(window), Environment.TickCount, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            card.Content.RaiseEvent(escape);
            check(!card.IsOpen && escape.Handled, "Escape also dismisses a metadata card when its own content receives keyboard input");
            card.Show("column:two", build, anchor, target, viewport);
            card.TrackPointer(false);
            card.Dispose();
            pump(300);
            check(!card.IsOpen && card.Content == null, "Disposal cancels delayed hover callbacks and clears popup content");
        }
        finally { card.Dispose(); window.Close(); }
    }

}
namespace SqlPilot.Ssms
{
    internal static class Brand
    {
        internal static System.Windows.Media.Imaging.BitmapImage Icon() => null;
    }
    internal static class Notifications
    {
        internal static void Show(string message)
        {
        }
    }
    internal static class Diagnostics
    {
        internal static void Write(string message)
        {
        }
    }
}
