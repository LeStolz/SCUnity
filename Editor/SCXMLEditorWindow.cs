using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SCUnity.Editor
{
    public class SCXMLEditorWindow : EditorWindow
    {
        private SCStateMachine currentStateMachine;
        private SCXMLGraphView graphView;
        private SCXMLParser parser;

        public static void OpenWindow(SCStateMachine stateMachine)
        {
            var window = GetWindow<SCXMLEditorWindow>("SCXML Editor");
            window.Initialize(stateMachine);
        }

        private void Initialize(SCStateMachine stateMachine)
        {
            currentStateMachine = stateMachine;
            titleContent = new GUIContent($"SCXML: {stateMachine.gameObject.name}");

            LoadGraph();
        }

        private void OnEnable()
        {
            parser = new SCXMLParser();
            GenerateToolbar();
            ConstructGraphView();
        }

        private void OnDisable()
        {
            if (graphView != null)
            {
                rootVisualElement.Remove(graphView);
            }
        }

        private void ConstructGraphView()
        {
            graphView = new SCXMLGraphView(this)
            {
                name = "SCXML Graph"
            };
            // Use flex-grow so it fills the remaining space under the toolbar instead of covering it absolutely
            graphView.style.flexGrow = 1;
            rootVisualElement.Add(graphView);

            LoadGraph();
        }

        private void LoadGraph()
        {
            if (currentStateMachine != null && graphView != null && parser != null)
            {
                string xml = currentStateMachine.ScXml;
                
                // Bypass Unity's TextAsset cache because AssetDatabase.ImportAsset is asynchronous 
                // and scAsset.text will return stale data if we click Refresh immediately after saving.
                var field = typeof(SCStateMachine).GetField("scAsset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    var textAsset = field.GetValue(currentStateMachine) as TextAsset;
                    if (textAsset != null)
                    {
                        string path = AssetDatabase.GetAssetPath(textAsset);
                        if (!string.IsNullOrEmpty(path))
                        {
                            xml = System.IO.File.ReadAllText(path);
                        }
                    }
                }

                var data = parser.Parse(xml);
                graphView.PopulateView(data);
            }
        }

        private void GenerateToolbar()
        {
            var toolbar = new UnityEditor.UIElements.Toolbar();
            var refreshIcon = EditorGUIUtility.IconContent("Refresh").image as Texture2D;
            var saveIcon = EditorGUIUtility.IconContent("SaveAs").image as Texture2D;

            var refreshButton = new UnityEditor.UIElements.ToolbarButton(() => LoadGraph())
            {
                tooltip = "Refresh",
                text = refreshIcon == null ? "Reload" : ""
            };
            refreshButton.style.justifyContent = Justify.Center;
            refreshButton.style.alignItems = Align.Center;

            if (refreshIcon != null)
            {
                var img = new Image { image = refreshIcon };
                img.style.width = 16;
                img.style.height = 16;
                refreshButton.Add(img);
            }

            var saveButton = new UnityEditor.UIElements.ToolbarButton(() => SaveGraph())
            {
                tooltip = "Save",
                text = saveIcon == null ? "Save" : ""
            };
            saveButton.style.justifyContent = Justify.Center;
            saveButton.style.alignItems = Align.Center;

            if (saveIcon != null)
            {
                var img = new Image { image = saveIcon };
                img.style.width = 16;
                img.style.height = 16;
                saveButton.Add(img);
            }

            toolbar.Add(refreshButton);
            toolbar.Add(saveButton);
            rootVisualElement.Add(toolbar);
        }

        private void SaveGraph()
        {
            if (currentStateMachine != null && currentStateMachine.ScXml != null && parser != null && graphView != null && graphView.Data != null)
            {
                graphView.SyncLayoutToData();
                parser.SaveLayout(currentStateMachine, graphView.Data);

                // Write directly to file to ensure persistence if backed by TextAsset
                var field = typeof(SCStateMachine).GetField("scAsset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    var textAsset = field.GetValue(currentStateMachine) as UnityEngine.TextAsset;
                    if (textAsset != null)
                    {
                        string path = UnityEditor.AssetDatabase.GetAssetPath(textAsset);
                        if (!string.IsNullOrEmpty(path))
                        {
                            System.IO.File.WriteAllText(path, currentStateMachine.ScXml);
                            UnityEditor.AssetDatabase.ImportAsset(path);
                        }
                    }
                }

                // Refresh graph to ensure everything is synced and parsed back correctly
                LoadGraph();
            }
        }
    }
}
