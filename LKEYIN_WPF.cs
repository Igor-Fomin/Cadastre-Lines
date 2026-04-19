#pragma warning disable CA1416 // Suppress Audio warnings
#pragma warning disable CS8618 // Suppress Non-nullable field warnings
#pragma warning disable CS8600 // Suppress Null conversion warnings
#pragma warning disable CS8601 // Suppress Null assignment warnings

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Xml.Serialization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Data;
using System.Text.RegularExpressions;

// AutoCAD Namespaces
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcColor = Autodesk.AutoCAD.Colors.Color;

[assembly: CommandClass(typeof(CadastreTools.LKeyinCommand))]

namespace CadastreTools;

#region 1. GLOBAL CONSTANTS
public static class CadConstants
{
    public const string BDY_BEARING = "BDY_BEARING";
    public const string BDY_DISTANCE = "BDY_DISTANCE";
    public const string CONNECTION_BEAR = "CONNECTION_BEAR";
    public const string CONNECTION_DIST = "CONNECTION_DIST";
    public const string SYMB_TEXT = "SYMB TEXT";
    public const string POINT_NUMBER = "POINT_NUMBER";
    public const string VAR_PT_COUNTER = "CADASTRE_PT_NUM";
}
#endregion

#region 2. SETTINGS
public class TextSettings
{
    public string Style { get; set; } = "Standard";
    public double Size { get; set; } = 1.0;
    public bool IsMText { get; set; } = false;
    public bool Masking { get; set; } = false;
    public short ColorIndex { get; set; } = 256; // Forced ByLayer
    public bool Visible { get; set; } = true;

    public void Reset(short unused)
    {
        Style = "Standard";
        Size = 1.0;
        IsMText = false;
        Masking = false;
        ColorIndex = 256; // Forced ByLayer
        Visible = true;
    }
}

public class AppSettings
{
    public bool AudioFeedback { get; set; } = true;
    public string AudioSound { get; set; } = "Asterisk";
    public double SnapTolerance { get; set; } = 0.005;

    public TextSettings TextBrg { get; set; } = new TextSettings();
    public TextSettings TextDist { get; set; } = new TextSettings();
    public TextSettings TextPt { get; set; } = new TextSettings();
    public TextSettings TextComm { get; set; } = new TextSettings();

    public static void Save(AppSettings settings)
    {
        try
        {
            string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CadastreTools");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string path = System.IO.Path.Combine(folder, "CadastreKeyinSettings.xml");
            XmlSerializer xs = new XmlSerializer(typeof(AppSettings));
            using (StreamWriter wr = new StreamWriter(path)) { xs.Serialize(wr, settings); }
        }
        catch (System.Exception ex) { MessageBox.Show("Error saving settings: " + ex.Message); }
    }

    public static AppSettings Load()
    {
        try
        {
            string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CadastreTools", "CadastreKeyinSettings.xml");
            if (File.Exists(path))
            {
                XmlSerializer xs = new XmlSerializer(typeof(AppSettings));
                using (StreamReader rd = new StreamReader(path)) { return (AppSettings)xs.Deserialize(rd) ?? new AppSettings(); }
            }
        }
        catch { }
        return new AppSettings();
    }

    public void ResetText()
    {
        TextBrg.Reset(256);
        TextDist.Reset(256);
        TextPt.Reset(256);
        TextComm.Reset(256);
    }
}
#endregion

#region 3. UI THEME
public static class UITheme
{
    public static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    public static readonly Brush CardBrush = new SolidColorBrush(Color.FromRgb(45, 45, 48));
    public static readonly Brush InputBackground = new SolidColorBrush(Color.FromRgb(20, 20, 20));
    public static readonly Brush AccentColor = new SolidColorBrush(Color.FromRgb(0, 122, 204));
    public static readonly Brush ActionBlue = new SolidColorBrush(Color.FromRgb(41, 128, 185));
    public static readonly Brush GuideColor = new SolidColorBrush(Color.FromRgb(0, 200, 0));
    
    private static readonly DropShadowEffect CardShadow = new DropShadowEffect() { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.4 };
    private static readonly FontFamily MonoFont = new FontFamily("Consolas");

    public static Border CreateCard() { return new Border() { Background = CardBrush, CornerRadius = new CornerRadius(4), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10), Effect = CardShadow }; }
    public static TextBox CreateInputBox() { return new TextBox() { Background = InputBackground, Foreground = Brushes.Cyan, FontFamily = MonoFont, FontSize = 16, Height = 45, VerticalContentAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, Padding = new Thickness(5), CaretBrush = Brushes.White }; }
    public static ComboBox CreateLayerCombo() { return new ComboBox() { Height = 30, Margin = new Thickness(2), IsEditable = true, Foreground = Brushes.Black, FontSize = 12 }; }
    public static Label CreateLabel(string text) { return new Label() { Content = text, Foreground = Brushes.LightGray, FontSize = 11, FontWeight = FontWeights.Bold, Padding = new Thickness(0, 5, 0, 2) }; }
    public static TextBlock CreateFooterText(string text, Brush color) { return new TextBlock() { Text = text, Foreground = color, FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(2) }; }

    public static Button CreateLayerBtn(string key) { return new Button() { Content = key, Height = 55, Margin = new Thickness(3), FontWeight = FontWeights.Bold, FontSize = 14, BorderThickness = new Thickness(0), Foreground = Brushes.White }; }
    public static CheckBox CreateToggle(string text) { return new CheckBox() { Content = text, Foreground = Brushes.White, Margin = new Thickness(5), FontSize = 14 }; }
    public static Button CreateActionBtn(string text, Brush bg) { return new Button() { Content = text, Height = 35, Background = bg, Foreground = Brushes.White, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 5, 0, 5), HorizontalAlignment = HorizontalAlignment.Stretch }; }

    public static UIElement CreateShortcutContent(string key, string description)
    {
        TextBlock tb = new TextBlock() { TextAlignment = System.Windows.TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, LineHeight = 12, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        tb.Inlines.Add(new Run(key) { FontSize = 10, FontWeight = FontWeights.Bold });
        tb.Inlines.Add(new LineBreak());
        tb.Inlines.Add(new Run(description) { FontSize = 13 });
        return tb;
    }

    public static Button CreateColorBtn(short colorIndex)
    {
        Button b = new Button() { Width = 40, Height = 25, Margin = new Thickness(2) };
        if (colorIndex == 256) b.Content = "ByL";
        else if (colorIndex == 0) b.Content = "ByB";
        else b.Background = new SolidColorBrush(GetWpfColor(colorIndex));
        return b;
    }
    public static Color GetWpfColor(short index) { try { var acCol = AcColor.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, index); return Color.FromRgb(acCol.ColorValue.R, acCol.ColorValue.G, acCol.ColorValue.B); } catch { return Colors.Gray; } }
}
#endregion

#region 4. COMMAND & MATH
public class LKeyinCommand
{
    static CadastreWpfWindow? _palette = null;
    [CommandMethod("LKY", CommandFlags.Session)]
    public void RunLKeyin()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        if (_palette == null || !_palette.IsLoaded) { _palette = new CadastreWpfWindow(doc); AcApp.ShowModelessWindow(_palette); }
        else { _palette.Show(); _palette.Activate(); }
    }
}

public static class CadMath
{
    public static double ParseDmsToDegrees(double rawInput)
    {
        // rawInput is in DDD.MMSS format (e.g. 123.4506)
        int d = (int)rawInput;
        double ms = Math.Round((rawInput - d) * 10000, 4);
        int m = (int)(ms / 100);
        double s = ms % 100;
        return d + (m / 60.0) + (s / 3600.0);
    }

    public static string DegreesToDmsString(double decimalDegrees)
    {
        decimalDegrees = decimalDegrees % 360;
        if (decimalDegrees < 0) decimalDegrees += 360;

        int d = (int)decimalDegrees;
        double remainder = (decimalDegrees - d) * 60.0;
        int m = (int)remainder;
        double s = Math.Round((remainder - m) * 60.0);
        
        if (s >= 60) { s = 0; m++; }
        if (m >= 60) { m = 0; d++; }
        d = d % 360;

        return $"{d}.{m:00}{s:00}";
    }

    public static string DmsToString(double dmsValue) { return dmsValue.ToString("0.0000"); }

    public static double AddSubDms(double dms1, double dms2, bool add)
    {
        double deg1 = ParseDmsToDegrees(dms1); double deg2 = ParseDmsToDegrees(dms2);
        double resDeg = add ? (deg1 + deg2) : (deg1 - deg2);
        return double.Parse(DegreesToDmsString(resDeg));
    }

