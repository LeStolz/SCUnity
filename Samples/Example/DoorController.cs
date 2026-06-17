
using System;
using Newtonsoft.Json;
using SCUnity;
using UnityEngine;

namespace SCUnitySamples
{
    /// <summary>
    /// Provide a general description of the public class.
    /// </summary>
    /// <remarks>
    /// Packages require XmlDoc documentation for ALL Package APIs.
    /// https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/xmldoc/xml-documentation-comments
    /// </remarks>
    public class DoorController : MonoBehaviour
    {
        public SCStateMachine stateMachine;

        void Start()
        {
            stateMachine.OnEventReceived.AddListener((data) => Debug.Log($"Event received with data: {data}"));
            stateMachine.OnStatesChanged.AddListener(states => Debug.Log($"State changed: {string.Join(", ", states)}"));
            stateMachine.OnFinished.AddListener(() => Debug.Log("StateMachine finished"));
        }

        /// <summary>
        /// Provide a description of what this public method does.
        /// </summary>
        void Update()
        {
            EventData eventData = new() { message = "Hello from Unity!" };

            if (Input.GetKeyDown(KeyCode.O)) _ = stateMachine.SendEvent("open", JsonConvert.SerializeObject(eventData));
            if (Input.GetKeyDown(KeyCode.C)) _ = stateMachine.SendEvent("close", JsonConvert.SerializeObject(eventData));
        }

        [Serializable]
        public class EventData
        {
            public string message;
        }
    }
}