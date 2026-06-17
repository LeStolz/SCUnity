using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Events;

namespace SCUnity
{
	public class SCStateMachine : MonoBehaviour
	{
		public class EventData
		{
			public string target;
			public string data;
		}

		public class ActiveStates
		{
			public string[] activeStates;
		}

		public static Dictionary<string, SCStateMachine> StateMachines { get; private set; } = new();

		[Header("State Machine")]
		[SerializeField] string scName;

		[Header("Asset Source")]
		[SerializeField] TextAsset scAsset;
		[Header("Or Text Source")]
		[SerializeField, TextArea(10, 20)] string scXml;

		[Header("Events")]
		public UnityEvent<ActiveStates> OnStatesChanged;
		public UnityEvent OnFinished;
		public UnityEvent<EventData> OnEventReceived;

		async void Start()
		{
			if (!string.IsNullOrEmpty(scName) && (scAsset != null || !string.IsNullOrEmpty(scXml)))
			{
				await CreateMachine(scName, scAsset, scXml);
			}
		}

		void OnEnable()
		{
			IEnumerator ConnectToSC()
			{
				yield return new WaitUntil(() => SCClient.Instance != null);
				SCClient.Instance.OnMessage.AddListener(HandleMessage);
			}

			StartCoroutine(ConnectToSC());
		}

		void OnDisable()
		{
			SCClient.Instance.OnMessage.RemoveListener(HandleMessage);
		}

		void HandleMessage(SCResponse msg)
		{
			if (msg.name != scName) return;

			switch (msg.type)
			{
				case "statesChanged":
					OnStatesChanged?.Invoke(JsonConvert.DeserializeObject<ActiveStates>(msg.data));
					break;

				case "finished":
					OnFinished?.Invoke();
					break;

				case "eventSent":
					OnEventReceived?.Invoke(JsonConvert.DeserializeObject<EventData>(msg.data));
					break;
			}
		}

		public string EncodeData(string data)
		{
			return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(data));
		}

		public async Task CreateMachine(string name, TextAsset asset, string xml)
		{
			scName = name;
			scAsset = asset;
			scXml = xml;

			string data = asset != null ? asset.text : xml;
			string encoded = EncodeData(data);

			if (StateMachines.ContainsKey(name))
			{
				Debug.LogError($"State machine with name {name} already exists.");
				return;
			}

			await SCClient.Instance.Send(new SCRequest
			{
				op = "createStateMachine",
				name = name,
				data = encoded
			});

			StateMachines[name] = this;
		}

		void DestroyMachine() => SCClient.Instance.Send(new SCRequest
		{
			op = "destroyStateMachine",
			name = scName
		}
		);

		void OnDestroy()
		{
			DestroyMachine();
			StateMachines.Remove(scName);
		}

		public async Task<ActiveStates> SendEvent(string @event, string data = null)
		{
			var payload = new Dictionary<string, string>
			{
				{ "event", @event },
				{ "data", data }
			};

			var res = await SCClient.Instance.Send(new SCRequest
			{
				op = "sendEvent",
				name = scName,
				data = EncodeData(JsonConvert.SerializeObject(payload))
			});

			return JsonConvert.DeserializeObject<ActiveStates>(res.data);
		}

		public async Task<string> GetValue(string key)
		{
			var payload = new Dictionary<string, string>
			{
				{ "key", key }
			};

			var res = await SCClient.Instance.Send(new SCRequest
			{
				op = "getValue",
				name = scName,
				data = EncodeData(JsonConvert.SerializeObject(payload))
			});

			return res.data;
		}

		public async Task<ActiveStates> GetActiveStates()
		{
			SCResponse res = await SCClient.Instance.Send(new SCRequest
			{
				op = "getActiveStates",
				name = scName
			});

			return JsonConvert.DeserializeObject<ActiveStates>(res.data);
		}
	}
}