    public static bool TryParseBearing(string input, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        input = input.Trim();

        // 1. Whole Degrees (e.g. "10")
        if (Regex.IsMatch(input, @"^\d+$"))
        {
            if (double.TryParse(input, out result)) return true;
        }
        // 2. Strict formats: DDD.MMSS
        else if (Regex.IsMatch(input, @"^\d+\.\d{1,4}$"))
        {
            if (double.TryParse(input, out result)) return true;
        }
        // 3. Strict formats: DDD MMSS
        else if (Regex.IsMatch(input, @"^\d+ \d{1,4}$"))
        {
            string converted = input.Replace(" ", ".");
            if (double.TryParse(converted, out result)) return true;
        }

        return false;
    }
}
#endregion

#region 5. DATA MANAGER
public static class DwgDataManager
{
    public static int GetNextPointNumber()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return 1;
        using (DocumentLock loc = doc.LockDocument())
        using (Transaction tr = doc.TransactionManager.StartTransaction())
        {
            int num = GetNextPointNumber(tr, doc.Database);
            tr.Commit();
            return num;
        }
    }

    public static int GetNextPointNumber(Transaction tr, Database db)
    {
        int nextNum = 1;
        DatabaseSummaryInfoBuilder infoBuilder = new DatabaseSummaryInfoBuilder(db.SummaryInfo);
        bool found = false;

        if (infoBuilder.CustomPropertyTable.Contains(CadConstants.VAR_PT_COUNTER))
        {
            string val = (string)infoBuilder.CustomPropertyTable[CadConstants.VAR_PT_COUNTER];
            if (int.TryParse(val, out int storedNum)) { nextNum = storedNum; found = true; }
        }
        if (!found)
        {
            nextNum = ScanLayerForMaxPoint(db, tr) + 1;
            SetNextPointNumber(nextNum, tr, db);
        }
        return nextNum;
    }

    public static void SetNextPointNumber(int num)
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        using (DocumentLock loc = doc.LockDocument())
        using (Transaction tr = doc.TransactionManager.StartTransaction())
        {
            SetNextPointNumber(num, tr, doc.Database);
            tr.Commit();
        }
    }

    public static void SetNextPointNumber(int num, Transaction tr, Database db)
    {
        DatabaseSummaryInfoBuilder infoBuilder = new DatabaseSummaryInfoBuilder(db.SummaryInfo);
        if (infoBuilder.CustomPropertyTable.Contains(CadConstants.VAR_PT_COUNTER)) infoBuilder.CustomPropertyTable[CadConstants.VAR_PT_COUNTER] = num.ToString();
        else infoBuilder.CustomPropertyTable.Add(CadConstants.VAR_PT_COUNTER, num.ToString());
        db.SummaryInfo = infoBuilder.ToDatabaseSummaryInfo();
    }

    private static int ScanLayerForMaxPoint(Database db, Transaction tr)
    {
        int max = 0;
        try
        {
            Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            TypedValue[] filter = new TypedValue[]
            {
                new TypedValue((int)DxfCode.LayerName, CadConstants.POINT_NUMBER),
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "TEXT"),
                new TypedValue((int)DxfCode.Start, "MTEXT"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            };
            PromptSelectionResult res = ed.SelectAll(new SelectionFilter(filter));
            if (res.Status == PromptStatus.OK)
            {
                foreach (ObjectId id in res.Value.GetObjectIds())
                {
                    Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    string txt = "";
                    if (ent is DBText dbt) txt = dbt.TextString; else if (ent is MText mt) txt = mt.Contents;
                    if (int.TryParse(txt, out int val)) { if (val > max) max = val; }
                }
            }
        }
        catch { }
        return max;
    }

    public static bool IsPointNumberAtLocation(Point3d pt, Transaction tr, Database db)
    {
        try
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(CadConstants.POINT_NUMBER)) return false;

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in btr)
            {
                Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                if (ent.Layer == CadConstants.POINT_NUMBER)
                {
                    Point3d txtPos = Point3d.Origin;
                    if (ent is DBText dbt) txtPos = dbt.Position;
                    else if (ent is MText mt) txtPos = mt.Location;

                    if (txtPos.DistanceTo(pt) < 0.01) return true;
                }
            }
        }
        catch { }
        return false;
    }
}
#endregion

#region 6. MAIN WINDOW
public class CadastreWpfWindow : System.Windows.Window
{
    #region Properties & State
    private struct LayerDef { public string Name; public short Color; public string Linetype; public double LinetypeScale; }
    private static readonly Dictionary<Key, LayerDef> LayerConfig = new Dictionary<Key, LayerDef>
    {
        { Key.Q, new LayerDef { Name = "BOUNDARY_SUBJECT", Color = 4, Linetype = "Continuous", LinetypeScale = 1.0 } },
        { Key.W, new LayerDef { Name = "BOUNDARY_ADJOINING", Color = 2, Linetype = "Continuous", LinetypeScale = 1.0 } },
        { Key.E, new LayerDef { Name = "CONNECTIONS", Color = 1, Linetype = "DASHED", LinetypeScale = 0.3 } },
        { Key.A, new LayerDef { Name = "BDY_EASEMENT", Color = 2, Linetype = "DASHED", LinetypeScale = 0.5 } },
        { Key.S, new LayerDef { Name = "ADDITIONAL_1", Color = 6, Linetype = "Continuous", LinetypeScale = 1.0 } },
        { Key.D, new LayerDef { Name = "ADDITIONAL_2", Color = 3, Linetype = "Continuous", LinetypeScale = 1.0 } }
    };

    private Document _doc;
    private Point3d _currentPoint;
    private Point3d _lastCreatedVertex;
    private bool _hasStartPoint = false;
    private Stack<List<ObjectId>> _undoStack = new Stack<List<ObjectId>>();
    private List<Point3d> _traversePath = new List<Point3d>();
    private AppSettings _config;
    private string _currentLayer = "BOUNDARY_SUBJECT";
    private bool _isBusy = false;

    // Controls
    private TextBox txtBearing = null!, txtDistance = null!;
    private TextBlock lblBearingTrace = null!, lblDistanceTrace = null!;
    private Button btnSound = null!;

    // Buttons
    private Button btnQ = null!, btnW = null!, btnE = null!, btnA = null!, btnS = null!, btnD = null!;

    private bool EnsureQuiescent()
    {
        if (!_doc.Editor.IsQuiescent)
        {
            _doc.Editor.WriteMessage("\n[BUSY] Please press ESC in AutoCAD before using the tool.");
            return false;
        }
        return true;
    }
    #endregion

    #region Constructor & Cleanup
    public CadastreWpfWindow(Document doc)
    {
        _doc = doc;
        _config = AppSettings.Load();
        
        InitializeCustomUI();
        InitializeProjectLayers();
        UpdateLayerButtons();

        // Default to 'W' layer def from config
        _currentLayer = LayerConfig[Key.W].Name;
        HighlightActiveLayer(btnW);

        this.Closed += CadastreWpfWindow_Closed;
    }

    private void CadastreWpfWindow_Closed(object? sender, EventArgs e)
    {
    }
    #endregion

    #region Centralized Action Handling
    private void ExecuteUiAction(Action action)
    {
        if (_isBusy) return;
        SetBusy(true);
        try
        {
            action();
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            _doc.Editor.WriteMessage($"\n[AutoCAD Error] {ex.Message}");
        }
        catch (System.Exception ex)
        {
            _doc.Editor.WriteMessage($"\n[System Error] {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        if (txtBearing != null) txtBearing.IsEnabled = !busy;
        if (txtDistance != null) txtDistance.IsEnabled = !busy;
        
        if (busy)
        {
            System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
        }
        else
        {
            System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default;
        }
    }
    #endregion

    #region Initialization
    private void InitializeProjectLayers()
    {
        try
        {
            using (DocumentLock loc = _doc.LockDocument())
            using (Transaction tr = _doc.TransactionManager.StartTransaction())
            {
                LayerTable lt = (LayerTable)tr.GetObject(_doc.Database.LayerTableId, OpenMode.ForRead);
                LinetypeTable ltt = (LinetypeTable)tr.GetObject(_doc.Database.LinetypeTableId, OpenMode.ForRead);

                foreach (var entry in LayerConfig.Values)
                {
                    if (!lt.Has(entry.Name))
                    {
                        lt.UpgradeOpen();
                        LayerTableRecord ltr = new LayerTableRecord();
                        ltr.Name = entry.Name;
                        ltr.Color = AcColor.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, entry.Color);

                        if (entry.Linetype != "Continuous")
                        {
                            if (!ltt.Has(entry.Linetype))
                            {
                                try { _doc.Database.LoadLineTypeFile(entry.Linetype, "acad.lin"); }
                                catch { _doc.Editor.WriteMessage($"\n[Error] Could not load linetype {entry.Linetype}. Ensure acad.lin is in search path."); }
                            }
                            if (ltt.Has(entry.Linetype)) ltr.LinetypeObjectId = ltt[entry.Linetype];
                        }

                        lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }
                }

                // Automated Layer Setup for Text Layers
                EnsureLayer(lt, CadConstants.BDY_DISTANCE, 2, tr);
                EnsureLayer(lt, CadConstants.BDY_BEARING, 2, tr);
                EnsureLayer(lt, CadConstants.CONNECTION_DIST, 1, tr);
                EnsureLayer(lt, CadConstants.CONNECTION_BEAR, 1, tr);
                EnsureLayer(lt, CadConstants.SYMB_TEXT, 1, tr);
                EnsureLayer(lt, CadConstants.POINT_NUMBER, 3, tr);

                tr.Commit();
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            _doc.Editor.WriteMessage($"\n[Critical] Layer initialization failed: {ex.Message}");
        }
    }

    private void EnsureLayer(LayerTable lt, string name, short colorIndex, Transaction tr)
    {
        if (!lt.Has(name))
        {
            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord();
            ltr.Name = name;
            ltr.Color = AcColor.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }
    }

    private void InitializeCustomUI()
    {
        this.Title = "CADASTRE PRO"; this.Width = 600; this.Height = 750;
        this.Topmost = true; this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.Background = UITheme.BackgroundBrush;

        Grid mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // 0: Header Icons
        mainGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) }); // 1: Main Content

