using UnityEngine;
using System.Collections;

public class ServerLogon : MonoBehaviour
{
	void Awake()
	{
		HostData[] hostData;
		
		MasterServer.ClearHostList();
    	MasterServer.RequestHostList("GraphiaAlpha");
		
		hostData = MasterServer.PollHostList();
		
		Debug.Log (hostData.Length);
		
		foreach(HostData server in hostData)
		{
			if(server.gameName.Equals(Application.loadedLevelName))
			{
				Network.Connect(server.ip, server.port, server.comment);
				return;
			}
		}
		
		Network.incomingPassword = "tempPass";
		Network.InitializeSecurity();
    	Network.InitializeServer(32, 25000, !Network.HavePublicAddress());
		MasterServer.RegisterHost("GraphiaAlpha", Application.loadedLevelName, "tempPass");
	}
}
