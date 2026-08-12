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
            ConstructToolbar();
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
            graphView = new SCXMLGraphView()
            {
                name = "SCXML Graph"
            };
            graphView.style.flexGrow = 1;
            rootVisualElement.Add(graphView);

            LoadGraph();
        }

        private void LoadGraph()
        {
            if (currentStateMachine == null || graphView == null || parser == null) return;

            string xml = currentStateMachine.ScXml;
            var data = parser.Parse(xml);
            graphView.PopulateView(data);
        }

        private void ConstructToolbar()
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
            if (
                currentStateMachine == null || currentStateMachine.ScXml == null ||
                parser == null || graphView == null || graphView.Data == null
            ) return;

            graphView.SyncData();
            parser.Save(currentStateMachine, graphView.Data);

            LoadGraph();
        }
    }
}
