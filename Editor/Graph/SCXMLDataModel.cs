using System.Collections.Generic;
using UnityEngine;

namespace SCUnity.Editor
{
    public enum StateType
    {
        Normal,
        Parallel,
        Initial,
        Final
    }

    public class SCXMLData
    {
        public string InitialStateId;
        public Dictionary<string, string> GlobalDataModel = new Dictionary<string, string>();
        public List<SCXMLStateData> States = new List<SCXMLStateData>();
        public List<SCXMLTransitionData> Transitions = new List<SCXMLTransitionData>();
    }

    public class SCXMLStateData
    {
        public string Id;
        public string OriginalId;
        public StateType Type;
        public string ParentId;
        public Vector2 Position;
        public bool HasSavedPosition;
        public bool IsCompound;
        public bool IsInitial;

        public Dictionary<string, string> DataModel = new Dictionary<string, string>();
        public List<string> OnEntryActions = new List<string>();
        public List<string> OnExitActions = new List<string>();
    }

    public class SCXMLTransitionData
    {
        public string SourceId;
        public string TargetId;
        public string Event;
        public string Condition;
        public List<string> Actions = new List<string>();

        public string OriginalTargetId;
        public string OriginalEvent;
        public string OriginalCondition;
    }
}
