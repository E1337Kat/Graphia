﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="NetworkingPeer.cs" company="Exit Games GmbH">
//   Part of: Photon Unity Networking
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ExitGames.Client.Photon;
using ExitGames.Client.Photon.Lite;
using UnityEngine;

public class NetworkingPeer : LoadbalancingPeer, IPhotonPeerListener
{
    public static string serverSettingsAssetPath = "Assets/Photon Unity Networking/Resources/PhotonServerSettings.asset";

    // game properties must be cached, because the game is created on the master and then "re-created" on the game server
    // both must use the same props for the game
    private string mAppVersion;

    private string mAppId;

    private byte nodeId = 0;

    private string masterServerAddress;

    private string playername;

    private IPhotonPeerListener externalListener;

    private JoinType mLastJoinType;

    private bool mPlayernameHasToBeUpdated;

    public string PlayerName
    {
        get
        {
            return this.playername;
        }

        set
        {
            if (string.IsNullOrEmpty(value) || value.Equals(this.playername))
            {
                return;
            }

            if (this.mLocalActor != null)
            {
                this.mLocalActor.name = value;
            }

            this.playername = value;
            if (this.mCurrentGame != null)
            {
                // Only when in a room
                this.SendPlayerName();
            }
        }
    }

    public PeerState State { get; internal set; }

    // "public" access to the current game - is null unless a room is joined on a gameserver
    public Room mCurrentGame
    {
        get
        {
            if (this.mRoomToGetInto != null && this.mRoomToGetInto.IsLocalClientInside)
            {
                return this.mRoomToGetInto;
            }

            return null;
        }
    }

    /// <summary>
    /// keeps the custom properties, gameServer address and anything else about the room we want to get into
    /// </summary>
    internal Room mRoomToGetInto { get; set; }

    public Dictionary<int, PhotonPlayer> mActors = new Dictionary<int, PhotonPlayer>();

    public List<PhotonPlayer> mPlayerList = new List<PhotonPlayer>();

    public List<PhotonPlayer> mOtherPlayerList = new List<PhotonPlayer>();

    public PhotonPlayer mLocalActor { get; internal set; }

    public PhotonPlayer mMasterClient = null;

    public string mGameserver { get; internal set; }

    public bool requestSecurity = false;

    private Dictionary<Type, List<MethodInfo>> monoRPCMethodsCache = new Dictionary<Type, List<MethodInfo>>();

    /// <summary>Count of instantiations. Used to assign an id to each Instantiate event (which is buffered server-side). Reset in LeftRoomCleanup().</summary>
    private ushort cacheInstantiationCount = 0;

    public Dictionary<string, Room> mGameList = new Dictionary<string, Room>();

    public int mQueuePosition { get; internal set; }

    // stat values:
    public int mMasterCount { get; internal set; }

    public int mGameCount { get; internal set; }

    public int mPeerCount { get; internal set; }

    /// <summary>
    /// Instantiated objects by their instantiationId. The id (key) is per actor.
    /// </summary>
    public Dictionary<int, GameObject> instantiatedObjects = new Dictionary<int, GameObject>();

    private List<int> blockReceivingGroups = new List<int>();

    private List<int> blockSendingGroups = new List<int>();

    private Dictionary<int, PhotonView> photonViewList = new Dictionary<int, PhotonView>();

    private int currentLevelPrefix = -1;

    public Dictionary<int, PhotonViewID> allocatedIDs = new Dictionary<int, PhotonViewID>(); // Accessed by PhotonNetwork

    public NetworkingPeer(
        IPhotonPeerListener listener, string playername, string appversion, ConnectionProtocol connectionProtocol)
        : base(listener, connectionProtocol)
    {
        this.Listener = this;
            
        // don't set the field directly! the listener is passed on to other classes, which get updated by the property set method
        this.externalListener = listener;
        this.PlayerName = playername;
        this.mLocalActor = new PhotonPlayer(true, -1, this.playername);
        this.AddNewPlayer(this.mLocalActor.ID, this.mLocalActor);

        this.State = global::PeerState.PeerCreated;
        this.mAppVersion = appversion;
    }

    #region Operations and Connection Methods

    public override bool Connect(string serverAddress, string appID, byte nodeId)
    {
        if (PhotonNetwork.connectionStateDetailed == global::PeerState.Disconnecting)
        {
            Debug.LogError("ERROR: Cannot connect to Photon while Disconnecting. Connection failed.");
            return false;
        }

        if (string.IsNullOrEmpty(this.masterServerAddress))
        {
            this.masterServerAddress = serverAddress;
        }

        this.mAppId = appID;
        this.State = global::PeerState.Connecting;
        return base.Connect(serverAddress, appID, nodeId);
    }

    /// <summary>
    /// Complete disconnect from photon (and the open master OR game server)
    /// </summary>
    public override void Disconnect()
    {
        if (this.PeerState == PeerStateValue.Disconnected)
        {
            if (this.DebugOut >= DebugLevel.WARNING)
            {
                this.DebugReturn(DebugLevel.WARNING, string.Format("Can't execute Disconnect() while not connected. Nothing changed. State: {0}", this.State));
            }

            return;
        }

        base.Disconnect();
        this.State = global::PeerState.Disconnecting;

        this.LeftRoomCleanup();
    }

    // just switches servers(Master->Game). don't remove the room, actors, etc
    private void DisconnectFromMaster()
    {
        base.Disconnect();
        this.State = global::PeerState.DisconnectingFromMasterserver;
    }

    // switches back from gameserver to master and removes the room, actors, etc
    private void DisconnectFromGameServer()
    {
        base.Disconnect();
        this.State = global::PeerState.DisconnectingFromGameserver;

        this.LeftRoomCleanup();

        SendMonoMessage(PhotonNetworkingMessage.OnLeftRoom);
    }

    private void LeftRoomCleanup()
    {
        this.mRoomToGetInto = null;
        this.mActors = new Dictionary<int, PhotonPlayer>();
        this.mMasterClient = null;
        this.blockReceivingGroups = new List<int>();
        this.blockSendingGroups = new List<int>();
        this.mGameList = new Dictionary<string, Room>();

        this.cacheInstantiationCount = 0;//ts: starts with 0.

        this.mPlayerList = new List<PhotonPlayer>();
        this.mOtherPlayerList = new List<PhotonPlayer>();
        this.ChangeLocalID(-1);

        // Cleanup all network objects (all spawned PhotonViews, local and remote)
        if (PhotonNetwork.autoCleanUpPlayerObjects)
        {
            foreach (GameObject go in this.instantiatedObjects.Values)
            {
                if (go != null)
                {
                    if (this.DebugOut >= DebugLevel.ALL)
                    {
                        this.DebugReturn(DebugLevel.ALL, "Network destroy(1) Instantiated GO: " + go.name);
                    }

                    GameObject.Destroy(go);
                }
            }

            this.instantiatedObjects = new Dictionary<int, GameObject>();

            foreach (PhotonView view in this.photonViewList.Values)
            {
                if (view != null && !view.isSceneView && view.gameObject != null)
                {
                    if (this.DebugOut >= DebugLevel.ALL)
                    {
                        this.DebugReturn(DebugLevel.ALL, "Network destroy(2) Instantiated GO: " + view.gameObject.name);
                    }

                    GameObject.Destroy(view.gameObject);
                }
            }

            this.allocatedIDs = new Dictionary<int, PhotonViewID>();
        }
    }

    private void SwitchNode(byte masterNodeId)
    {
        Debug.Log("Switching node to " + masterNodeId);
        this.nodeId = masterNodeId;

        // initiates a connection to the master server at disconnect
        this.DisconnectFromGameServer();
    }

    // gameID can be null (optional). The server assigns a unique name if no name is set

    // joins a room and sets your current username as custom actorproperty (will broadcast that)

    #endregion

    #region Helpers

    private void readoutStandardProperties(Hashtable gameProperties, Hashtable pActorProperties, int targetActorNr)
    {
        // Debug.LogWarning("readoutStandardProperties game=" + gameProperties + " actors(" + pActorProperties + ")=" + pActorProperties + " " + targetActorNr);
        // read game properties and cache them locally
        if (this.mCurrentGame != null && gameProperties != null)
        {
            this.mCurrentGame.CacheProperties(gameProperties);
        }

        if (pActorProperties != null && pActorProperties.Count > 0)
        {
            if (targetActorNr > 0)
            {
                // we have a single entry in the pActorProperties with one
                // user's name
                // targets MUST exist before you set properties
                PhotonPlayer target = this.GetPlayerWithID(targetActorNr);
                if (target != null)
                {
                    target.CacheProperties(this.GetActorPropertiesForActorNr(pActorProperties, targetActorNr));
                }
            }
            else
            {
                // in this case, we've got a key-value pair per actor (each
                // value is a hashtable with the actor's properties then)
                int actorNr;
                Hashtable props;
                string newName;
                PhotonPlayer target;

                foreach (object key in pActorProperties.Keys)
                {
                    actorNr = (int)key;
                    props = (Hashtable)pActorProperties[key];
                    newName = (string)props[ActorProperties.PlayerName];

                    target = this.GetPlayerWithID(actorNr);
                    if (target == null)
                    {
                        target = new PhotonPlayer(false, actorNr, newName);
                        this.AddNewPlayer(actorNr, target);
                    }

                    target.CacheProperties(props);
                }
            }
        }
    }

    private void AddNewPlayer(int ID, PhotonPlayer player)
    {
        if (!this.mActors.ContainsKey(ID))
        {
            this.mActors[ID] = player;
            this.mPlayerList.Add(player);
            if (!player.isLocal)
            {
                this.mOtherPlayerList.Add(player);
            }
        }
        else
        {
            Debug.LogError("Adding player twice: " + ID);
        }

        if (!player.isLocal)
        {
            this.ResetPhotonViewsOnSerialize(); // This sets the correct OnSerializeState for Reliable OnSerialize
        }
    }

    /// <summary>
    /// Resets the PhotonView "lastOnSerializeDataSent" so that "OnReliable" synched PhotonViews send an correct initial state to new clients (if the state doesnt change, no messages would be send otherwise!)
    /// Note that due to this reset, ALL other players will receive the OnSerialize.
    /// </summary>
    private void ResetPhotonViewsOnSerialize()
    {
        foreach (KeyValuePair<int, PhotonView> kvp in this.photonViewList)
        {
            PhotonView view = kvp.Value;
            if (view.owner == this.mLocalActor)
            {
                view.lastOnSerializeDataSent = null;
            }
        }
    }