        // Header Icons
        UIElement headerIcons = BuildHeaderIcons();
        Grid.SetRow(headerIcons, 0); mainGrid.Children.Add(headerIcons);

        // Main Content (Directly Input Tab)
        object inputContent = BuildInputTab();
        if (inputContent is UIElement uiContent)
        {
            Grid.SetRow(uiContent, 1); mainGrid.Children.Add(uiContent);
        }

        this.Content = mainGrid;
        this.PreviewKeyDown += Window_PreviewKeyDown;
        UpdateSoundIcon();
    }

    private UIElement BuildHeaderIcons()
    {
        StackPanel sp = new StackPanel() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 5, 15, 0) };
        
        btnSound = new Button() { Content = "\ud83d\udd0a", FontSize = 18, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, ToolTip = "Toggle Audio Feedback" };
        btnSound.Click += (s, e) => { 
            _config.AudioFeedback = !_config.AudioFeedback; 
            AppSettings.Save(_config);
            UpdateSoundIcon(); 
            if (_config.AudioFeedback) PlayAudio();
        };

        Button btnAbout = new Button() { Content = "?", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.LightGray, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = "About / Help" };
        btnAbout.Click += (s, e) => ShowAboutPopup();

        sp.Children.Add(btnSound);
        sp.Children.Add(btnAbout);
        return sp;
    }

    private void UpdateSoundIcon()
    {
        if (btnSound == null) return;
        btnSound.Foreground = _config.AudioFeedback ? UITheme.AccentColor : Brushes.Gray;
    }

    private void ShowAboutPopup()
    {
        string aboutMsg = "CADASTRE PRO\n\n" +
                          "WORKFLOW:\n" +
                          "1. PgUp: Start Point Menu (Type or Pick).\n" +
                          "2. Enter Bearing/Dist (Auto-Calc available).\n" +
                          "3. Press Enter to Draw.\n" +
                          "4. Use QWE-ASD to switch layers.\n\n" +
                          "HOTKEYS:\n" +
                          " • End/PgDn: New Line / Pick Point\n" +
                          " • PgUp: Side Shot Menu\n" +
                          " • Insert: Add Comment\n" +
                          " • Delete: Undo Last\n" +
                          " • Arrows: Rotate Bearing";
        MessageBox.Show(aboutMsg, "About Cadastre Pro", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    #endregion

    #region Tab Building
    private object BuildInputTab()
    {
        Grid mainG = new Grid();
        mainG.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // 0: Data Card
        mainG.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // 1: Quick Actions Card
        mainG.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // 2: Layer Card
        mainG.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });

        // --- 1. DATA ENTRY CARD ---
        Border cardData = UITheme.CreateCard(); cardData.Margin = new Thickness(15, 10, 15, 10);
        StackPanel spData = new StackPanel();

        // --- Traverse Setup Row (E & N / PICK) ---
        Grid gPos = new Grid();
        gPos.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
        gPos.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
        gPos.Margin = new Thickness(0, 0, 0, 10);

        Button btnEN = UITheme.CreateActionBtn("", UITheme.ActionBlue);
        btnEN.Content = UITheme.CreateShortcutContent("End", "\ud83d\udccd E & N");
        btnEN.Height = 45; btnEN.Margin = new Thickness(0, 0, 5, 0);
        btnEN.ToolTip = "Enter starting coordinates manually (Easting/Northing).";
        btnEN.Click += (s, e) => TriggerCoordsWindow();

        Button btnPick = UITheme.CreateActionBtn("", UITheme.ActionBlue);
        btnPick.Content = UITheme.CreateShortcutContent("PgDn", "\ud83d\uddb1\ufe0f PICK");
        btnPick.Height = 45; btnPick.Margin = new Thickness(5, 0, 0, 0);
        btnPick.ToolTip = "Select a starting point directly from the AutoCAD drawing screen.";
        btnPick.Click += (s, e) => ExecuteScreenPick();

        Grid.SetColumn(btnEN, 0); Grid.SetColumn(btnPick, 1);
        gPos.Children.Add(btnEN); gPos.Children.Add(btnPick);
        spData.Children.Add(gPos);

        // --- Master Input Grid (Aligns Bearing and Distance Columns) ---
        Grid gInputMaster = new Grid();
        gInputMaster.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
        gInputMaster.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(192) });
        
        gInputMaster.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // 0: Bearing Label
        gInputMaster.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // 1: Bearing Row
        gInputMaster.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // 2: Bearing Trace
        gInputMaster.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // 3: Distance Label
        gInputMaster.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // 4: Distance Row
        gInputMaster.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // 5: Distance Trace

        // Bearing Label
        Label lblBrg = UITheme.CreateLabel("BEARING (DDD.MMSS)");
        Grid.SetRow(lblBrg, 0); Grid.SetColumnSpan(lblBrg, 2);
        gInputMaster.Children.Add(lblBrg);

        // txtBearing (Row 1, Col 0)
        txtBearing = UITheme.CreateInputBox(); 
        txtBearing.Height = 45;
        txtBearing.PreviewKeyDown += Input_PreviewKeyDown;
        Grid.SetRow(txtBearing, 1); Grid.SetColumn(txtBearing, 0);
        gInputMaster.Children.Add(txtBearing);

        // Bearing Buttons (Row 1, Col 1)
        Grid gBrgBtns = new Grid();
        for (int i = 0; i < 4; i++) gBrgBtns.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(48) });

        Button CreateBrgBtn(object content, string tip, double delta) {
            Button b = new Button() { Content = content, Width = 45, Height = 45, Margin = new Thickness(3, 0, 0, 0), Background = UITheme.ActionBlue, Foreground = Brushes.White, FontWeight = FontWeights.Bold, ToolTip = tip };
            b.Click += (s, e) => { ModifyBearing(delta); txtBearing.Focus(); txtBearing.SelectAll(); };
            return b;
        }

        Button bP90 = CreateBrgBtn(UITheme.CreateShortcutContent("\u2191", "+90\u00B0"), "\u21BB Rotate bearing +90\u00B0", 90);
        Button bM90 = CreateBrgBtn(UITheme.CreateShortcutContent("\u2193", "-90\u00B0"), "\u21BA Rotate bearing -90\u00B0", -90);
        Button bP180 = CreateBrgBtn(UITheme.CreateShortcutContent("\u2192", "+180\u00B0"), "\u21C5 Rotate bearing +180\u00B0", 180);
        Button bM180 = CreateBrgBtn(UITheme.CreateShortcutContent("\u2190", "-180\u00B0"), "\u21C5 Rotate bearing -180\u00B0", -180);

        Grid.SetColumn(bP90, 0); Grid.SetColumn(bM90, 1); Grid.SetColumn(bP180, 2); Grid.SetColumn(bM180, 3);
        gBrgBtns.Children.Add(bP90); gBrgBtns.Children.Add(bM90); gBrgBtns.Children.Add(bP180); gBrgBtns.Children.Add(bM180);
        Grid.SetRow(gBrgBtns, 1); Grid.SetColumn(gBrgBtns, 1);
        gInputMaster.Children.Add(gBrgBtns);

        // Bearing Trace (Row 2)
        lblBearingTrace = new TextBlock() { FontSize = 13, Foreground = Brushes.LightGray, FontStyle = FontStyles.Italic, FontWeight = FontWeights.SemiBold, Margin = new Thickness(5, -2, 0, 10) };
        Grid.SetRow(lblBearingTrace, 2); Grid.SetColumnSpan(lblBearingTrace, 2);
        gInputMaster.Children.Add(lblBearingTrace);

        // Distance Label (Row 3)
        Label lblDist = UITheme.CreateLabel("DISTANCE (m)");
        Grid.SetRow(lblDist, 3); Grid.SetColumnSpan(lblDist, 2);
        gInputMaster.Children.Add(lblDist);

        // txtDistance (Row 4, Col 0)
        txtDistance = UITheme.CreateInputBox();
        txtDistance.Height = 45;
        txtDistance.PreviewKeyDown += Input_PreviewKeyDown;
        txtDistance.GotFocus += (s, e) => { txtDistance.BorderBrush = Brushes.WhiteSmoke; txtDistance.BorderThickness = new Thickness(2); };
        txtDistance.LostFocus += (s, e) => { txtDistance.BorderBrush = Brushes.Gray; txtDistance.BorderThickness = new Thickness(1); };
        Grid.SetRow(txtDistance, 4); Grid.SetColumn(txtDistance, 0);
        gInputMaster.Children.Add(txtDistance);

        // SIDE SHOT Button (Row 4, Col 1)
        Button bSS = new Button() { 
            Content = UITheme.CreateShortcutContent("PgUp", "\u2699 SIDE SHOT"),
            Height = 45, 
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0), 
            Background = UITheme.ActionBlue, 
            Foreground = Brushes.White, 
            FontWeight = FontWeights.Bold, 
            FontSize = 12,
            ToolTip = "Open Side Shot/Radiation menu (PGUP)" 
        };
        bSS.Click += (s, e) => { OpenSideShotForm(); txtBearing.Focus(); txtBearing.SelectAll(); };
        Grid.SetRow(bSS, 4); Grid.SetColumn(bSS, 1);
        gInputMaster.Children.Add(bSS);

        // Distance Trace (Row 5)
        lblDistanceTrace = new TextBlock() { FontSize = 13, Foreground = Brushes.LightGray, FontStyle = FontStyles.Italic, FontWeight = FontWeights.SemiBold, Margin = new Thickness(5, 2, 0, 10) };
        Grid.SetRow(lblDistanceTrace, 5); Grid.SetColumnSpan(lblDistanceTrace, 2);
        gInputMaster.Children.Add(lblDistanceTrace);

        spData.Children.Add(gInputMaster);
        cardData.Child = spData;
        Grid.SetRow(cardData, 0); mainG.Children.Add(cardData);

        // --- 2. QUICK ACTIONS CARD ---
        Border cardQuick = UITheme.CreateCard(); cardQuick.Margin = new Thickness(15, 0, 15, 10);
        Grid gActions = new Grid();
        for (int i = 0; i < 2; i++) gActions.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });

        Button CreateQuickBtn(object content, string tip, Action action)
        {
            Button b = new Button() { Content = content, Height = 45, Margin = new Thickness(2), Background = UITheme.ActionBlue, Foreground = Brushes.White, FontWeight = FontWeights.Bold, ToolTip = tip, HorizontalAlignment = HorizontalAlignment.Stretch };
            b.Click += (s, e) => { action(); txtBearing.Focus(); txtBearing.SelectAll(); };
            return b;
        }

        Button bUndo = CreateQuickBtn(UITheme.CreateShortcutContent("Del", "\u21B2 Undo"), "Delete last line/text (DEL)", () => ExecuteUiAction(() => UndoLastStep()));
        Button bComm = CreateQuickBtn(UITheme.CreateShortcutContent("Ins", "\ud83d\udcac Comment"), "Add Text Comment/Symbol (INS)", () => ExecuteUiAction(() => AddTextComment(null)));

        Grid.SetColumn(bUndo, 0); Grid.SetColumn(bComm, 1);
        gActions.Children.Add(bUndo); gActions.Children.Add(bComm);

        cardQuick.Child = gActions;
        Grid.SetRow(cardQuick, 1); mainG.Children.Add(cardQuick);

        // --- 3. LAYER SELECTION CARD ---
        Border cardLay = UITheme.CreateCard(); 
        cardLay.Margin = new Thickness(15, 0, 15, 15);
        cardLay.Padding = new Thickness(5);
        StackPanel spLay = new StackPanel();
        spLay.Children.Add(UITheme.CreateLabel("ACTIVE LAYER (QWE ASD)"));
        Grid g = new Grid() { Margin = new Thickness(0) };
        g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
        for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });

        btnQ = UITheme.CreateLayerBtn("Q"); btnQ.HorizontalAlignment = HorizontalAlignment.Stretch; btnQ.Height = 55; btnQ.Click += (s, e) => SetCurrentLayer(LayerConfig[Key.Q].Name, btnQ);
        btnW = UITheme.CreateLayerBtn("W"); btnW.HorizontalAlignment = HorizontalAlignment.Stretch; btnW.Height = 55; btnW.Click += (s, e) => SetCurrentLayer(LayerConfig[Key.W].Name, btnW);
        btnE = UITheme.CreateLayerBtn("E"); btnE.HorizontalAlignment = HorizontalAlignment.Stretch; btnE.Height = 55; btnE.Click += (s, e) => SetCurrentLayer(LayerConfig[Key.E].Name, btnE);
        btnA = UITheme.CreateLayerBtn("A"); btnA.HorizontalAlignment = HorizontalAlignment.Stretch; btnA.Height = 55; btnA.Click += (s, e) => SetCurrentLayer(LayerConfig[Key.A].Name, btnA);
        btnS = UITheme.CreateLayerBtn("S"); btnS.HorizontalAlignment = HorizontalAlignment.Stretch; btnS.Height = 55; btnS.Click += (s, e) => SetCurrentLayer(LayerConfig[Key.S].Name, btnS);
        btnD = UITheme.CreateLayerBtn("D"); btnD.HorizontalAlignment = HorizontalAlignment.Stretch; btnD.Height = 55; btnD.Click += (s, e) => SetCurrentLayer(LayerConfig[Key.D].Name, btnD);

        Grid.SetRow(btnQ, 0); Grid.SetColumn(btnQ, 0); Grid.SetRow(btnW, 0); Grid.SetColumn(btnW, 1); Grid.SetRow(btnE, 0); Grid.SetColumn(btnE, 2);
        Grid.SetRow(btnA, 1); Grid.SetColumn(btnA, 0); Grid.SetRow(btnS, 1); Grid.SetColumn(btnS, 1); Grid.SetRow(btnD, 1); Grid.SetColumn(btnD, 2);

        g.Children.Add(btnQ); g.Children.Add(btnW); g.Children.Add(btnE);
        g.Children.Add(btnA); g.Children.Add(btnS); g.Children.Add(btnD);
        spLay.Children.Add(g); cardLay.Child = spLay;
        Grid.SetRow(cardLay, 2); mainG.Children.Add(cardLay);

        return mainG;
    }
    #endregion

    #region Calculation & Analysis
    private void CalculateArea()
    {
    }

    private void UpdateRunningMisclosure()
    {
    }
    #endregion

    #region UI & Input Handlers
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (LayerConfig.ContainsKey(e.Key))
        {
            var def = LayerConfig[e.Key];
            Button b = e.Key switch { Key.Q => btnQ, Key.W => btnW, Key.E => btnE, Key.A => btnA, Key.S => btnS, Key.D => btnD, _ => btnW };
            SetCurrentLayer(def.Name, b);
            e.Handled = true;
        }

        if (e.Key == Key.PageUp) { e.Handled = true; OpenSideShotForm(); }
        else if (e.Key == Key.PageDown) { e.Handled = true; ExecuteScreenPick(); }
        else if (e.Key == Key.End) { e.Handled = true; TriggerCoordsWindow(); }
        else if (e.Key == Key.Insert) { e.Handled = true; ExecuteUiAction(() => AddTextComment(null)); }
        else if (e.Key == Key.Delete) { e.Handled = true; ExecuteUiAction(() => UndoLastStep()); }
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        TextBox tb = (TextBox)sender;
        if (tb == txtBearing)
        {
            if (e.Key == Key.Up) { ModifyBearing(90); e.Handled = true; }
            if (e.Key == Key.Down) { ModifyBearing(-90); e.Handled = true; }
            if (e.Key == Key.Right) { ModifyBearing(180); e.Handled = true; }
            if (e.Key == Key.Left) { ModifyBearing(-180); e.Handled = true; }
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            string input = tb.Text.Trim();
            
            if (input.Contains("+") || input.Contains("-") || input.Contains("*") || input.Contains("/"))
            {
                string oldVal = input;
                string result = EvaluateInlineExpression(input, tb == txtBearing);
                if (result != null)
                {
                    tb.Text = result;
                    if (tb == txtBearing) lblBearingTrace.Text = $"{oldVal} = {result}";
                    else lblDistanceTrace.Text = $"{oldVal} = {result}";
                    
                    tb.Foreground = Brushes.White;
                    tb.FontWeight = FontWeights.Bold;
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    timer.Tick += (s, ev) => { tb.Foreground = Brushes.Cyan; tb.FontWeight = FontWeights.Normal; timer.Stop(); };
                    timer.Start();

                    tb.SelectAll();
                    return; // Wait for next Enter to move focus or draw
                }
            }

            if (string.IsNullOrWhiteSpace(txtBearing.Text) && string.IsNullOrWhiteSpace(txtDistance.Text))
            {
                if (!_hasStartPoint)
                {
                    TriggerCoordsWindow();
                    return;
                }
                else
                {
                    txtBearing.Focus();
                }
            }
            else
            {
                if (tb == txtBearing) 
                { 
                    txtDistance.Focus(); 
                    txtDistance.SelectAll(); 
                }
                else if (tb == txtDistance) 
                {
                    if (string.IsNullOrWhiteSpace(txtBearing.Text) || string.IsNullOrWhiteSpace(txtDistance.Text))
                    {
                        // Don't draw if one is missing, but also don't show warning if just tabbing through
                        if (string.IsNullOrWhiteSpace(txtBearing.Text)) txtBearing.Focus();
                        return;
                    }
                    ExecuteUiAction(() => ExecuteManualDraw());
                }
            }
        }
    }

    private string EvaluateInlineExpression(string input, bool isDms)
    {
        try
        {
            if (isDms)
            {
                string[] parts;
                if (input.Contains("+"))
                {
                    parts = input.Split('+');
                    double v1 = double.Parse(parts[0].Trim()); double v2 = double.Parse(parts[1].Trim());
                    return CadMath.DmsToString(CadMath.AddSubDms(v1, v2, true));
                }
                else if (input.Contains("-"))
                {
                    parts = input.Split('-');
                    double v1 = double.Parse(parts[0].Trim()); double v2 = double.Parse(parts[1].Trim());
                    return CadMath.DmsToString(CadMath.AddSubDms(v1, v2, false));
                }
            }
            
            System.Data.DataTable dt = new System.Data.DataTable();
            var v = dt.Compute(input, "");
            double res = Convert.ToDouble(v);
            return isDms ? CadMath.DmsToString(res) : res.ToString("0.000");
        }
        catch
        {
            _doc.Editor.WriteMessage("\n[Error] Invalid Math Expression.");
            return null;
        }
    }
    #endregion

    #region Primary Drawing Logic
    private void ExecuteManualDraw()
    {
        if (!_hasStartPoint) { TriggerCoordsWindow(); return; }
        if (!EnsureQuiescent()) return;
        if (!ValidateDocument()) return;

        using (DocumentLock loc = _doc.LockDocument())
        using (Transaction tr = _doc.TransactionManager.StartTransaction())
        {
            BlockTable bt = (BlockTable)tr.GetObject(_doc.Database.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            Point3d newPt = DrawGeometryToDatabase(tr, btr, txtBearing.Text, txtDistance.Text, _currentPoint, _currentLayer);
            
            int currentNum = DwgDataManager.GetNextPointNumber(tr, _doc.Database) - 1;
            tr.Commit();

            _lastCreatedVertex = newPt; _currentPoint = newPt; _traversePath.Add(newPt);

            CalculateArea(); PlayAudio(); PanToPoint(newPt); _doc.Editor.UpdateScreen();
            
            this.Dispatcher.BeginInvoke(new Action(() => {
                txtBearing.Focus();
                txtBearing.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void SetStartPoint(Point3d pt)
    {
        if (!EnsureQuiescent()) return;
        if (!ValidateDocument()) return;
        
        ExecuteUiAction(() => {
            _currentPoint = pt; _lastCreatedVertex = pt; _hasStartPoint = true;
            _undoStack.Clear(); _traversePath.Clear(); _traversePath.Add(_currentPoint);

            using (DocumentLock loc = _doc.LockDocument())
            using (Transaction tr = _doc.TransactionManager.StartTransaction())
            {
                bool exists = DwgDataManager.IsPointNumberAtLocation(pt, tr, _doc.Database);
                if (!exists)
                {
                    int nextNum = DwgDataManager.GetNextPointNumber(tr, _doc.Database);
                    BlockTable bt = (BlockTable)tr.GetObject(_doc.Database.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    TextSettings ptSettings = new TextSettings { ColorIndex = 3, Size = 2.5, Style = "ROMAND140" };
                    Entity ptTxt = CreateText(nextNum.ToString(), CadConstants.POINT_NUMBER, pt, AttachmentPoint.BottomLeft, tr, _doc.Database, ptSettings);
                    AddToDb(ptTxt, btr, tr);
                    DwgDataManager.SetNextPointNumber(nextNum + 1, tr, _doc.Database);
                }
                tr.Commit();
            }

            CalculateArea();
            lblBearingTrace.Text = ""; lblDistanceTrace.Text = "";
            txtBearing.Focus();
            txtBearing.SelectAll();
            PanToPoint(pt);
        });
    }

    private Point3d DrawGeometryToDatabase(Transaction tr, BlockTableRecord btr, string brgStr, string distStr, Point3d startPt, string layer)
    {
        double rawBrg, dist;
        if (!CadMath.TryParseBearing(brgStr, out rawBrg) || !double.TryParse(distStr, out dist))
        {
            _doc.Editor.WriteMessage("\n[Error] Invalid Bearing or Distance format.");
            throw new System.Exception("Invalid Bearing or Distance format.");
        }
        
        // Ensure layer exists before database operation
        bool layerExists = false;
        using (Transaction checkTr = btr.Database.TransactionManager.StartTransaction())
        {
            LayerTable lt = (LayerTable)checkTr.GetObject(btr.Database.LayerTableId, OpenMode.ForRead);
            layerExists = lt.Has(layer);
            checkTr.Commit();
        }
        if (!layerExists) EnsureLayerExists(layer);

        double angleDeg = CadMath.ParseDmsToDegrees(rawBrg);
        double cadAngleRad = (90.0 - angleDeg) * (Math.PI / 180.0);
        Point3d endPoint = new Point3d(startPt.X + (dist * Math.Cos(cadAngleRad)), startPt.Y + (dist * Math.Sin(cadAngleRad)), startPt.Z);
        endPoint = CheckSnapping(endPoint, tr, btr);
        List<ObjectId> createdEntities = new List<ObjectId>();
        Autodesk.AutoCAD.DatabaseServices.Line ln = new Autodesk.AutoCAD.DatabaseServices.Line(startPt, endPoint);
        ln.Layer = layer;

        // Apply linetype scale from hardcoded config
        var configEntry = LayerConfig.Values.FirstOrDefault(ld => ld.Name == layer);
        if (!string.IsNullOrEmpty(configEntry.Name))
        {
            ln.LinetypeScale = configEntry.LinetypeScale;
        }

        createdEntities.Add(AddToDb(ln, btr, tr));
        createdEntities.AddRange(CreateAnnotatedText(btr, tr, ln, rawBrg, dist, cadAngleRad));

        // FIX: Check if number exists before creating
        bool exists = DwgDataManager.IsPointNumberAtLocation(endPoint, tr, btr.Database);
        if (!exists)
        {
            int nextNum = DwgDataManager.GetNextPointNumber(tr, btr.Database);
            TextSettings ptSettings = new TextSettings { ColorIndex = 3, Size = 2.5, Style = "ROMAND140" };
            Entity ptTxt = CreateText(nextNum.ToString(), CadConstants.POINT_NUMBER, endPoint, AttachmentPoint.BottomLeft, tr, btr.Database, ptSettings);
            createdEntities.Add(AddToDb(ptTxt, btr, tr));
            DwgDataManager.SetNextPointNumber(nextNum + 1, tr, btr.Database);
        }

        _undoStack.Push(createdEntities);
        return endPoint;
    }
    #endregion

    #region Database Operations
    private void UndoLastStep()
    {
        if (_undoStack.Count == 0) return;
        if (!ValidateDocument()) return;

        using (DocumentLock loc = _doc.LockDocument())
        using (Transaction tr = _doc.TransactionManager.StartTransaction())
        {
            List<ObjectId> stepObjects = _undoStack.Pop();
            bool pointDeleted = false;
            foreach (ObjectId id in stepObjects)
            {
                if (!id.IsErased)
                {
                    Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                    if (ent.Layer == CadConstants.POINT_NUMBER) pointDeleted = true;
                    if (ent is Autodesk.AutoCAD.DatabaseServices.Line ln) { _currentPoint = ln.StartPoint; _lastCreatedVertex = ln.StartPoint; }
                    ent.Erase();
                }
            }

            if (pointDeleted)
            {
                int current = DwgDataManager.GetNextPointNumber(tr, _doc.Database);
                if (current > 1) DwgDataManager.SetNextPointNumber(current - 1, tr, _doc.Database);
            }

            if (_traversePath.Count > 1) _traversePath.RemoveAt(_traversePath.Count - 1);
            CalculateArea(); tr.Commit(); _doc.Editor.UpdateScreen();
        }
    }

    private void ToggleLayerVisibility(string layerName, bool isVisible)
    {
        if (!ValidateDocument()) return;

        using (DocumentLock loc = _doc.LockDocument())
        using (Transaction tr = _doc.TransactionManager.StartTransaction())
        {
            LayerTable lt = (LayerTable)tr.GetObject(_doc.Database.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName))
            {
                LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
                ltr.IsOff = !isVisible;
            }
            else if (isVisible)
            {
                EnsureLayerExistsInternal(layerName, null, tr, _doc.Database);
            }
            tr.Commit();
        }
    }

    private void EnsureLayerExists(string layerName)
    {
        if (!ValidateDocument()) return;
        
        Autodesk.AutoCAD.Colors.Color? selectedColor = null;
        using (var checkTr = _doc.Database.TransactionManager.StartTransaction())
        {
            LayerTable lt = (LayerTable)checkTr.GetObject(_doc.Database.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                Autodesk.AutoCAD.Windows.ColorDialog cd = new Autodesk.AutoCAD.Windows.ColorDialog();
                if (cd.ShowDialog() == System.Windows.Forms.DialogResult.OK) selectedColor = cd.Color;
                else selectedColor = AcColor.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7);
            }
            checkTr.Commit();
        }

        if (selectedColor != null)
        {
            using (_doc.LockDocument())
            using (Transaction tr = _doc.TransactionManager.StartTransaction())
            {
                EnsureLayerExistsInternal(layerName, selectedColor, tr, _doc.Database);
                tr.Commit();
            }
        }
    }

    private void EnsureLayerExistsInternal(string layerName, Autodesk.AutoCAD.Colors.Color? color, Transaction tr, Database db)
    {
        LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(layerName))
        {
            lt.UpgradeOpen(); LayerTableRecord ltr = new LayerTableRecord(); ltr.Name = layerName;
            ltr.Color = color ?? AcColor.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7);
            lt.Add(ltr); tr.AddNewlyCreatedDBObject(ltr, true);
        }
    }

    private ObjectId AddToDb(Entity ent, BlockTableRecord btr, Transaction tr)
    {
        ObjectId id = btr.AppendEntity(ent); tr.AddNewlyCreatedDBObject(ent, true); return id;
    }

    private Point3d CheckSnapping(Point3d target, Transaction tr, BlockTableRecord btr)
    {
        foreach (ObjectId id in btr)
        {
            if (id.IsValid)
            {
                Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                if (ent is DBPoint pt && pt.Position.DistanceTo(target) < _config.SnapTolerance) return pt.Position;
            }
        }
        return target;
    }

    private List<ObjectId> CreateAnnotatedText(BlockTableRecord btr, Transaction tr, Entity baseEnt, double rawBrg, double dist, double cadAngleRad)
    {
        List<ObjectId> ids = new List<ObjectId>();
        double textRot = cadAngleRad; 
        double normAng = cadAngleRad % (Math.PI * 2); 
        if (normAng < 0) normAng += (Math.PI * 2);
        bool isFlipped = false; 
        if (normAng > (Math.PI / 2) && normAng <= (3 * Math.PI / 2)) { textRot += Math.PI; isFlipped = true; }
        
        Point3d mid = ((Autodesk.AutoCAD.DatabaseServices.Line)baseEnt).StartPoint + (((Autodesk.AutoCAD.DatabaseServices.Line)baseEnt).EndPoint - ((Autodesk.AutoCAD.DatabaseServices.Line)baseEnt).StartPoint) / 2.0;
        double dx = Math.Cos(cadAngleRad); 
        double dy = Math.Sin(cadAngleRad); 
        
        TextSettings brgSettings = new TextSettings();
        TextSettings distSettings = new TextSettings();
        string brgLayer, distLayer;

        if (_currentLayer == "BOUNDARY_SUBJECT")
        {
            brgLayer = CadConstants.BDY_BEARING;
            brgSettings.ColorIndex = 256;
            brgSettings.Size = 3.0;
            brgSettings.Style = "STENDOT100";

            distLayer = CadConstants.BDY_DISTANCE;
            distSettings.ColorIndex = 256;
            distSettings.Size = 3.0;
            distSettings.Style = "STENDOT100S";
        }
        else
        {
            brgLayer = CadConstants.CONNECTION_BEAR;
            brgSettings.ColorIndex = 256;
            brgSettings.Size = 2.5;
            brgSettings.Style = "STENDOT80";

            distLayer = CadConstants.CONNECTION_DIST;
            distSettings.ColorIndex = 256;
            distSettings.Size = 2.5;
            distSettings.Style = "STENDOT80";
        }

        double offsetDist = brgSettings.Size * 1.2;
        Vector3d upVec = isFlipped ? new Vector3d(dy, -dx, 0) : new Vector3d(-dy, dx, 0);

        int d = (int)rawBrg; 
        int m = (int)((rawBrg - d) * 100); 
        double s = ((rawBrg * 10000) % 100);
        
        ids.Add(AddToDb(CreateText($"{d}\u00B0{m:00}'{s:00}\"", brgLayer, mid + (upVec * offsetDist), AttachmentPoint.BottomCenter, tr, btr.Database, brgSettings, textRot), btr, tr));
        ids.Add(AddToDb(CreateText(dist.ToString("0.000"), distLayer, mid - (upVec * offsetDist), AttachmentPoint.TopCenter, tr, btr.Database, distSettings, textRot), btr, tr));
        
        return ids;
    }

    private Entity CreateText(string content, string layer, Point3d pt, AttachmentPoint align, Transaction tr, Database db, TextSettings ts, double rotation = 0)
    {
        EnsureLayerExistsInternal(layer, null, tr, db);
        ObjectId styleId = GetTextStyleId(tr, ts.Style, db);
        if (ts.IsMText)
        {
            MText mt = new MText(); 
            mt.Contents = content; 
            mt.Layer = layer; 
            mt.TextHeight = ts.Size; 
            mt.TextStyleId = styleId;
            mt.Rotation = rotation; 
            mt.Location = pt; 
            mt.Attachment = align;
            mt.Color = AcColor.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 256); // Force ByLayer
            if (ts.Masking) { mt.BackgroundFill = true; mt.UseBackgroundColor = true; mt.BackgroundScaleFactor = 1.1; }
            mt.Annotative = AnnotativeStates.True;
            return mt;
        }
        else
        {
            DBText dt = new DBText(); 
            dt.TextString = content; 
            dt.Layer = layer; 
            dt.Height = ts.Size; 
            dt.TextStyleId = styleId;
            dt.Rotation = rotation; 
            dt.Position = pt; 
            dt.Justify = align;
            dt.AlignmentPoint = pt;
            dt.Color = AcColor.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 256); // Force ByLayer
            dt.Annotative = AnnotativeStates.True;
            return dt;
        }
    }

    private ObjectId GetTextStyleId(Transaction tr, string styleName, Database db)
    {
        TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        if (tst.Has(styleName)) return tst[styleName];
        return db.Textstyle;
    }
    #endregion

    #region Helper UI Methods
    private void PlayAudio()
    {
        if (!_config.AudioFeedback) return;

        try
        {
            string soundFile = "";
            string winMedia = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

            switch (_config.AudioSound)
            {
                case "Beep": soundFile = System.IO.Path.Combine(winMedia, "Windows Default.wav"); break;
                case "Asterisk": soundFile = System.IO.Path.Combine(winMedia, "Windows Background.wav"); break; // "Ding"
                case "Exclamation": soundFile = System.IO.Path.Combine(winMedia, "Windows Exclamation.wav"); break;
                case "Hand": soundFile = System.IO.Path.Combine(winMedia, "Windows Critical Stop.wav"); break;
                case "Question": soundFile = System.IO.Path.Combine(winMedia, "Windows Notify System Generic.wav"); break;
                default: soundFile = System.IO.Path.Combine(winMedia, "Windows Background.wav"); break;
            }

            if (File.Exists(soundFile))
            {
                SoundPlayer sp = new SoundPlayer(soundFile);
                sp.Play();
            }
            else
            {
                switch (_config.AudioSound)
                {
                    case "Beep": SystemSounds.Beep.Play(); break;
                    case "Asterisk": SystemSounds.Asterisk.Play(); break;
                    case "Exclamation": SystemSounds.Exclamation.Play(); break;
                    case "Hand": SystemSounds.Hand.Play(); break;
                    case "Question": SystemSounds.Question.Play(); break;
                    default: SystemSounds.Asterisk.Play(); break;
                }
            }
        }
        catch
        {
            System.Console.Beep();
        }
    }

    private void UpdateLayerButtons()
    {
        if (_doc == null || _doc.IsDisposed) return;
        void UpdateBtn(Button b, string layerName, string key)
        {
            if (b == null) return;
            b.Content = new TextBlock() { Text = $"{key}\n{layerName}", TextAlignment = System.Windows.TextAlignment.Center, FontSize = 11, TextWrapping = TextWrapping.Wrap };
            b.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)); b.Foreground = Brushes.White;
            if (layerName == "NOT SET") return;
            try
            {
                using (_doc.LockDocument())
                using (Transaction tr = _doc.TransactionManager.StartTransaction())
                {
                    LayerTable lt = (LayerTable)tr.GetObject(_doc.Database.LayerTableId, OpenMode.ForRead);
                    if (lt.Has(layerName))
                    {
                        LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForRead);
                        System.Drawing.Color sysCol = ltr.Color.ColorValue;
                        b.Background = new SolidColorBrush(Color.FromRgb(sysCol.R, sysCol.G, sysCol.B));
                        double brt = (sysCol.R * 0.299 + sysCol.G * 0.587 + sysCol.B * 0.114);
                        b.Foreground = (brt > 128) ? Brushes.Black : Brushes.White;
                    }
                }
            }
            catch { }
        }
        UpdateBtn(btnQ, LayerConfig[Key.Q].Name, "Q"); UpdateBtn(btnW, LayerConfig[Key.W].Name, "W"); UpdateBtn(btnE, LayerConfig[Key.E].Name, "E");
        UpdateBtn(btnA, LayerConfig[Key.A].Name, "A"); UpdateBtn(btnS, LayerConfig[Key.S].Name, "S"); UpdateBtn(btnD, LayerConfig[Key.D].Name, "D");
    }

    private void SetCurrentLayer(string layerName, Button btn)
    {
        _currentLayer = layerName; HighlightActiveLayer(btn); txtBearing.Focus();
    }

    private void HighlightActiveLayer(Button active)
    {
        Button[] btns = { btnQ, btnW, btnE, btnA, btnS, btnD };
        foreach (var b in btns) { b.BorderThickness = new Thickness(1); b.BorderBrush = Brushes.Gray; }
        active.BorderThickness = new Thickness(3); active.BorderBrush = Brushes.White;
    }

    private bool ValidateDocument()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc == null || doc != _doc)
        {
            MessageBox.Show("Active document changed or lost. Please restart the tool in the correct document.");
            return false;
        }
        return true;
    }

    private void ModifyBearing(double deltaDegrees)
    {
        double currentVal = 0;
        if (CadMath.TryParseBearing(txtBearing.Text, out currentVal))
        {
            double decDeg = CadMath.ParseDmsToDegrees(currentVal);
            decDeg += deltaDegrees;
            txtBearing.Text = CadMath.DegreesToDmsString(decDeg);
            
            _doc.Editor.WriteMessage($"\n[Bearing] Adjusted by {deltaDegrees:+#;-#;0}\u00B0");

            txtBearing.Focus();
            txtBearing.SelectAll();
        }
        else
        {
            _doc.Editor.WriteMessage("\n[Error] Invalid Bearing Format.");
        }
    }

    private void PanToPoint(Point3d target)
    {
        var ed = _doc.Editor;
        using (ViewTableRecord view = ed.GetCurrentView())
        {
            Matrix3d matWCS2DCS = Matrix3d.PlaneToWorld(view.ViewDirection) * Matrix3d.Displacement(view.Target - Point3d.Origin) * Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target);
            matWCS2DCS = matWCS2DCS.Inverse();
            Point3d centerPt = target.TransformBy(matWCS2DCS);
            view.CenterPoint = new Point2d(centerPt.X, centerPt.Y);
            ed.SetCurrentView(view);
        }
    }
    #endregion

    #region Secondary Modal Windows
    private void AddTextComment(string? preDefinedComment)
    {
        if (!_hasStartPoint) return;
        if (!ValidateDocument()) return;

        string finalComment = "";
        if (preDefinedComment != null) finalComment = preDefinedComment;
        else
        {
            CommentWpfWindow cWin = new CommentWpfWindow();
            cWin.Owner = this;
            if (cWin.ShowDialog() == true) finalComment = cWin.Comment;
        }

        if (!string.IsNullOrWhiteSpace(finalComment))
        {
            using (DocumentLock loc = _doc.LockDocument())
            using (Transaction tr = _doc.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(_doc.Database.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                TextSettings commSettings = new TextSettings { ColorIndex = 1, Size = 2.5, Style = "ROMANS80" };
                Entity txt = CreateText(finalComment, CadConstants.SYMB_TEXT, _lastCreatedVertex, AttachmentPoint.MiddleLeft, tr, _doc.Database, commSettings);
                ObjectId txtId = AddToDb(txt, btr, tr);
                if (_undoStack.Count > 0) _undoStack.Peek().Add(txtId);
                tr.Commit(); _doc.Editor.UpdateScreen();
            }
        }
    }

    private void OpenSideShotForm()
    {
        SideShotWpfWindow ssWin = new SideShotWpfWindow();
        ssWin.Owner = this;
        if (ssWin.ShowDialog() == true)
        {
            if (!ValidateDocument()) return;
            ExecuteUiAction(() => {
                using (DocumentLock loc = _doc.LockDocument())
                using (Transaction tr = _doc.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(_doc.Database.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    DrawGeometryToDatabase(tr, btr, ssWin.Bearing, ssWin.Distance, _currentPoint, _currentLayer);
                    if (!string.IsNullOrEmpty(ssWin.Comment))
                    {
                        double rawBrg; 
                        CadMath.TryParseBearing(ssWin.Bearing, out rawBrg);
                        double dist = double.Parse(ssWin.Distance);
                        double angleDeg = CadMath.ParseDmsToDegrees(rawBrg);
                        double rad = (90.0 - angleDeg) * (Math.PI / 180.0);
                        Point3d endPt = new Point3d(_currentPoint.X + (dist * Math.Cos(rad)), _currentPoint.Y + (dist * Math.Sin(rad)), _currentPoint.Z);
                        TextSettings commSettings = new TextSettings { ColorIndex = 1, Size = 2.5, Style = "ROMANS80" };
                        Entity txt = CreateText(ssWin.Comment, CadConstants.SYMB_TEXT, endPt, AttachmentPoint.MiddleLeft, tr, _doc.Database, commSettings);
                        ObjectId txtId = AddToDb(txt, btr, tr);
                        if (_undoStack.Count > 0) _undoStack.Peek().Add(txtId);
                    }
                    tr.Commit(); _doc.Editor.UpdateScreen();
                }
            });
            txtBearing.Focus(); txtBearing.SelectAll();
        }
    }

    private void OpenCalculator(TextBox txt, bool isDms)
    {
        CalculatorWindow cWin = new CalculatorWindow(txt.Text, isDms);
        cWin.Owner = this;
        if (cWin.ShowDialog() == true) { txt.Text = cWin.Result; txt.Focus(); txt.SelectAll(); }
    }

    private void ExecuteScreenPick()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        var ed = doc.Editor;

        this.Visibility = System.Windows.Visibility.Collapsed;
        System.Windows.Forms.Application.DoEvents();
        
        PromptPointResult ppr = ed.GetPoint("\nPick Start Point: ");
        
        this.Visibility = System.Windows.Visibility.Visible;
        this.Activate();

        if (ppr.Status == PromptStatus.OK)
        {
            SetStartPoint(ppr.Value);
            txtBearing.Focus();
            txtBearing.SelectAll();
        }
    }

    private void TriggerCoordsWindow()
    {
        this.Visibility = System.Windows.Visibility.Collapsed;
        System.Windows.Forms.Application.DoEvents();

        CoordsInputWindow w = new CoordsInputWindow(); w.Owner = this;
        bool? res = w.ShowDialog();
        
        this.Visibility = System.Windows.Visibility.Visible;
        this.Activate();

        if (res == true)
        {
            if (w.PickRequested)
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    this.Visibility = System.Windows.Visibility.Collapsed;
                    System.Windows.Forms.Application.DoEvents();

                    PromptPointResult ppr = doc.Editor.GetPoint("\nPick Start Point: ");
                    
                    this.Visibility = System.Windows.Visibility.Visible;
                    this.Activate();

                    if (ppr.Status == PromptStatus.OK)
                    {
                        SetStartPoint(ppr.Value);
                        txtBearing.Focus();
                        txtBearing.SelectAll();
                    }
                }
            }
            else
            {
                SetStartPoint(w.ResultPoint);
            }
        }
    }
    #endregion
}
#endregion

