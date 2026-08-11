using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace SCUnity.Editor
{
    public class SCXMLNode : Node
    {
        public string StateId { get; set; }
        public string StateType { get; private set; } // "state", "initial", "final", "parallel"

        public Port InputPort { get; private set; }
        public Port OutputPort { get; private set; }
        public SCXMLStateData Data { get; private set; }

        public SCXMLNode(SCXMLStateData data, bool isInitial)
        {
            Data = data;
            StateId = data.Id;
            StateType = data.Type.ToString().ToLower();
            title = data.Id.StartsWith("_initial_") ? "Initial" : data.Id;
            
            UpdateStyling();

            CreatePorts();
            PopulateData(data);
        }

        public void UpdateStyling()
        {
            mainContainer.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);

            bool isGlobalInitial = false;
            var graphView = GetFirstAncestorOfType<SCXMLGraphView>();
            if (graphView != null && graphView.Data != null && graphView.Data.InitialStateId == Data.Id) isGlobalInitial = true;

            if (Data.Type == SCUnity.Editor.StateType.Initial || isGlobalInitial)
            {
                titleContainer.style.backgroundColor = new Color(0.1f, 0.5f, 0.1f, 1f);
            }
            else if (Data.Type == SCUnity.Editor.StateType.Final)
            {
                titleContainer.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f, 1f);
            }
            else if (Data.Type == SCUnity.Editor.StateType.Parallel)
            {
                titleContainer.style.backgroundColor = new Color(0.2f, 0.2f, 0.6f, 1f);
            }
            else
            {
                titleContainer.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f); // Default Unity Node title color
            }
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

            evt.menu.MenuItems().RemoveAll(a => a is DropdownMenuSeparator || (a is DropdownMenuAction action && action.name.Contains("Disconnect")));

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
            // We no longer populate onentry, onexit, or data models on the node itself.
            // This is now handled by the contextual Inspector (Blackboard).
            RefreshExpandedState();
        }
    }
}
