using UnityEngine;
using System.Collections;

/// <summary>
/// This is the CameraFollow component. It is used to force the camera to follow the player or another assigned target. In this
/// version, it simply restrains the camera to float at a distance from the player equivalent to offset.z, and locks the y
/// transformation axis to match that of offset.y. The x and z axises will be allowed to drift at a distance equivalent to the
/// offset.z axis. As well, the camera is set to look at a point parallel to the player offset.x from the player left or right.
/// This will allow for cinematic camera angles by changing the offset from other scripts. Eventually, the offset should also be
/// modifable with zoom in and zoom out gestures. As a final touch, the camera should as be modified with collision detection
/// and raycasting to keep the player in view at all times, even with intervening objects.
/// </summary>
public class CameraFollow : MonoBehaviour
{
	/// <summary>
	/// This is the target transform to follow. Traditionally this would be the player, but for cinematics and some other 
	/// purposes it could be set to another transform, such as another player.
	/// </summary>
	public Transform target;
	/// <summary>
	/// The offset from the target at which the camera should float. y is the height relative to the player. x is the distance
	/// either left or right of the player to look at. z is distance at which to follow the player.
	/// </summary>
	public Vector3 offset;
	
	/// <summary>
	/// This is the update method. It is called every frame to move the camera to its appropriate position.
	/// </summary>
	void Update ()
	{
		if(target==null)
			return;
		
		Vector3 screenCenter = target.position;
		float distance = 0.0f;
		float height = 0.0f;
		
		// Set camera rotation.
		screenCenter += Camera.main.transform.TransformDirection(Vector3.left) * offset.x;
		transform.LookAt (screenCenter);
		
		// Push or pull the camera from the player to appropriate following distance.
		distance = Vector3.Distance(target.position, transform.position) - offset.z;
		transform.Translate(distance * Vector3.forward);
		
		// Push or pull height to constrain it to that of the offset.
		height = (target.position.y + offset.y) - transform.position.y;
		transform.Translate(height * Vector3.up);
	}
}