#region 7. SECONDARY DIALOGS
// --- DMS CALCULATOR WINDOW ---
public class CalculatorWindow : System.Windows.Window
{
    public string Result = "";
    private TextBox txtInput;
    private bool _isDms;
    public CalculatorWindow(string initial, bool isDms)
    {
        _isDms = isDms;
        this.Title = isDms ? "DMS Calc (+/-)" : "Calc"; this.Width = 300; this.Height = 150;
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Background = UITheme.BackgroundBrush;
        Grid g = new Grid(); g.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) }); g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
        txtInput = UITheme.CreateInputBox(); txtInput.Text = initial; txtInput.Margin = new Thickness(10);
        txtInput.KeyDown += (s, e) => { if (e.Key == Key.Enter) DoCalc(); };
        Button btnOk = new Button() { Content = "=", Height = 40, Background = Brushes.Cyan }; btnOk.Click += (s, e) => DoCalc();
        g.Children.Add(txtInput); Grid.SetRow(txtInput, 0); g.Children.Add(btnOk); Grid.SetRow(btnOk, 1);
        this.Content = g; this.Loaded += (s, e) => { txtInput.Focus(); txtInput.Select(txtInput.Text.Length, 0); };
    }
    private void DoCalc()
    {
        try
        {
            if (_isDms)
            {
                string[] parts;
                if (txtInput.Text.Contains("+")) { parts = txtInput.Text.Split('+'); Result = CadMath.DmsToString(CadMath.AddSubDms(double.Parse(parts[0]), double.Parse(parts[1]), true)); }
                else if (txtInput.Text.Contains("-")) { parts = txtInput.Text.Split('-'); Result = CadMath.DmsToString(CadMath.AddSubDms(double.Parse(parts[0]), double.Parse(parts[1]), false)); }
                else Result = txtInput.Text;
            }
            else
            {
                System.Data.DataTable dt = new System.Data.DataTable(); var v = dt.Compute(txtInput.Text, ""); Result = v.ToString();
            }
            this.DialogResult = true; this.Close();
        }
        catch { MessageBox.Show("Invalid Math"); }
    }
}

