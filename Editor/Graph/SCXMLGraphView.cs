using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace SCUnity.Editor
{
    public class SCXMLGraphView : GraphView
    {
        private SCXMLEditorWindow window;

        private SCXMLNode pendingTransitionSource;
        private Edge pendingTransitionEdge;

        public SCXMLGraphView(SCXMLEditorWindow editorWindow)
        {
            window = editorWindow;

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            // Add standard grid background with stylesheet for visibility
            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            // Load standard graphview styles so the grid is visible
            StyleSheet styleSheet = EditorGUIUtility.Load("StyleSheets/GraphView/GraphView.uss") as StyleSheet;
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }

            RegisterCallback<MouseMoveEvent>(OnMouseMove);
            RegisterCallback<MouseUpEvent>(OnMouseUp);
        }

        public void StartTransition(SCXMLNode source)
        {
            if (pendingTransitionEdge != null)
            {
                RemoveElement(pendingTransitionEdge);
            }

            pendingTransitionSource = source;
            pendingTransitionEdge = new Edge
            {
                output = source.OutputPort,
                pickingMode = PickingMode.Ignore // So it doesn't block mouse up events
            };
            
            AddElement(pendingTransitionEdge);
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (pendingTransitionEdge != null)
            {
                // Convert mouse position to graph local space
                Vector2 localMousePos = contentViewContainer.WorldToLocal(evt.mousePosition);
                pendingTransitionEdge.candidatePosition = localMousePos;
                // Force layout update for the edge so it draws to the cursor
                pendingTransitionEdge.UpdateEdgeControl();
            }
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            if (pendingTransitionEdge != null)
            {
                // Find node under mouse
                var targetNode = panel.Pick(evt.mousePosition)?.GetFirstAncestorOfType<SCXMLNode>();

                if (targetNode != null && targetNode != pendingTransitionSource)
                {
                    // Create actual transition
                    // We need to tell the DataModel to add a transition, but we don't have direct access here 
                    // without rebuilding or adding to the SCXMLData directly.
                    // For now, we visually complete it. Persistence will be handled later.
                    var edge = new SCXMLEdge("", "", new List<string>(), targetNode);
                    edge.output = pendingTransitionSource.OutputPort;
                    edge.input = targetNode.InputPort;
                    pendingTransitionSource.OutputPort.Connect(edge);
                    targetNode.InputPort.Connect(edge);
                    AddElement(edge);
                }

                RemoveElement(pendingTransitionEdge);
                pendingTransitionEdge = null;
                pendingTransitionSource = null;
            }
        }

        public void PopulateView(SCXMLData data)
        {
            DeleteElements(graphElements);
            
            var oldBlackboard = this.Q<Blackboard>();
            if (oldBlackboard != null) Remove(oldBlackboard);

            if (data == null || data.States.Count == 0) return;
            
            Debug.Log($"[SCUnity] Loaded SCXML Graph with {data.States.Count} states.");

            // Display Global Metadata using Blackboard
            if (data.GlobalDataModel.Count > 0)
            {
                var blackboard = new Blackboard(this)
                {
                    title = "Global Data Model",
                    subTitle = "Variables",
                    scrollable = true
                };
                
                // Position blackboard on the left
                blackboard.SetPosition(new Rect(10, 30, 200, 300));

                foreach (var kvp in data.GlobalDataModel)
                {
                    var field = new BlackboardField { text = kvp.Key, typeText = kvp.Value };
                    blackboard.Add(field);
                }
                
                Add(blackboard);
            }

            Dictionary<string, SCXMLNode> nodeLookup = new();
            Dictionary<string, Group> groupLookup = new();

            // 1. Create nodes and groups
            foreach (var state in data.States)
            {
                bool isInitial = (state.Id == data.InitialStateId) || state.IsInitial;
                var node = new SCXMLNode(state, isInitial);
                nodeLookup[state.Id] = node;
                AddElement(node);

                if (state.IsCompound)
                {
                    var group = new Group { title = state.Id };
                    groupLookup[state.Id] = group;
                    AddElement(group);
                    group.AddElement(node);
                }
            }

            // 2. Assign parenting (nesting)
            foreach (var state in data.States)
            {
                if (!string.IsNullOrEmpty(state.ParentId) && groupLookup.TryGetValue(state.ParentId, out Group parentGroup))
                {
                    parentGroup.AddElement(nodeLookup[state.Id]);

                    if (state.IsCompound && groupLookup.TryGetValue(state.Id, out Group myGroup))
                    {
                        parentGroup.AddElement(myGroup);
                    }
                }
            }

            // 3. Create edges
            foreach (var transition in data.Transitions)
            {
                if (nodeLookup.TryGetValue(transition.SourceId, out SCXMLNode sourceNode) &&
                    nodeLookup.TryGetValue(transition.TargetId, out SCXMLNode targetNode))
                {
                    var edge = new SCXMLEdge(transition.Event, transition.Condition, transition.Actions, targetNode);
                    
                    edge.output = sourceNode.OutputPort;
                    edge.input = targetNode.InputPort;
                    sourceNode.OutputPort.Connect(edge);
                    targetNode.InputPort.Connect(edge);
                    
                    AddElement(edge);
                }
            }

            // 4. Layout nodes to prevent nested states from overlapping top-level states
            Dictionary<string, int> childCounts = new Dictionary<string, int>();
            foreach (var state in data.States)
            {
                if (!string.IsNullOrEmpty(state.ParentId))
                {
                    if (!childCounts.ContainsKey(state.ParentId)) childCounts[state.ParentId] = 0;
                    childCounts[state.ParentId]++;
                }
            }

            float currentX = 0;
            float currentY = 0;
            float maxRowWidth = 1600f; 
            
            Dictionary<string, Rect> assignedPositions = new Dictionary<string, Rect>();

            // First, position all top-level states with dynamic horizontal flow layout
            foreach (var state in data.States)
            {
                if (string.IsNullOrEmpty(state.ParentId))
                {
                    int children = childCounts.ContainsKey(state.Id) ? childCounts[state.Id] : 0;
                    float totalWidth = children > 0 ? (children * 250f + 200f) : 200f;
                    
                    if (currentX > 0 && currentX + totalWidth > maxRowWidth)
                    {
                        currentX = 0;
                        currentY += 300f;
                    }
                    
                    var node = nodeLookup[state.Id];
                    Rect rect = new Rect(currentX, currentY, 200, 150);
                    node.SetPosition(rect);
                    assignedPositions[state.Id] = rect;
                    
                    currentX += totalWidth + 200f; // 200f horizontal padding between families
                }
            }

            // Next, carefully position nested states relative to their parents
            Dictionary<string, int> placedChildCounts = new Dictionary<string, int>();

            foreach (var state in data.States)
            {
                if (!string.IsNullOrEmpty(state.ParentId))
                {
                    var parentNode = nodeLookup[state.ParentId];
                    var node = nodeLookup[state.Id];
                    
                    if (!placedChildCounts.ContainsKey(state.ParentId)) placedChildCounts[state.ParentId] = 0;
                    int childIndex = placedChildCounts[state.ParentId]++;
                    
                    // Retrieve parent's explicitly assigned position (GetPosition() fails before layout pass)
                    Rect parentPos = assignedPositions.ContainsKey(state.ParentId) ? assignedPositions[state.ParentId] : new Rect(0,0,200,150);
                    
                    // Stack children horizontally inside the group, offset to the right of the parent node
                    float childX = parentPos.x + 250f + (childIndex * 250f); 
                    float childY = parentPos.y; // Keep same vertical level as parent for clean horizontal groups
                    
                    Rect childRect = new Rect(childX, childY, 200, 150);
                    node.SetPosition(childRect);
                    assignedPositions[state.Id] = childRect;
                }
            }

            // 5. Center the graph on the nodes reliably
            bool frameAllPending = true;
            RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (frameAllPending)
                {
                    FrameAll();
                    frameAllPending = false;
                }
            });
        }
    }
}
