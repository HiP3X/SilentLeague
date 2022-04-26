using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Net;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Management;

namespace LCUClient
{
    class LCUListener
    {
        private Thread listeningThread;
        private bool listening = false; 

        private ConcurrentDictionary<int, LCUClient> gatheredLCUs = new ConcurrentDictionary<int, LCUClient>();
		private ConcurrentDictionary<int, RiotClient> gatheredRiots = new ConcurrentDictionary<int, RiotClient>();
		public void StartListening()
        {
            listening = true;
            listeningThread = new Thread(new ThreadStart(ListenForAnyClients));
            listeningThread.Start();
        }

        public void StopListening()
        {
            listening = false;
            listeningThread.Join();
        }

		public void WaitForAnyClient()
        {
			while(true)
            {
				if (GetGatheredLCUs().Count > 0)
					break;
				Thread.Sleep(100);
            }
        }
        public List<LCUClient> GetGatheredLCUs()
        {
            foreach (var pair in gatheredLCUs)
            {
                var pid = pair.Key;
                var LCU = pair.Value;
                if (!LCU.IsAlive())
                {
                    gatheredLCUs.TryRemove(pid, out var temp);
                }
            }
            return gatheredLCUs.Values.ToList();
        }
		public List<RiotClient> GetGatheredRiots()
		{
			foreach (var pair in gatheredRiots)
			{
				var pid = pair.Key;
				var riot = pair.Value;
				if (!riot.IsAlive())
				{
					gatheredRiots.TryRemove(pid, out var temp);
				}
			}
			return gatheredRiots.Values.ToList();
		}


        private void ListenForAnyClients()
        {
            while (listening)
            {
                foreach (var proc in Process.GetProcessesByName("LeagueClientUx"))
                {
                    var pid = proc.Id;
                    if (IsAlreadyFound(pid))
                        continue;

                    ProcessCommandLine.Retrieve(proc, out var cmd);
                    string authToken = Regex.Match(cmd, "(\"--remoting-auth-token=)([^\"]*)(\")").Groups[2].Value;
                    string appPort = int.Parse(Regex.Match(cmd, "(\"--app-port=)([^\"]*)(\")").Groups[2].Value).ToString();

                    var LCUEntry = new LCUClient(appPort, authToken, proc.Id);

                    gatheredLCUs.TryAdd(proc.Id, LCUEntry);
                }

				foreach (var proc in Process.GetProcessesByName("RiotClientUx"))
				{
					var pid = proc.Id;
					if (IsAlreadyFound(pid))
						continue;

					ProcessCommandLine.Retrieve(proc, out var cmd);
					string authToken = Regex.Match(cmd, "(--remoting-auth-token=)([^\\s]+)").Groups[2].Value;
					string appPort = int.Parse(Regex.Match(cmd, "(--app-port=)([0-9]+)").Groups[2].Value).ToString();

					var RiotEntry = new RiotClient(appPort, authToken, proc.Id);

					gatheredRiots.TryAdd(proc.Id, RiotEntry);
				}

				Thread.Sleep(100);
            }
        }
		private bool IsAlreadyFound(int pid)
        {
            foreach (var pair in gatheredLCUs)
            {
                if (pair.Key == pid)
                {
                    return true;
                }
            }
			foreach(var pair in gatheredRiots)
            {
				if (pair.Key == pid)
				{
					return true;
				}
			}
            return false;
        }
    }

    class LCUClient
    {
        public readonly string appPort;
        public readonly string authToken;
        public readonly int processId;


        public readonly string clientUri;

        private HttpClient httpClient = null;
        private HttpClientHandler clientHandler = new HttpClientHandler();

        public LCUClient(string appPort, string authToken, int processId)
        {
            this.appPort = appPort;
            this.authToken = authToken;
            this.processId = processId;

            clientHandler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => { return true; };
            httpClient = new HttpClient(clientHandler);
            clientUri = "https://127.0.0.1:" + appPort;

            var byteArray = Encoding.ASCII.GetBytes("riot:" + authToken);
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;
        }

        public bool IsAlive()
        {
            return Process.GetProcesses().Any(x => x.Id == processId);
        }
		
		public void SetHeadless()
		{
			ProcessHelper.HideWindow(processId);
			ProcessHelper.SuspendProcess(processId);

			foreach (var pid in GetProcessChildsIDs())
				ProcessHelper.SuspendProcess(pid);

			foreach (var pid in GetProcessChildsIDs())
				ProcessHelper.KillProcess(pid);
		}