// --- COORDS INPUT WINDOW ---
public class CoordsInputWindow : System.Windows.Window
{
    public Point3d ResultPoint;
    public bool PickRequested = false;
    private TextBox txtE, txtN;
    public CoordsInputWindow()
    {
        this.Title = "Start Point"; this.Width = 400; this.Height = 300;
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Background = UITheme.BackgroundBrush;
        StackPanel sp = new StackPanel() { Margin = new Thickness(15) };
        sp.Children.Add(UITheme.CreateLabel("EASTING (X)"));
        txtE = UITheme.CreateInputBox(); sp.Children.Add(txtE);
        sp.Children.Add(UITheme.CreateLabel("NORTHING (Y)"));
        txtN = UITheme.CreateInputBox(); sp.Children.Add(txtN);

        txtE.KeyDown += (s, e) => { if (e.Key == Key.Enter) txtN.Focus(); };
        txtN.KeyDown += (s, e) => { if (e.Key == Key.Enter) Submit(); };

        Button btnPick = UITheme.CreateActionBtn("PICK ON SCREEN", Brushes.Orange);
        btnPick.Click += (s, e) => {
            PickRequested = true;
            this.DialogResult = true;
            this.Close();
        };
        sp.Children.Add(btnPick);

        Button btnOk = UITheme.CreateActionBtn("OK", Brushes.Cyan);
        btnOk.Click += (s, e) => Submit();
        sp.Children.Add(btnOk);
        this.Content = sp;
        this.Loaded += (s, e) => txtE.Focus();
    }

