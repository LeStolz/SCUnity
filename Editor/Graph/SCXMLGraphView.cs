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
        public SCXMLData Data { get; private set; }

        private SCXMLNode pendingTransitionSource;
        private Edge pendingTransitionEdge;
        private readonly Blackboard blackboard;
        private bool isPopulating = false;

        public SCXMLGraphView()
        {
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            StyleSheet styleSheet = EditorGUIUtility.Load("StyleSheets/GraphView/GraphView.css") as StyleSheet;
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
                foreach (var el in selection)
                {
                    if (el is GraphElement graphElement)
                    {
                        elementsToDelete.Add(graphElement);
                        if (graphElement is SCXMLNode node)
                        {
                            var connectedEdges = edges.ToList().OfType<SCXMLEdge>()
                                .Where(e => e.data != null && (e.data.sourceId == node.Data.id || e.data.targetId == node.Data.id))
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

                foreach (var el in elementsToDelete)
                {
                    if (el is SCXMLEdge edge)
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
            blackboard.SetPosition(new Rect(0, 0, 300, 400));
            Add(blackboard);
        }

        private void OnValidateCommand(ValidateCommandEvent ev)
        {
            if (ev.commandName == "Duplicate" || ev.commandName == "SoftDuplicate")
            {
                if (selection.OfType<SCXMLNode>().Any())
                {
                    ev.StopPropagation();
                }
            }
        }

        private void OnExecuteCommand(ExecuteCommandEvent ev)
        {
            if (ev.commandName == "Duplicate" || ev.commandName == "SoftDuplicate")
            {
                DuplicateSelectedNodes();
                ev.StopPropagation();
            }
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange graphViewChange)
        {
            if (isPopulating) return graphViewChange;

            if (graphViewChange.elementsToRemove != null && Data != null)
            {
                for (int i = 0; i < graphViewChange.elementsToRemove.Count; i++)
                {
                    var el = graphViewChange.elementsToRemove[i];
                    if (el is SCXMLNode node)
                    {
                        if (node.Data != null)
                        {
                            Data.states.RemoveAll(s => s.id == node.Data.id);
                        }

                        Data.transitions.RemoveAll(t => t.sourceId == node.Data.id || t.targetId == node.Data.id);
                    }
                    else if (el is SCXMLEdge edge)
                    {
                        if (edge.data != null)
                        {
                            Data.transitions.RemoveAll(t =>
                                t == edge.data || (
                                    t.sourceId == edge.data.sourceId &&
                                    t.targetId == edge.data.targetId &&
                                    t.@event == edge.data.@event
                                )
                            );
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

            // Global Data Model
            if (selection.Count == 0)
            {
                blackboard.title = "Global Data Model";
                blackboard.subTitle = "Variables";

                blackboard.addItemRequested = (b) =>
                {
                    int i = 1;
                    while (Data.globalDataModel.ContainsKey($"new_var_{i}")) i++;
                    Data.globalDataModel[$"new_var_{i}"] = "0";
                    UpdateBlackboard();
                };

                foreach (var kvp in Data.globalDataModel)
                {
                    string currentKey = kvp.Key;

                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };

                    var keyField = new TextField { value = currentKey, style = { flexGrow = 1 } };
                    var valField = new TextField { value = kvp.Value, style = { flexGrow = 2 } };
                    var delBtn = new Button(() =>
                    {
                        Data.globalDataModel.Remove(currentKey);
                        UpdateBlackboard();
                    })
                    { text = "x" };

                    keyField.RegisterValueChangedCallback(e =>
                    {
                        if (!Data.globalDataModel.ContainsKey(e.newValue) && !string.IsNullOrEmpty(e.newValue))
                        {
                            var oldVal = Data.globalDataModel[currentKey];
                            Data.globalDataModel.Remove(currentKey);
                            Data.globalDataModel[e.newValue] = oldVal;
                            currentKey = e.newValue;
                        }
                        else
                        {
                            keyField.SetValueWithoutNotify(currentKey);
                        }
                    });

                    valField.RegisterValueChangedCallback(e =>
                    {
                        if (Data.globalDataModel.ContainsKey(currentKey))
                        {
                            Data.globalDataModel[currentKey] = e.newValue;
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
                    blackboard.title = $"{node.Data.id}";
                    blackboard.subTitle = "State Properties";
                    blackboard.addItemRequested = null;

                    var idField = new TextField("State ID") { value = node.Data.id };
                    idField.RegisterValueChangedCallback(e =>
                    {
                        string oldId = node.Data.id;
                        string newId = e.newValue;

                        if (string.IsNullOrEmpty(newId) || (Data != null && Data.states.Any(s => s.id == newId && s != node.Data)))
                        {
                            idField.SetValueWithoutNotify(oldId);
                            return;
                        }

                        node.Data.id = newId;
                        node.title = newId;
                        blackboard.title = newId;

                        if (Data != null)
                        {
                            foreach (var transition in Data.transitions)
                            {
                                if (transition.sourceId == oldId) transition.sourceId = newId;
                                if (transition.targetId == oldId) transition.targetId = newId;
                            }
                            
                            foreach (var s in Data.states)
                            {
                                if (s.parentId == oldId) s.parentId = newId;
                            }
                        }
                    });
                    blackboard.Add(idField);

                    var typeField = new EnumField("State Type", node.Data.type);
                    typeField.RegisterValueChangedCallback(e =>
                    {
                        var newType = (StateType)e.newValue;
                        node.Data.type = newType;

                        if (newType == StateType.Initial && Data != null)
                        {
                            foreach (var s in Data.states)
                            {
                                if (s != node.Data && s.parentId == node.Data.parentId && s.type == StateType.Initial)
                                {
                                    s.type = StateType.State;
                                    if (nodes.ToList().Find(n => n is SCXMLNode scn && scn.Data == s) is SCXMLNode otherNode)
                                    {
                                        otherNode.title = otherNode.Data.id;
                                        otherNode.UpdateStyling();
                                    }
                                }
                            }
                        }

                        node.title = node.Data.id;
                        node.UpdateStyling();
                    });
                    blackboard.Add(typeField);

                    var entryField = new TextField("On Entry")
                    {
                        value = string.Join("\n", node.Data.onEntryActions),
                        multiline = true
                    };
                    entryField.RegisterValueChangedCallback(e =>
                    {
                        node.Data.onEntryActions.Clear();
                        if (!string.IsNullOrEmpty(e.newValue)) node.Data.onEntryActions.Add(e.newValue);
                    });
                    blackboard.Add(entryField);

                    var exitField = new TextField("On Exit")
                    {
                        value = string.Join("\n", node.Data.onExitActions),
                        multiline = true
                    };
                    exitField.RegisterValueChangedCallback(e =>
                    {
                        node.Data.onExitActions.Clear();
                        if (!string.IsNullOrEmpty(e.newValue)) node.Data.onExitActions.Add(e.newValue);
                    });
                    blackboard.Add(exitField);
                }
                else if (selected is SCXMLEdge edge)
                {
                    blackboard.title = $"{edge.data.@event}";
                    blackboard.subTitle = "Transition Properties";
                    blackboard.addItemRequested = null;

                    var eventField = new TextField("Event") { value = edge.data.@event };
                    eventField.RegisterValueChangedCallback(e =>
                    {
                        edge.data.@event = e.newValue;
                        edge.UpdateLabel();
                    });
                    blackboard.Add(eventField);

                    var condField = new TextField("Condition") { value = edge.data.condition };
                    condField.RegisterValueChangedCallback(e =>
                    {
                        edge.data.condition = e.newValue;
                        edge.UpdateLabel();
                    });
                    blackboard.Add(condField);

                    var actionsField = new TextField("On Transition")
                    {
                        value = string.Join("\n", edge.data.onTransitionActions),
                        multiline = true
                    };
                    actionsField.RegisterValueChangedCallback(e =>
                    {
                        edge.data.onTransitionActions.Clear();
                        if (!string.IsNullOrEmpty(e.newValue)) edge.data.onTransitionActions.Add(e.newValue);
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

        public override void BuildContextualMenu(ContextualMenuPopulateEvent ev)
        {
            base.BuildContextualMenu(ev);

            ev.menu.MenuItems().RemoveAll(a => a is DropdownMenuSeparator || (a is DropdownMenuAction action
                && (
                        action.name == "Cut" || action.name == "Copy" || action.name == "Paste" ||
                        action.name == "Duplicate" || action.name.Contains("Disconnect")
                    )
            ));

            var mousePos = contentViewContainer.WorldToLocal(ev.mousePosition);

            Group targetGroup = ev.target as Group;
            if (targetGroup == null && ev.target is VisualElement ve)
            {
                targetGroup = ve.GetFirstAncestorOfType<Group>();
            }
            string parentId = targetGroup?.title;

            ev.menu.AppendAction("Create State", a => CreateNewNode(mousePos, parentId));
            ev.menu.AppendAction("Duplicate",
                a => DuplicateSelectedNodes(),
                a => selection.OfType<SCXMLNode>().Any() ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Hidden
            );
        }

        private void DuplicateSelectedNodes()
        {
            if (Data == null) return;

            var selectedNodes = selection.OfType<SCXMLNode>().ToList();
            if (selectedNodes.Count == 0) return;

            ClearSelection();

            Dictionary<string, string> idMapping = new();
            List<SCXMLNode> newNodes = new();

            foreach (var node in selectedNodes)
            {
                var state = node.Data;

                int i = 1;
                string newId = state.id + " Copy";
                string currentId = newId;
                while (Data.states.Exists(s => s.id == currentId)) currentId = $"{newId} {i++}";

                idMapping[state.id] = currentId;

                var newState = new SCXMLStateData
                {
                    id = currentId,
                    originalId = currentId,
                    type = state.type,
                    position = state.position + new Vector2(60, 60),
                    hasSavedPosition = true,
                    parentId = state.parentId,
                    isCompound = state.isCompound,
                    onEntryActions = new List<string>(state.onEntryActions),
                    onExitActions = new List<string>(state.onExitActions),
                    dataModel = new Dictionary<string, string>(state.dataModel)
                };

                Data.states.Add(newState);

                var newNode = new SCXMLNode(newState);
                newNode.schedule.Execute(() =>
                {
                    newNode.SetPosition(new Rect(
                        newState.position.x, newState.position.y, newNode.layout.width, newNode.layout.height
                    ));
                });

                if (!string.IsNullOrEmpty(newState.parentId))
                {
                    if (graphElements.ToList().Find(e => e is Group g && g.title == newState.parentId) is Group group)
                    {
                        group.AddElement(newNode);
                    }
                    else AddElement(newNode);
                }
                else
                {
                    AddElement(newNode);
                }

                newNodes.Add(newNode);
                AddToSelection(newNode);
            }

            // Duplicate transitions between selected nodes
            foreach (var node in selectedNodes)
            {
                var state = node.Data;
                var transitionsToDuplicate = Data.transitions.Where(
                    t => t.sourceId == state.id && idMapping.ContainsKey(t.targetId)
                ).ToList();

                foreach (var t in transitionsToDuplicate)
                {
                    var newTrans = new SCXMLTransitionData
                    {
                        sourceId = idMapping[t.sourceId],
                        targetId = idMapping[t.targetId],
                        @event = t.@event,
                        condition = t.condition,
                        onTransitionActions = new List<string>(t.onTransitionActions)
                    };
                    Data.transitions.Add(newTrans);

                    var sourceNode = newNodes.Find(n => n.Data.id == newTrans.sourceId);
                    var targetNode = newNodes.Find(n => n.Data.id == newTrans.targetId);

                    if (sourceNode != null && targetNode != null)
                    {
                        var edge = new SCXMLEdge(newTrans, targetNode)
                        {
                            output = sourceNode.OutputPort,
                            input = targetNode.InputPort
                        };
                        sourceNode.OutputPort.Connect(edge);
                        targetNode.InputPort.Connect(edge);
                        AddElement(edge);

                        sourceNode.schedule.Execute(() =>
                        {
                            foreach (var conn in sourceNode.OutputPort.connections)
                            {
                                if (conn is SCXMLEdge scxmlEdge) scxmlEdge.UpdateEdgeControl();
                            }
                        });
                    }
                }
            }
        }

        private void CreateNewNode(Vector2 position, string parentId)
        {
            if (Data == null) return;

            int i = 1;
            string newId = "New State";
            string currentId = newId;
            while (Data.states.Exists(s => s.id == currentId)) currentId = $"{newId} {i++}";

            var newState = new SCXMLStateData
            {
                id = currentId,
                originalId = currentId,
                type = StateType.State,
                position = position,
                hasSavedPosition = true,
                parentId = parentId,
            };

            Data.states.Add(newState);

            var node = new SCXMLNode(newState);
            node.schedule.Execute(() =>
            {
                node.SetPosition(new Rect(position.x, position.y, node.layout.width, node.layout.height));
            });

            if (!string.IsNullOrEmpty(parentId))
            {
                if (graphElements.ToList().Find(e => e is Group g && g.title == parentId) is Group group)
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
                pickingMode = PickingMode.Ignore
            };

            AddElement(pendingTransitionEdge);
        }

        private void OnMouseMove(MouseMoveEvent ev)
        {
            if (pendingTransitionEdge != null)
            {
                pendingTransitionEdge.candidatePosition = ev.mousePosition;
                pendingTransitionEdge.UpdateEdgeControl();
            }
        }

        private void OnMouseUp(MouseUpEvent ev)
        {
            if (pendingTransitionEdge != null)
            {
                var targetNode = panel.Pick(ev.mousePosition)?.GetFirstAncestorOfType<SCXMLNode>();

                if (targetNode != null && targetNode != pendingTransitionSource)
                {
                    var newTransition = new SCXMLTransitionData
                    {
                        sourceId = pendingTransitionSource.Data.id,
                        targetId = targetNode.Data.id
                    };
                    Data?.transitions.Add(newTransition);

                    var edge = new SCXMLEdge(newTransition, targetNode)
                    {
                        output = pendingTransitionSource.OutputPort,
                        input = targetNode.InputPort
                    };
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

        public void SyncData()
        {
            if (Data == null) return;

            // Ensure Data.states only contains nodes that are actually present in the graph view
            var nodeIdsInView = new HashSet<string>(nodes.ToList().OfType<SCXMLNode>().Select(n => n.Data.id));
            Data.states.RemoveAll(s => !nodeIdsInView.Contains(s.id));

            // Similarly, ensure transitions match edges in the view
            var edgesInView = new HashSet<SCXMLTransitionData>(edges.ToList().OfType<SCXMLEdge>().Select(e => e.data));
            Data.transitions.RemoveAll(t => !edgesInView.Contains(t));

            foreach (var node in nodes)
            {
                if (node is SCXMLNode scxmlNode && scxmlNode.Data != null)
                {
                    var pos = scxmlNode.GetPosition();
                    scxmlNode.Data.position = new Vector2(pos.x, pos.y);
                }
            }
        }

        public void PopulateView(SCXMLData data)
        {
            isPopulating = true;
            Data = data;

            DeleteElements(graphElements.ToList());

            if (data == null || Data.states.Count == 0)
            {
                isPopulating = false;
                return;
            }

            Dictionary<string, SCXMLNode> nodeLookup = new();
            Dictionary<string, Group> groupLookup = new();

            // Create nodes and groups
            foreach (var state in Data.states)
            {
                var node = new SCXMLNode(state);
                nodeLookup[state.id] = node;
                AddElement(node);

                if (state.hasSavedPosition)
                {
                    node.schedule.Execute(() =>
                    {
                        node.SetPosition(new Rect(state.position.x, state.position.y, node.layout.width, node.layout.height));
                    });
                }

                if (state.isCompound)
                {
                    var group = new Group { title = state.id };
                    groupLookup[state.id] = group;
                    AddElement(group);
                    group.AddElement(node);
                }
            }

            // Assign parenting (nesting)
            foreach (var state in Data.states)
            {
                if (!string.IsNullOrEmpty(state.parentId) && groupLookup.TryGetValue(state.parentId, out Group parentGroup))
                {
                    parentGroup.AddElement(nodeLookup[state.id]);
                }
            }

            // Create edges
            foreach (var transition in Data.transitions)
            {
                if (nodeLookup.TryGetValue(transition.sourceId, out SCXMLNode sourceNode) &&
                    nodeLookup.TryGetValue(transition.targetId, out SCXMLNode targetNode))
                {
                    var edge = new SCXMLEdge(transition, targetNode)
                    {
                        output = sourceNode.OutputPort,
                        input = targetNode.InputPort
                    };
                    sourceNode.OutputPort.Connect(edge);
                    targetNode.InputPort.Connect(edge);

                    AddElement(edge);
                }
            }

            // Layout nodes (Sequential states left-to-right, Parallel orthogonal groups top-to-bottom)
            Dictionary<string, Rect> assignedPositions = new();

            Vector2 MeasureAndLayout(string stateId, float startX, float startY)
            {
                var state = Data.states.First(s => s.id == stateId);
                var children = Data.states.Where(s => s.parentId == stateId).ToList();

                // Position the node itself
                if (!state.hasSavedPosition)
                {
                    Rect rect = new(startX, startY, 160, 100);
                    assignedPositions[state.id] = rect;
                    nodeLookup[state.id].schedule.Execute(() => { nodeLookup[state.id].SetPosition(rect); });
                }
                else
                {
                    assignedPositions[state.id] = new Rect(state.position.x, state.position.y, 160, 100);
                }

                if (children.Count == 0)
                {
                    return new Vector2(160f, 100f);
                }

                // Position children
                float currentChildX = startX + 40f;
                float currentChildY = startY + 120f;
                float totalWidth = 160f;
                float totalHeight = 100f;

                float maxChildHeight = 0;
                foreach (var child in children)
                {
                    Vector2 childSize = MeasureAndLayout(child.id, currentChildX, currentChildY);
                    currentChildX += childSize.x + 60f;
                    maxChildHeight = Mathf.Max(maxChildHeight, childSize.y);
                }
                totalWidth = currentChildX - startX;
                totalHeight = 120f + maxChildHeight + 40f;

                return new Vector2(totalWidth, totalHeight);
            }

            var rootStates = Data.states.Where(s => string.IsNullOrEmpty(s.parentId)).ToList();
            float rootX = 0;
            float rootMaxHeight = 0;

            // Root states are sequential, so lay them out left to right
            foreach (var rootState in rootStates)
            {
                Vector2 size = MeasureAndLayout(rootState.id, rootX, 0);
                rootX += size.x + 60f;
                rootMaxHeight = Mathf.Max(rootMaxHeight, size.y);
            }

            // Center the graph on the nodes
            bool frameAllPending = true;
            RegisterCallback<GeometryChangedEvent>(ev =>
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