        #region InternalFunctions
        internal List<int> GetProcessChildsIDs()
		{
			var childsIDs = new List<int>();
			ManagementObjectSearcher mos = new ManagementObjectSearcher(String.Format("Select * From Win32_Process Where ParentProcessID={0}", processId));

			foreach (ManagementObject mo in mos.Get())
			{
				childsIDs.Add(int.Parse(mo["ProcessID"].ToString()));
			}
			
			return childsIDs;
		}
        #endregion

        #region HttpFunctions

        public async Task<HttpResponseMessage> HttpGet(string apiUrl)
        {
            return await httpClient.GetAsync(clientUri + apiUrl);
        }

        public async Task<HttpResponseMessage> HttpPostJson(string apiUrl, string body)
        {
            if (body == "" || body == null)
                return await httpClient.PostAsync(clientUri + apiUrl, null);

            using (var stringContent = new StringContent(body, Encoding.UTF8, "application/json"))
            {
                return await httpClient.PostAsync(clientUri + apiUrl, stringContent);
            }
        }

        public async Task<HttpResponseMessage> HttpPostForm(string apiUrl, IEnumerable<KeyValuePair<string,string>> formData)
        {
            using(var content = new FormUrlEncodedContent(formData))
            {
                return await httpClient.PostAsync(clientUri + apiUrl, content);
            }
        }

        public async Task<HttpResponseMessage> HttpPostForm(string apiUrl, KeyValuePair<string, string> formData)
        {
            using (var content = new FormUrlEncodedContent(new[] { formData }))
            {
                return await httpClient.PostAsync(clientUri + apiUrl, content);
            }
        }

        public async Task<HttpResponseMessage> HttpDelete(string apiUrl)
        {
            return await httpClient.DeleteAsync(clientUri + apiUrl);
        }
        #endregion
    }
    class RiotClient
    {
		public readonly string appPort;
		public readonly string authToken;
		public readonly int processId;


		public readonly string clientUri;

		private HttpClient httpClient = null;
		private HttpClientHandler clientHandler = new HttpClientHandler();

		public RiotClient(string appPort, string authToken, int processId)
		{
			this.appPort = appPort;
			this.authToken = authToken;
			this.processId = processId;

			clientHandler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => { return true; };
			httpClient = new HttpClient(clientHandler);
			clientUri = "https://127.0.0.1:" + appPort;

			var byteArray = Encoding.ASCII.GetBytes("riot:" + authToken);
			httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

			ServicePointManager.ServerCertificateValidationCallback += (sender, certificate, chain, sslPolicyErrors) => true;
		}

		public bool IsAlive()
		{
			return Process.GetProcesses().Any(x => x.Id == processId);
		}

		public async Task<bool> LoginAsync(string username, string password)
        {
			string body1 = "{\"clientId\":\"riot-client\",\"trustLevels\":[\"always_trusted\"]}";
			await HttpPostJson("/rso-auth/v2/authorizations", body1);
			
			string body2 = "{\"username\":\"" + username + "\",\"password\":\"" + password + "\",\"persistLogin\":false}";
			var response = await HttpPut("/rso-auth/v1/session/credentials", body2);
			if (!response.IsSuccessStatusCode)
				return false;

			Thread.Sleep(1000);

			var response2 = await HttpPostJson("/product-launcher/v1/products/league_of_legends/patchlines/live", null);

			return true;
        }

		#region HttpFunctions
		public async Task<HttpResponseMessage> HttpPostJson(string apiUrl, string body)
		{
			if (body == "" || body == null)
				return await httpClient.PostAsync(clientUri + apiUrl, null);

			using (var stringContent = new StringContent(body, Encoding.UTF8, "application/json"))
			{
				return await httpClient.PostAsync(clientUri + apiUrl, stringContent);
			}
		}

		public async Task<HttpResponseMessage> HttpPut(string apiUrl, string body)
		{
			if (body == "" || body == null)
				return await httpClient.PutAsync(clientUri + apiUrl, null);

			using (var stringContent = new StringContent(body, Encoding.UTF8, "application/json"))
			{
				return await httpClient.PutAsync(clientUri + apiUrl, stringContent);
			}
		}
        #endregion
    }

    #region dependencies
    internal static class ProcessCommandLine
	{
		private static class Win32Native
		{
			public const uint PROCESS_BASIC_INFORMATION = 0;

			[Flags]
			public enum OpenProcessDesiredAccessFlags : uint
			{
				PROCESS_VM_READ = 0x0010,
				PROCESS_QUERY_INFORMATION = 0x0400,
			}