    /// <summary>
    /// Called when the event Leave (of some other player) arrived.
    /// Cleans game objects, views locally. The master will also clean the 
    /// </summary>
    /// <param name="actorID"></param>
    private void HandleEventLeave(int actorID)
    {
        // actorNr is fetched out of event above
        if (actorID < 0 || !this.mActors.ContainsKey(actorID))
        {
            if (this.DebugOut >= DebugLevel.WARNING)
            {
                this.DebugReturn(DebugLevel.WARNING, string.Format("Received event Leave for unknown actorNumber: {0}", actorID));
            }
        }
        else
        {
            SendMonoMessage(PhotonNetworkingMessage.OnPhotonPlayerDisconnected, this.mActors[actorID]);
        }

        if (this.DebugOut >= DebugLevel.INFO)
        {
            this.DebugReturn(DebugLevel.INFO, "HandleEventLeave actorNr: " + actorID);
        }

        PhotonPlayer player = this.GetPlayerWithID(actorID);

        if (PhotonNetwork.autoCleanUpPlayerObjects)
        {
            this.DestroyPlayerObjects(player);
        }

        this.mPlayerList.Remove(player);
        if (!player.isLocal)
        {
            this.mOtherPlayerList.Remove(player);
        }

        this.mActors.Remove(actorID);

        if (this.mMasterClient != null && this.mMasterClient.ID == actorID)
        {
            this.mMasterClient = null;
        }

        this.CheckMasterClient();

        if (PhotonNetwork.isMasterClient && PhotonNetwork.autoCleanUpPlayerObjects)
        {
            //ts: cleans up everything (RPCs, Instantiate, anything cached by this player)
            this.RemoveCompleteCacheOfPlayer(actorID);
        }
    }

    private void CheckMasterClient()
    {
        int lowestActorNumber = int.MaxValue;

        if (this.mMasterClient != null && this.mActors.ContainsKey(this.mMasterClient.ID))
        {
            // the current masterClient is still in the list of players, so it can't change););
            return;
        }

        // the master is unknown. find lowest actornumber == master
        foreach (int actorNumber in this.mActors.Keys)
        {
            if (actorNumber < lowestActorNumber)
            {
                lowestActorNumber = actorNumber;
            }
        }

        if (this.mMasterClient == null || this.mMasterClient.ID != lowestActorNumber)
        {
            this.mMasterClient = this.mActors[lowestActorNumber];
            SendMonoMessage(PhotonNetworkingMessage.OnMasterClientSwitched, this.mMasterClient);
        }
    }

    private Hashtable GetActorPropertiesForActorNr(Hashtable actorProperties, int actorNr)
    {
        if (actorProperties.ContainsKey(actorNr))
        {
            return (Hashtable)actorProperties[actorNr];
        }

        return actorProperties;
    }

    private PhotonPlayer GetPlayerWithID(int number)
    {
        if (this.mActors != null && this.mActors.ContainsKey(number))
        {
            return this.mActors[number];
        }

        return null;
    }

    private void SendPlayerName()
    {
        if (this.State == global::PeerState.Joining)
        {
            // this means, the join on the gameServer is sent (with an outdated name). send the new when in game
            this.mPlayernameHasToBeUpdated = true;
            return;
        }

        if (this.mLocalActor != null)
        {
            this.mLocalActor.name = this.PlayerName;
            Hashtable properties = new Hashtable();
            properties[ActorProperties.PlayerName] = this.PlayerName;
            this.OpSetPropertiesOfActor(this.mLocalActor.ID, properties, true, (byte)0);
            this.mPlayernameHasToBeUpdated = false;
        }
    }

    private void GameEnteredOnGameServer(OperationResponse operationResponse)
    {
        if (operationResponse.ReturnCode != 0)
        {
            switch (operationResponse.OperationCode)
            {
                case OperationCode.CreateGame:
                    this.DebugReturn(DebugLevel.ERROR, "Create failed on GameServer. Changing back to MasterServer.");
                    SendMonoMessage(PhotonNetworkingMessage.OnPhotonCreateGameFailed);
                    break;
                case OperationCode.JoinGame:
                case OperationCode.JoinRandomGame:
                    this.DebugReturn(DebugLevel.ERROR, "Join failed on GameServer. Changing back to MasterServer.");
                    if (operationResponse.ReturnCode == ErrorCode.GameDoesNotExist)
                    {
                        Debug.Log("Most likely the game became empty during the switch to GameServer.");
                    }

                    SendMonoMessage(PhotonNetworkingMessage.OnPhotonJoinGameFailed);
                    break;
            }

            this.DisconnectFromGameServer();
            return;
        }

        this.State = global::PeerState.Joined;
        this.mRoomToGetInto.IsLocalClientInside = true;

        Hashtable actorProperties = (Hashtable)operationResponse[ParameterCode.ActorProperties];
        Hashtable gameProperties = (Hashtable)operationResponse[ParameterCode.GameProperties];
        this.readoutStandardProperties(gameProperties, actorProperties, 0);

        // the local player's actor-properties are not returned in join-result. add this player to the list
        int localActorNr = (int)operationResponse[ParameterCode.ActorNr];

        this.ChangeLocalID(localActorNr);
        this.CheckMasterClient();

        if (this.mPlayernameHasToBeUpdated)
        {
            this.SendPlayerName();
        }

        switch (operationResponse.OperationCode)
        {
            case OperationCode.CreateGame:
                SendMonoMessage(PhotonNetworkingMessage.OnCreatedRoom);
                break;
            case OperationCode.JoinGame:
            case OperationCode.JoinRandomGame:

                // the mono message for this is sent at another place
                break;
        }
    }

    private Hashtable GetLocalActorProperties()
    {
        Hashtable actorProperties = new Hashtable();
        actorProperties[ActorProperties.PlayerName] = this.PlayerName;
        return actorProperties;
    }

    public void ChangeLocalID(int newID)
    {
        if (this.mLocalActor == null)
        {
            Debug.LogWarning(
                string.Format(
                    "Local actor is null or not in mActors! mLocalActor: {0} mActors==null: {1} newID: {2}", 
                    this.mLocalActor, 
                    this.mActors == null, 
                    newID));
        }

        if (this.mActors.ContainsKey(this.mLocalActor.ID))
        {
            this.mActors.Remove(this.mLocalActor.ID);
        }

        this.mLocalActor.ChangeLocalID(newID);
        this.mActors[this.mLocalActor.ID] = this.mLocalActor;

        if (!this.mPlayerList.Contains(this.mLocalActor))
        {
            this.mPlayerList.Add(this.mLocalActor);
        }
    }

    #endregion

    #region Operations

    public bool OpCreateGame(string gameID, bool isVisible, bool isOpen, byte maxPlayers, Hashtable customGameProperties)
    {
        this.mRoomToGetInto = new Room(gameID, customGameProperties, isVisible, isOpen, maxPlayers);
        return this.OpCreateGame(gameID, isVisible, isOpen, maxPlayers, customGameProperties, this.GetLocalActorProperties());
    }

    public bool OpJoin(string gameID)
    {
        this.mRoomToGetInto = new Room(gameID, null);
        return this.OpJoin(gameID, this.GetLocalActorProperties());
    }

    /// <remarks>the hashtable is (optionally) used to filter games: only those that fit the contained custom properties will be matched</remarks>
    public override bool OpJoinRandom(Hashtable expectedGameProperties)
    {
        this.mRoomToGetInto = new Room(null, expectedGameProperties);
        return base.OpJoinRandom(expectedGameProperties);
    }

    public virtual bool OpLeave()
    {
        if (this.State != global::PeerState.Joined)
        {
            this.DebugReturn(DebugLevel.ERROR, "NetworkingPeer::leaveGame() - ERROR: no game is currently joined");
            return false;
        }

        this.mRoomToGetInto = null;

        // this.mLocalActor = null;
        this.mActors = new Dictionary<int, PhotonPlayer>();

        return this.OpCustom((byte)OperationCode.Leave, null, true, 0);
    }

    public override bool OpRaiseEvent(byte eventCode, Hashtable evData, bool sendReliable, byte channelId, int[] targetActors, EventCaching cache)
    {
        if (PhotonNetwork.offlineMode)
        {
            return false;
        }

        return base.OpRaiseEvent(eventCode, evData, sendReliable, channelId, targetActors, cache);
    }

    public override bool OpRaiseEvent(byte eventCode, Hashtable evData, bool sendReliable, byte channelId, EventCaching cache, ReceiverGroup receivers)
    {
        if (PhotonNetwork.offlineMode)
        {
            return false;
        }

        return base.OpRaiseEvent(eventCode, evData, sendReliable, channelId, cache, receivers);
    }

    #endregion

    #region Implementation of IPhotonPeerListener

    public void DebugReturn(DebugLevel level, string message)
    {
        this.externalListener.DebugReturn(level, message);
    }

