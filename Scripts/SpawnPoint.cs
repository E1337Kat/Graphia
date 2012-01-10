using UnityEngine;
using System.Collections;

/// <summary>
/// This component is used to create simple spawn points. Upon connecting to a server, it will spawn a prefab at its location
/// and rotation.
/// </summary>
public class SpawnPoint : MonoBehaviour
{
	public Transform playerPrefab;
	public Transform cameraPrefab;
	
	void OnConnectedToServer ()
	{
		Instantiate(cameraPrefab, transform.position, transform.rotation);
		Camera.main.transform.GetComponent<CameraFollow>().target = PhotonNetwork.Instantiate(playerPrefab, transform.position, transform.rotation, 0).transform;
	}
	
	void OnServerInitialized ()
	{
		Instantiate(cameraPrefab, transform.position, transform.rotation);
		Camera.main.transform.GetComponent<CameraFollow>().target = PhotonNetwork.Instantiate(playerPrefab, transform.position, transform.rotation, 0).transform;
	}
}
