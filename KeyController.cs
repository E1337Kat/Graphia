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
			// Calculate a vector of the direction to move in.
			Vector3 target = Vector3.right * Input.GetAxis("Horizontal");
			target += Vector3.forward * Input.GetAxis("Vertical");
			
			// Normalize and multiply by deltaTime to remove any bias in player movement.
			target.Normalize();
			target *= Time.deltaTime;
			
			// Compensate for offset on camera position.
			target += Camera.main.transform.InverseTransformPoint(transform.position);
			
			// Transform the camera destination into world space.
			target = Camera.main.transform.TransformPoint(target);
			
			// Assign the target to the walk controller's destination to begin movement.
			walkController.destination = target;
		}
	}
}
