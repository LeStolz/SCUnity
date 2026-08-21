using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace SCUnity.Editor
{
    public class SCXMLParser
    {
        public static readonly XNamespace EditorNs = "http://lepoopz.com/editor";

        public SCXMLData Parse(string xmlString)
        {
            var data = new SCXMLData();
            if (string.IsNullOrEmpty(xmlString)) return data;

            try
            {
                XDocument doc = XDocument.Parse(xmlString);
                XElement root = doc.Root;
                if (root == null || root.Name.LocalName != "scxml")
                {
                    Debug.LogError("Invalid SCXML root element.");
                    return data;
                }

                ParseDataModel(root, data.globalDataModel);
                ParseStateLevel(root, null, data);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error parsing SCXML: {e.Message}");
            }

            return data;
        }

        private static void ParseDataModel(XElement element, Dictionary<string, string> dataModel)
        {
            var dataModelEl = element.Element(element.Name.Namespace + "datamodel");
            if (dataModelEl != null)
            {
                foreach (var dataEl in dataModelEl.Elements(element.Name.Namespace + "data"))
                {
                    string dataId = dataEl.Attribute("id")?.Value;
                    string dataExpr = dataEl.Attribute("expr")?.Value;
                    if (!string.IsNullOrEmpty(dataId))
                    {
                        dataModel[dataId] = dataExpr;
                    }
                }
            }
        }

        private void ParseStateLevel(XElement parentEl, string parentId, SCXMLData data)
        {
            var elements = parentEl.Elements().ToList();

            foreach (var el in elements)
            {
                string localName = el.Name.LocalName;
                if (Enum.GetNames(typeof(StateType)).Contains(localName, StringComparer.OrdinalIgnoreCase))
                {
                    ParseState(el, parentId, data);
                }
            }
        }

        private void ParseState(XElement stateEl, string parentId, SCXMLData data)
        {
            string id = stateEl.Attribute("id")?.Value;
            string typeString = stateEl.Name.LocalName;
            StateType type = Enum.TryParse(typeString, true, out StateType parsedType) ? parsedType : StateType.State;

            if (type == StateType.State && string.IsNullOrEmpty(parentId))
            {
                var rootInitial = stateEl.Parent?.Attribute("initial")?.Value;
                if (rootInitial != null && rootInitial == id)
                {
                    type = StateType.Initial;
                }
            }

            if (string.IsNullOrEmpty(id))
            {
                id = $"_{typeString}_{Guid.NewGuid().ToString()[..5]}";
            }

            bool isCompound = stateEl.Elements().Any(
                e => Enum.GetNames(typeof(StateType)).Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase)
            );

            var stateData = new SCXMLStateData
            {
                id = id,
                originalId = id,
                type = type,
                parentId = parentId,
                isCompound = isCompound
            };

            var xAttr = stateEl.Attribute(EditorNs + "x");
            var yAttr = stateEl.Attribute(EditorNs + "y");
            if (xAttr != null && float.TryParse(xAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
            {
                stateData.position.x = x;
                stateData.hasSavedPosition = true;
            }
            if (yAttr != null && float.TryParse(yAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                stateData.position.y = y;
                stateData.hasSavedPosition = true;
            }

            ParseDataModel(stateEl, stateData.dataModel);

            foreach (var onentry in stateEl.Elements(stateEl.Name.Namespace + "onentry"))
            {
                stateData.onEntryActions.Add(GetCleanInnerXml(onentry));
            }
            foreach (var onexit in stateEl.Elements(stateEl.Name.Namespace + "onexit"))
            {
                stateData.onExitActions.Add(GetCleanInnerXml(onexit));
            }

            data.states.Add(stateData);

            foreach (var transition in stateEl.Elements(stateEl.Name.Namespace + "transition"))
            {
                string targetId = transition.Attribute("target")?.Value;
                string ev = transition.Attribute("event")?.Value;
                string cond = transition.Attribute("cond")?.Value;

                List<string> actions = new();
                var innerElements = transition.Elements().ToList();
                if (innerElements.Count > 0)
                {
                    actions.Add(GetCleanInnerXml(transition));
                }

                if (!string.IsNullOrEmpty(targetId))
                {
                    data.transitions.Add(new SCXMLTransitionData
                    {
                        sourceId = stateData.id,
                        targetId = targetId,
                        @event = ev,
                        condition = cond,
                        originalTargetId = targetId,
                        originalEvent = ev,
                        originalCondition = cond,
                        onTransitionActions = actions
                    });
                }
            }

            ParseStateLevel(stateEl, id, data);
        }

        private string GetCleanInnerXml(XElement element)
        {
            var copy = new XElement(element);
            foreach (var el in copy.DescendantsAndSelf())
            {
                el.Name = el.Name.LocalName;
                var attrs = el.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
                el.ReplaceAttributes(attrs);
            }
            return string.Join("\n", copy.Elements().Select(e => e.ToString()));
        }

        public void Save(SCStateMachine stateMachine, SCXMLData data)
        {
            try
            {
                XDocument doc = XDocument.Parse(stateMachine.ScXml);
                XElement root = doc.Root;
                if (root == null) return;

                var nsAttr = root.Attribute(XNamespace.Xmlns + "editor");
                if (nsAttr == null)
                {
                    root.Add(new XAttribute(XNamespace.Xmlns + "editor", EditorNs.NamespaceName));
                }

                SyncDataModel(root, data.globalDataModel);
                UpsertStates(root, data);
                RemoveDeletedStates(root, data.states);

                stateMachine.ScXml = doc.ToString();

                UnityEditor.EditorUtility.SetDirty(stateMachine);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error saving SCXML layout: {e.Message}");
            }
        }

        private void UpsertStates(XElement root, SCXMLData data)
        {
            root.Attribute("initial")?.Remove();

            foreach (var state in data.states)
            {
                var element = FindStateElement(root, state);
                string targetTag = Enum.GetName(typeof(StateType), state.type).ToLower();

                if (state.type == StateType.Initial && string.IsNullOrEmpty(state.parentId))
                {
                    targetTag = "state";
                    root.SetAttributeValue("initial", state.id);
                }

                // Element doesn't exist in XML, meaning it was created in the editor
                if (element == null)
                {
                    element = new XElement(root.Name.Namespace + targetTag);
                    element.SetAttributeValue("id", state.id);
                    state.originalId = state.id;

                    if (string.IsNullOrEmpty(state.parentId))
                    {
                        root.Add(element);
                    }
                    else
                    {
                        var parentElement = FindElementById(root, state.parentId);
                        if (parentElement != null) parentElement.Add(element);
                        else root.Add(element);
                    }
                }
                else
                {
                    if (element.Attribute("id") == null)
                    {
                        element.SetAttributeValue("id", state.id);
                        state.originalId = state.id;
                    }

                    if (state.id != (state.originalId ?? state.id))
                    {
                        element.SetAttributeValue("id", state.id);
                        state.originalId = state.id;
                    }

                    if (element.Name.LocalName != targetTag)
                    {
                        element.Name = element.Name.Namespace + targetTag;
                    }
                }

                element.SetAttributeValue(EditorNs + "x", state.position.x.ToString("F1", CultureInfo.InvariantCulture));
                element.SetAttributeValue(EditorNs + "y", state.position.y.ToString("F1", CultureInfo.InvariantCulture));

                SyncActionBlock(element, "onentry", state.onEntryActions);
                SyncActionBlock(element, "onexit", state.onExitActions);

                var validTransitions = data.transitions.Where(t => t.sourceId == state.id).ToList();
                SyncTransitions(element, validTransitions);

                SyncDataModel(element, state.dataModel);
            }
        }

        private void RemoveDeletedStates(XElement parent, List<SCXMLStateData> states)
        {
            var validIds = new HashSet<string>(states.Select(s => s.originalId ?? s.id));
            var elements = parent.Elements().ToList();
            foreach (var el in elements)
            {
                if (Enum.GetNames(typeof(StateType)).Contains(el.Name.LocalName, StringComparer.OrdinalIgnoreCase))
                {
                    var idAttr = el.Attribute("id");
                    if (idAttr != null && !validIds.Contains(idAttr.Value))
                    {
                        el.Remove();
                    }
                    else if (idAttr == null)
                    {
                        el.Remove();
                    }
                    else
                    {
                        RemoveDeletedStates(el, states);
                    }
                }
            }
        }

        private void SyncDataModel(XElement element, Dictionary<string, string> dataModel)
        {
            var dataModelEl = element.Element(element.Name.Namespace + "datamodel");
            if (dataModel.Count > 0 && dataModelEl == null)
            {
                dataModelEl = new XElement(element.Name.Namespace + "datamodel");
                element.AddFirst(dataModelEl);
            }

            if (dataModelEl != null)
            {
                var currentKeys = new HashSet<string>(dataModel.Keys);
                var dataElements = dataModelEl.Elements(element.Name.Namespace + "data").ToList();

                foreach (var dataEl in dataElements)
                {
                    var idAttr = dataEl.Attribute("id");
                    if (idAttr != null && !currentKeys.Contains(idAttr.Value))
                    {
                        dataEl.Remove();
                    }
                }

                foreach (var kvp in dataModel)
                {
                    var dataEl = dataModelEl
                        .Elements(element.Name.Namespace + "data")
                        .FirstOrDefault(e => e.Attribute("id")?.Value == kvp.Key);

                    if (dataEl == null)
                    {
                        dataEl = new XElement(
                            element.Name.Namespace + "data",
                            new XAttribute("id", kvp.Key),
                            new XAttribute("expr", kvp.Value)
                        );
                        dataModelEl.Add(dataEl);
                    }
                    else
                    {
                        dataEl.SetAttributeValue("expr", kvp.Value);
                    }
                }

                if (!dataModelEl.HasElements) dataModelEl.Remove();
            }
        }

        private void SyncActionBlock(XElement stateEl, string actionName, List<string> newContents)
        {
            var actionEl = stateEl.Element(stateEl.Name.Namespace + actionName);
            if (
                newContents == null || newContents.Count == 0 ||
                (newContents.Count == 1 && string.IsNullOrWhiteSpace(newContents[0]))
            )
            {
                actionEl?.Remove();
                return;
            }

            if (actionEl == null)
            {
                actionEl = new XElement(stateEl.Name.Namespace + actionName);
                stateEl.AddFirst(actionEl);
            }

            actionEl.RemoveNodes();
            foreach (var content in newContents)
            {
                if (string.IsNullOrWhiteSpace(content)) continue;
                try
                {
                    var parsed = XElement.Parse($"<root xmlns='{stateEl.Name.Namespace.NamespaceName}'>{content}</root>");
                    actionEl.Add(parsed.Nodes());
                }
                catch
                {
                    actionEl.Add(new XText(content));
                }
            }
        }

        private void SyncTransitions(XElement stateEl, List<SCXMLTransitionData> validTransitions)
        {
            var transElements = stateEl.Elements(stateEl.Name.Namespace + "transition").ToList();

            foreach (var transEl in transElements)
            {
                string target = transEl.Attribute("target")?.Value;
                string ev = transEl.Attribute("event")?.Value;
                string cond = transEl.Attribute("cond")?.Value;

                var matchingData = validTransitions.FirstOrDefault(t =>
                    (t.originalTargetId ?? t.targetId) == target &&
                    (t.originalEvent ?? t.@event) == ev &&
                    (t.originalCondition ?? t.condition) == cond
                );

                if (matchingData == null)
                {
                    transEl.Remove();
                    continue;
                }

                if (!string.IsNullOrEmpty(matchingData.@event)) transEl.SetAttributeValue("event", matchingData.@event);
                else transEl.Attribute("event")?.Remove();

                if (!string.IsNullOrEmpty(matchingData.condition)) transEl.SetAttributeValue("cond", matchingData.condition);
                else transEl.Attribute("cond")?.Remove();

                transEl.SetAttributeValue("target", matchingData.targetId);

                matchingData.originalTargetId = matchingData.targetId;
                matchingData.originalEvent = matchingData.@event;
                matchingData.originalCondition = matchingData.condition;

                if (
                    matchingData.onTransitionActions != null
                    && matchingData.onTransitionActions.Count > 0
                    && !string.IsNullOrWhiteSpace(matchingData.onTransitionActions[0])
                )
                {
                    transEl.RemoveNodes();
                    foreach (var content in matchingData.onTransitionActions)
                    {
                        if (string.IsNullOrWhiteSpace(content)) continue;
                        try
                        {
                            var parsed = XElement.Parse($"<root xmlns='{stateEl.Name.Namespace.NamespaceName}'>{content}</root>");
                            transEl.Add(parsed.Nodes());
                        }
                        catch
                        {
                            transEl.Add(new XText(content));
                        }
                    }
                }
                else
                {
                    transEl.RemoveNodes();
                }

                validTransitions.Remove(matchingData);
            }

            // Add newly created transitions
            foreach (var newTrans in validTransitions)
            {
                var newTransEl = new XElement(stateEl.Name.Namespace + "transition");
                newTransEl.SetAttributeValue("target", newTrans.targetId);
                if (!string.IsNullOrEmpty(newTrans.@event)) newTransEl.SetAttributeValue("event", newTrans.@event);
                if (!string.IsNullOrEmpty(newTrans.condition)) newTransEl.SetAttributeValue("cond", newTrans.condition);
                stateEl.Add(newTransEl);
            }
        }

        private XElement FindElementById(XElement parent, string id)
        {
            foreach (var el in parent.Elements())
            {
                if (Enum.GetNames(typeof(StateType)).Contains(el.Name.LocalName, StringComparer.OrdinalIgnoreCase))
                {
                    var idAttr = el.Attribute("id");
                    if (idAttr != null && idAttr.Value == id)
                    {
                        return el;
                    }
                    var childMatch = FindElementById(el, id);
                    if (childMatch != null) return childMatch;
                }
            }
            return null;
        }

        private XElement FindStateElement(XElement root, SCXMLStateData state)
        {
            string searchId = state.originalId ?? state.id;
            var exactMatch = FindElementById(root, searchId);
            if (exactMatch != null) return exactMatch;

            if (searchId.StartsWith("_"))
            {
                XElement parentEl = string.IsNullOrEmpty(state.parentId) ? root : FindElementById(root, state.parentId);
                if (parentEl != null)
                {
                    string targetTag = Enum.GetName(typeof(StateType), state.type).ToLower();
                    return parentEl.Elements().FirstOrDefault(e =>
                        e.Name.LocalName.Equals(targetTag, StringComparison.OrdinalIgnoreCase) &&
                        e.Attribute("id") == null
                    );
                }
            }

            return null;
        }
    }
}