    public void OnOperationResponse(OperationResponse operationResponse)
    {
        if (PhotonNetwork.networkingPeer.State == global::PeerState.Disconnecting)
        {
            if (this.DebugOut >= DebugLevel.INFO)
            {
                this.DebugReturn(DebugLevel.INFO, "OperationResponse ignored while disconnecting: " + operationResponse.OperationCode);
            }

            return;
        }

        if (operationResponse.ReturnCode == 0)
        {
            if (this.DebugOut >= DebugLevel.INFO)
            {
                this.DebugReturn(DebugLevel.INFO, operationResponse.ToString());
            }
        }
        else
        {
            if (this.DebugOut >= DebugLevel.WARNING)
            {
                this.DebugReturn(DebugLevel.WARNING, operationResponse.ToStringFull());
            }
        }

        switch (operationResponse.OperationCode)
        {
            case OperationCode.Authenticate:
                {
                    // PeerState oldState = this.State;

                    if (operationResponse.ReturnCode != 0)
                    {
                        if (this.DebugOut >= DebugLevel.ERROR)
                        {
                            this.DebugReturn(DebugLevel.ERROR, string.Format("Authentication failed. Check AppId.\n{0}", operationResponse.ToStringFull()));
                        }
                        this.Disconnect();
                        this.State = global::PeerState.Disconnecting;
                        break;
                    }
                    else
                    {
                        if (this.State == global::PeerState.Connected
                            || this.State == global::PeerState.ConnectedComingFromGameserver)
                        {
                            if (operationResponse.Parameters.ContainsKey(ParameterCode.Position))
                            {
                                this.mQueuePosition = (int)operationResponse[ParameterCode.Position];
                                    
                                    // returnValues for Authenticate always include this value!
                                if (this.mQueuePosition > 0)
                                {
                                    // should only happen, if just out of nowhere the
                                    // amount of players going online at the same time
                                    // is increasing faster, than automatically started
                                    // additional gameservers could have been booten up
                                    if (this.State == global::PeerState.ConnectedComingFromGameserver)
                                    {
                                        this.State = global::PeerState.QueuedComingFromGameserver;
                                    }
                                    else
                                    {
                                        this.State = global::PeerState.Queued;
                                    }

                                    // TODO TS: check how to react to being queued
                                    // this.mListener.gotQueuedReturn();
                                    break;
                                }
                            }

                            // Auto connect to LOBBY.
                            this.OpJoinLobby();
                            this.State = global::PeerState.Authenticated;
                        }
                        else if (this.State == global::PeerState.ConnectedToGameserver)
                        {
                            this.State = global::PeerState.Joining;
                            if (this.mLastJoinType == JoinType.JoinGame || this.mLastJoinType == JoinType.JoinRandomGame)
                            {
                                // if we just "join" the game, do so
                                this.OpJoin(this.mRoomToGetInto.name);
                            }
                            else if (this.mLastJoinType == JoinType.CreateGame)
                            {
                                this.OpCreateGame(
                                    this.mRoomToGetInto.name, 
                                    this.mRoomToGetInto.visible, 
                                    this.mRoomToGetInto.open, 
                                    this.mRoomToGetInto.maxPlayers, 
                                    this.mRoomToGetInto.properties);
                            }

                            break;
                        }
                    }
                    break;
                }

            case OperationCode.CreateGame:
                {
                    if (this.State != global::PeerState.Joining)
                    {
                        if (operationResponse.ReturnCode != 0)
                        {
                            if (this.DebugOut >= DebugLevel.ERROR)
                            {
                                this.DebugReturn(DebugLevel.ERROR, string.Format("createGame failed, client stays on masterserver: {0}.", operationResponse.ToStringFull()));
                            }
                            SendMonoMessage(PhotonNetworkingMessage.OnPhotonCreateGameFailed);
                            break;
                        }

                        string gameID = (string)operationResponse[ParameterCode.GameId];
                        if (!string.IsNullOrEmpty(gameID))
                        {
                            // is only sent by the server's response, if it has not been
                            // sent with the client's request before!
                            this.mRoomToGetInto.name = gameID;
                        }

                        this.mGameserver = (string)operationResponse[ParameterCode.Address];
                        this.DisconnectFromMaster();
                        this.mLastJoinType = JoinType.CreateGame;
                    }
                    else
                    {
                        this.GameEnteredOnGameServer(operationResponse);
                    }

                    break;
                }

            case OperationCode.JoinGame:
                {
                    if (this.State != global::PeerState.Joining)
                    {
                        if (operationResponse.ReturnCode != 0)
                        {
                            SendMonoMessage(PhotonNetworkingMessage.OnPhotonJoinGameFailed);

                            if (this.DebugOut >= DebugLevel.ERROR)
                            {
                                this.DebugReturn(DebugLevel.ERROR, string.Format("joinGame failed, client stays on masterserver: {0}. State: {1}", operationResponse.ToStringFull(), this.State));
                            }

                            // this.mListener.joinGameReturn(0, null, null, returnCode, debugMsg);
                            break;
                        }

                        this.mGameserver = (string)operationResponse[ParameterCode.Address];
                        this.DisconnectFromMaster();
                        this.mLastJoinType = JoinType.JoinGame;
                    }
                    else
                    {
                        this.GameEnteredOnGameServer(operationResponse);
                    }

                    break;
                }

            case OperationCode.JoinRandomGame:
                {
                    // happens only on master. on gameserver, this is a regular join (we don't need to find a random game again)
                    // the operation OpJoinRandom either fails (with returncode 8) or returns game-to-join information
                    if (operationResponse.ReturnCode != 0)
                    {
                        SendMonoMessage(PhotonNetworkingMessage.OnPhotonRandomJoinFailed);
                        if (this.DebugOut >= DebugLevel.ERROR)
                        {
                            this.DebugReturn(DebugLevel.ERROR, string.Format("joinrandom failed, client stays on masterserver: {0}.", operationResponse.ToStringFull()));
                        }

                        // this.mListener.createGameReturn(0, null, null, returnCode, debugMsg);
                        break;
                    }

                    string gameID = (string)operationResponse[ParameterCode.GameId];

                    this.mRoomToGetInto.name = gameID;
                    this.mGameserver = (string)operationResponse[ParameterCode.Address];
                    this.DisconnectFromMaster();
                    this.mLastJoinType = JoinType.JoinRandomGame;
                    break;
                }

            case OperationCode.JoinLobby:
                this.State = global::PeerState.JoinedLobby;
                SendMonoMessage(PhotonNetworkingMessage.OnJoinedLobby);

                // this.mListener.joinLobbyReturn();
                break;
            case OperationCode.LeaveLobby:
                this.State = global::PeerState.Authenticated;
                SendMonoMessage(PhotonNetworkingMessage.OnLeftLobby);
                break;

            case OperationCode.Leave:
                this.DisconnectFromGameServer();
                break;

            case OperationCode.SetProperties:
                // this.mListener.setPropertiesReturn(returnCode, debugMsg);
                break;

            case OperationCode.GetProperties:
                {
                    Hashtable actorProperties = (Hashtable)operationResponse[ParameterCode.ActorProperties];
                    Hashtable gameProperties = (Hashtable)operationResponse[ParameterCode.GameProperties];
                    this.readoutStandardProperties(gameProperties, actorProperties, 0);

                    // RemoveByteTypedPropertyKeys(actorProperties, false);
                    // RemoveByteTypedPropertyKeys(gameProperties, false);
                    // this.mListener.getPropertiesReturn(gameProperties, actorProperties, returnCode, debugMsg);
                    break;
                }

            case OperationCode.RaiseEvent:
                // this usually doesn't give us a result. only if the caching is affected the server will send one.
                break;

            default:
                if (this.DebugOut >= DebugLevel.ERROR)
                {
                    this.DebugReturn(DebugLevel.ERROR, string.Format("operationResponse unhandled: {0}", operationResponse.ToString()));
                }
                break;
        }

        this.externalListener.OnOperationResponse(operationResponse);
    }

    public void OnStatusChanged(StatusCode statusCode)
    {
        if (this.DebugOut >= DebugLevel.INFO)
        {
            this.DebugReturn(DebugLevel.INFO, string.Format("OnStatusChanged: {0}", statusCode.ToString()));
        }

        switch (statusCode)
        {
            case StatusCode.Connect:
                if (this.State == global::PeerState.ConnectingToGameserver)
                {
                    if (this.DebugOut >= DebugLevel.ALL)
                    {
                        this.DebugReturn(DebugLevel.ALL, "Connected to gameserver.");
                    }
                    this.State = global::PeerState.ConnectedToGameserver;
                }
                else
                {
                    if (this.DebugOut >= DebugLevel.ALL)
                    {
                        this.DebugReturn(DebugLevel.ALL, "Connected to masterserver.");
                    }
                    if (this.State == global::PeerState.Connecting)
                    {
                        SendMonoMessage(PhotonNetworkingMessage.OnConnectedToPhoton);
                        this.State = global::PeerState.Connected;
                    }
                    else
                    {
                        this.State = global::PeerState.ConnectedComingFromGameserver;
                    }
                }

                if (this.requestSecurity)
                {
                    this.EstablishEncryption();
                }

                if (!this.OpAuthenticate(this.mAppId, this.mAppVersion))
                {
                    this.externalListener.DebugReturn(DebugLevel.ERROR, "Error Authenticating! Did not work.");
                }

                break;

            case StatusCode.Disconnect:
                if (this.State == global::PeerState.DisconnectingFromMasterserver)
                {
                    if (this.nodeId != 0)
                    {
                        Debug.Log("connecting to game on node " + this.nodeId);
                    }

                    this.Connect(this.mGameserver, this.mAppId, this.nodeId);
                    this.State = global::PeerState.ConnectingToGameserver;
                }
                else if (this.State == global::PeerState.DisconnectingFromGameserver)
                {
                    // don't preselect node
                    this.nodeId = 0;
                    this.Connect(this.masterServerAddress, this.mAppId, 0);
                    this.State = global::PeerState.ConnectingToMasterserver;
                }
                else
                {
                    SendMonoMessage(PhotonNetworkingMessage.OnDisconnectedFromPhoton);
                    this.LeftRoomCleanup();
                    this.State = global::PeerState.PeerCreated;
                }

                break;
            case StatusCode.Exception:
            case StatusCode.ExceptionOnConnect:
            case StatusCode.InternalReceiveException:
            case StatusCode.TimeoutDisconnect:
            case StatusCode.DisconnectByServer:
            case StatusCode.DisconnectByServerLogic:
            case StatusCode.DisconnectByServerUserLimit:
                SendMonoMessage(PhotonNetworkingMessage.OnFailedToConnectToPhoton, statusCode);
                this.State = global::PeerState.PeerCreated;
                break;
            case StatusCode.SendError:

                // this.mListener.clientErrorReturn(statusCode);
                break;
            case StatusCode.QueueOutgoingReliableWarning:
            case StatusCode.QueueOutgoingUnreliableWarning:
            case StatusCode.QueueOutgoingAcksWarning:
            case StatusCode.QueueSentWarning:

                // this.mListener.warningReturn(statusCode);
                break;

            case StatusCode.EncryptionEstablished:
            case StatusCode.EncryptionFailedToEstablish:
                this.externalListener.DebugReturn(DebugLevel.INFO, "Encryption: " + statusCode);

                // TODO: add reaction to either
                break;

            case StatusCode.TcpRouterResponseOk:
                break;
            case StatusCode.TcpRouterResponseEndpointUnknown:
            case StatusCode.TcpRouterResponseNodeIdUnknown:
            case StatusCode.TcpRouterResponseNodeNotReady:

                // TODO: reconnect to master
                this.DebugReturn(DebugLevel.ERROR, "Unexpected router response: " + statusCode);
                break;

            default:

                // this.mListener.serverErrorReturn(statusCode.value());
                this.DebugReturn(DebugLevel.ERROR, "Received unknown status code: " + statusCode);
                break;
        }

        this.externalListener.OnStatusChanged(statusCode);
    }