    private void Submit()
    {
        if (double.TryParse(txtE.Text, out double x) && double.TryParse(txtN.Text, out double y))
        {
            ResultPoint = new Point3d(x, y, 0);
            PickRequested = false;
            this.DialogResult = true;
            this.Close();
        }
    }
}

public class SideShotWpfWindow : System.Windows.Window
{
    public string Bearing => txtBrg.Text; public string Distance => txtDist.Text; public string Comment => txtComm.Text;
    private TextBox txtBrg = null!, txtDist = null!, txtComm = null!;
    public SideShotWpfWindow()
    {
        this.Title = "SIDE SHOT"; this.Width = 600; this.Height = 600;
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Background = UITheme.BackgroundBrush; this.ResizeMode = ResizeMode.NoResize;
        Grid root = new Grid(); root.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
        Border header = new Border() { Background = UITheme.CardBrush, Padding = new Thickness(15) };
        header.Child = new TextBlock() { Text = "SIDE SHOT", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetRow(header, 0); root.Children.Add(header);

        Border card = UITheme.CreateCard(); card.Margin = new Thickness(20); StackPanel pnl = new StackPanel();

        Grid gBrg = new Grid(); gBrg.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) }); gBrg.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(50) });
        pnl.Children.Add(UITheme.CreateLabel("BEARING"));
        txtBrg = UITheme.CreateInputBox(); txtBrg.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; txtDist.Focus(); txtDist.SelectAll(); } };
        Button btnCBrg = new Button() { Content = "C", Height = 35 }; btnCBrg.Click += (s, e) => { CalculatorWindow c = new CalculatorWindow(txtBrg.Text, true); c.Owner = this; if (c.ShowDialog() == true) txtBrg.Text = c.Result; };
        Grid.SetColumn(txtBrg, 0); Grid.SetColumn(btnCBrg, 1); gBrg.Children.Add(txtBrg); gBrg.Children.Add(btnCBrg); pnl.Children.Add(gBrg); pnl.Children.Add(new Border() { Height = 15 });

        Grid gDst = new Grid(); gDst.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) }); gDst.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(50) });
        pnl.Children.Add(UITheme.CreateLabel("DISTANCE"));
        txtDist = UITheme.CreateInputBox(); txtDist.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; txtComm.Focus(); txtComm.SelectAll(); } };
        Button btnCDst = new Button() { Content = "C", Height = 35 }; btnCDst.Click += (s, e) => { CalculatorWindow c = new CalculatorWindow(txtDist.Text, false); c.Owner = this; if (c.ShowDialog() == true) txtDist.Text = c.Result; };
        Grid.SetColumn(txtDist, 0); Grid.SetColumn(btnCDst, 1); gDst.Children.Add(txtDist); gDst.Children.Add(btnCDst); pnl.Children.Add(gDst); pnl.Children.Add(new Border() { Height = 15 });

        pnl.Children.Add(UITheme.CreateLabel("COMMENT")); txtComm = UITheme.CreateInputBox();
        txtComm.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter) { this.DialogResult = true; this.Close(); } };
        pnl.Children.Add(txtComm);
        card.Child = pnl; Grid.SetRow(card, 1); root.Children.Add(card);

        StackPanel btns = new StackPanel() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20) };
        Button btnCancel = new Button() { Content = "CANCEL", Width = 120, Height = 45, Background = Brushes.Gray, Foreground = Brushes.White, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 20, 0) };
        Button btnOk = new Button() { Content = "OK", Width = 120, Height = 45, Background = Brushes.DimGray, Foreground = Brushes.White, FontWeight = FontWeights.Bold };
        btnCancel.Click += (s, e) => { this.DialogResult = false; this.Close(); };
        btnOk.Click += (s, e) => { this.DialogResult = true; this.Close(); };
        btns.Children.Add(btnCancel); btns.Children.Add(btnOk); Grid.SetRow(btns, 2); root.Children.Add(btns);
        this.Content = root; this.Loaded += (s, e) => txtBrg.Focus();
    }
}

