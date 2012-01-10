using UnityEngine;
using System.Collections;

public class ServerLogon : Photon.MonoBehaviour
{
	void Awake()
	{
		PhotonNetwork.ConnectUsingSettings();
		
		if(PlayerPrefs.HasKey("PlayerName"))
		{
			PhotonNetwork.playerName = PlayerPrefs.GetString("PlayerName");
		}
		else
		{
			PlayerPrefs.SetString("PlayerName", PhotonNetwork.playerName);
			PhotonNetwork.playerName = "Guest" + Random.Range(1, 9999);
		}
		
		PhotonNetwork.JoinRoom(Application.loadedLevelName);
	}
}
