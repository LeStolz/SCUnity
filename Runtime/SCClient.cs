using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Events;

namespace SCUnity
{
	public class SCClient : MonoBehaviour
	{
		public static readonly string PYTHON_PLUGIN_DIR = Path.GetFullPath(
			"Packages/com.lepoopz.scunity/Runtime/Plugins/sc"
		);
		public static SCClient Instance { get; private set; }

		readonly Queue<Action> mainThreadActionQueue = new();
		readonly Process python = new();
		readonly Dictionary<int, TaskCompletionSource<SCResponse>> pendingRequests = new();
		int nextRequestId = 0;

		public UnityEvent<SCResponse> OnMessage = new();

		void Awake()
		{
			if (Instance != null)
			{
				Destroy(gameObject);
				return;
			}
			Instance = this;
			DontDestroyOnLoad(gameObject);

			python.StartInfo.FileName = Path.Combine(PYTHON_PLUGIN_DIR, "Python/python.exe");
			python.StartInfo.Arguments = Path.Combine(PYTHON_PLUGIN_DIR, "scbackend.py");
			python.StartInfo.RedirectStandardInput = true;
			python.StartInfo.RedirectStandardOutput = true;
			python.StartInfo.RedirectStandardError = true;
			python.StartInfo.UseShellExecute = false;
			python.StartInfo.CreateNoWindow = true;
			python.OutputDataReceived += OnDataReceived;
			python.ErrorDataReceived += OnDataReceived;
			python.Start();
			python.BeginOutputReadLine();
			python.BeginErrorReadLine();
		}

		void Update()
		{
			lock (mainThreadActionQueue)
			{
				while (mainThreadActionQueue.Count > 0)
				{
					var action = mainThreadActionQueue.Dequeue();
					action?.Invoke();
				}
			}
		}


		void OnDataReceived(object sender, DataReceivedEventArgs e)
		{
			if (string.IsNullOrEmpty(e.Data)) return;

			try
			{
				var response = JsonConvert.DeserializeObject<SCResponse>(e.Data);

				if (response.type == "log")
				{
					UnityEngine.Debug.Log(response.data);
					return;
				}

				if (!response.ok)
				{
					UnityEngine.Debug.LogError(response.data);
				}

				if (response.id != 0 && pendingRequests.TryGetValue(response.id, out var tcs))
				{
					tcs.SetResult(response);
					pendingRequests.Remove(response.id);
					return;
				}

				lock (mainThreadActionQueue)
				{
					mainThreadActionQueue.Enqueue(() => OnMessage?.Invoke(response));
				}
			}
			catch (Exception ex)
			{
				UnityEngine.Debug.LogError($"Failed to parse response: {e.Data} Exception: {ex}");
			}
		}

		public Task<SCResponse> Send(SCRequest request)
		{
			if (python.HasExited) UnityEngine.Debug.LogError("Python backend crashed");

			if (nextRequestId == int.MaxValue) nextRequestId = 0;
			int requestId = ++nextRequestId;

			var tcs = new TaskCompletionSource<SCResponse>();
			pendingRequests[requestId] = tcs;
			request.id = requestId;

			var data = JsonConvert.SerializeObject(request);
			python.StandardInput.WriteLine(data);
			python.StandardInput.Flush();

			return tcs.Task;
		}

		public void Dispose()
		{
			if (!python.HasExited) python.Kill();
			python.Dispose();
		}
	}

	[Serializable]
	public class SCRequest
	{
		public int id;
		public string op;
		public string name;
		public string data;
	}

	[Serializable]
	public class SCResponse
	{
		public int id;
		public string type;
		public string name;
		public string data;
		public bool ok;
	}
}