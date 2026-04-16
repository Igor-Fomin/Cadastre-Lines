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
    public const string LAY_TXT_BRG = "BEARING";
    public const string LAY_TXT_DIST = "DISTANCE";
    public const string LAY_TXT_SYMB = "SYMB TEXT";
    public const string LAY_TXT_PTNUM = "POINT_NUMBERS";
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
    public short ColorIndex { get; set; } = 256;
    public bool Visible { get; set; } = true;

    public void Reset(short defaultColor)
    {
        Style = "Standard";
        Size = 1.0;
        IsMText = false;
        Masking = false;
        ColorIndex = defaultColor;
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
    public TextSettings TextPt { get; set; } = new TextSettings() { ColorIndex = 1 };
    public TextSettings TextComm { get; set; } = new TextSettings() { ColorIndex = 7 };

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
        TextPt.Reset(1);
        TextComm.Reset(7);
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
    public static readonly Brush GuideColor = new SolidColorBrush(Color.FromRgb(0, 200, 0));
    
    private static readonly DropShadowEffect CardShadow = new DropShadowEffect() { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.4 };
    private static readonly FontFamily MonoFont = new FontFamily("Consolas");

    public static Border CreateCard() { return new Border() { Background = CardBrush, CornerRadius = new CornerRadius(4), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10), Effect = CardShadow }; }
    public static TextBox CreateInputBox() { return new TextBox() { Background = InputBackground, Foreground = Brushes.Cyan, FontFamily = MonoFont, FontSize = 16, Height = 35, VerticalContentAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, Padding = new Thickness(5), CaretBrush = Brushes.White }; }
    public static ComboBox CreateLayerCombo() { return new ComboBox() { Height = 30, Margin = new Thickness(2), IsEditable = true, Foreground = Brushes.Black, FontSize = 12 }; }
    public static Label CreateLabel(string text) { return new Label() { Content = text, Foreground = Brushes.LightGray, FontSize = 11, FontWeight = FontWeights.Bold, Padding = new Thickness(0, 5, 0, 2) }; }
    public static TextBlock CreateFooterText(string text, Brush color) { return new TextBlock() { Text = text, Foreground = color, FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(2) }; }

    public static Button CreateLayerBtn(string key) { return new Button() { Content = key, Height = 55, Margin = new Thickness(3), FontWeight = FontWeights.Bold, FontSize = 14, BorderThickness = new Thickness(0), Foreground = Brushes.White }; }
    public static CheckBox CreateToggle(string text) { return new CheckBox() { Content = text, Foreground = Brushes.White, Margin = new Thickness(5), FontSize = 14 }; }
    public static Button CreateActionBtn(string text, Brush bg) { return new Button() { Content = text, Height = 35, Background = bg, Foreground = Brushes.White, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 5, 0, 5) }; }

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
    // Refactored Regex to handle more separators: space, dash, period, or dms symbols
    private static readonly Regex DmsRegex = new Regex(@"(\d+)[^\d\.]{1,3}\s*(\d{1,2})[^\d\.]{1,3}\s*(\d{1,2}(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static double ParseDmsToDegrees(double rawInput)
    {
        // rawInput is typically in DDD.MMSS format (e.g. 123.4506)
        int d = (int)rawInput;
        double ms = Math.Round((rawInput - d) * 10000, 4);
        int m = (int)(ms / 100);
        double s = ms % 100;
        return d + (m / 60.0) + (s / 3600.0);
    }

    public static string DegreesToDmsString(double decimalDegrees)
    {
        int d = (int)decimalDegrees;
        double remainder = (decimalDegrees - d) * 60.0;
        int m = (int)remainder;
        double s = (remainder - m) * 60.0;
        return $"{d}.{m:00}{s:00.##}".Replace(".00", "").Replace(".", "");
    }

    public static string DmsToString(double dmsValue) { return dmsValue.ToString("0.0000"); }

    public static double AddSubDms(double dms1, double dms2, bool add)
    {
        double deg1 = ParseDmsToDegrees(dms1); double deg2 = ParseDmsToDegrees(dms2);
        double resDeg = add ? (deg1 + deg2) : (deg1 - deg2);
        resDeg = resDeg % 360; if (resDeg < 0) resDeg += 360;
        
        int d = (int)resDeg;
        double rem = (resDeg - d) * 60.0;
        int m = (int)rem;
        double s = (rem - m) * 60.0;
        return double.Parse($"{d}.{m:00}{Math.Round(s):00}");
    }

    public static bool TryParseAzimuth(string input, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        input = input.Trim();

        // 1. Try advanced Regex for formats like "123 45 06", "123-45-06", "123d45m06s", "123.45.06"
        var match = DmsRegex.Match(input);
        if (match.Success)
        {
            if (double.TryParse(match.Groups[1].Value, out double d) &&
                double.TryParse(match.Groups[2].Value, out double m) &&
                double.TryParse(match.Groups[3].Value, out double s))
            {
                result = d + (m / 100.0) + (s / 10000.0);
                return true;
            }
        }

        // 2. Fallback: handle DDD.MMSS literal (e.g. 123.4506) or plain decimal
        // Replace common separators with period to help double.TryParse if it's a simple split
        string cleaned = input.Replace("-", ".").Replace(" ", ".");
        string[] parts = cleaned.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length == 3) // 123.45.06 format
        {
            if (double.TryParse(parts[0], out double d) && double.TryParse(parts[1], out double m) && double.TryParse(parts[2], out double s))
            {
                result = d + (m / 100.0) + (s / 10000.0);
                return true;
            }
        }

        if (double.TryParse(cleaned, out double val))
        {
            result = val;
            return true;
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
                new TypedValue((int)DxfCode.LayerName, CadConstants.LAY_TXT_PTNUM),
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
            if (!lt.Has(CadConstants.LAY_TXT_PTNUM)) return false;

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in btr)
            {
                Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                if (ent.Layer == CadConstants.LAY_TXT_PTNUM)
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
    private bool _isInitializing = true;
    private int _lastTabIndex = 0;
    private bool _isBusy = false;

    // Controls
    private TextBox txtAzimuth = null!, txtDistance = null!;
    private Label lblStatus = null!;
    private TextBlock txtRunningClosure = null!;
    private TextBlock txtAreaInfo = null!;
    private TextBlock lblGuide = null!;
    private TabControl mainTabs = null!;

    // Buttons
    private Button btnQ = null!, btnW = null!, btnE = null!, btnA = null!, btnS = null!, btnD = null!;

    // Settings UI
    private ComboBox cmbSound = null!;
    private CheckBox setChkAudio = null!;

    private class TextUiRow
    {
        public ComboBox CmbStyle; public TextBox TxtSize; public Button BtnColor; public CheckBox ChkMText; public CheckBox ChkMask; public CheckBox ChkVisible;
        public TextSettings SettingsRef;
        public string AssociatedLayer;
    }
    private List<TextUiRow> _textUiRows = new List<TextUiRow>();
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

        this.Loaded += (s, e) => {
            PopulateComboBoxes();
            UpdateUIFromConfig();
            _isInitializing = false;
        };

        this.Closed += CadastreWpfWindow_Closed;
    }

    private void CadastreWpfWindow_Closed(object? sender, EventArgs e)
    {
        // Cleanup resources or event subscriptions if any were added to DocumentManager
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
            AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[AutoCAD Error] {ex.Message}");
        }
        catch (System.Exception ex)
        {
            AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\n[System Error] {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        if (mainTabs != null) mainTabs.IsEnabled = !busy;
        if (txtAzimuth != null) txtAzimuth.IsEnabled = !busy;
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

    private bool EnsureQuiescent()
    {
        if (!_doc.Editor.IsQuiescent)
        {
            lblStatus.Content = "BUSY: PRESS ESC FIRST";
            lblStatus.Foreground = Brushes.OrangeRed;
            return false;
        }
        lblStatus.Foreground = Brushes.White;
        return true;
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
                tr.Commit();
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            _doc.Editor.WriteMessage($"\n[Critical] Layer initialization failed: {ex.Message}");
        }
    }

    private void InitializeCustomUI()
    {
        this.Title = "CADASTRE PRO"; this.Width = 600; this.Height = 950;
        this.Topmost = true; this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.Background = UITheme.BackgroundBrush;

        mainTabs = new TabControl() { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        mainTabs.Items.Add(new TabItem() { Header = " INPUT ", Content = BuildInputTab(), FontSize = 14, FontWeight = FontWeights.Bold });
        mainTabs.Items.Add(new TabItem() { Header = " CONFIG ", Content = BuildSettingsTab(), FontSize = 14, FontWeight = FontWeights.Bold });
        mainTabs.Items.Add(new TabItem() { Header = " ABOUT ", Content = BuildAboutTab(), FontSize = 14, FontWeight = FontWeights.Bold });
        mainTabs.SelectionChanged += MainTabs_SelectionChanged;

        Grid mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
        mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(30) });

        mainGrid.Children.Add(mainTabs); Grid.SetRow(mainTabs, 0);

        // Closure / Area Panel
        Border closureBorder = new Border() { Background = new SolidColorBrush(Color.FromRgb(25, 25, 25)), Padding = new Thickness(8) };
        StackPanel spClose = new StackPanel();
        txtRunningClosure = new TextBlock() { Text = "Misclosure: N/A", Foreground = Brushes.Cyan, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 };
        txtAreaInfo = new TextBlock() { Text = "Area: 0 m²", Foreground = Brushes.Yellow, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
        spClose.Children.Add(txtRunningClosure);
        spClose.Children.Add(txtAreaInfo);
        closureBorder.Child = spClose;
        Grid.SetRow(closureBorder, 1); mainGrid.Children.Add(closureBorder);

        Border footer = new Border() { Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)), Padding = new Thickness(5) };
        StackPanel fs = new StackPanel() { HorizontalAlignment = HorizontalAlignment.Center };
        fs.Children.Add(UITheme.CreateFooterText("PGUP: Coords | PGDN: Side Shot | INS: Comment | DEL: Undo", Brushes.WhiteSmoke));
        fs.Children.Add(UITheme.CreateFooterText("ARROWS: \u00B1180\u00B0 / \u00B190\u00B0 | QWE-ASD: Layers (Input Tab Only)", Brushes.LightGray));
        footer.Child = fs;
        Grid.SetRow(footer, 2); mainGrid.Children.Add(footer);

        Border st = new Border() { Background = UITheme.AccentColor };
        lblStatus = new Label() { Content = "USE E & N OR PICK TO START NEW LINE", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
        st.Child = lblStatus;
        Grid.SetRow(st, 3); mainGrid.Children.Add(st);

        this.Content = mainGrid;
        this.PreviewKeyDown += Window_PreviewKeyDown;
        UpdateGuideText("USE E & N OR PICK TO START NEW LINE");
    }
    #endregion

    #region Tab Building
    private object BuildInputTab()
    {
        Grid mainG = new Grid();
        mainG.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // 0: Guide
        mainG.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // 1: Data Card
        mainG.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // 2: Quick Actions Card
        mainG.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto }); // 3: Layer Card
        mainG.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });

        lblGuide = new TextBlock() { Text = "START", Foreground = UITheme.GuideColor, FontSize = 16, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 15) };
        Grid.SetRow(lblGuide, 0); mainG.Children.Add(lblGuide);

        // --- 1. DATA ENTRY CARD ---
        Border cardData = UITheme.CreateCard(); cardData.Margin = new Thickness(15, 0, 15, 10);
        StackPanel spData = new StackPanel();

        // --- Traverse Setup Row (E & N / PICK) ---
        Grid gPos = new Grid();
        gPos.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
        gPos.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
        gPos.Margin = new Thickness(0, 0, 0, 10);

        Button btnEN = UITheme.CreateActionBtn("\ud83d\udccd E & N", new SolidColorBrush(Color.FromRgb(41, 128, 185)));
        btnEN.Height = 40; btnEN.Margin = new Thickness(0, 0, 5, 0);
        btnEN.ToolTip = "Enter starting coordinates manually (Easting/Northing).";
        btnEN.Click += (s, e) => TriggerCoordsWindow();

        Button btnPick = UITheme.CreateActionBtn("\ud83d\uddb1\ufe0f PICK", new SolidColorBrush(Color.FromRgb(41, 128, 185)));
        btnPick.Height = 40; btnPick.Margin = new Thickness(5, 0, 0, 0);
        btnPick.ToolTip = "Select a starting point directly from the AutoCAD drawing screen.";
        btnPick.Click += (s, e) => ExecuteUiAction(() => ExecuteScreenPick());

        Grid.SetColumn(btnEN, 0); Grid.SetColumn(btnPick, 1);
        gPos.Children.Add(btnEN); gPos.Children.Add(btnPick);
        spData.Children.Add(gPos);

        // Bearing Toolset Header
        spData.Children.Add(UITheme.CreateLabel("AZIMUTH & ADJUSTMENTS"));

        // Visual Grouping for Bearing Toolset
        Border grpAz = new Border() { Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), CornerRadius = new CornerRadius(4), Padding = new Thickness(5), Margin = new Thickness(0, 0, 0, 10) };
        Grid gAz = new Grid(); 
        gAz.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) }); 
        gAz.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(50) });
        gAz.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(48) });
        gAz.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(48) });
        gAz.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(48) });
        gAz.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(48) });

        txtAzimuth = UITheme.CreateInputBox(); txtAzimuth.PreviewKeyDown += Input_PreviewKeyDown;
        
        Button btnCalcAz = new Button() { Content = "Calc", Height = 35, Margin = new Thickness(5, 0, 0, 0), Background = Brushes.DimGray, Foreground = Brushes.White, FontWeight = FontWeights.Bold, ToolTip = "Open DMS calculator" };
        btnCalcAz.Click += (s, e) => OpenCalculator(txtAzimuth, true);

        Button bP90 = new Button() { Content = "+90\u00B0", Width = 45, Height = 35, Margin = new Thickness(2, 0, 0, 0), Background = Brushes.DimGray, Foreground = Brushes.White, FontWeight = FontWeights.Bold, ToolTip = "\u21BB Rotate bearing +90\u00B0" };
        bP90.Click += (s, e) => { ModifyBearing(90); txtAzimuth.Focus(); txtAzimuth.SelectAll(); };
        Button bM90 = new Button() { Content = "-90\u00B0", Width = 45, Height = 35, Margin = new Thickness(2, 0, 0, 0), Background = Brushes.DimGray, Foreground = Brushes.White, FontWeight = FontWeights.Bold, ToolTip = "\u21BA Rotate bearing -90\u00B0" };
        bM90.Click += (s, e) => { ModifyBearing(-90); txtAzimuth.Focus(); txtAzimuth.SelectAll(); };
        Button bP180 = new Button() { Content = "+180\u00B0", Width = 45, Height = 35, Margin = new Thickness(2, 0, 0, 0), Background = Brushes.DimGray, Foreground = Brushes.White, FontWeight = FontWeights.Bold, ToolTip = "\u21C5 Rotate bearing +180\u00B0" };
        bP180.Click += (s, e) => { ModifyBearing(180); txtAzimuth.Focus(); txtAzimuth.SelectAll(); };
        Button bM180 = new Button() { Content = "-180\u00B0", Width = 45, Height = 35, Margin = new Thickness(2, 0, 0, 0), Background = Brushes.DimGray, Foreground = Brushes.White, FontWeight = FontWeights.Bold, ToolTip = "\u21C5 Rotate bearing -180\u00B0" };
        bM180.Click += (s, e) => { ModifyBearing(-180); txtAzimuth.Focus(); txtAzimuth.SelectAll(); };

        Grid.SetColumn(txtAzimuth, 0); Grid.SetColumn(btnCalcAz, 1);
        Grid.SetColumn(bP90, 2); Grid.SetColumn(bM90, 3); Grid.SetColumn(bP180, 4); Grid.SetColumn(bM180, 5);
        
        gAz.Children.Add(txtAzimuth); gAz.Children.Add(btnCalcAz);
        gAz.Children.Add(bP90); gAz.Children.Add(bM90); gAz.Children.Add(bP180); gAz.Children.Add(bM180);
        grpAz.Child = gAz;
        spData.Children.Add(grpAz);

        spData.Children.Add(UITheme.CreateLabel("DISTANCE (m)"));
        Grid gDist = new Grid(); gDist.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) }); gDist.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(50) });
        txtDistance = UITheme.CreateInputBox(); txtDistance.PreviewKeyDown += Input_PreviewKeyDown;
        Button btnCalcDist = new Button() { Content = "Calc", Height = 35, Margin = new Thickness(5, 0, 0, 0), Background = Brushes.DimGray, Foreground = Brushes.White, FontWeight = FontWeights.Bold, ToolTip = "Open distance calculator" };
        btnCalcDist.Click += (s, e) => OpenCalculator(txtDistance, false);
        Grid.SetColumn(txtDistance, 0); Grid.SetColumn(btnCalcDist, 1); gDist.Children.Add(txtDistance); gDist.Children.Add(btnCalcDist);
        spData.Children.Add(gDist);

        cardData.Child = spData;
        Grid.SetRow(cardData, 1); mainG.Children.Add(cardData);

        // --- 2. QUICK ACTIONS CARD ---
        Border cardQuick = UITheme.CreateCard(); cardQuick.Margin = new Thickness(15, 0, 15, 10);
        Grid gActions = new Grid();
        for (int i = 0; i < 4; i++) gActions.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });

        Button CreateQuickBtn(string text, string tip, Action action)
        {
            Button b = new Button() { Content = text, Height = 35, Margin = new Thickness(2), Background = Brushes.DimGray, Foreground = Brushes.White, FontWeight = FontWeights.Bold, ToolTip = tip };
            b.Click += (s, e) => { action(); txtAzimuth.Focus(); txtAzimuth.SelectAll(); };
            return b;
        }

        Button bUndo = CreateQuickBtn("\u21B2 Undo", "Delete last line/text (DEL)", () => ExecuteUiAction(() => UndoLastStep()));
        Button bCoords = CreateQuickBtn("\ud83d\udccd Coords", "Set/Pick Start Coordinates (PGUP)", () => TriggerCoordsWindow());
        Button bRad = CreateQuickBtn("\u2600 Side Shot", "Open Side Shot/Offset Menu (PGDN)", () => OpenSideShotForm());
        Button bComm = CreateQuickBtn("\ud83d\udcac Comment", "Add Text Comment/Symbol (INS)", () => ExecuteUiAction(() => AddTextComment(null)));

        Grid.SetColumn(bUndo, 0); Grid.SetColumn(bCoords, 1); Grid.SetColumn(bRad, 2); Grid.SetColumn(bComm, 3);
        gActions.Children.Add(bUndo); gActions.Children.Add(bCoords); gActions.Children.Add(bRad); gActions.Children.Add(bComm);

        cardQuick.Child = gActions;
        Grid.SetRow(cardQuick, 2); mainG.Children.Add(cardQuick);

        // --- 3. LAYER SELECTION CARD ---
        Border cardLay = UITheme.CreateCard(); cardLay.Margin = new Thickness(15, 0, 15, 15);
        StackPanel spLay = new StackPanel();
        spLay.Children.Add(UITheme.CreateLabel("ACTIVE LAYER (QWE ASD)"));
        Grid g = new Grid();
        g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
        for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });

        btnQ = UITheme.CreateLayerBtn("Q"); btnQ.Click += (s, e) => SetCurrentLayer(LayerConfig[Key.Q].Name, btnQ);
        btnW = UITheme.CreateLayerBtn("W"); btnW.Click += (s, e) => SetCurrentLayer(LayerConfig[Key.W].Name, btnW);
        btnE = UITheme.CreateLayerBtn("E"); btnE.Click += (s, e) => SetCurrentLayer(LayerConfig[Key.E].Name, btnE);
        btnA = UITheme.CreateLayerBtn("A"); btnA.Click += (s, e) => SetCurrentLayer(LayerConfig[Key.A].Name, btnA);
        btnS = UITheme.CreateLayerBtn("S"); btnS.Click += (s, e) => SetCurrentLayer(LayerConfig[Key.S].Name, btnS);
        btnD = UITheme.CreateLayerBtn("D"); btnD.Click += (s, e) => SetCurrentLayer(LayerConfig[Key.D].Name, btnD);

        Grid.SetRow(btnQ, 0); Grid.SetColumn(btnQ, 0); Grid.SetRow(btnW, 0); Grid.SetColumn(btnW, 1); Grid.SetRow(btnE, 0); Grid.SetColumn(btnE, 2);
        Grid.SetRow(btnA, 1); Grid.SetColumn(btnA, 0); Grid.SetRow(btnS, 1); Grid.SetColumn(btnS, 1); Grid.SetRow(btnD, 1); Grid.SetColumn(btnD, 2);

        g.Children.Add(btnQ); g.Children.Add(btnW); g.Children.Add(btnE);
        g.Children.Add(btnA); g.Children.Add(btnS); g.Children.Add(btnD);
        spLay.Children.Add(g); cardLay.Child = spLay;
        Grid.SetRow(cardLay, 3); mainG.Children.Add(cardLay);

        return mainG;
    }

    private object BuildSettingsTab()
    {
        ScrollViewer scroll = new ScrollViewer() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        StackPanel pnl = new StackPanel() { Margin = new Thickness(15) };

        Border cardT = UITheme.CreateCard(); StackPanel spT = new StackPanel();
        spT.Children.Add(UITheme.CreateLabel("TEXT CONFIGURATION"));
        Grid gHead = new Grid();
        gHead.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(60) });
        gHead.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
        gHead.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(40) });
        gHead.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(40) });
        gHead.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(40) });
        gHead.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(40) });
        gHead.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(30) });
        void AddHead(string t, int c) { var l = new Label() { Content = t, Foreground = Brushes.Gray, FontSize = 9, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center }; Grid.SetColumn(l, c); gHead.Children.Add(l); }
        AddHead("Type", 0); AddHead("Style", 1); AddHead("Size", 2); AddHead("Col", 3); AddHead("MText", 4); AddHead("Mask", 5); AddHead("Vis", 6);
        spT.Children.Add(gHead);

        _textUiRows.Clear();
        spT.Children.Add(BuildTextRow("Bearing", _config.TextBrg, CadConstants.LAY_TXT_BRG));
        spT.Children.Add(BuildTextRow("Distance", _config.TextDist, CadConstants.LAY_TXT_DIST));
        spT.Children.Add(BuildTextRow("Point #", _config.TextPt, CadConstants.LAY_TXT_PTNUM));
        spT.Children.Add(BuildTextRow("Comment", _config.TextComm, CadConstants.LAY_TXT_SYMB));

        Button btnResetT = UITheme.CreateActionBtn("RESET TEXT TO DEFAULT", Brushes.DimGray);
        btnResetT.Click += (s, e) => { _config.ResetText(); UpdateUIFromConfig(); };
        spT.Children.Add(btnResetT);
        cardT.Child = spT; pnl.Children.Add(cardT);

        Border cardO = UITheme.CreateCard(); StackPanel spO = new StackPanel();
        spO.Children.Add(UITheme.CreateLabel("GENERAL OPTIONS"));

        setChkAudio = UITheme.CreateToggle("Enable Audio Feedback");

        cmbSound = new ComboBox() { Height = 25, Margin = new Thickness(5) };
        cmbSound.ItemsSource = new List<string> { "Beep", "Asterisk", "Exclamation", "Hand", "Question" };

        spO.Children.Add(setChkAudio); spO.Children.Add(cmbSound);
        cardO.Child = spO; pnl.Children.Add(cardO);

        Button btnSave = UITheme.CreateActionBtn("SAVE SETTINGS", Brushes.Teal);
        btnSave.Click += (s, e) => ExecuteUiAction(() => SaveSettings(false));
        pnl.Children.Add(btnSave);

        PopulateComboBoxes();
        UpdateUIFromConfig();

        scroll.Content = pnl; return scroll;
    }

    private Grid BuildTextRow(string label, TextSettings ts, string layerName)
    {
        Grid g = new Grid(); g.Margin = new Thickness(0, 2, 0, 5);
        g.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(60) });
        g.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(30) });

        Label l = new Label() { Content = label, Foreground = Brushes.Cyan, VerticalAlignment = VerticalAlignment.Center, FontSize = 10 };
        ComboBox cb = UITheme.CreateLayerCombo(); cb.Height = 25;
        TextBox tb = UITheme.CreateInputBox(); tb.Height = 25; tb.FontSize = 12;
        Button bc = UITheme.CreateColorBtn(ts.ColorIndex);
        CheckBox cm = new CheckBox() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        CheckBox ck = new CheckBox() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        CheckBox cv = new CheckBox() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

        bc.Click += (s, e) => {
            Autodesk.AutoCAD.Windows.ColorDialog cd = new Autodesk.AutoCAD.Windows.ColorDialog();
            if (cd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ts.ColorIndex = cd.Color.ColorIndex;
                bc.Background = new SolidColorBrush(UITheme.GetWpfColor(ts.ColorIndex));
                bc.Content = "";
            }
        };

        Grid.SetColumn(l, 0); Grid.SetColumn(cb, 1); Grid.SetColumn(tb, 2); Grid.SetColumn(bc, 3); Grid.SetColumn(cm, 4); Grid.SetColumn(ck, 5); Grid.SetColumn(cv, 6);
        g.Children.Add(l); g.Children.Add(cb); g.Children.Add(tb); g.Children.Add(bc); g.Children.Add(cm); g.Children.Add(ck); g.Children.Add(cv);

        _textUiRows.Add(new TextUiRow() { CmbStyle = cb, TxtSize = tb, BtnColor = bc, ChkMText = cm, ChkMask = ck, ChkVisible = cv, SettingsRef = ts, AssociatedLayer = layerName });
        return g;
    }

    private object BuildAboutTab()
    {
        ScrollViewer scroll = new ScrollViewer() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        StackPanel pnl = new StackPanel() { Margin = new Thickness(15) };
        TextBlock Header(string txt) => new TextBlock() { Text = txt, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.Cyan, Margin = new Thickness(0, 15, 0, 5) };
        TextBlock Body(string txt) => new TextBlock() { Text = txt, FontSize = 12, Foreground = Brushes.WhiteSmoke, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5) };
        TextBlock Bullet(string txt) => new TextBlock() { Text = " • " + txt, FontSize = 12, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap };

        pnl.Children.Add(Header("WORKFLOW"));
        pnl.Children.Add(Body("1. PgUp: Open Coords Window (Type or Pick)."));
        pnl.Children.Add(Body("2. Enter Azimuth/Dist (Auto-Calc available)."));
        pnl.Children.Add(Body("3. Press Enter to Draw."));
        pnl.Children.Add(Body("4. Use QWE-ASD to switch layers."));

        pnl.Children.Add(Header("HOTKEYS"));
        pnl.Children.Add(Bullet("PgUp: Start Point Menu"));
        pnl.Children.Add(Bullet("PgDn: Side Shot Menu"));
        pnl.Children.Add(Bullet("Insert: Add Comment"));
        pnl.Children.Add(Bullet("Delete: Undo Last"));
        pnl.Children.Add(Bullet("Arrows: Rotate Bearing"));

        scroll.Content = pnl; return scroll;
    }
    #endregion

    #region Settings Logic
    private void SaveSettings(bool silent)
    {
        foreach (var r in _textUiRows)
        {
            r.SettingsRef.Style = r.CmbStyle.Text;
            if (double.TryParse(r.TxtSize.Text, out double d)) r.SettingsRef.Size = d;
            r.SettingsRef.IsMText = (r.ChkMText.IsChecked == true);
            r.SettingsRef.Masking = (r.ChkMask.IsChecked == true);
            r.SettingsRef.Visible = (r.ChkVisible.IsChecked == true);
            ToggleLayerVisibility(r.AssociatedLayer, r.SettingsRef.Visible);
        }

        _config.AudioFeedback = (setChkAudio.IsChecked == true);
        _config.AudioSound = cmbSound.Text;

        AppSettings.Save(_config);
        UpdateLayerButtons();

        if (!silent)
        {
            PlayAudio();
            MessageBox.Show("Saved & Applied!");
        }
        try { AcApp.DocumentManager.MdiActiveDocument.Editor.Regen(); } catch { }
    }

    private void UpdateUIFromConfig()
    {
        foreach (var r in _textUiRows)
        {
            r.CmbStyle.Text = r.SettingsRef.Style;
            r.TxtSize.Text = r.SettingsRef.Size.ToString();
            r.ChkMText.IsChecked = r.SettingsRef.IsMText;
            r.ChkMask.IsChecked = r.SettingsRef.Masking;
            r.ChkVisible.IsChecked = r.SettingsRef.Visible;
            r.BtnColor.Background = new SolidColorBrush(UITheme.GetWpfColor(r.SettingsRef.ColorIndex));
            r.BtnColor.Content = (r.SettingsRef.ColorIndex == 256 || r.SettingsRef.ColorIndex == 0) ? "By" : "";
        }
        setChkAudio.IsChecked = _config.AudioFeedback; cmbSound.SelectedItem = _config.AudioSound;
    }
    #endregion

    #region Calculation & Analysis
    private void CalculateArea()
    {
        if (_traversePath.Count < 2) { txtAreaInfo.Text = "Area: N/A"; return; }
        List<Point3d> poly = new List<Point3d>(_traversePath);
        if (poly[0].DistanceTo(poly[poly.Count - 1]) > 0.001) poly.Add(poly[0]);
        double area = 0.0;
        for (int i = 0; i < poly.Count - 1; i++)
            area += (poly[i].X * poly[i + 1].Y) - (poly[i + 1].X * poly[i].Y);
        area = Math.Abs(area) / 2.0;
        txtAreaInfo.Text = $"Area: {area:0.00} m²";
    }

    private void UpdateRunningMisclosure()
    {
        if (_traversePath.Count < 2) { txtRunningClosure.Text = "Misclosure: N/A"; return; }
        Point3d start = _traversePath[0]; Point3d end = _traversePath[_traversePath.Count - 1];
        double mis = start.DistanceTo(end);
        double dx = start.X - end.X; double dy = start.Y - end.Y;
        double rad = Math.Atan2(dy, dx); double deg = 90.0 - (rad * 180.0 / Math.PI); if (deg < 0) deg += 360.0;
        string bearing = CadMath.DegreesToDmsString(deg);
        double perim = 0; for (int i = 0; i < _traversePath.Count - 1; i++) perim += _traversePath[i].DistanceTo(_traversePath[i + 1]);
        double prec = (mis > 0.0001) ? Math.Round(perim / mis) : 0;
        txtRunningClosure.Text = $"Err: {mis:0.000}m (1:{prec}) @ {bearing}";
    }
    #endregion

    #region UI & Input Handlers
    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl && !_isInitializing)
        {
            if (_lastTabIndex == 1 && mainTabs.SelectedIndex != 1)
            {
                this.Dispatcher.BeginInvoke(new Action(() => ExecuteUiAction(() => SaveSettings(silent: true))), System.Windows.Threading.DispatcherPriority.Background);
            }
            _lastTabIndex = mainTabs.SelectedIndex;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (mainTabs.SelectedIndex == 1) return;

        if (LayerConfig.ContainsKey(e.Key))
        {
            var def = LayerConfig[e.Key];
            Button b = e.Key switch { Key.Q => btnQ, Key.W => btnW, Key.E => btnE, Key.A => btnA, Key.S => btnS, Key.D => btnD, _ => btnW };
            SetCurrentLayer(def.Name, b);
            e.Handled = true;
        }

        if (e.Key == Key.PageUp) { e.Handled = true; TriggerCoordsWindow(); }
        else if (e.Key == Key.PageDown) { e.Handled = true; OpenSideShotForm(); }
        else if (e.Key == Key.Insert) { e.Handled = true; ExecuteUiAction(() => AddTextComment(null)); }
        else if (e.Key == Key.Delete) { e.Handled = true; ExecuteUiAction(() => UndoLastStep()); }
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender == txtAzimuth)
        {
            if (e.Key == Key.Up) { ModifyBearing(90); e.Handled = true; }
            if (e.Key == Key.Down) { ModifyBearing(-90); e.Handled = true; }
            if (e.Key == Key.Right) { ModifyBearing(180); e.Handled = true; }
            if (e.Key == Key.Left) { ModifyBearing(-180); e.Handled = true; }
        }
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (string.IsNullOrWhiteSpace(txtAzimuth.Text) && string.IsNullOrWhiteSpace(txtDistance.Text))
            {
                TriggerCoordsWindow();
            }
            else
            {
                if (sender == txtAzimuth) { txtDistance.Focus(); txtDistance.SelectAll(); }
                else if (sender == txtDistance) ExecuteUiAction(() => ExecuteManualDraw());
            }
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
            Point3d newPt = DrawGeometryToDatabase(tr, btr, txtAzimuth.Text, txtDistance.Text, _currentPoint, _currentLayer);
            
            int currentNum = DwgDataManager.GetNextPointNumber(tr, _doc.Database) - 1;
            tr.Commit();

            _lastCreatedVertex = newPt; _currentPoint = newPt; _traversePath.Add(newPt);

            UpdateRunningMisclosure(); CalculateArea(); PlayAudio(); PanToPoint(newPt); _doc.Editor.UpdateScreen();
            txtAzimuth.Focus(); txtAzimuth.SelectAll();
            UpdateGuideText("LINE ADDED. NEXT?");
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
                    Entity ptTxt = CreateText(nextNum.ToString(), CadConstants.LAY_TXT_PTNUM, pt, AttachmentPoint.BottomLeft, tr, _doc.Database, _config.TextPt);
                    AddToDb(ptTxt, btr, tr);
                    DwgDataManager.SetNextPointNumber(nextNum + 1, tr, _doc.Database);
                }
                tr.Commit();
            }

            UpdateRunningMisclosure(); CalculateArea();
            UpdateGuideText("ENTER AZIMUTH/DIST");
            lblStatus.Content = "Start Set.";
            txtAzimuth.Focus();
            txtAzimuth.SelectAll();
            PanToPoint(pt);
        });
    }

    private Point3d DrawGeometryToDatabase(Transaction tr, BlockTableRecord btr, string azStr, string distStr, Point3d startPt, string layer)
    {
        double rawAz, dist;
        if (!CadMath.TryParseAzimuth(azStr, out rawAz) || !double.TryParse(distStr, out dist))
        {
            lblStatus.Content = "INVALID DATA";
            lblStatus.Foreground = Brushes.Red;
            throw new System.Exception($"Invalid Azimuth or Distance format.");
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

        double angleDeg = CadMath.ParseDmsToDegrees(rawAz);
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
        createdEntities.AddRange(CreateAnnotatedText(btr, tr, ln, rawAz, dist, cadAngleRad));

        // FIX: Check if number exists before creating
        bool exists = DwgDataManager.IsPointNumberAtLocation(endPoint, tr, btr.Database);
        if (!exists)
        {
            int nextNum = DwgDataManager.GetNextPointNumber(tr, btr.Database);
            Entity ptTxt = CreateText(nextNum.ToString(), CadConstants.LAY_TXT_PTNUM, endPoint, AttachmentPoint.BottomLeft, tr, btr.Database, _config.TextPt);
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
                    if (ent.Layer == CadConstants.LAY_TXT_PTNUM) pointDeleted = true;
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
            UpdateRunningMisclosure(); CalculateArea(); tr.Commit(); _doc.Editor.UpdateScreen();
            lblStatus.Content = "Undo performed.";
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

    private List<ObjectId> CreateAnnotatedText(BlockTableRecord btr, Transaction tr, Entity baseEnt, double rawAz, double dist, double cadAngleRad)
    {
        List<ObjectId> ids = new List<ObjectId>();
        double textRot = cadAngleRad; double normAng = cadAngleRad % (Math.PI * 2); if (normAng < 0) normAng += (Math.PI * 2);
        bool isFlipped = false; if (normAng > (Math.PI / 2) && normAng <= (3 * Math.PI / 2)) { textRot += Math.PI; isFlipped = true; }
        Point3d mid = ((Autodesk.AutoCAD.DatabaseServices.Line)baseEnt).StartPoint + (((Autodesk.AutoCAD.DatabaseServices.Line)baseEnt).EndPoint - ((Autodesk.AutoCAD.DatabaseServices.Line)baseEnt).StartPoint) / 2.0;
        double dx = Math.Cos(cadAngleRad); double dy = Math.Sin(cadAngleRad); double offsetDist = _config.TextBrg.Size * 1.2;
        Vector3d upVec = isFlipped ? new Vector3d(dy, -dx, 0) : new Vector3d(-dy, dx, 0);

        int d = (int)rawAz; int m = (int)((rawAz - d) * 100); double s = ((rawAz * 10000) % 100);
        ids.Add(AddToDb(CreateText($"{d}\u00B0{m:00}'{s:00}\"", CadConstants.LAY_TXT_BRG, mid + (upVec * offsetDist), AttachmentPoint.BottomCenter, tr, btr.Database, _config.TextBrg, textRot), btr, tr));
        ids.Add(AddToDb(CreateText(dist.ToString("0.000"), CadConstants.LAY_TXT_DIST, mid - (upVec * offsetDist), AttachmentPoint.TopCenter, tr, btr.Database, _config.TextDist, textRot), btr, tr));
        return ids;
    }

    private Entity CreateText(string content, string layer, Point3d pt, AttachmentPoint align, Transaction tr, Database db, TextSettings ts, double rotation = 0)
    {
        EnsureLayerExistsInternal(layer, null, tr, db);
        ObjectId styleId = GetTextStyleId(tr, ts.Style, db);
        if (ts.IsMText)
        {
            MText mt = new MText(); mt.Contents = content; mt.Layer = layer; mt.TextHeight = ts.Size; mt.TextStyleId = styleId;
            mt.Rotation = rotation; mt.Location = pt; mt.Attachment = align;
            mt.Color = AcColor.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, ts.ColorIndex);
            if (ts.Masking) { mt.BackgroundFill = true; mt.UseBackgroundColor = true; mt.BackgroundScaleFactor = 1.1; }
            return mt;
        }
        else
        {
            DBText dt = new DBText(); dt.TextString = content; dt.Layer = layer; dt.Height = ts.Size; dt.TextStyleId = styleId;
            dt.Rotation = rotation; dt.Position = pt; dt.Justify = AttachmentPoint.MiddleCenter; dt.AlignmentPoint = pt;
            dt.Color = AcColor.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, ts.ColorIndex);
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

    private void PopulateComboBoxes()
    {
        if (_doc == null || _doc.IsDisposed) return;
        try
        {
            List<string> layers = new List<string>(); List<string> styles = new List<string>();
            using (DocumentLock loc = _doc.LockDocument())
            using (Transaction tr = _doc.TransactionManager.StartTransaction())
            {
                LayerTable lt = (LayerTable)tr.GetObject(_doc.Database.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt) { layers.Add(((LayerTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name); }
                TextStyleTable tst = (TextStyleTable)tr.GetObject(_doc.Database.TextStyleTableId, OpenMode.ForRead);
                foreach (ObjectId id in tst) { styles.Add(((TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name); }
                tr.Commit();
            }
            layers.Sort(); styles.Sort();
            foreach (var r in _textUiRows) if (r.CmbStyle != null) r.CmbStyle.ItemsSource = styles;
        }
        catch { }
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
        _currentLayer = layerName; HighlightActiveLayer(btn); txtAzimuth.Focus();
    }

    private void HighlightActiveLayer(Button active)
    {
        Button[] btns = { btnQ, btnW, btnE, btnA, btnS, btnD };
        foreach (var b in btns) { b.BorderThickness = new Thickness(1); b.BorderBrush = Brushes.Gray; }
        active.BorderThickness = new Thickness(3); active.BorderBrush = Brushes.White;
    }

    private void UpdateGuideText(string text) { if (lblGuide != null) lblGuide.Text = text; }

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
        double current = 0;
        if (double.TryParse(txtAzimuth.Text, out current))
        {
            double decDeg = CadMath.ParseDmsToDegrees(current);
            decDeg += deltaDegrees; decDeg = decDeg % 360; if (decDeg < 0) decDeg += 360;
            txtAzimuth.Text = CadMath.DegreesToDmsString(decDeg);
            
            lblStatus.Content = $"Bearing Modified: {deltaDegrees}\u00B0";
            lblStatus.Foreground = Brushes.White;

            txtAzimuth.Focus();
            txtAzimuth.SelectAll();
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
                Entity txt = CreateText(finalComment, CadConstants.LAY_TXT_SYMB, _lastCreatedVertex, AttachmentPoint.MiddleLeft, tr, _doc.Database, _config.TextComm);
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
                    DrawGeometryToDatabase(tr, btr, ssWin.Azimuth, ssWin.Distance, _currentPoint, _currentLayer);
                    if (!string.IsNullOrEmpty(ssWin.Comment))
                    {
                        double rawAz; CadMath.TryParseAzimuth(ssWin.Azimuth, out rawAz);
                        double dist = double.Parse(ssWin.Distance);
                        double angleDeg = CadMath.ParseDmsToDegrees(rawAz);
                        double rad = (90.0 - angleDeg) * (Math.PI / 180.0);
                        Point3d endPt = new Point3d(_currentPoint.X + (dist * Math.Cos(rad)), _currentPoint.Y + (dist * Math.Sin(rad)), _currentPoint.Z);
                        Entity txt = CreateText(ssWin.Comment, CadConstants.LAY_TXT_SYMB, endPt, AttachmentPoint.MiddleLeft, tr, _doc.Database, _config.TextComm);
                        ObjectId txtId = AddToDb(txt, btr, tr);
                        if (_undoStack.Count > 0) _undoStack.Peek().Add(txtId);
                    }
                    tr.Commit(); _doc.Editor.UpdateScreen();
                }
            });
            txtAzimuth.Focus(); txtAzimuth.SelectAll();
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
        this.Hide();
        PromptPointResult ppr = _doc.Editor.GetPoint("\nPick Start Point: ");
        this.Show();
        if (ppr.Status == PromptStatus.OK) SetStartPoint(ppr.Value);
    }

    private void TriggerCoordsWindow()
    {
        this.Hide();
        CoordsInputWindow w = new CoordsInputWindow(); w.Owner = this;
        bool? res = w.ShowDialog();
        this.Show();
        if (res == true)
        {
            if (w.PickRequested)
            {
                this.Hide();
                PromptPointResult ppr = AcApp.DocumentManager.MdiActiveDocument.Editor.GetPoint("\nPick Start Point: ");
                this.Show();
                if (ppr.Status == PromptStatus.OK) SetStartPoint(ppr.Value);
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
    public string Azimuth => txtAz.Text; public string Distance => txtDist.Text; public string Comment => txtComm.Text;
    private TextBox txtAz = null!, txtDist = null!, txtComm = null!;
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

        Grid gAz = new Grid(); gAz.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) }); gAz.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(50) });
        pnl.Children.Add(UITheme.CreateLabel("AZIMUTH"));
        txtAz = UITheme.CreateInputBox(); txtAz.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; txtDist.Focus(); txtDist.SelectAll(); } };
        Button btnCAz = new Button() { Content = "C", Height = 35 }; btnCAz.Click += (s, e) => { CalculatorWindow c = new CalculatorWindow(txtAz.Text, true); c.Owner = this; if (c.ShowDialog() == true) txtAz.Text = c.Result; };
        Grid.SetColumn(txtAz, 0); Grid.SetColumn(btnCAz, 1); gAz.Children.Add(txtAz); gAz.Children.Add(btnCAz); pnl.Children.Add(gAz); pnl.Children.Add(new Border() { Height = 15 });

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
        this.Content = root; this.Loaded += (s, e) => txtAz.Focus();
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