			[StructLayout(LayoutKind.Sequential)]
			public struct ProcessBasicInformation
			{
				public IntPtr Reserved1;
				public IntPtr PebBaseAddress;
				[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
				public IntPtr[] Reserved2;
				public IntPtr UniqueProcessId;
				public IntPtr Reserved3;
			}

			[StructLayout(LayoutKind.Sequential)]
			public struct UnicodeString
			{
				public ushort Length;
				public ushort MaximumLength;
				public IntPtr Buffer;
			}

			// This is not the real struct!
			// I faked it to get ProcessParameters address.
			// Actual struct definition:
			// https://docs.microsoft.com/en-us/windows/win32/api/winternl/ns-winternl-peb
			[StructLayout(LayoutKind.Sequential)]
			public struct PEB
			{
				[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
				public IntPtr[] Reserved;
				public IntPtr ProcessParameters;
			}

			[StructLayout(LayoutKind.Sequential)]
			public struct RtlUserProcessParameters
			{
				public uint MaximumLength;
				public uint Length;
				public uint Flags;
				public uint DebugFlags;
				public IntPtr ConsoleHandle;
				public uint ConsoleFlags;
				public IntPtr StandardInput;
				public IntPtr StandardOutput;
				public IntPtr StandardError;
				public UnicodeString CurrentDirectory;
				public IntPtr CurrentDirectoryHandle;
				public UnicodeString DllPath;
				public UnicodeString ImagePathName;
				public UnicodeString CommandLine;
			}

			[DllImport("ntdll.dll")]
			public static extern uint NtQueryInformationProcess(
				IntPtr ProcessHandle,
				uint ProcessInformationClass,
				IntPtr ProcessInformation,
				uint ProcessInformationLength,
				out uint ReturnLength);

			[DllImport("kernel32.dll")]
			public static extern IntPtr OpenProcess(
				OpenProcessDesiredAccessFlags dwDesiredAccess,
				[MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
				uint dwProcessId);

			[DllImport("kernel32.dll")]
			[return: MarshalAs(UnmanagedType.Bool)]
			public static extern bool ReadProcessMemory(
				IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer,
				uint nSize, out uint lpNumberOfBytesRead);

			[DllImport("kernel32.dll")]
			[return: MarshalAs(UnmanagedType.Bool)]
			public static extern bool CloseHandle(IntPtr hObject);

			[DllImport("shell32.dll", SetLastError = true,
				CharSet = CharSet.Unicode, EntryPoint = "CommandLineToArgvW")]
			public static extern IntPtr CommandLineToArgv(string lpCmdLine, out int pNumArgs);
		}

		private static bool ReadStructFromProcessMemory<TStruct>(
			IntPtr hProcess, IntPtr lpBaseAddress, out TStruct val)
		{
			val = default;
			var structSize = Marshal.SizeOf<TStruct>();
			var mem = Marshal.AllocHGlobal(structSize);
			try
			{
				if (Win32Native.ReadProcessMemory(
					hProcess, lpBaseAddress, mem, (uint)structSize, out var len) &&
					(len == structSize))
				{
					val = Marshal.PtrToStructure<TStruct>(mem);
					return true;
				}
			}
			finally
			{
				Marshal.FreeHGlobal(mem);
			}
			return false;
		}

		public static string ErrorToString(int error) =>
			new string[]
			{
			"Success",
			"Failed to open process for reading",
			"Failed to query process information",
			"PEB address was null",
			"Failed to read PEB information",
			"Failed to read process parameters",
			"Failed to read parameter from process"
			}[Math.Abs(error)];

		public enum Parameter
		{
			CommandLine,
			WorkingDirectory,
		}

