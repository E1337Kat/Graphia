using UnityEngine;
using System.Collections;

/// <summary>
/// This is the spawn point controller component.
/// </summary>
public class SpawnPointController : MonoBehaviour
{
	public Transform playerPrefab;
	public Transform cameraPrefab;
	
	// Use this for initialization
	public void Awake ()
	{
		HostData[] hostList;
		bool gameJoined = false;
		
		MasterServer.ipAddress = "149.149.200.187";
		MasterServer.ClearHostList();
        MasterServer.RequestHostList("Graphia Alpha");
		
		hostList = MasterServer.PollHostList();
		
        foreach(HostData hostData in hostList)
		{
			if(hostData.gameName.Equals(Application.loadedLevelName))
			{
				JoinServer(hostData);
				gameJoined = true;
			}
		}
			
		if(!gameJoined)
			CreateServer();
	}
	
	public void OnServerInitialized ()
	{
		Spawn();
	}
	
	public void OnConnectedToServer ()
	{
		Spawn();
	}
	
	private void CreateServer ()
	{
		Network.InitializeSecurity();
		Network.incomingPassword = "HolyMoly";
    	bool useNat = !Network.HavePublicAddress();
    	Network.InitializeServer(32, 25000, useNat);
		MasterServer.RegisterHost("Graphia Alpha", Application.loadedLevelName, Network.incomingPassword);
	}
	
	private void JoinServer (HostData hostData)
	{
		Network.Connect(hostData, hostData.comment);
	}
	
	private IEnumerator Spawn ()
	{
		Transform player = Network.Instantiate(playerPrefab, transform.position, transform.rotation, 0) as Transform;
		CameraFollow camera = Instantiate(cameraPrefab, transform.position, transform.rotation) as CameraFollow;
		yield return player;
		camera.target = player;
		Destroy (gameObject);
	}
}
