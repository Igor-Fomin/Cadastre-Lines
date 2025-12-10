using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MspTransmitPlugin
{
    public class MspCommands
    {
        [CommandMethod("MSPTRANSMIT")]
        public void ExportAllObjectsWith3DPolyDistinction()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            Dictionary<string, HashSet<string>> layerData = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId objId in modelSpace)
                    {
                        Entity ent = tr.GetObject(objId, OpenMode.ForRead) as Entity;

                        if (ent != null)
                        {
                            string layerName = ent.Layer;
                            string typeName = GetSmartTypeName(ent);

                            if (!layerData.ContainsKey(layerName))
                            {
                                layerData[layerName] = new HashSet<string>();
                            }

                            layerData[layerName].Add(typeName);
                        }
                    }
                    tr.Commit();
                }

                if (layerData.Count == 0)
                {
                    ed.WriteMessage("\nModel Space is empty.");
                    return;
                }

                StringBuilder csvContent = new StringBuilder();
                csvContent.AppendLine("Layers,Objects");

                var sortedLayers = layerData.Keys.OrderBy(k => k).ToList();

                foreach (string layerName in sortedLayers)
                {
                    string objectString = string.Join(" / ", layerData[layerName]);
                    csvContent.AppendLine($"{layerName},\"{objectString}\"");
                }

                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv",
                    FileName = "Drawing_Object_List.csv",
                    Title = "Export CAD & Civil 3D Object List"
                };

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllText(saveFileDialog.FileName, csvContent.ToString(), Encoding.UTF8);
                    ed.WriteMessage($"\nSuccessfully exported list to: {saveFileDialog.FileName}");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError: {ex.Message}");
            }
        }

        // ---------------------------------------------------------
        //  HELPER: SMART NAMING (Specific 3D Polyline Check)
        // ---------------------------------------------------------
        private string GetSmartTypeName(Entity ent)
        {
            // 1. Check SPECIFICALLY for 3D Polyline first
            if (ent is Polyline3d)
                return "3D Polyline";

            // 2. Standard Polylines (LWPolyline) and "Heavy" 2D Polylines
            if (ent is Polyline)
                return "Polyline";
            if (ent is Polyline2d)
                return "2D Polyline"; // Or just "Polyline" if you prefer

            // 3. Other Standard CAD Types
            if (ent is BlockReference) return "Block Reference";
            if (ent is DBText) return "Text";
            if (ent is MText) return "MText";
            if (ent is Circle) return "Circle";
            if (ent is Arc) return "Arc";
            if (ent is Line) return "Line";
            if (ent is DBPoint) return "Point";
            if (ent is Hatch) return "Hatch";
            if (ent is Solid) return "Solid";
            if (ent is Spline) return "Spline";
            if (ent is Region) return "Region";
            if (ent is FeatureControlFrame) return "Feature Control Frame";
            if (ent is Leader) return "Leader";
            if (ent is MLeader) return "Multileader";

            // 4. Handle Civil 3D & Complex Objects dynamically
            // Clean up names like "AeccDbTinSurface" -> "Tin Surface"
            string rawName = ent.GetType().Name;
            rawName = rawName.Replace("AcDb", "")
                             .Replace("AeccDb", "")
                             .Replace("Aecc", "")
                             .Replace("Imp", "");

            string friendlyName = Regex.Replace(rawName, "(\\B[A-Z])", " $1");

            return friendlyName.Trim();
        }
    }
}