		public static int Retrieve(Process process, out string parameterValue, Parameter parameter = Parameter.CommandLine)
		{
			int rc = 0;
			parameterValue = null;
			var hProcess = Win32Native.OpenProcess(
				Win32Native.OpenProcessDesiredAccessFlags.PROCESS_QUERY_INFORMATION |
				Win32Native.OpenProcessDesiredAccessFlags.PROCESS_VM_READ, false, (uint)process.Id);
			if (hProcess != IntPtr.Zero)
			{
				try
				{
					var sizePBI = Marshal.SizeOf<Win32Native.ProcessBasicInformation>();
					var memPBI = Marshal.AllocHGlobal(sizePBI);
					try
					{
						var ret = Win32Native.NtQueryInformationProcess(
							hProcess, Win32Native.PROCESS_BASIC_INFORMATION, memPBI,
							(uint)sizePBI, out var len);
						if (0 == ret)
						{
							var pbiInfo = Marshal.PtrToStructure<Win32Native.ProcessBasicInformation>(memPBI);
							if (pbiInfo.PebBaseAddress != IntPtr.Zero)
							{
								if (ReadStructFromProcessMemory<Win32Native.PEB>(hProcess,
									pbiInfo.PebBaseAddress, out var pebInfo))
								{
									if (ReadStructFromProcessMemory<Win32Native.RtlUserProcessParameters>(
										hProcess, pebInfo.ProcessParameters, out var ruppInfo))
									{
										string ReadUnicodeString(Win32Native.UnicodeString unicodeString)
										{
											var clLen = unicodeString.MaximumLength;
											var memCL = Marshal.AllocHGlobal(clLen);
											try
											{
												if (Win32Native.ReadProcessMemory(hProcess,
													unicodeString.Buffer, memCL, clLen, out len))
												{
													rc = 0;
													return Marshal.PtrToStringUni(memCL);
												}
												else
												{
													// couldn't read parameter line buffer
													rc = -6;
												}
											}
											finally
											{
												Marshal.FreeHGlobal(memCL);
											}
											return null;
										}

										switch (parameter)
										{
											case Parameter.CommandLine:
												parameterValue = ReadUnicodeString(ruppInfo.CommandLine);
												break;
											case Parameter.WorkingDirectory:
												parameterValue = ReadUnicodeString(ruppInfo.CurrentDirectory);
												break;
										}
									}
									else
									{
										// couldn't read ProcessParameters
										rc = -5;
									}
								}
								else
								{
									// couldn't read PEB information
									rc = -4;
								}
							}
							else
							{
								// PebBaseAddress is null
								rc = -3;
							}
						}
						else
						{
							// NtQueryInformationProcess failed
							rc = -2;
						}
					}
					finally
					{
						Marshal.FreeHGlobal(memPBI);
					}
				}
				finally
				{
					Win32Native.CloseHandle(hProcess);
				}
			}
			else
			{
				// couldn't open process for VM read
				rc = -1;
			}
			return rc;
		}

		public static IReadOnlyList<string> CommandLineToArgs(string commandLine)
		{
			if (string.IsNullOrEmpty(commandLine)) { return Array.Empty<string>(); }

			var argv = Win32Native.CommandLineToArgv(commandLine, out var argc);
			if (argv == IntPtr.Zero)
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
			try
			{
				var args = new string[argc];
				for (var i = 0; i < args.Length; ++i)
				{
					var p = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
					args[i] = Marshal.PtrToStringUni(p);
				}
				return args.ToList().AsReadOnly();
			}
			finally
			{
				Marshal.FreeHGlobal(argv);
			}
		}
	}

	internal static class ProcessHelper
	{
		[DllImport("kernel32.dll")]
		static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
		[DllImport("kernel32.dll")]
		static extern uint SuspendThread(IntPtr hThread);
		[DllImport("kernel32.dll")]
		static extern int ResumeThread(IntPtr hThread);

		[DllImport("user32.dll")]
		static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
		const int SW_HIDE = 0;

		public static void HideWindow(int processId)
        {
			var windowHandle = Process.GetProcessById(processId).MainWindowHandle;
			ShowWindow(windowHandle, SW_HIDE);
        }

		public static void SuspendProcess(int processId)
		{
			foreach (ProcessThread thread in Process.GetProcessById(processId).Threads)
			{
				var pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);
				if (pOpenThread == IntPtr.Zero)
				{
					break;
				}
				SuspendThread(pOpenThread);
			}
		}
		public static void ResumeProcess(int processId)
		{
			foreach (ProcessThread thread in Process.GetProcessById(processId).Threads)
			{
				var pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);
				if (pOpenThread == IntPtr.Zero)
				{
					break;
				}
				ResumeThread(pOpenThread);
			}
		}

		public static void KillProcess(int processId)
		{
			Process.GetProcessById(processId).Kill();
		}

		public enum Options
		{
			List,
			Kill,
			Suspend,
			Resume
		}

		[Flags]
		public enum ThreadAccess : int
		{
			TERMINATE = (0x0001),
			SUSPEND_RESUME = (0x0002),
			GET_CONTEXT = (0x0008),
			SET_CONTEXT = (0x0010),
			SET_INFORMATION = (0x0020),
			QUERY_INFORMATION = (0x0040),
			SET_THREAD_TOKEN = (0x0080),
			IMPERSONATE = (0x0100),
			DIRECT_IMPERSONATION = (0x0200)
		}

		public class Param
		{
			public int PID { get; set; }
			public string Expression { get; set; }
			public Options Option { get; set; }
		}
	}
	#endregion
}
