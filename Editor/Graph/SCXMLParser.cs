using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace SCUnity.Editor
{
    public class SCXMLParser
    {
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
                Type = type,
                ParentId = parentId,
                IsCompound = isCompound
            };

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
                string target = transition.Attribute("target")?.Value;
                string evt = transition.Attribute("event")?.Value;
                string cond = transition.Attribute("cond")?.Value;

                List<string> actions = new List<string>();
                var innerElements = transition.Elements().ToList();
                if (innerElements.Count > 0)
                {
                    actions.Add(string.Join("\n", innerElements.Select(e => System.Text.RegularExpressions.Regex.Replace(e.ToString(), @"\s*xmlns=""[^""]*""", ""))));
                }

                if (!string.IsNullOrEmpty(target))
                {
                    data.Transitions.Add(new SCXMLTransitionData
                    {
                        SourceId = id,
                        TargetId = target,
                        Event = evt,
                        Condition = cond,
                        Actions = actions
                    });
                }
            }

            ParseStateLevel(stateElement, id, data);
        }
    }
}
