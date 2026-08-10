using UnityEngine;
using UnityEditor.AssetImporters;
using System.IO;

namespace SCUnity.Editor
{
    [ScriptedImporter(1, "scxml")]
    public class SCXMLImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            // Read the file contents
            string text = File.ReadAllText(ctx.assetPath);
            
            // Create a native Unity TextAsset from the contents
            TextAsset textAsset = new TextAsset(text);
            
            // Register it as the main asset object so Unity treats the .scxml file as a TextAsset
            ctx.AddObjectToAsset("main", textAsset);
            ctx.SetMainObject(textAsset);
        }
    }
}
