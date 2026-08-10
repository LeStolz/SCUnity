using UnityEditor;
using UnityEngine;

namespace SCUnity.Editor
{
    [CustomEditor(typeof(SCStateMachine))]
    public class SCStateMachineEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();

            if (GUILayout.Button("Open in SCXML Editor", GUILayout.Height(24)))
            {
                SCXMLEditorWindow.OpenWindow((SCStateMachine)target);
            }
        }
    }
}
