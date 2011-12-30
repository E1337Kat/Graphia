using UnityEngine;
using System.Collections;

public class KeyController : MonoBehaviour
{
	/// <summary>
	/// A reference to the NavMeshAgent to speed runtime.
	/// </summary>
	private WalkController walkController;
	
	/// <summary>
	/// Initialize walkController to the instance of NavMeshAgent on this object.
	/// </summary>
	void Start ()
	{
		walkController = GetComponent<WalkController>();
	}
	
	/// <summary>
	/// If either of the movement axises are being pressed, assign the walk controller's destination using the horizontal and vertical axises.
	/// </summary>
	void Update ()
	{
		// If any of the arrow keys are being pressed,
		if(Input.GetButton("Horizontal") || Input.GetButton("Vertical"))
		{
			// Set the target of the walk controller to a point represenative of the direction in which to move.
			Vector3 direction = (Vector3.left * Input.GetAxis("Horizontal")) + (Vector3.forward * Input.GetAxis("Vertical"));
			direction.Normalize();
			Vector3 target = transform.TransformPoint(direction * Time.deltaTime);
			walkController.destination = target;
		}
	}
}