public class CommentWpfWindow : System.Windows.Window
{
    public string Comment => txtComm.Text; private TextBox txtComm = null!;
    public CommentWpfWindow()
    {
        this.Title = "ADD COMMENT"; this.Width = 500; this.Height = 250;
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Background = UITheme.BackgroundBrush; this.ResizeMode = ResizeMode.NoResize;
        Grid root = new Grid(); root.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
        Border card = UITheme.CreateCard(); card.Margin = new Thickness(20); StackPanel pnl = new StackPanel();
        pnl.Children.Add(UITheme.CreateLabel("ENTER TEXT")); txtComm = UITheme.CreateInputBox();
        txtComm.KeyDown += (s, e) => { if (e.Key == Key.Enter) { this.DialogResult = true; this.Close(); } };
        pnl.Children.Add(txtComm); card.Child = pnl;
        Button btnOk = new Button() { Content = "OK", Width = 100, Height = 40, Background = UITheme.AccentColor, Foreground = Brushes.White, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 20) };
        btnOk.Click += (s, e) => { this.DialogResult = true; this.Close(); };
        Grid.SetRow(card, 0); Grid.SetRow(btnOk, 1);
        root.Children.Add(card); root.Children.Add(btnOk);
        this.Content = root; this.Loaded += (s, e) => txtComm.Focus();
    }
}
#endregion