    public void OnEvent(EventData photonEvent)
    {
        if (this.DebugOut >= DebugLevel.INFO)
        {
            this.DebugReturn(DebugLevel.INFO, string.Format("OnEvent: {0}", photonEvent.ToString()));
        }

        int actorNr = -1;
        PhotonPlayer orgPlayer = null;

        if (photonEvent.Parameters.ContainsKey(ParameterCode.ActorNr))
        {
            actorNr = (int)photonEvent[ParameterCode.ActorNr];
            if (this.mActors.ContainsKey(actorNr))
            {
                orgPlayer = (PhotonPlayer)this.mActors[actorNr];
            }

            // else
            // {
            // if (photonEvent.Code != (byte)LiteOpCode.Join)
            // {
            // Debug.LogError("Received event, but we do not have this actor:  " + actorNr);//TODO: CACHE THIS EVENT FOR A FEW SECONDS AND TRY AGAIN?
            // }
            // }
        }

        switch (photonEvent.Code)
        {
            case EventCode.AzureNodeInfo:
                {
                    byte currentNodeId = (byte)photonEvent[ParameterCode.AzureLocalNodeId];
                    byte masterNodeId = (byte)photonEvent[ParameterCode.AzureMasterNodeId];

                    if (currentNodeId != masterNodeId)
                    {
                        this.SwitchNode(masterNodeId);
                    }
                    else
                    {
                        this.nodeId = currentNodeId;
                    }

                    break;
                }

            case EventCode.GameList:
                {
                    this.mGameList = new Dictionary<string, Room>();
                    Hashtable games = (Hashtable)photonEvent[ParameterCode.GameList];
                    foreach (DictionaryEntry game in games)
                    {
                        string gameName = (string)game.Key;
                        this.mGameList[gameName] = new Room(gameName, (Hashtable)game.Value);
                    }

                    SendMonoMessage(PhotonNetworkingMessage.OnReceivedRoomList);
                    break;
                }

            case EventCode.GameListUpdate:
                {
                    Hashtable games = (Hashtable)photonEvent[ParameterCode.GameList];
                    foreach (DictionaryEntry room in games)
                    {
                        string gameName = (string)room.Key;
                        Room game = new Room(gameName, (Hashtable)room.Value);
                        if (game.mRemoved)
                        {
                            this.mGameList.Remove(gameName);
                        }
                        else
                        {
                            this.mGameList[gameName] = game;
                        }
                    }

                    SendMonoMessage(PhotonNetworkingMessage.OnReceivedRoomListUpdate);
                    break;
                }

            case EventCode.QueueState:
                if (photonEvent.Parameters.ContainsKey(ParameterCode.Position))
                {
                    this.mQueuePosition = (int)photonEvent[ParameterCode.Position];
                }
                else
                {
                    this.DebugReturn(DebugLevel.ERROR, "Event QueueState must contain position!");
                }

                if (this.mQueuePosition == 0)
                {
                    // PeerState oldState = this.State;
                    this.State = global::PeerState.Authenticated;

                    // if (oldState == PeerState.QueuedComingFromGameserver)
                    // {
                    // this.mListener.leaveGameReturn(0, null);
                    // }
                    // else
                    // {
                    // this.mListener.connectReturn(0, null);
                    // }
                }

                break;
            case EventCode.AppStats:

                // Debug.LogInfo("Received stats!");
                this.mPeerCount = (int)photonEvent[ParameterCode.PeerCount];
                this.mGameCount = (int)photonEvent[ParameterCode.GameCount];
                this.mMasterCount = (int)photonEvent[ParameterCode.MasterPeerCount];
                break;
            case EventCode.Join:

                // actorNr is fetched out of event above
                Hashtable actorProperties = (Hashtable)photonEvent[ParameterCode.ActorProperties];
                string actorName = (string)actorProperties[ActorProperties.PlayerName];
                if (orgPlayer == null)
                {
                    bool isLocal = this.mLocalActor.ID == actorNr;

                    // Debug.LogWarning("New player=" + isLocal + " player: " + actorName + " nr=" + actorNr);
                    this.AddNewPlayer(actorNr, new PhotonPlayer(isLocal, actorNr, actorName));
                }

                this.mRoomToGetInto.playerCount = this.mActors.Count;
                if (this.mActors[actorNr] == this.mLocalActor)
                {
                    SendMonoMessage(PhotonNetworkingMessage.OnJoinedRoom);
                }
                else
                {
                    SendMonoMessage(PhotonNetworkingMessage.OnPhotonPlayerConnected, this.mActors[actorNr]);
                }

                // this.mListener.joinGameEventAction(actorNr, actorNrList, actorName);
                break;

            case EventCode.Leave:
                this.HandleEventLeave(actorNr);
                break;

            case EventCode.SetProperties:
                int targetActorNr = (int)photonEvent[ParameterCode.TargetActorNr];
                Hashtable gameProperties = null;
                Hashtable actorProps = null;
                if (targetActorNr == 0)
                {
                    gameProperties = (Hashtable)photonEvent[ParameterCode.Properties];
                }
                else
                {
                    actorProps = (Hashtable)photonEvent[ParameterCode.Properties];
                }

                this.readoutStandardProperties(gameProperties, actorProps, targetActorNr);
                break;

            case PhotonNetworkMessages.RPC:
                //ts: each event now contains a single RPC. execute this
                this.ExecuteRPC(photonEvent[ParameterCode.Data] as Hashtable, orgPlayer);
                break;

            case PhotonNetworkMessages.SendSerialize:
                Hashtable serializeData = (Hashtable)photonEvent[ParameterCode.Data];
                this.ReadOnSerialize(serializeData, orgPlayer);
                break;

            case PhotonNetworkMessages.Instantiation:
                this.DoInstantiate((Hashtable)photonEvent[ParameterCode.Data], orgPlayer, null);
                break;

            case PhotonNetworkMessages.CloseConnection:
                // MasterClient "requests" a disconnection from us
                if (orgPlayer == null || !orgPlayer.isMasterClient)
                {
                    Debug.LogError("Error: Someone else(" + orgPlayer + ") then the masterserver requests a disconnect!");
                }
                else
                {
                    PhotonNetwork.LeaveRoom();
                }

                break;

            case PhotonNetworkMessages.Destroy:
                Hashtable data = (Hashtable)photonEvent[ParameterCode.Data];
                int viewID = (int)data[(byte)0];
                PhotonView view = this.GetPhotonView(viewID);

                if (view == null || orgPlayer == null)
                {
                    Debug.LogError("ERROR: Illegal destroy request on view ID=" + viewID + " from player/actorNr: " + actorNr);
                }
                else
                {
					// TODO: re-implement this check when a master-switch also changes the owner
                    //if (orgPlayer == view.owner)
                    //{
                        this.DoDestroyPhotonView(view);
                    //}
                }

                break;

            default:

                // actorNr might be null. it is fetched out of event on top of method
                // Hashtable eventContent = (Hashtable) photonEvent[ParameterCode.Data];
                // this.mListener.customEventAction(actorNr, eventCode, eventContent);
                Debug.LogError("Error. Unhandled event: " + photonEvent);
                break;
        }

        this.externalListener.OnEvent(photonEvent);
    }

    #endregion

    public static void SendMonoMessage(PhotonNetworkingMessage methodString, params object[] parameters)
    {
        List<GameObject> haveSendGOS = new List<GameObject>();
        MonoBehaviour[] mos = (MonoBehaviour[])GameObject.FindObjectsOfType(typeof(MonoBehaviour));
        foreach (MonoBehaviour mo in mos)
        {
            if(!haveSendGOS.Contains(mo.gameObject))
            {
                haveSendGOS.Add(mo.gameObject);
                if (parameters != null && parameters.Length == 1)
                {
                    mo.SendMessage(methodString.ToString(), parameters[0], SendMessageOptions.DontRequireReceiver);
                }
                else
                {
                    mo.SendMessage(methodString.ToString(), parameters, SendMessageOptions.DontRequireReceiver);
                }
            }
        }      
    }

    // NETWORKVIEW/RPC related

