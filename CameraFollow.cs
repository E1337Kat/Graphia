using UnityEngine;
using System.Collections;

public class CameraFollow : MonoBehaviour
{
	/// <summary>
	/// The target to follow.
	/// </summary>
	public Transform target;
	/// <summary>
	/// The offset from the target at which the camera should float.
	/// </summary>
	public Vector3 offset;
	
	/// <summary>
	/// Update this instance by setting the camera's position with that of the target's position offset with the offset vector.
	/// </summary>
	void Update ()
	{
		float distance = 0.0f;
		
		// Set camera rotation.
		transform.LookAt (target.position);
		
		// Push or pull the camera from the player to appropriate following distance.
		distance = Vector3.Distance(target.position, transform.position) - offset.magnitude;
		transform.Translate(distance * Vector3.forward);
	}
}
