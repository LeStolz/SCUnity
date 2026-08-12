using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace SCUnity.Editor
{
    public class SCXMLNode : Node
    {
        public Port InputPort { get; private set; }
        public Port OutputPort { get; private set; }
        public SCXMLStateData Data { get; private set; }

        public SCXMLNode(SCXMLStateData data)
        {
            Data = data;
            title = data.id.StartsWith("_initial_") ? "Initial" : data.id;

            UpdateStyling();
            CreatePorts();
            RefreshExpandedState();
        }

        public void UpdateStyling()
        {
            mainContainer.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);

            if (Data.type == StateType.Initial)
            {
                titleContainer.style.backgroundColor = new Color(0.2f, 0.6f, 0.2f, 1f);
            }
            else if (Data.type == StateType.Final)
            {
                titleContainer.style.backgroundColor = new Color(0.6f, 0.2f, 0.2f, 1f);
            }
            else if (Data.type == StateType.Parallel)
            {
                titleContainer.style.backgroundColor = new Color(0.2f, 0.2f, 0.6f, 1f);
            }
            else
            {
                titleContainer.style.backgroundColor = new StyleColor(StyleKeyword.Null);
            }
        }

        void CreatePorts()
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
            Add(InputPort);

            OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
            OutputPort.portName = "";
            OutputPort.style.position = Position.Absolute;
            OutputPort.style.top = new Length(50, LengthUnit.Percent);
            OutputPort.style.left = new Length(50, LengthUnit.Percent);
            OutputPort.style.width = 0;
            OutputPort.style.height = 0;
            OutputPort.style.opacity = 0;
            OutputPort.pickingMode = PickingMode.Ignore;
            Add(OutputPort);

            inputContainer.style.display = DisplayStyle.None;
            outputContainer.style.display = DisplayStyle.None;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);

            evt.menu.MenuItems().RemoveAll(a =>
                a is DropdownMenuSeparator
                || (a is DropdownMenuAction action && action.name.Contains("Disconnect"))
            );

            evt.menu.AppendAction("Make Transition", a =>
            {
                var graphView = GetFirstAncestorOfType<SCXMLGraphView>();
                graphView?.StartTransition(this);
            });
        }
    }
}