    /// <summary>
    /// Executes a received RPC event
    /// </summary>
    public void ExecuteRPC(Hashtable rpcData, PhotonPlayer sender)
    {
        if (rpcData == null || !rpcData.ContainsKey((byte)0))
        {
            this.DebugReturn(DebugLevel.ERROR, "Malformed RPC; this should never occur.");
            return;
        }

        //ts: updated with "flat" event data
        int netViewID = (int)rpcData[(byte)0]; // LIMITS NETWORKVIEWS&PLAYERS
        int otherSidePrefix = -1;
        if (rpcData.ContainsKey((byte)1))
        {
            otherSidePrefix = (int) rpcData[(byte) 1];
        }
        string inMethodName = (string)rpcData[(byte)3];
        object[] inMethodParameters = (object[])rpcData[(byte)4];

        if (inMethodParameters == null)
        {
            inMethodParameters = new object[0];
        }

        PhotonView photonNetview = this.GetPhotonView(netViewID);
        if (photonNetview == null)
        {
            Debug.LogError("Received RPC \"" + inMethodName + "\" for viewID " + netViewID + " but this networkview does not exist!");
            return;
        }

        if (photonNetview.prefix != otherSidePrefix)
        {
            Debug.LogError(
                "Received RPC \"" + inMethodName + "\" on viewID " + netViewID + " with a prefix of " + otherSidePrefix
                + ", our prefix is " + photonNetview.prefix + ". The RPC has been ignored.");
            return;
        }

        // Get method name
        if (inMethodName == string.Empty)
        {
            this.DebugReturn(DebugLevel.ERROR, "Malformed RPC; this should never occur.");
            return;
        }

        if (this.DebugOut >= DebugLevel.ALL)
        {
            this.DebugReturn(DebugLevel.ALL, "Received RPC; " + inMethodName);
        }

        // SetReceiving filtering
        if (this.blockReceivingGroups.Contains(photonNetview.group))
        {
            return; // Ignore group
        }

        Type[] argTypes = Type.EmptyTypes;
        if (inMethodParameters.Length > 0)
        {
            argTypes = new Type[inMethodParameters.Length];
            int i = 0;
            foreach (object objX in inMethodParameters)
            {
                if (objX == null)
                {
                    argTypes[i] = null;
                }
                else
                {
                    argTypes[i] = objX.GetType();
                }

                i++;
            }
        }

        int receivers = 0;
        int foundMethods = 0;
        foreach (MonoBehaviour monob in photonNetview.GetComponents<MonoBehaviour>())
        {
            Type type = monob.GetType();

            // Get [RPC] methods from cache
            List<MethodInfo> cachedRPCMethods = null;
            if (this.monoRPCMethodsCache.ContainsKey(type))
            {
                cachedRPCMethods = this.monoRPCMethodsCache[type];
            }

            if (cachedRPCMethods == null)
            {
                List<MethodInfo> entries = new List<MethodInfo>();
                MethodInfo[] myMethods = type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance );
                for (int i = 0; i < myMethods.Length; i++)
                {
                    if (myMethods[i].IsDefined(typeof(UnityEngine.RPC), false))
                    {
                        entries.Add(myMethods[i]);
                    }
                }

                cachedRPCMethods = this.monoRPCMethodsCache[type] = entries;
            }

            if (cachedRPCMethods == null)
            {
                continue;
            }

            // Check cache for valid methodname+arguments
            foreach (MethodInfo mInfo in cachedRPCMethods)
            {
                if (mInfo.Name == inMethodName)
                {
                    foundMethods++;
                    ParameterInfo[] pArray = mInfo.GetParameters();
                    if (pArray.Length == argTypes.Length)
                    {
                        // Normal, PhotonNetworkMessage left out
                        if (this.CheckTypeMatch(pArray, argTypes))
                        {
                            receivers++;
                            object result = mInfo.Invoke((object)monob, inMethodParameters);
                            if (mInfo.ReturnType == typeof(System.Collections.IEnumerator))
                            {
                                PhotonHandler.SP.StartCoroutine((IEnumerator)result);
                            }
                        }
                    }
                    else if ((pArray.Length - 1) == argTypes.Length)
                    {
                        // Check for PhotonNetworkMessage being the last
                        if (this.CheckTypeMatch(pArray, argTypes))
                        {
                            if (pArray[pArray.Length - 1].ParameterType == typeof(PhotonMessageInfo))
                            {
                                receivers++;

                                int sendTime = (int)rpcData[(byte)2];
                                object[] deParamsWithInfo = new object[inMethodParameters.Length + 1];
                                inMethodParameters.CopyTo(deParamsWithInfo, 0);
                                deParamsWithInfo[deParamsWithInfo.Length - 1] = new PhotonMessageInfo(sender, sendTime, photonNetview);

                                object result = mInfo.Invoke((object)monob, deParamsWithInfo);
                                if (mInfo.ReturnType == typeof(System.Collections.IEnumerator))
                                {
                                    PhotonHandler.SP.StartCoroutine((IEnumerator)result);
                                }
                            }
                        }
                    }
                }
            }
        }

