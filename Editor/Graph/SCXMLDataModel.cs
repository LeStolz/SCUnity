using System.Collections.Generic;
using UnityEngine;

namespace SCUnity.Editor
{
    public enum StateType
    {
        State,
        Parallel,
        Initial,
        Final
    }

    public class SCXMLData
    {
        public Dictionary<string, string> globalDataModel = new();
        public List<SCXMLStateData> states = new();
        public List<SCXMLTransitionData> transitions = new();
    }

    public class SCXMLStateData
    {
        public string id;
        public string originalId;
        public StateType type;
        public string parentId;
        public Vector2 position;
        public bool hasSavedPosition;
        public bool isCompound;

        public Dictionary<string, string> dataModel = new();
        public List<string> onEntryActions = new();
        public List<string> onExitActions = new();
    }

    public class SCXMLTransitionData
    {
        public string sourceId;
        public string targetId;
        public string @event;
        public string condition;
        public List<string> onTransitionActions = new();

        public string originalTargetId;
        public string originalEvent;
        public string originalCondition;
    }
}
