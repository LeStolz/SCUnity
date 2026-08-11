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

                // Check for initial attribute on root
                var initialAttr = root.Attribute("initial");
                if (initialAttr != null)
                {
                    data.InitialStateId = initialAttr.Value;
                }

                // Parse global Datamodel
                var rootDatamodel = root.Element(root.Name.Namespace + "datamodel");
                if (rootDatamodel != null)
                {
                    foreach (var dataEl in rootDatamodel.Elements(root.Name.Namespace + "data"))
                    {
                        string dataId = dataEl.Attribute("id")?.Value;
                        string dataExpr = dataEl.Attribute("expr")?.Value;
                        if (!string.IsNullOrEmpty(dataId))
                        {
                            data.GlobalDataModel[dataId] = dataExpr;
                        }
                    }
                }

                ParseStateLevel(root, null, data);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error parsing SCXML: {e.Message}");
            }

            return data;
        }

        private void ParseStateLevel(XElement parentElement, string parentId, SCXMLData data)
        {
            var elements = parentElement.Elements().ToList();

            foreach (var el in elements)
            {
                string localName = el.Name.LocalName;
                if (localName == "state" || localName == "parallel" || localName == "initial" || localName == "final")
                {
                    ParseState(el, parentId, data);
                }
            }
        }

        private void ParseState(XElement stateElement, string parentId, SCXMLData data)
        {
            string id = stateElement.Attribute("id")?.Value;
            string typeString = stateElement.Name.LocalName;

            StateType type = StateType.Normal;
            if (typeString == "parallel") type = StateType.Parallel;
            else if (typeString == "initial") type = StateType.Initial;
            else if (typeString == "final") type = StateType.Final;

            if (string.IsNullOrEmpty(id))
            {
                id = $"_{typeString}_{Guid.NewGuid().ToString().Substring(0, 5)}";
            }

            bool isCompound = stateElement.Elements().Any(e =>
                e.Name.LocalName == "state" || e.Name.LocalName == "parallel" || e.Name.LocalName == "final");

            var stateData = new SCXMLStateData
            {
                Id = id,
                OriginalId = id,
                Type = type,
                ParentId = parentId,
                IsCompound = isCompound
            };

            var xAttr = stateElement.Attribute(EditorNs + "x");
            var yAttr = stateElement.Attribute(EditorNs + "y");
            if (xAttr != null && float.TryParse(xAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
            {
                stateData.Position.x = x;
                stateData.HasSavedPosition = true;
            }
            if (yAttr != null && float.TryParse(yAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                stateData.Position.y = y;
                stateData.HasSavedPosition = true;
            }

            // Check if this state itself has an 'initial' attribute
            var initialAttr = stateElement.Attribute("initial");
            if (initialAttr != null)
            {
                // If we are at the root, set it as global initial
                if (string.IsNullOrEmpty(parentId) && string.IsNullOrEmpty(data.InitialStateId))
                {
                    data.InitialStateId = initialAttr.Value;
                }
            }

            // Check if THIS state is the initial child of its parent
            if (!string.IsNullOrEmpty(parentId))
            {
                var parentElement = stateElement.Parent;
                if (parentElement != null && parentElement.Attribute("initial")?.Value == id)
                {
                    stateData.IsInitial = true;
                }
            }

            var datamodel = stateElement.Element(stateElement.Name.Namespace + "datamodel");
            if (datamodel != null)
            {
                foreach (var dataEl in datamodel.Elements(stateElement.Name.Namespace + "data"))
                {
                    string dataId = dataEl.Attribute("id")?.Value;
                    string dataExpr = dataEl.Attribute("expr")?.Value;
                    if (!string.IsNullOrEmpty(dataId))
                    {
                        stateData.DataModel[dataId] = dataExpr;
                    }
                }
            }

            foreach (var onentry in stateElement.Elements(stateElement.Name.Namespace + "onentry"))
            {
                var innerXml = string.Join("\n", onentry.Elements().Select(e => System.Text.RegularExpressions.Regex.Replace(e.ToString(), @"\s*xmlns=""[^""]*""", "")));
                stateData.OnEntryActions.Add(innerXml);
            }
            foreach (var onexit in stateElement.Elements(stateElement.Name.Namespace + "onexit"))
            {
                var innerXml = string.Join("\n", onexit.Elements().Select(e => System.Text.RegularExpressions.Regex.Replace(e.ToString(), @"\s*xmlns=""[^""]*""", "")));
                stateData.OnExitActions.Add(innerXml);
            }

            data.States.Add(stateData);

            foreach (var transition in stateElement.Elements(stateElement.Name.Namespace + "transition"))
            {
                string targetId = transition.Attribute("target")?.Value;
                string ev = transition.Attribute("event")?.Value;
                string cond = transition.Attribute("cond")?.Value;

                List<string> actions = new List<string>();
                var innerElements = transition.Elements().ToList();
                if (innerElements.Count > 0)
                {
                    actions.Add(string.Join("\n", innerElements.Select(e => System.Text.RegularExpressions.Regex.Replace(e.ToString(), @"\s*xmlns=""[^""]*""", ""))));
                }

                if (!string.IsNullOrEmpty(targetId))
                {
                    data.Transitions.Add(new SCXMLTransitionData
                    {
                        SourceId = stateData.Id,
                        TargetId = targetId,
                        Event = ev,
                        Condition = cond,
                        OriginalTargetId = targetId,
                        OriginalEvent = ev,
                        OriginalCondition = cond,
                        Actions = actions
                    });
                }
            }

            ParseStateLevel(stateElement, id, data);
        }

        public void SaveLayout(SCStateMachine stateMachine, SCXMLData data)
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

                // 1. Sync Global Data Model
                SyncGlobalDataModel(root, data.GlobalDataModel);

                // We no longer use or update the 'initial' attribute on the root or parent elements.
                // We only use explicit <initial> tags.
                // Any existing 'initial' attributes could technically be removed here, 
                // but the prompt says "just ignore it", so we leave them alone or do nothing.

                // 3. Remove Deleted States
                var allStateIds = new HashSet<string>(data.States.Select(s => s.OriginalId ?? s.Id));
                RemoveDeletedStates(root, allStateIds);

                // 3. Update Existing States and Transitions, and Create New Ones
                foreach (var state in data.States)
                {
                    var element = FindElementById(root, state.OriginalId ?? state.Id);
                    
                    if (element == null)
                    {
                        // Element doesn't exist in XML, meaning it was created in the editor
                        string targetTag = state.Type == StateType.Final ? "final" : (state.Type == StateType.Parallel ? "parallel" : (state.Type == StateType.Initial ? "initial" : "state"));
                        element = new XElement(root.Name.Namespace + targetTag);
                        element.SetAttributeValue("id", state.Id);
                        state.OriginalId = state.Id;
                        
                        if (string.IsNullOrEmpty(state.ParentId))
                        {
                            root.Add(element);
                        }
                        else
                        {
                            var parentElement = FindElementById(root, state.ParentId);
                            if (parentElement != null) parentElement.Add(element);
                            else root.Add(element); // Fallback
                        }
                    }

                    if (element != null)
                    {
                        // Update ID and layout
                        if (state.Id != (state.OriginalId ?? state.Id))
                        {
                            element.SetAttributeValue("id", state.Id);
                            state.OriginalId = state.Id;
                        }

                        // Update Type tag (e.g. state -> final)
                        string targetTag = state.Type == StateType.Final ? "final" : (state.Type == StateType.Parallel ? "parallel" : (state.Type == StateType.Initial ? "initial" : "state"));
                        if (element.Name.LocalName != targetTag)
                        {
                            element.Name = element.Name.Namespace + targetTag;
                        }

                        element.SetAttributeValue(EditorNs + "x", state.Position.x.ToString("F1", CultureInfo.InvariantCulture));
                        element.SetAttributeValue(EditorNs + "y", state.Position.y.ToString("F1", CultureInfo.InvariantCulture));

                        // Sync Action Blocks
                        SyncActionBlock(element, "onentry", state.OnEntryActions);
                        SyncActionBlock(element, "onexit", state.OnExitActions);

                        // Sync Transitions for this state
                        var validTransitions = data.Transitions.Where(t => t.SourceId == state.Id).ToList();
                        SyncTransitions(element, validTransitions);
                    }
                }

                stateMachine.ScXml = doc.ToString();
                
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(stateMachine);
#endif
            }
            catch (Exception e)
            {
                Debug.LogError($"Error saving SCXML layout: {e.Message}");
            }
        }

        private void SyncGlobalDataModel(XElement root, Dictionary<string, string> globalDataModel)
        {
            var datamodelElement = root.Element(root.Name.Namespace + "datamodel");
            if (globalDataModel.Count > 0 && datamodelElement == null)
            {
                datamodelElement = new XElement(root.Name.Namespace + "datamodel");
                root.AddFirst(datamodelElement);
            }
            
            if (datamodelElement != null)
            {
                var currentKeys = new HashSet<string>(globalDataModel.Keys);
                var dataElements = datamodelElement.Elements(root.Name.Namespace + "data").ToList();
                
                foreach (var dataElem in dataElements)
                {
                    var idAttr = dataElem.Attribute("id");
                    if (idAttr != null && !currentKeys.Contains(idAttr.Value))
                    {
                        dataElem.Remove();
                    }
                }
                
                foreach (var kvp in globalDataModel)
                {
                    var dataElem = datamodelElement.Elements(root.Name.Namespace + "data").FirstOrDefault(e => e.Attribute("id")?.Value == kvp.Key);
                    if (dataElem == null)
                    {
                        dataElem = new XElement(root.Name.Namespace + "data", new XAttribute("id", kvp.Key), new XAttribute("expr", kvp.Value));
                        datamodelElement.Add(dataElem);
                    }
                    else
                    {
                        dataElem.SetAttributeValue("expr", kvp.Value);
                    }
                }

                if (!datamodelElement.HasElements) datamodelElement.Remove();
            }
        }

        private void RemoveDeletedStates(XElement parent, HashSet<string> validIds)
        {
            var elements = parent.Elements().ToList();
            foreach (var el in elements)
            {
                if (el.Name.LocalName == "state" || el.Name.LocalName == "parallel" || el.Name.LocalName == "initial" || el.Name.LocalName == "final")
                {
                    var idAttr = el.Attribute("id");
                    if (idAttr != null && !validIds.Contains(idAttr.Value))
                    {
                        el.Remove();
                    }
                    else
                    {
                        RemoveDeletedStates(el, validIds);
                    }
                }
            }
        }

        private void SyncActionBlock(XElement stateElement, string actionName, List<string> newContents)
        {
            var actionElement = stateElement.Element(stateElement.Name.Namespace + actionName);
            if (newContents == null || newContents.Count == 0 || (newContents.Count == 1 && string.IsNullOrWhiteSpace(newContents[0])))
            {
                if (actionElement != null) actionElement.Remove();
            }
            else
            {
                if (actionElement == null)
                {
                    actionElement = new XElement(stateElement.Name.Namespace + actionName);
                    stateElement.AddFirst(actionElement);
                }
                actionElement.RemoveNodes();
                foreach (var content in newContents)
                {
                    if (string.IsNullOrWhiteSpace(content)) continue;
                    try 
                    {
                        var parsed = XElement.Parse($"<root xmlns='{stateElement.Name.Namespace.NamespaceName}'>{content}</root>");
                        actionElement.Add(parsed.Nodes());
                    }
                    catch
                    {
                        actionElement.Add(new XText(content));
                    }
                }
            }
        }

        private void SyncTransitions(XElement stateElement, List<SCXMLTransitionData> validTransitions)
        {
            var transElements = stateElement.Elements(stateElement.Name.Namespace + "transition").ToList();
            
            // First loop: match existing transitions and update or delete them
            foreach (var transElem in transElements)
            {
                string target = transElem.Attribute("target")?.Value;
                string evt = transElem.Attribute("event")?.Value;
                string cond = transElem.Attribute("cond")?.Value;

                var matchedData = validTransitions.FirstOrDefault(t => 
                    (t.OriginalTargetId ?? t.TargetId) == target && 
                    (t.OriginalEvent ?? t.Event) == evt && 
                    (t.OriginalCondition ?? t.Condition) == cond);

                if (matchedData == null)
                {
                    transElem.Remove();
                }
                else
                {
                    if (!string.IsNullOrEmpty(matchedData.Event)) transElem.SetAttributeValue("event", matchedData.Event);
                    else transElem.Attribute("event")?.Remove();

                    if (!string.IsNullOrEmpty(matchedData.Condition)) transElem.SetAttributeValue("cond", matchedData.Condition);
                    else transElem.Attribute("cond")?.Remove();

                    transElem.SetAttributeValue("target", matchedData.TargetId);
                    
                    matchedData.OriginalTargetId = matchedData.TargetId;
                    matchedData.OriginalEvent = matchedData.Event;
                    matchedData.OriginalCondition = matchedData.Condition;

                    // Sync actions inside transition
                    if (matchedData.Actions != null && matchedData.Actions.Count > 0 && !string.IsNullOrWhiteSpace(matchedData.Actions[0]))
                    {
                        transElem.RemoveNodes();
                        foreach (var content in matchedData.Actions)
                        {
                            if (string.IsNullOrWhiteSpace(content)) continue;
                            try {
                                var parsed = XElement.Parse($"<root xmlns='{stateElement.Name.Namespace.NamespaceName}'>{content}</root>");
                                transElem.Add(parsed.Nodes());
                            } catch { transElem.Add(new XText(content)); }
                        }
                    }
                    else
                    {
                        transElem.RemoveNodes();
                    }
                    
                    validTransitions.Remove(matchedData); // Processed
                }
            }
            
            // Second loop: Add newly created transitions
            foreach (var newTrans in validTransitions)
            {
                var newElem = new XElement(stateElement.Name.Namespace + "transition");
                newElem.SetAttributeValue("target", newTrans.TargetId);
                if (!string.IsNullOrEmpty(newTrans.Event)) newElem.SetAttributeValue("event", newTrans.Event);
                if (!string.IsNullOrEmpty(newTrans.Condition)) newElem.SetAttributeValue("cond", newTrans.Condition);
                stateElement.Add(newElem);
            }
        }

        private XElement FindElementById(XElement parent, string id)
        {
            foreach (var el in parent.Elements())
            {
                if (el.Name.LocalName == "state" || el.Name.LocalName == "parallel" || el.Name.LocalName == "initial" || el.Name.LocalName == "final")
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
    }
}