        // Error handling
        if (receivers != 1)
        {
            string argsString = string.Empty;
            foreach (Type ty in argTypes)
            {
                if (argsString != string.Empty)
                {
                    argsString += ", ";
                }

                if (ty == null)
                {
                    argsString += "null";
                }
                else
                {
                    argsString += ty.Name;
                }
            }

            if (receivers == 0)
            {
                if (foundMethods == 0)
                {
                    this.DebugReturn(
                        DebugLevel.ERROR, 
                        "PhotonView with ID " + netViewID + " has no method \"" + inMethodName
                        + "\" marked with the [RPC](C#) or @RPC(JS) property!");
                }
                else
                {
                    this.DebugReturn(
                        DebugLevel.ERROR, 
                        "PhotonView with ID " + netViewID + " has no method \"" + inMethodName + "\" that takes "
                        + argTypes.Length + " argument(s): " + argsString);
                }
            }
            else
            {
                this.DebugReturn(
                    DebugLevel.ERROR, 
                    "PhotonView with ID " + netViewID + " has " + receivers + " methods \"" + inMethodName
                    + "\" that takes " + argTypes.Length + " argument(s): " + argsString + ". Should be just one?");
            }
        }
    }

    /// <summary>
    /// Check if all types match with parameters. We can have more paramters then types (allow last RPC type to be different).
    /// </summary>
    /// <param name="parameters"></param>
    /// <param name="types"></param>
    /// <returns>If the types-array has matching parameters (of method) in the parameters array (which may be longer).</returns>
    private bool CheckTypeMatch(ParameterInfo[] parameters, Type[] types)
    {
        if (parameters.Length < types.Length)
        {
            return false;
        }

        int i = 0;
        foreach (Type type in types)
        {
            if (type != null && parameters[i].ParameterType != type)
            {
                return false;
            }

            i++;
        }

        return true;
    }

    int AllocateInstantiationId()
    {
        int id = ++this.cacheInstantiationCount;
        id += this.mLocalActor.ID << 16;

        if (this.cacheInstantiationCount == short.MaxValue)
        {
            Debug.LogError("Next Instantiation will create a overflow.");
        }
        
        return id;
    }

    internal Hashtable SendInstantiate(string prefabName, Vector3 position, Quaternion rotation, int group, PhotonViewID[] viewIDs, object[] data)
    {
        int instantiateId = this.AllocateInstantiationId();

        Hashtable instantiateEvent = new Hashtable(); // This players info is sent via ActorID
        instantiateEvent[(byte)0] = prefabName;
        
        if (position != Vector3.zero)
        {
            instantiateEvent[(byte)1] = position;
        }

        instantiateEvent[(byte)2] = rotation;
        
        if (group != 0)
        {
            instantiateEvent[(byte)3] = group;
        }

        if (viewIDs != null && viewIDs.Length > 0)
        {
            instantiateEvent[(byte)4] = viewIDs; // LIMITS NETWORKVIEWS&PLAYERS
        }
        
        if (data != null)
        {
            instantiateEvent[(byte)5] = data;
        }

        instantiateEvent[(byte)6] = this.ServerTimeInMilliSeconds;
        instantiateEvent[(byte)7] = instantiateId;

        this.OpRaiseEvent(PhotonNetworkMessages.Instantiation, instantiateEvent, true, 0, EventCaching.AddToRoomCache, ReceiverGroup.Others);
        return instantiateEvent;
    }

    internal GameObject DoInstantiate(Hashtable evData, PhotonPlayer photonPlayer, GameObject resourceGameObject)
    {
        string prefabName = (string)evData[(byte)0];
        
        Vector3 position;
        if (evData.ContainsKey((byte)1))
        {
            position = (Vector3)evData[(byte)1];
        }
        else
        {
            position = Vector3.zero;
        }

        Quaternion rotation = (Quaternion)evData[(byte)2];

        int group = 0;
        if (evData.ContainsKey((byte)3))
        {
            group = (int)evData[(byte)3];
        }

        PhotonViewID[] viewsIDs;
        if (evData.ContainsKey((byte)4))
        {
            viewsIDs = (PhotonViewID[])evData[(byte)4];
        }
        else
        {
            viewsIDs = new PhotonViewID[0];
        }

        object[] data;
        if (evData.ContainsKey((byte)5))
        {
            data = (object[])evData[(byte)5];
        }
        else
        {
            data = new object[0];
        }

        int serverTime = (int)evData[(byte)6];
        int instantiationId = (int)evData[(byte)7];

        // SetReceiving filtering
        if (this.blockReceivingGroups.Contains(group))
        {
            return null; // Ignore group
        }

        // Check prefab
        if (resourceGameObject == null)
        {
            resourceGameObject = (GameObject)Resources.Load(prefabName, typeof(GameObject));
            if (resourceGameObject == null)
            {
                Debug.LogError("PhotonNetwork error: Could not Instantiate the prefab [" + prefabName + "]. Please verify you have this gameobject in a Resources folder.");
                return null;
            }
        }

        // Instantiate the object
        GameObject go = (GameObject)GameObject.Instantiate(resourceGameObject, position, rotation);
        this.instantiatedObjects.Add(instantiationId, go);

        // Assign view IDs
        PhotonView[] views = (PhotonView[])go.GetComponentsInChildren<PhotonView>();
        int i = 0;
        foreach (PhotonView view in views)
        {
            view.viewID = viewsIDs[i];
            view.group = group;
            view.instantiationData = data;
            i++;
        }

        // Send mono event
        object[] messageInfoParam = new object[1];
        messageInfoParam[0] = new PhotonMessageInfo(photonPlayer, serverTime, null);

        MonoBehaviour[] monos = go.GetComponentsInChildren<MonoBehaviour>();
        foreach (MonoBehaviour mono in monos)
        {
            MethodInfo methodI = this.GetCachedMethod(mono, ReflectionMethods.OnPhotonInstantiate);
            if (methodI != null)
            {
                object result = methodI.Invoke((object)mono, messageInfoParam);
                if (methodI.ReturnType == typeof(System.Collections.IEnumerator))
                {
                    PhotonHandler.SP.StartCoroutine((IEnumerator)result);
                }
            }
        }

        return go;
    }

    // Removes PhotonNetwork.Instantiate-ed objects
    // Does not remove any manually assigned PhotonViews.
    public void RemoveAllInstantiatedObjects()
    {
        GameObject[] instantiatedGoArray = new GameObject[this.instantiatedObjects.Count];
        this.instantiatedObjects.Values.CopyTo(instantiatedGoArray, 0);

        foreach (GameObject go in instantiatedGoArray)
        {
            if (go == null)
            {
                continue;
            }

            this.RemoveInstantiatedGO(go);
        }

        if (this.instantiatedObjects.Count > 0)
        {
            Debug.LogError("RemoveAllInstantiatedObjects() this.instantiatedObjects.Count should be 0 by now.");
        }

        this.instantiatedObjects = new Dictionary<int, GameObject>();
    }

    public void RemoveAllInstantiatedObjectsByPlayer(PhotonPlayer player)
    {
        GameObject[] instantiatedGoArray = new GameObject[this.instantiatedObjects.Count];
        this.instantiatedObjects.Values.CopyTo(instantiatedGoArray, 0);

        foreach (GameObject go in instantiatedGoArray)
        {
            if (go == null)
            {
                continue;
            }

            //TODO: change this process: if a GO has no view, it can't be compared with the player
            PhotonView[] views = go.GetComponentsInChildren<PhotonView>();
            for (int j = views.Length - 1; j >= 0; j--)
            {
                PhotonView view = views[j];
                if (view.owner == player)
                {
                    this.RemoveInstantiatedGO(go);
                    break;
                }
            }
        }
    }

    public void RemoveInstantiatedGO(GameObject go)
    {
        if (go == null)
        {
            if (DebugOut == DebugLevel.ERROR)
            {
                this.DebugReturn(DebugLevel.ERROR, "Can't remove instantiated GO if it's null.");
            }

            return;
        }

        int instantiateId = this.GetInstantiatedObjectsId(go);
        if (instantiateId == -1)
        {
            if (DebugOut == DebugLevel.ERROR)
            {
                this.DebugReturn(DebugLevel.ERROR, "Can't find GO in instantiation list. Object: " + go);
            }

            return;
        }
        
        this.instantiatedObjects.Remove(instantiateId);

        PhotonView[] views = go.GetComponentsInChildren<PhotonView>();
        bool removedFromServer = false;
        for (int j = views.Length - 1; j >= 0; j--)
        {
            PhotonView view = views[j];
            if (view == null)
            {
                continue;
            }

            if (!removedFromServer)
            {
                // first view's owner should be the same as any further view's owner. use it to clean cache
                this.RemoveFromServerInstantiationCache(instantiateId, view.owner.ID);
                removedFromServer = true;
            }

            PhotonNetwork.UnAllocateViewID(view.viewID.ID);
            this.DestroyPhotonView(view);
        }

        if (this.DebugOut >= DebugLevel.ALL)
        {
            this.DebugReturn(DebugLevel.ALL, "Network destroy Instantiated GO: " + go.name);
        }

        GameObject.Destroy(go);
    }

    /// <summary>
    /// This returns -1 if the GO could not be found in list of instantiatedObjects.
    /// </summary>
    private int GetInstantiatedObjectsId(GameObject go)
    {
        int id = -1;
        if (go == null)
        {
            this.DebugReturn(DebugLevel.ERROR, "GetInstantiatedObjectsId() for GO == null.");
            return id;
        }

        foreach (KeyValuePair<int, GameObject> pair in this.instantiatedObjects)
        {
            if (go == pair.Value)
            {
                id = pair.Key;
                break;
            }
        }
        if (id == -1)
        {
            if (DebugOut == DebugLevel.ALL)
            {
                this.DebugReturn(DebugLevel.ALL, "instantiatedObjects does not contain: " + go);
            }
        }

        return id;
    }

    /// <summary>
    /// Removes an instantiation event from the server's cache. Needs id and actorNr of player who instantiated.
    /// </summary>
    private void RemoveFromServerInstantiationCache(int instantiateId, int actorNr)
    {
        Hashtable removeFilter = new Hashtable();
        removeFilter[(byte)7] = instantiateId;
        this.OpRaiseEvent(PhotonNetworkMessages.Instantiation, removeFilter, true, 0, new int[] { actorNr }, EventCaching.RemoveFromRoomCache);
    }

    private void RemoveFromServerInstantiationsOfPlayer(int actorNr)
    {
        // removes all "Instantiation" events of player actorNr. this is not an event for anyone else
        this.OpRaiseEvent(PhotonNetworkMessages.Instantiation, null, true, 0, new int[] { actorNr }, EventCaching.RemoveFromRoomCache);
    }

    // Destroys all gameobjects from a player with a networkview that they own
    // FIRST: Instantiated objects are deleted.
    // SECOND: Destroy entire gameobject+children of networkviews that they are owner of.
    // This can mess up if theres no networkview on root of the objects!
    public void DestroyPlayerObjects(PhotonPlayer player)
    {
        this.RemoveAllInstantiatedObjectsByPlayer(player); // Instantiated objects

        // Manually spawned ones:
        PhotonView[] views = (PhotonView[])GameObject.FindObjectsOfType(typeof(PhotonView));
        for (int i = views.Length - 1; i >= 0; i--)
        {
            PhotonView view = views[i];
            if (view.owner == player)
            {
                this.DoDestroyPhotonView(view);
            }
        }
    }

    public void DestroyPhotonView(PhotonView view)
    {
        // sends the "destroy view" message so others will destroy the view, too. this is not cached
        Hashtable evData = new Hashtable();
        evData[(byte)0] = view.viewID.ID;
        this.OpRaiseEvent(PhotonNetworkMessages.Destroy, evData, true, 0, EventCaching.DoNotCache, ReceiverGroup.Others);

        this.DoDestroyPhotonView(view);
    }

    private void DoDestroyPhotonView(PhotonView view)
    {
        if (view.isMine)
        {
            // Only remove cached RPCs if they are ours
            this.RemoveRPCs(view);
        }

        int id = this.GetInstantiatedObjectsId(view.gameObject);
        if (id != -1)
        {
            Debug.Log("Found view in instantiatedObjects.");
            this.instantiatedObjects.Remove(id);
        }

        if (this.DebugOut >= DebugLevel.ALL)
        {
            this.DebugReturn(DebugLevel.ALL, "Network destroy PhotonView GO: " + view.gameObject.name);
        }

        GameObject.Destroy(view.gameObject); // OnDestroy calls  RemovePhotonView(view);
    }

    public PhotonView GetPhotonView(int viewID)
    {
        PhotonView result = null;
        this.photonViewList.TryGetValue(viewID, out result);
        return result;
    }

    public void RegisterPhotonView(PhotonView netView)
    {
        if (!Application.isPlaying)
        {
            this.photonViewList = new Dictionary<int, PhotonView>();
            return;
        }

        netView.prefix = this.currentLevelPrefix;
        if (netView.owner != null)
        {
            // Error checking
            int correctOwnerID = netView.viewID.ID / PhotonNetwork.MAX_VIEW_IDS;
            if (netView.owner.ID != correctOwnerID)
            {
                Debug.LogError(
                    "RegisterPhotonView: registered view ID " + netView.viewID + " with owner " + netView.owner.ID
                    + " but it should be " + correctOwnerID);
            }
        }

        if (!this.photonViewList.ContainsKey(netView.viewID.ID))
        {
            this.photonViewList.Add(netView.viewID.ID, netView);
            if (this.DebugOut >= DebugLevel.ALL)
            {
                this.DebugReturn(DebugLevel.ALL, "Registered PhotonView: " + netView.viewID);
            }
        }
    }

    public void RemovePhotonView(PhotonView netView)
    {
        if (!Application.isPlaying)
        {
            this.photonViewList = new Dictionary<int, PhotonView>();
            return;
        }

        if (this.photonViewList.ContainsKey(netView.viewID.ID))
        {
            if (this.photonViewList[netView.viewID.ID] != netView)
            {
                // Only remove it if this ID belongs to the PhotonView we're removing
                Debug.LogError(
                    "PHOTON ERROR: This should never be possible: Two PhotonViews with same ID registered? ID="
                    + netView.viewID.ID + " " + netView.name + "  and " + this.photonViewList[netView.viewID.ID].name);
                return;
            }

            this.photonViewList.Remove(netView.viewID.ID);
            if (this.DebugOut >= DebugLevel.ALL)
            {
                this.DebugReturn(DebugLevel.ALL, "Removed PhotonView: " + netView.viewID);
            }
        }
    }

    // Remove local RPCs
    public void RemoveRPCsOfLocalPlayer()
    {
        //ts: will remove eventCode RPC for target actor "me"
        this.OpRaiseEvent(PhotonNetworkMessages.RPC, null, true, 0, new int[] { this.mLocalActor.ID }, EventCaching.RemoveFromRoomCache);
    }

    /// <summary>
    /// Removes the RPCs of someone else (to be used as master).
    /// This won't clean any local caches. It just tells the server to forget a player's RPCs and instantiates.
    /// </summary>
    /// <param name="actorNumber"></param>
    public void RemoveRPCs(int actorNumber)
    {
        this.OpRaiseEvent(PhotonNetworkMessages.RPC, null, true, 0, new int[] { actorNumber }, EventCaching.RemoveFromRoomCache);
    }

    /// <summary>
    /// Instead removint RPCs or Instantiates, this removed everything cached by the actor.
    /// </summary>
    /// <param name="actorNumber"></param>
    private void RemoveCompleteCacheOfPlayer(int actorNumber)
    {
        this.OpRaiseEvent(0, null, true, 0, new int[] { actorNumber }, EventCaching.RemoveFromRoomCache);
    }

    // Remove RPCs of view (if they are local player's RPCs)
    public void RemoveRPCs(PhotonView view)
    {
        if (view.owner != this.mLocalActor)
        {
            Debug.LogError("Error, cannot remove cached RPCs on a networkview thats not ours! " + view.owner + " scene: " + view.isSceneView);
            return;
        }

        Hashtable rpcFilterByViewId = new Hashtable();
        rpcFilterByViewId[(byte) 0] = view.viewID.ID;
        this.OpRaiseEvent(PhotonNetworkMessages.RPC, rpcFilterByViewId, true, 0, EventCaching.RemoveFromRoomCache, ReceiverGroup.Others);
    }

    public void RemoveRPCsInGroup(int group)
    {
        foreach (KeyValuePair<int, PhotonView> kvp in this.photonViewList)
        {
            PhotonView view = kvp.Value;
            if (view.group == group)
            {
                this.RemoveRPCs(view);
            }
        }
    }

    public void SetLevelPrefix(int prefix)
    {
        this.currentLevelPrefix = prefix;
        foreach (KeyValuePair<int, PhotonView> kvp in this.photonViewList)
        {
            PhotonView view = kvp.Value;
            view.prefix = prefix;
        }
    }

    public void RPC(PhotonView view, string methodName, PhotonPlayer player, params object[] parameters)
    {
        if (this.blockSendingGroups.Contains(view.group))
        {
            return; // Block sending on this group
        }

        if (view.viewID.ID < 1)
        {
            Debug.LogError("Illegal view ID:" + view.viewID + " method: " + methodName + " GO:" + view.gameObject.name);
        }

        if (this.DebugOut >= DebugLevel.INFO)
        {
            this.DebugReturn(DebugLevel.INFO, "Sending RPC \"" + methodName + "\" to player[" + player + "]");
        }

        //ts: changed RPCs to a one-level hashtable as described in internal.txt
        Hashtable rpcEvent = new Hashtable();
        rpcEvent[(byte)0] = (int)view.viewID.ID; // LIMITS NETWORKVIEWS&PLAYERS
        if (view.prefix > 0)
        {
            rpcEvent[(byte)1] = view.prefix;
        }
        rpcEvent[(byte)2] = this.ServerTimeInMilliSeconds;
        rpcEvent[(byte)3] = methodName;
        rpcEvent[(byte)4] = (object[])parameters;

        if (this.mLocalActor == player)
        {
            this.ExecuteRPC(rpcEvent, player);
        }
        else
        {
            int[] targetActors = new int[] { player.ID };
            this.OpRaiseEvent(PhotonNetworkMessages.RPC, rpcEvent, true, 0, targetActors);
        }
    }

    public void RPC(PhotonView view, string methodName, PhotonTargets target, params object[] parameters)
    {
        if (this.blockSendingGroups.Contains(view.group))
        {
            return; // Block sending on this group
        }

        if (view.viewID.ID < 1)
        {
            Debug.LogError("Illegal view ID:" + view.viewID + " method: " + methodName + " GO:" + view.gameObject.name);
        }

        if (this.DebugOut >= DebugLevel.INFO)
        {
            this.DebugReturn(DebugLevel.INFO, "Sending RPC \"" + methodName + "\" to " + target);
        }

        //ts: changed RPCs to a one-level hashtable as described in internal.txt
        Hashtable rpcEvent = new Hashtable();
        rpcEvent[(byte)0] = (int)view.viewID.ID; // LIMITS NETWORKVIEWS&PLAYERS
        if (view.prefix > 0)
        {
            rpcEvent[(byte)1] = view.prefix;
        }
        rpcEvent[(byte)2] = this.ServerTimeInMilliSeconds;
        rpcEvent[(byte)3] = methodName;
        rpcEvent[(byte)4] = (object[])parameters;

        // Check scoping
        if (target == PhotonTargets.All)
        {
            this.OpRaiseEvent(PhotonNetworkMessages.RPC, rpcEvent, true, 0);

            // Execute local
            this.ExecuteRPC(rpcEvent, this.mLocalActor);
        }
        else if (target == PhotonTargets.Others)
        {
            this.OpRaiseEvent(PhotonNetworkMessages.RPC, rpcEvent, true, 0);
        }
        else if (target == PhotonTargets.AllBuffered)
        {
            this.OpRaiseEvent(PhotonNetworkMessages.RPC, rpcEvent, true, 0, EventCaching.AddToRoomCache, ReceiverGroup.Others);

            // Execute local
            this.ExecuteRPC(rpcEvent, this.mLocalActor);
        }
        else if (target == PhotonTargets.OthersBuffered)
        {
            this.OpRaiseEvent(PhotonNetworkMessages.RPC, rpcEvent, true, 0, EventCaching.AddToRoomCache, ReceiverGroup.Others);
        }
        else if (target == PhotonTargets.MasterClient)
        {
            if (this.mMasterClient == this.mLocalActor)
            {
                this.ExecuteRPC(rpcEvent, this.mLocalActor);
            }
            else
            {
                this.OpRaiseEvent(PhotonNetworkMessages.RPC, rpcEvent, true, 0, EventCaching.DoNotCache, ReceiverGroup.MasterClient);//TS: changed from caching to non-cached. this goes to master only
            }
        }
        else
        {
            Debug.LogError("Unsupported target enum: " + target);
        }
    }

    // SetReceiving
    public void SetReceivingEnabled(int group, bool enabled)
    {
        if (!enabled)
        {
            if (!this.blockReceivingGroups.Contains(group))
                this.blockReceivingGroups.Add(group);            
        }
        else
        {
            this.blockReceivingGroups.Remove(group);
        }
    }

    // SetSending
    public void SetSendingEnabled(int group, bool enabled)
    {
        if (!enabled)
        {
            if(! this.blockSendingGroups.Contains(group))
                this.blockSendingGroups.Add(group);
        }
        else
        {
            this.blockSendingGroups.Remove(group);
        }
    }

    public void NewSceneLoaded()
    {
        List<int> removeKeys = new List<int>();
        foreach (KeyValuePair<int, PhotonView> kvp in this.photonViewList)
        {
            PhotonView view = kvp.Value;
            if (view == null)
            {
                removeKeys.Add(kvp.Key);
            }
        }

        foreach (int key in removeKeys)
        {
            this.photonViewList.Remove(key);
        }

        if (removeKeys.Count > 0)
        {
            if (this.DebugOut >= DebugLevel.INFO)
            {
                this.DebugReturn(DebugLevel.INFO, "Removed " + removeKeys.Count + " scene view IDs from last scene.");
            }
        }
    }

    public void RunViewUpdate()
    {
        if (!PhotonNetwork.connected)
        {
            return;
        }

        if (this.mActors == null || this.mActors.Count <= 1)
        {
            return; // No need to send OnSerialize messages (these are never buffered anyway)
        }

        foreach (KeyValuePair<int, PhotonView> kvp in this.photonViewList)
        {
            PhotonView view = kvp.Value;

            if (view.observed != null && view.synchronization != ViewSynchronization.Off)
            {
                // Fetch all sending photonViews
                if (view.owner == this.mLocalActor || (view.isSceneView && this.mMasterClient == this.mLocalActor))
                {
                    if (!view.gameObject.active)
                    {
                        continue; // Only on actives                   
                    }

                    if (this.blockSendingGroups.Contains(view.group))
                    {
                        continue; // Block sending on this group
                    }

                    // run it trough its onserialize
                    Hashtable evData = this.WriteSerializeData(view);

                    if (view.synchronization == ViewSynchronization.ReliableDeltaCompressed)
                    {
                        if (!this.CompareWriteSerializeData(evData, view.lastOnSerializeDataSent))
                        {
                            this.OpRaiseEvent(PhotonNetworkMessages.SendSerialize, evData, true, 0);
                            view.lastOnSerializeDataSent = evData;
                        }
                        else
                        {
                            // Debug.Log("Same OnSer. data");
                        }
                    }
                    else
                    {
                        this.OpRaiseEvent(PhotonNetworkMessages.SendSerialize, evData, false, 1);
                        view.lastOnSerializeDataSent = evData;    
                    }
                }
                else
                {
                    // Debug.Log(" NO OBS on " + view.name + " " + view.owner);
                }
            }
            else
            {
            }
        }
    }

    /// <summary>
    /// This (exclusively) compares hashtables produced by WriteSerializeData(). 
    /// In key (byte)1 has to be a object[] or a Hashtable which is then compared.
    /// </summary>
    /// <param name="one"></param>
    /// <param name="two"></param>
    /// <returns></returns>
    private bool CompareWriteSerializeData(Hashtable one, Hashtable two)
    {
        if (two == null || one == null)
        {
            return one == null && two == null;
        }

        // check if object[] and compare
        object[] arrayA = one[(byte)1] as object[];
        if (arrayA != null)
        {
            object[] arrayB = two[(byte)1] as object[];

            if (arrayB == null || arrayA.Length != arrayB.Length)
            {
                // not equal: one has a object[], one doesnt or length don't match
                return false;
            }

            for (int i = 0; i < arrayA.Length; i++)
            {
                if (arrayA[i] != null)
                {
                    if (!arrayA[i].Equals(arrayB[i]))
                    {
                        //// if A is not B, lets check if A is almost B
                        //if (arrayA[i] is Vector3)
                        //{
                        //    Vector3 a = (Vector3)arrayA[i];
                        //    Vector3 b = (Vector3)arrayB[i];

                        //    if (a.AlmostEquals(b, 0.3f))
                        //    {
                        //        continue;
                        //    }
                        //}
                        //else if (arrayA[i] is Quaternion)
                        //{
                        //    Quaternion a = (Quaternion)arrayA[i];
                        //    Quaternion b = (Quaternion)arrayB[i];

                        //    if (a.AlmostEquals(b, 0.3f))
                        //    {
                        //        Debug.Log("Quaternion almost equal: " + arrayA[i] + "!=" + arrayB[i]);
                        //        continue;
                        //    }
                        //}

                        // array[i] does not equal arrayB[i]
                        return false;
                    }
                }
                else if (arrayB[i] != null)
                {
                    // array[i] is null but arrayB[i] is not
                    return false;
                }
            }

            // if no check failed, we got two equal object-arrays here
            return true;
        }

        // compare Hashtables (if not object[])
        Hashtable dataOne = one[(byte)1] as Hashtable;
        Hashtable dataTwo = two[(byte)1] as Hashtable;

        if (dataOne == null || dataTwo == null)
        {
            return dataOne == null && dataTwo == null;
        }

        if (dataTwo.Count != dataOne.Count)
        {
            return false;
        }

        foreach (DictionaryEntry entry in dataOne)
        {
            if (entry.Key is byte)
            {
                object objOne = entry.Value;
                object objTwo = dataTwo[entry.Key];
                if (objOne == null || objTwo == null)
                {
                    if (!(objOne == null && objTwo == null))
                    {
                        return false;
                    }
                }

                if (objOne is string && objTwo is string)
                {
                    if (!((string)objOne).Equals((string)objTwo))
                    {
                        return false;
                    }
                }
                else if (objOne is int && objTwo is int)
                {
                    if (!((int)objOne).Equals((int)objTwo))
                    {
                        return false;
                    }
                }
                else if (objOne is float && objTwo is float)
                {
                    if (!((float)objOne).Equals((float)objTwo))
                    {
                        return false;
                    }
                }
                else if (objOne is Quaternion && objTwo is Quaternion)
                {
                    if (!((Quaternion)objOne).Equals((Quaternion)objTwo))
                    {
                        return false;
                    }
                }
                else if (objOne is bool && objTwo is bool)
                {
                    if (!((bool)objOne).Equals((bool)objTwo))
                    {
                        return false;
                    }
                }
                else if (objOne is char && objTwo is char)
                {
                    if (!((char)objOne).Equals((char)objTwo))
                    {
                        return false;
                    }
                }
                else if (objOne is short && objTwo is short)
                {
                    if (!((short)objOne).Equals((short)objTwo))
                    {
                        return false;
                    }
                }
                else if (objOne is ushort && objTwo is ushort)
                {
                    if (!((ushort)objOne).Equals((ushort)objTwo))
                    {
                        return false;
                    }
                }
                else if (objOne is Vector3 && objTwo is Vector3)
                {
                    if (!((Vector3)objOne).Equals((Vector3)objTwo))
                    {
                        return false;
                    }
                }
                else if (objOne is Vector2 && objTwo is Vector2)
                {
                    if (!((Vector2)objOne).Equals((Vector2)objTwo))
                    {
                        return false;
                    }
                }
                else if (objOne is PhotonPlayer && objTwo is PhotonPlayer)
                {
                    if (!((PhotonPlayer)objOne).Equals((PhotonPlayer)objTwo))
                    {
                        return false;
                    }
                }
                else if (objOne is PhotonViewID && objTwo is PhotonViewID)
                {
                    if (!((PhotonViewID)objOne).Equals((PhotonViewID)objTwo))
                    {
                        return false;
                    }
                }
                else
                {
                    Debug.LogError("Unsupported OnSerializeType: " + objOne.GetType() + "  and: " + objTwo.GetType());
                    return false;
                }
            }
            else
            {
                Debug.LogError(
                    "Invalid OnSerializeContent: type=" + entry.Key.GetType() + " str=" + entry.Key.ToString());
                return false;
            }
        }

        return true;
    }

    // calls OnPhotonSerializeView (through ExecuteOnSerialize)
    // the content created here is consumed by receivers in: ReadOnSerialize
    private Hashtable WriteSerializeData(PhotonView view)
    {
        Hashtable evData = new Hashtable();
        Hashtable messageData = new Hashtable();

        messageData[(byte)0] = (int)view.viewID.ID; // LIMITS NETWORKVIEWS&PLAYERS
        if (view.prefix > 0)
        {
            messageData[(byte)1] = view.prefix;
        }

        messageData[(byte)2] = this.ServerTimeInMilliSeconds;

        evData[(byte)0] = messageData;

        Type type = view.observed.GetType();

        if (typeof(MonoBehaviour).IsInstanceOfType(view.observed))
        {
            MonoBehaviour monob = (MonoBehaviour)view.observed;
            PhotonStream pStream = new PhotonStream(true, null);
            PhotonMessageInfo info = new PhotonMessageInfo(this.mLocalActor, this.ServerTimeInMilliSeconds, view);

            this.ExecuteOnSerialize(monob, pStream, info);
            evData[(byte)1] = pStream.ToArray();
        }
        else if (type == typeof(Transform))
        {
            Transform trans = (Transform)view.observed;
            Hashtable data = new Hashtable();
            if (view.onSerializeTransformOption == OnSerializeTransform.OnlyPosition
                || view.onSerializeTransformOption == OnSerializeTransform.PositionAndRotation
                || view.onSerializeTransformOption == OnSerializeTransform.All)
            {
                data[(byte)0] = trans.localPosition;
            }

            if (view.onSerializeTransformOption == OnSerializeTransform.OnlyRotation
                || view.onSerializeTransformOption == OnSerializeTransform.PositionAndRotation
                || view.onSerializeTransformOption == OnSerializeTransform.All)
            {
                data[(byte)1] = trans.localRotation;
            }

            if (view.onSerializeTransformOption == OnSerializeTransform.OnlyScale
                || view.onSerializeTransformOption == OnSerializeTransform.All)
            {
                data[(byte)2] = trans.localScale;
            }

            evData[(byte)1] = data;
        }
        else if (type == typeof(Rigidbody))
        {
            Rigidbody rigidB = (Rigidbody)view.observed;
            Hashtable data = new Hashtable();
            if (view.onSerializeRigidBodyOption != OnSerializeRigidBody.OnlyAngularVelocity)
            {
                data[(byte)0] = rigidB.velocity;
            }

            if (view.onSerializeRigidBodyOption == OnSerializeRigidBody.OnlyVelocity)
            {
                data[(byte)1] = rigidB.angularVelocity;
            }

            evData[(byte)1] = data;
        } 
        //else if (type == typeof(Animation))
        //{
        //    // TODO ADD ANIMATION
        //}
        else
        {
            Debug.LogError("Type of observed is unknown! " + type + "  and " + type.BaseType);
        }

        return evData;
    }

    // reads incoming messages created by "OnSerialize"
    private void ReadOnSerialize(Hashtable data, PhotonPlayer sender)
    {
        Hashtable messageData = (Hashtable)data[(byte)0];

        int viewID = (int)messageData[(byte)0]; // LIMITS NETWORKVIEWS&PLAYERS
        int correctPrefix = 0;
        if (messageData.ContainsKey((byte)1))
        {
            correctPrefix = (int)messageData[(byte)1];
        }

        int networkTime = (int)messageData[(byte)2];

        PhotonView view = this.GetPhotonView(viewID);
        if (view == null)
        {
            Debug.LogWarning("Received OnSerialization for view ID " + viewID + ". We have no such PhotonView! Ignored this if you're leaving a room. State: " + this.State);
            return;
        }

        if (view.prefix > 0 && correctPrefix != view.prefix)
        {
            Debug.LogError(
                "Received OnSerialization for view ID " + viewID + " with prefix " + correctPrefix + ". Our prefix is "
                + view.prefix);
            return;
        }


        // SetReceiving filtering
        if (this.blockReceivingGroups.Contains(view.group))
        {
            return; // Ignore group
        }

        Type type = view.observed.GetType();
        if (typeof(MonoBehaviour).IsInstanceOfType(view.observed))
        {
            object[] contents = data[(byte)1] as object[];
            MonoBehaviour monob = (MonoBehaviour)view.observed;
            PhotonStream pStream = new PhotonStream(false, contents);

            PhotonMessageInfo info = new PhotonMessageInfo(sender, networkTime, view);

            this.ExecuteOnSerialize(monob, pStream, info);
        }
        else if (type == typeof(Transform))
        {
            Hashtable contents = data[(byte)1] as Hashtable;
            Transform trans = (Transform)view.observed;
            if (contents.ContainsKey((byte)0))
            {
                trans.localPosition = (Vector3)contents[(byte)0];
            }

            if (contents.ContainsKey((byte)1))
            {
                trans.localRotation = (Quaternion)contents[(byte)1];
            }

            if (contents.ContainsKey((byte)2))
            {
                trans.localScale = (Vector3)contents[(byte)2];
            }
        }
        else if (type == typeof(Rigidbody))
        {
            Hashtable contents = data[(byte)1] as Hashtable;
            Rigidbody rigidB = (Rigidbody)view.observed;
            if (contents.ContainsKey((byte)0))
            {
                rigidB.angularVelocity = (Vector3)contents[(byte)0];
            }

            if (contents.ContainsKey((byte)1))
            {
                rigidB.velocity = (Vector3)contents[(byte)1];
            }
        }
            
            /*//TODO ADD ANIMATION
         * else if (type == typeof(Animation))
        {
            Animation rigidB = (Animation)view.observed;
            if (contents.ContainsKey((byte)0))
                rigidB.angularVelocity = (Vector3)contents[(byte)0];
            if (contents.ContainsKey((byte)1))
                rigidB.velocity = (Vector3)contents[(byte)1];
        }*/
        else
        {
            Debug.LogError("Type of observed is unknown when receiving.");
        }
    }

    private void ExecuteOnSerialize(MonoBehaviour monob, PhotonStream pStream, PhotonMessageInfo info)
    {
        object[] paramsX = new object[2];
        paramsX[0] = pStream;
        paramsX[1] = info;

        MethodInfo methodI = this.GetCachedMethod(monob, ReflectionMethods.OnPhotonSerializeView);
        if (methodI != null)
        {
            object result = methodI.Invoke((object)monob, paramsX);
            if (methodI.ReturnType == typeof(System.Collections.IEnumerator))
            {
                PhotonHandler.SP.StartCoroutine((IEnumerator)result);
            }
        }
        else
        {
            Debug.LogError("Tried to run " + ReflectionMethods.OnPhotonSerializeView + ", but this method was missing on: " + monob);
        }
    }

    // HEAVY REFLECTION
    // Gets "OnPhotonSerializeView" and "OnPhotonInstantiate" methods. Caches at first call.
    private enum ReflectionMethods
    {
        OnPhotonSerializeView, 
        OnPhotonInstantiate
    }

    private Dictionary<Type, Dictionary<ReflectionMethods, MethodInfo>> cachedMethods =
        new Dictionary<Type, Dictionary<ReflectionMethods, MethodInfo>>();

    private MethodInfo GetCachedMethod(MonoBehaviour monob, ReflectionMethods methodType)
    {
        Type type = monob.GetType();
        if (!this.cachedMethods.ContainsKey(type))
        {
            Dictionary<ReflectionMethods, MethodInfo> newMethodsDict = new Dictionary<ReflectionMethods, MethodInfo>();
            this.cachedMethods.Add(type, newMethodsDict);
        }

        // Get method type list
        Dictionary<ReflectionMethods, MethodInfo> methods = this.cachedMethods[type];
        if (!methods.ContainsKey(methodType))
        {
            // Load into cache
            Type[] argTypes;
            if (methodType == ReflectionMethods.OnPhotonSerializeView)
            {
                argTypes = new Type[2];
                argTypes[0] = typeof(PhotonStream);
                argTypes[1] = typeof(PhotonMessageInfo);
            }
            else if (methodType == ReflectionMethods.OnPhotonInstantiate)
            {
                argTypes = new Type[1];
                argTypes[0] = typeof(PhotonMessageInfo);
            }
            else
            {
                Debug.LogError("Invalid ReflectionMethod!");
                return null;
            }

            MethodInfo metInfo = monob.GetType().GetMethod(
                methodType + string.Empty, 
                BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, 
                null, 
                argTypes, 
                null);
            if (metInfo != null)
            {
                methods.Add(methodType, metInfo);
            }
        }

        if (methods.ContainsKey(methodType))
        {
            return methods[methodType];
        }
        else
        {
            return null;
        }
    }
}
