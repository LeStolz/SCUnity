using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace SCUnity.Editor
{
    public class SCXMLGraphView : GraphView
    {
        private SCXMLEditorWindow window;
        public SCXMLData Data { get; private set; }

        private SCXMLNode pendingTransitionSource;
        private Edge pendingTransitionEdge;
        private Blackboard blackboard;
        private bool isPopulating = false;

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
            RegisterCallback<ValidateCommandEvent>(OnValidateCommand);
            RegisterCallback<ExecuteCommandEvent>(OnExecuteCommand);

            graphViewChanged = OnGraphViewChanged;

            deleteSelection = (operationName, askUser) =>
            {
                var elementsToDelete = new List<GraphElement>();
                foreach (var elem in selection)
                {
                    if (elem is GraphElement graphElement)
                    {
                        elementsToDelete.Add(graphElement);
                        if (graphElement is SCXMLNode node)
                        {
                            var connectedEdges = edges.ToList().OfType<SCXMLEdge>()
                                .Where(e => e.Data != null && (e.Data.SourceId == node.StateId || e.Data.TargetId == node.StateId))
                                .ToList();
                            
                            foreach (var e in connectedEdges)
                            {
                                if (!elementsToDelete.Contains(e))
                                {
                                    elementsToDelete.Add(e);
                                }
                            }
                        }
                    }
                }
                
                // Explicitly unhook edges so they are fully detached before GraphView removes them
                foreach (var elem in elementsToDelete)
                {
                    if (elem is SCXMLEdge edge)
                    {
                        edge.input?.Disconnect(edge);
                        edge.output?.Disconnect(edge);
                    }
                }

                DeleteElements(elementsToDelete);
            };

            blackboard = new Blackboard(this)
            {
                scrollable = true
            };
            blackboard.SetPosition(new Rect(10, 30, 250, 400));
            Add(blackboard);
        }

        private void OnValidateCommand(ValidateCommandEvent evt)
        {
            if (evt.commandName == "Duplicate" || evt.commandName == "SoftDuplicate")
            {
                if (selection.OfType<SCXMLNode>().Any())
                {
                    evt.StopPropagation();
                }
            }
        }

        private void OnExecuteCommand(ExecuteCommandEvent evt)
        {
            if (evt.commandName == "Duplicate" || evt.commandName == "SoftDuplicate")
            {
                DuplicateSelectedNodes();
                evt.StopPropagation();
            }
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange graphViewChange)
        {
            if (isPopulating) return graphViewChange;

            if (graphViewChange.elementsToRemove != null && Data != null)
            {
                for (int i = 0; i < graphViewChange.elementsToRemove.Count; i++)
                {
                    var elem = graphViewChange.elementsToRemove[i];
                    if (elem is SCXMLNode node)
                    {
                        if (node.Data != null)
                        {
                            Data.States.RemoveAll(s => s.Id == node.Data.Id);
                        }
                        
                        // Explicitly scrub all transitions referencing this node to prevent orphans
                        Data.Transitions.RemoveAll(t => t.SourceId == node.StateId || t.TargetId == node.StateId);
                    }
                    else if (elem is SCXMLEdge edge)
                    {
                        if (edge.Data != null)
                        {
                            Data.Transitions.RemoveAll(t => t == edge.Data || (t.SourceId == edge.Data.SourceId && t.TargetId == edge.Data.TargetId && t.Event == edge.Data.Event));
                        }
                        
                        // Force update siblings after removal
                        if (edge.output != null && edge.output.node != null)
                        {
                            var outNode = edge.output.node as SCXMLNode;
                            outNode.schedule.Execute(() => {
                                foreach (var conn in outNode.OutputPort.connections)
                                {
                                    if (conn != edge && conn is SCXMLEdge scxmlEdge) scxmlEdge.UpdateEdgeControl();
                                }
                            });
                        }
                    }
                }
            }
            return graphViewChange;
        }

        public override void AddToSelection(ISelectable selectable)
        {
            base.AddToSelection(selectable);
            UpdateBlackboard();
        }

        public override void RemoveFromSelection(ISelectable selectable)
        {
            base.RemoveFromSelection(selectable);
            UpdateBlackboard();
        }

        public override void ClearSelection()
        {
            base.ClearSelection();
            UpdateBlackboard();
        }

        private void UpdateBlackboard()
        {
            if (Data == null || blackboard == null) return;
            
            blackboard.Clear();

            if (selection.Count == 0)
            {
                // Global Data Model
                blackboard.title = "Global Data Model";
                blackboard.subTitle = "Variables";

                blackboard.addItemRequested = (b) => {
                    int i = 1;
                    while (Data.GlobalDataModel.ContainsKey($"new_var_{i}")) i++;
                    Data.GlobalDataModel[$"new_var_{i}"] = "0";
                    UpdateBlackboard();
                };

                foreach (var kvp in Data.GlobalDataModel)
                {
                    string currentKey = kvp.Key;
                    
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
                    
                    var keyField = new TextField { value = currentKey, style = { flexGrow = 1 } };
                    var valField = new TextField { value = kvp.Value, style = { flexGrow = 2 } };
                    var delBtn = new Button(() => {
                        Data.GlobalDataModel.Remove(currentKey);
                        UpdateBlackboard();
                    }) { text = "X" };

                    keyField.RegisterValueChangedCallback(e => {
                        if (!Data.GlobalDataModel.ContainsKey(e.newValue) && !string.IsNullOrEmpty(e.newValue))
                        {
                            var oldVal = Data.GlobalDataModel[currentKey];
                            Data.GlobalDataModel.Remove(currentKey);
                            Data.GlobalDataModel[e.newValue] = oldVal;
                            currentKey = e.newValue;
                        }
                    });

                    valField.RegisterValueChangedCallback(e => {
                        if (Data.GlobalDataModel.ContainsKey(currentKey))
                        {
                            Data.GlobalDataModel[currentKey] = e.newValue;
                        }
                    });

                    row.Add(keyField);
                    row.Add(valField);
                    row.Add(delBtn);
                    blackboard.Add(row);
                }
            }
            else if (selection.Count == 1)
            {
                var selected = selection[0];
                if (selected is SCXMLNode node)
                {
                    blackboard.title = $"{node.StateId}";
                    blackboard.subTitle = "State Properties";
                    blackboard.addItemRequested = null;

                    var idField = new TextField("State ID") { value = node.StateId };
                    idField.RegisterValueChangedCallback(e => {
                        string oldId = node.StateId;
                        string newId = e.newValue;
                        
                        node.StateId = newId;
                        node.Data.Id = newId;
                        node.title = newId;
                        blackboard.title = newId;

                        // Update all transitions that reference this state
                        if (Data != null)
                        {
                            if (Data.InitialStateId == oldId) Data.InitialStateId = newId;
                            
                            foreach (var transition in Data.Transitions)
                            {
                                if (transition.SourceId == oldId) transition.SourceId = newId;
                                if (transition.TargetId == oldId) transition.TargetId = newId;
                            }
                        }
                    });
                    blackboard.Add(idField);
                    
                    var typeField = new UnityEngine.UIElements.EnumField("State Type", node.Data.Type);
                    typeField.RegisterValueChangedCallback(e => {
                        var newType = (StateType)e.newValue;
                        node.Data.Type = newType;
                        
                        if (newType == StateType.Initial && Data != null)
                        {
                            if (string.IsNullOrEmpty(node.Data.ParentId))
                            {
                                Data.InitialStateId = node.Data.Id;
                            }
                            
                            foreach (var s in Data.States)
                            {
                                if (s != node.Data && s.ParentId == node.Data.ParentId && s.Type == StateType.Initial)
                                {
                                    s.Type = StateType.Normal;
                                    var otherNode = nodes.ToList().Find(n => n is SCXMLNode scn && scn.Data == s) as SCXMLNode;
                                    if (otherNode != null)
                                    {
                                        otherNode.title = otherNode.Data.Id;
                                        otherNode.UpdateStyling();
                                    }
                                }
                            }
                        }
                        else if (Data != null && string.IsNullOrEmpty(node.Data.ParentId) && Data.InitialStateId == node.Data.Id && newType != StateType.Initial)
                        {
                            Data.InitialStateId = null;
                        }

                        node.title = node.Data.Id;
                        node.UpdateStyling();
                    });
                    blackboard.Add(typeField);

                    var entryField = new TextField("On Entry") { value = string.Join("\n", node.Data.OnEntryActions), multiline = true };
                    entryField.RegisterValueChangedCallback(e => {
                        node.Data.OnEntryActions.Clear();
                        if (!string.IsNullOrEmpty(e.newValue)) node.Data.OnEntryActions.Add(e.newValue);
                    });
                    blackboard.Add(entryField);

                    var exitField = new TextField("On Exit") { value = string.Join("\n", node.Data.OnExitActions), multiline = true };
                    exitField.RegisterValueChangedCallback(e => {
                        node.Data.OnExitActions.Clear();
                        if (!string.IsNullOrEmpty(e.newValue)) node.Data.OnExitActions.Add(e.newValue);
                    });
                    blackboard.Add(exitField);
                }
                else if (selected is SCXMLEdge edge)
                {
                    blackboard.title = "Transition Properties";
                    blackboard.subTitle = "Edge Properties";
                    blackboard.addItemRequested = null;

                    var evtField = new TextField("Event") { value = edge.Data.Event };
                    evtField.RegisterValueChangedCallback(e => {
                        edge.Data.Event = e.newValue;
                        edge.UpdateLabel();
                    });
                    blackboard.Add(evtField);

                    var condField = new TextField("Condition") { value = edge.Data.Condition };
                    condField.RegisterValueChangedCallback(e => {
                        edge.Data.Condition = e.newValue;
                        edge.UpdateLabel();
                    });
                    blackboard.Add(condField);

                    var actionsField = new TextField("On Transition") { value = string.Join("\n", edge.Data.Actions), multiline = true };
                    actionsField.RegisterValueChangedCallback(e => {
                        edge.Data.Actions.Clear();
                        if (!string.IsNullOrEmpty(e.newValue)) edge.Data.Actions.Add(e.newValue);
                    });
                    blackboard.Add(actionsField);
                }
            }
            else
            {
                blackboard.title = "Multiple Selected";
                blackboard.subTitle = "";
                blackboard.addItemRequested = null;
            }
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);
            
            // Remove unsupported actions and separators
            evt.menu.MenuItems().RemoveAll(a => a is DropdownMenuSeparator || (a is DropdownMenuAction action && (action.name == "Cut" || action.name == "Copy" || action.name == "Paste" || action.name == "Duplicate" || action.name.Contains("Disconnect"))));
            
            var mousePos = contentViewContainer.WorldToLocal(evt.mousePosition);
            
            Group targetGroup = evt.target as Group;
            if (targetGroup == null && evt.target is VisualElement ve)
            {
                targetGroup = ve.GetFirstAncestorOfType<Group>();
            }
            string parentId = targetGroup != null ? targetGroup.title : null;

            // Add Context Menu Items for creating nodes
            evt.menu.AppendAction("Create State", a => CreateNewNode(mousePos, StateType.Normal, parentId));
            
            // Custom Duplicate Action
            evt.menu.AppendAction("Duplicate", a => DuplicateSelectedNodes(), a => selection.OfType<SCXMLNode>().Any() ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Hidden);
        }

        private void DuplicateSelectedNodes()
        {
            if (Data == null) return;
            
            var selectedNodes = selection.OfType<SCXMLNode>().ToList();
            if (selectedNodes.Count == 0) return;

            ClearSelection();

            Dictionary<string, string> idMapping = new Dictionary<string, string>();
            List<SCXMLNode> newNodes = new List<SCXMLNode>();

            foreach (var node in selectedNodes)
            {
                var state = node.Data;
                
                int i = 1;
                string newId = state.Id + "_Copy";
                string currentId = newId;
                while (Data.States.Exists(s => s.Id == currentId))
                {
                    currentId = $"{newId}_{i++}";
                }

                idMapping[state.Id] = currentId;

                var newState = new SCXMLStateData
                {
                    Id = currentId,
                    OriginalId = currentId,
                    Type = state.Type,
                    Position = state.Position + new Vector2(50, 50),
                    HasSavedPosition = true,
                    ParentId = state.ParentId, // Duplicate in the same group
                    IsInitial = false,
                    IsCompound = state.IsCompound,
                    OnEntryActions = new List<string>(state.OnEntryActions),
                    OnExitActions = new List<string>(state.OnExitActions),
                    DataModel = new Dictionary<string, string>(state.DataModel)
                };

                Data.States.Add(newState);
                
                var newNode = new SCXMLNode(newState, false);
                newNode.schedule.Execute(() =>
                {
                    newNode.SetPosition(new Rect(newState.Position.x, newState.Position.y, newNode.layout.width, newNode.layout.height));
                });
                
                if (!string.IsNullOrEmpty(newState.ParentId))
                {
                    var group = graphElements.ToList().Find(e => e is Group g && g.title == newState.ParentId) as Group;
                    if (group != null) group.AddElement(newNode);
                    else AddElement(newNode);
                }
                else
                {
                    AddElement(newNode);
                }

                newNodes.Add(newNode);
                AddToSelection(newNode);
            }

            // Duplicate internal transitions between selected nodes
            foreach (var node in selectedNodes)
            {
                var state = node.Data;
                var transitionsToDuplicate = Data.Transitions.Where(t => t.SourceId == state.Id && idMapping.ContainsKey(t.TargetId)).ToList();
                
                foreach (var t in transitionsToDuplicate)
                {
                    var newTrans = new SCXMLTransitionData
                    {
                        SourceId = idMapping[t.SourceId],
                        TargetId = idMapping[t.TargetId],
                        Event = t.Event,
                        Condition = t.Condition,
                        Actions = new List<string>(t.Actions)
                    };
                    Data.Transitions.Add(newTrans);
                    
                    var sourceNode = newNodes.Find(n => n.StateId == newTrans.SourceId);
                    var targetNode = newNodes.Find(n => n.StateId == newTrans.TargetId);
                    
                    if (sourceNode != null && targetNode != null)
                    {
                        var edge = new SCXMLEdge(newTrans, targetNode);
                        edge.output = sourceNode.OutputPort;
                        edge.input = targetNode.InputPort;
                        sourceNode.OutputPort.Connect(edge);
                        targetNode.InputPort.Connect(edge);
                        AddElement(edge);
                        
                        sourceNode.schedule.Execute(() => {
                            foreach (var conn in sourceNode.OutputPort.connections)
                                if (conn is SCXMLEdge scxmlEdge) scxmlEdge.UpdateEdgeControl();
                        });
                    }
                }
            }
        }

        private void CreateNewNode(Vector2 position, StateType type, string parentId)
        {
            if (Data == null) return;

            int i = 1;
            string newId = type == StateType.Final ? "FinalState" : "NewState";
            string currentId = newId;
            while (Data.States.Exists(s => s.Id == currentId))
            {
                currentId = $"{newId}_{i++}";
            }

            var newState = new SCXMLStateData
            {
                Id = currentId,
                OriginalId = currentId,
                Type = type,
                Position = position,
                HasSavedPosition = true,
                ParentId = parentId,
                IsInitial = false
            };

            Data.States.Add(newState);
            
            var node = new SCXMLNode(newState, false);
            node.schedule.Execute(() =>
            {
                node.SetPosition(new Rect(position.x, position.y, node.layout.width, node.layout.height));
            });
            
            if (!string.IsNullOrEmpty(parentId))
            {
                var group = graphElements.ToList().Find(e => e is Group g && g.title == parentId) as Group;
                if (group != null)
                {
                    group.AddElement(node);
                    return;
                }
            }
            
            AddElement(node);
        }

        private class PreviewEdge : Edge
        {
            public override bool UpdateEdgeControl()
            {
                bool result = base.UpdateEdgeControl();
                if (edgeControl != null && edgeControl.controlPoints != null && edgeControl.controlPoints.Length == 4)
                {
                    // The controlPoints property is read-only (returns an array), 
                    // but we can modify the array elements directly to force a straight line!
                    edgeControl.controlPoints[1] = edgeControl.controlPoints[0];
                    edgeControl.controlPoints[2] = edgeControl.controlPoints[3];
                }
                return result;
            }
        }

        public void StartTransition(SCXMLNode source)
        {
            if (pendingTransitionEdge != null)
            {
                RemoveElement(pendingTransitionEdge);
            }

            pendingTransitionSource = source;
            pendingTransitionEdge = new PreviewEdge
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
                // Edge candidatePosition expects coordinates in World/Panel space, just like Port.worldBound
                pendingTransitionEdge.candidatePosition = evt.mousePosition;
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
                    var newTransition = new SCXMLTransitionData { SourceId = pendingTransitionSource.StateId, TargetId = targetNode.StateId };
                    if (Data != null) Data.Transitions.Add(newTransition);
                    
                    var edge = new SCXMLEdge(newTransition, targetNode);
                    edge.output = pendingTransitionSource.OutputPort;
                    edge.input = targetNode.InputPort;
                    pendingTransitionSource.OutputPort.Connect(edge);
                    targetNode.InputPort.Connect(edge);
                    AddElement(edge);

                    // Force sibling edges in the same bundle (or reverse bundle) to recalculate curvature
                    foreach (var conn in pendingTransitionSource.OutputPort.connections)
                    {
                        if (conn is SCXMLEdge scxmlEdge) scxmlEdge.UpdateEdgeControl();
                    }
                    if (targetNode.OutputPort != null)
                    {
                        foreach (var conn in targetNode.OutputPort.connections)
                        {
                            if (conn is SCXMLEdge scxmlEdge) scxmlEdge.UpdateEdgeControl();
                        }
                    }
                }

                RemoveElement(pendingTransitionEdge);
                pendingTransitionEdge = null;
                pendingTransitionSource = null;
            }
        }

        public void SyncLayoutToData()
        {
            if (Data == null) return;

            // Ensure Data.States only contains nodes that are actually present in the graph view
            var nodeIdsInView = new HashSet<string>(nodes.ToList().OfType<SCXMLNode>().Select(n => n.StateId));
            Data.States.RemoveAll(s => !nodeIdsInView.Contains(s.Id));

            // Similarly, ensure transitions match edges in the view
            var edgesInView = new HashSet<SCXMLTransitionData>(edges.ToList().OfType<SCXMLEdge>().Select(e => e.Data));
            Data.Transitions.RemoveAll(t => !edgesInView.Contains(t));

            foreach (var node in nodes)
            {
                if (node is SCXMLNode scxmlNode && scxmlNode.Data != null)
                {
                    var pos = scxmlNode.GetPosition();
                    scxmlNode.Data.Position = new Vector2(pos.x, pos.y);
                }
            }
        }

        public void PopulateView(SCXMLData data)
        {
            isPopulating = true;
            Data = data;
            
            DeleteElements(graphElements.ToList());
            
            if (data == null || data.States.Count == 0)
            {
                isPopulating = false;
                return;
            }
            
            Debug.Log($"[SCUnity] Loaded SCXML Graph with {data.States.Count} states.");

            Dictionary<string, SCXMLNode> nodeLookup = new();
            Dictionary<string, Group> groupLookup = new();

            // 1. Create nodes and groups
            foreach (var state in data.States)
            {
                bool isInitial = (state.Id == data.InitialStateId) || state.IsInitial;
                var node = new SCXMLNode(state, isInitial);
                nodeLookup[state.Id] = node;
                AddElement(node);
                
                // UI Toolkit's layout engine can aggressively reset node positions when they are first added to the hierarchy.
                // We schedule the position assignment for the next frame, preserving the auto-calculated width/height.
                if (state.HasSavedPosition)
                {
                    node.schedule.Execute(() =>
                    {
                        node.SetPosition(new Rect(state.Position.x, state.Position.y, node.layout.width, node.layout.height));
                    });
                }

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
                    
                    // Note: Unity's native GraphView does not support nesting Group elements inside other Group elements.
                    // Doing so triggers a native "Nested group is not supported yet" console warning.
                    // For now, the visual node of the compound state is added to the parent group, 
                    // but its own inner group (which holds its children) will float on the graph level.
                }
            }

            // 3. Create edges
            foreach (var transition in data.Transitions)
            {
                if (nodeLookup.TryGetValue(transition.SourceId, out SCXMLNode sourceNode) &&
                    nodeLookup.TryGetValue(transition.TargetId, out SCXMLNode targetNode))
                {
                    var edge = new SCXMLEdge(transition, targetNode);
                    
                    edge.output = sourceNode.OutputPort;
                    edge.input = targetNode.InputPort;
                    sourceNode.OutputPort.Connect(edge);
                    targetNode.InputPort.Connect(edge);
                    
                    AddElement(edge);
                }
            }

            // 4. Layout nodes (Sequential states left-to-right, Parallel orthogonal groups top-to-bottom)
            Dictionary<string, Rect> assignedPositions = new Dictionary<string, Rect>();
            
            Vector2 MeasureAndLayout(string stateId, float startX, float startY)
            {
                var state = data.States.First(s => s.Id == stateId);
                var children = data.States.Where(s => s.ParentId == stateId).ToList();

                // 1. Position the node itself
                if (!state.HasSavedPosition)
                {
                    Rect rect = new Rect(startX, startY, 150, 100);
                    assignedPositions[state.Id] = rect;
                    nodeLookup[state.Id].schedule.Execute(() => { nodeLookup[state.Id].SetPosition(rect); });
                }
                else
                {
                    assignedPositions[state.Id] = new Rect(state.Position.x, state.Position.y, 150, 100);
                }

                if (children.Count == 0)
                {
                    return new Vector2(150f, 100f);
                }

                // 2. Position children
                float currentChildX = startX + 40f; // Inner left padding
                float currentChildY = startY + 120f; // Must be > 100f to clear the parent node's own header height!
                float totalWidth = 150f;
                float totalHeight = 100f;

                // UML Standard: Parallel state regions are stacked vertically. Normal states flow horizontally.
                bool stackVertically = (state.Type == StateType.Parallel);

                if (stackVertically)
                {
                    // Top to Bottom
                    foreach (var child in children)
                    {
                        Vector2 childSize = MeasureAndLayout(child.Id, currentChildX, currentChildY);
                        currentChildY += childSize.y + 60f; // Vertical gap between stacked groups
                        totalWidth = Mathf.Max(totalWidth, childSize.x + 80f);
                    }
                    totalHeight = (currentChildY - startY);
                }
                else
                {
                    // Left to Right
                    float maxChildHeight = 0;
                    foreach (var child in children)
                    {
                        Vector2 childSize = MeasureAndLayout(child.Id, currentChildX, currentChildY);
                        currentChildX += childSize.x + 60f; // Horizontal gap between sibling nodes/groups
                        maxChildHeight = Mathf.Max(maxChildHeight, childSize.y);
                    }
                    totalWidth = (currentChildX - startX);
                    totalHeight = 120f + maxChildHeight + 40f; // 120f (header offset) + child height + 40f (bottom padding)
                }

                return new Vector2(totalWidth, totalHeight);
            }

            var rootStates = data.States.Where(s => string.IsNullOrEmpty(s.ParentId)).ToList();
            float rootX = 0;
            float rootMaxHeight = 0;
            
            // Root states are sequential, so lay them out left to right
            foreach (var rootState in rootStates)
            {
                Vector2 size = MeasureAndLayout(rootState.Id, rootX, 0);
                rootX += size.x + 50f;
                rootMaxHeight = Mathf.Max(rootMaxHeight, size.y);
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

            UpdateBlackboard();
        }
    }
}
