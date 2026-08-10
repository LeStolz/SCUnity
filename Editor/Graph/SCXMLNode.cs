using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace SCUnity.Editor
{
    public class SCXMLNode : Node
    {
        public string StateId { get; private set; }
        public string StateType { get; private set; } // "state", "initial", "final", "parallel"

        public Port InputPort { get; private set; }
        public Port OutputPort { get; private set; }

        public SCXMLNode(SCXMLStateData data, bool isInitial)
        {
            StateId = data.Id;
            StateType = data.Type.ToString().ToLower();
            title = data.Id;
            // Restore default GraphView styling but fully opaque
            mainContainer.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);

            if (isInitial || data.Type == SCUnity.Editor.StateType.Initial)
            {
                titleContainer.style.backgroundColor = new Color(0.1f, 0.5f, 0.1f, 1f);
            }
            else if (data.Type == SCUnity.Editor.StateType.Final)
            {
                titleContainer.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f, 1f);
            }
            else if (data.Type == SCUnity.Editor.StateType.Parallel)
            {
                titleContainer.style.backgroundColor = new Color(0.2f, 0.2f, 0.6f, 1f);
            }

            CreatePorts();
            PopulateData(data);
        }

        private void CreatePorts()
        {
            // We use standard horizontal ports but hide them completely in the exact center of the node
            InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            InputPort.portName = "";
            InputPort.style.position = Position.Absolute;
            InputPort.style.top = new Length(50, LengthUnit.Percent);
            InputPort.style.left = new Length(50, LengthUnit.Percent);
            InputPort.style.width = 0;
            InputPort.style.height = 0;
            InputPort.style.opacity = 0;
            InputPort.pickingMode = PickingMode.Ignore;
            this.Add(InputPort); // Add to Node directly, so 50% is the center of the ENTIRE node

            OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
            OutputPort.portName = "";
            OutputPort.style.position = Position.Absolute;
            OutputPort.style.top = new Length(50, LengthUnit.Percent);
            OutputPort.style.left = new Length(50, LengthUnit.Percent);
            OutputPort.style.width = 0;
            OutputPort.style.height = 0;
            OutputPort.style.opacity = 0;
            OutputPort.pickingMode = PickingMode.Ignore;
            this.Add(OutputPort);

            // Hide the default port containers to reclaim space
            inputContainer.style.display = DisplayStyle.None;
            outputContainer.style.display = DisplayStyle.None;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);

            evt.menu.AppendAction("Make Transition", a =>
            {
                var graphView = GetFirstAncestorOfType<SCXMLGraphView>();
                if (graphView != null)
                {
                    graphView.StartTransition(this);
                }
            });
        }

        private void PopulateData(SCXMLStateData data)
        {
            foreach (var kvp in data.DataModel)
            {
                var label = new Label($"{kvp.Key} = {kvp.Value}");
                label.style.color = new Color(0.8f, 0.8f, 0.8f);
                label.style.paddingLeft = 15;
                extensionContainer.Add(label);
            }

            foreach (var entry in data.OnEntryActions)
            {
                AddActionFoldout("onentry", entry);
            }

            foreach (var exit in data.OnExitActions)
            {
                AddActionFoldout("onexit", exit);
            }

            RefreshExpandedState();
        }

        private void AddActionFoldout(string titleText, string innerXml)
        {
            var foldout = new Foldout
            {
                text = titleText,
                value = false // Collapsed by default
            };

            var textElement = new Label(innerXml);
            textElement.style.whiteSpace = WhiteSpace.Normal; // Allows wrapping or preserves spacing
            textElement.style.color = new Color(0.7f, 0.7f, 0.7f);

            foldout.Add(textElement);
            extensionContainer.Add(foldout);
            RefreshExpandedState();
        }
    }
}
