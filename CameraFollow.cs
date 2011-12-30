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
		transform.position = target.TransformPoint(offset);
		transform.LookAt (target.position);
	}
}
